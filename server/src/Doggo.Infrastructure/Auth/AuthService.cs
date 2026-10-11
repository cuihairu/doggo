using System.Security.Cryptography;
using System.Text;
using Doggo.Domain.Users;
using Doggo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Auth;

/// <summary>Raised for malformed input (missing fields, short passwords) — maps to 400.</summary>
public sealed class AuthValidationException(string message) : Exception(message)
{
}

/// <summary>Raised for bad credentials or unusable tokens — maps to 401.</summary>
public sealed class AuthException(string message) : Exception(message)
{
}

/// <summary>Raised when the phone or email is already registered — maps to 409.</summary>
public sealed class AccountExistsException(string message) : Exception(message)
{
}

public sealed record RegisterCommand(string Account, string Password, string? Nickname, string? InviteCode);

public sealed record AuthResult(
    Guid UserId,
    string Nickname,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken);

public sealed record RefreshedResult(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken);

public sealed record ProfileView(
    Guid Id,
    string Phone,
    string Email,
    string Nickname,
    string AvatarUrl,
    string InviteCode,
    Guid? InvitedByUserId,
    string Status,
    DateTime? LastLoginAtUtc,
    DateTime CreatedAtUtc);

/// <summary>
/// Account registration and login with PBKDF2 password hashing (ASP.NET Identity hasher),
/// short-lived access tokens plus rotating refresh tokens (only SHA-256 hashes stored), and
/// login audit logs.
/// </summary>
public sealed partial class AuthService(
    DoggoDbContext db,
    IOptions<AuthOptions> optionsFactory,
    TokenService tokens,
    ILogger<AuthService> logger)
{
    private readonly AuthOptions _options = optionsFactory.Value;
    private readonly PasswordHasher<UserAccount> _hasher = new();

    public async Task<AuthResult> RegisterAsync(RegisterCommand command, string ip, string userAgent, CancellationToken cancellationToken = default)
    {
        var account = command.Account.Trim();
        if (account.Length is < 3 or > 128)
        {
            throw new AuthValidationException("Account must be 3-128 characters.");
        }

        if (command.Password.Length < 8)
        {
            throw new AuthValidationException("Password must be at least 8 characters.");
        }

        var isPhone = account.All(char.IsDigit) && account.Length is >= 5 and <= 20;
        var isEmail = account.Contains('@', StringComparison.Ordinal);
        if (!isPhone && !isEmail)
        {
            throw new AuthValidationException("Account must be a phone number or an email address.");
        }

        var exists = await db.UserAccounts.AsNoTracking()
            .AnyAsync(u => (!isPhone && u.Email == account) || (isPhone && u.Phone == account), cancellationToken);
        if (exists)
        {
            throw new AccountExistsException("This account is already registered.");
        }

        Guid? invitedBy = null;
        if (!string.IsNullOrWhiteSpace(command.InviteCode))
        {
            var inviter = await db.UserAccounts.AsNoTracking()
                .FirstOrDefaultAsync(u => u.InviteCode == command.InviteCode.Trim(), cancellationToken);
            if (inviter is not null)
            {
                invitedBy = inviter.Id;
            }
        }

        var user = new UserAccount
        {
            Id = Guid.CreateVersion7(),
            Phone = isPhone ? account : string.Empty,
            Email = isEmail ? account : string.Empty,
            PasswordHash = _hasher.HashPassword(null!, command.Password),
            Nickname = string.IsNullOrWhiteSpace(command.Nickname) ? MaskAccount(account) : command.Nickname.Trim(),
            InviteCode = await NewInviteCodeAsync(cancellationToken),
            InvitedByUserId = invitedBy,
            Status = UserStatus.Active
        };
        db.UserAccounts.Add(user);

        var (accessToken, expiresAt, refreshToken) = IssueTokens(user.Id, user.IsAdmin);
        db.UserRefreshTokens.Add(RefreshTokenRow(user.Id, refreshToken));
        db.UserLoginLogs.Add(new UserLoginLog
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Account = account,
            Ip = ip,
            UserAgent = userAgent,
            Success = true
        });
        await db.SaveChangesAsync(cancellationToken);

        LogRegistered(user.Nickname);
        return new AuthResult(user.Id, user.Nickname, accessToken, expiresAt, refreshToken);
    }

    public async Task<AuthResult> LoginAsync(string account, string password, string ip, string userAgent, CancellationToken cancellationToken = default)
    {
        account = account.Trim();
        var user = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.Phone == account || u.Email == account, cancellationToken);
        if (user is null)
        {
            await WriteLoginLogAsync(null, account, ip, userAgent, success: false, cancellationToken);
            throw new AuthException("Account or password is wrong.");
        }

        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            await WriteLoginLogAsync(user.Id, account, ip, userAgent, success: false, cancellationToken);
            throw new AuthException("Account or password is wrong.");
        }

        if (user.Status == UserStatus.Disabled)
        {
            await WriteLoginLogAsync(user.Id, account, ip, userAgent, success: false, cancellationToken);
            throw new AuthException("This account is disabled.");
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        var (accessToken, expiresAt, refreshToken) = IssueTokens(user.Id, user.IsAdmin);
        db.UserRefreshTokens.Add(RefreshTokenRow(user.Id, refreshToken));
        await WriteLoginLogAsync(user.Id, account, ip, userAgent, success: true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        LogLogin(user.Nickname);
        return new AuthResult(user.Id, user.Nickname, accessToken, expiresAt, refreshToken);
    }

    /// <summary>Exchanges a valid refresh token for a new pair; the old token is revoked.</summary>
    public async Task<RefreshedResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new AuthValidationException("Refresh token is required.");
        }

        var hash = HashToken(refreshToken);
        var row = await db.UserRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            ?? throw new AuthException("Refresh token is invalid.");

        if (row.RevokedAtUtc is not null)
        {
            throw new AuthException("Refresh token was revoked.");
        }

        if (row.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new AuthException("Refresh token has expired.");
        }

        var (newRaw, newHash) = NewRefreshToken();
        row.RevokedAtUtc = DateTime.UtcNow;
        row.ReplacedByTokenHash = newHash;
        row.LastSeenAtUtc = DateTime.UtcNow;
        db.UserRefreshTokens.Add(new UserRefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = row.UserId,
            TokenHash = newHash,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(Math.Max(_options.RefreshTokenDays, 1))
        });
        await db.SaveChangesAsync(cancellationToken);

        var account = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == row.UserId, cancellationToken);
        var (accessToken, expiresAt) = tokens.CreateAccessToken(row.UserId, account?.IsAdmin ?? false);
        return new RefreshedResult(accessToken, expiresAt, newRaw);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(refreshToken);
        var row = await db.UserRefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAtUtc == null, cancellationToken);
        if (row is null)
        {
            return; // logout is idempotent; unknown or already-revoked tokens are fine
        }

        row.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        LogLogout(row.UserId);
    }

    public async Task<ProfileView> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.UserAccounts.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new AuthException("User was not found.");

        return new ProfileView(
            user.Id,
            user.Phone,
            user.Email,
            user.Nickname,
            user.AvatarUrl,
            user.InviteCode,
            user.InvitedByUserId,
            user.Status.ToString(),
            user.LastLoginAtUtc,
            user.CreatedAtUtc);
    }

    /// <summary>Links a verified Steam identity to the account; one steamid binds once.</summary>
    public async Task<UserSteamProfile> BindSteamAsync(Guid userId, string steamId, CancellationToken cancellationToken = default)
    {
        var existing = await db.UserSteamProfiles
            .FirstOrDefaultAsync(p => p.SteamId == steamId, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != userId)
            {
                throw new AuthValidationException("This Steam account is already bound to another user.");
            }

            return existing; // idempotent rebind
        }

        var profile = new UserSteamProfile
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SteamId = steamId
        };
        db.UserSteamProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        LogSteamBound(userId, steamId);
        return profile;
    }

    public async Task<UserSteamProfile?> GetSteamProfileAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.UserSteamProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    private (string AccessToken, DateTime ExpiresAtUtc, string RefreshToken) IssueTokens(Guid userId, bool isAdmin)
    {
        var (accessToken, expiresAt) = tokens.CreateAccessToken(userId, isAdmin);
        var (raw, _) = NewRefreshToken();
        return (accessToken, expiresAt, raw);
    }

    private UserRefreshToken RefreshTokenRow(Guid userId, string rawToken) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = HashToken(rawToken),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(Math.Max(_options.RefreshTokenDays, 1))
        };

    private static (string Raw, string Hash) NewRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var raw = Convert.ToBase64String(bytes);
        return (raw, HashToken(raw));
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task WriteLoginLogAsync(Guid? userId, string account, string ip, string userAgent, bool success, CancellationToken cancellationToken)
    {
        db.UserLoginLogs.Add(new UserLoginLog
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Account = account,
            Ip = ip,
            UserAgent = userAgent,
            Success = success
        });
        // Login failures must reach the database even though the caller throws afterwards.
        if (!success)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<string> NewInviteCodeAsync(CancellationToken cancellationToken)
    {
        // Short human-shareable code; retry on the (rare) unique-index collision.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            Span<byte> bytes = stackalloc byte[5];
            RandomNumberGenerator.Fill(bytes);
            var candidate = Convert.ToBase64String(bytes).Replace('+', 'A').Replace('/', 'B');
            var taken = await db.UserAccounts.AsNoTracking()
                .AnyAsync(u => u.InviteCode == candidate, cancellationToken);
            if (!taken)
            {
                return candidate;
            }
        }

        return Guid.CreateVersion7().ToString("N")[..8];
    }

    private static string MaskAccount(string account)
    {
        if (account.Contains('@', StringComparison.Ordinal))
        {
            var at = account.IndexOf('@');
            var name = account[..at];
            var visible = Math.Min(2, name.Length);
            return string.Concat(name.AsSpan(0, visible), "***", account[at..]);
        }

        return string.Concat(account.AsSpan(0, Math.Min(3, account.Length)), "***");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "user {Nickname} registered")]
    private partial void LogRegistered(string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "user {Nickname} logged in")]
    private partial void LogLogin(string nickname);

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} logged out")]
    private partial void LogLogout(Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "user {UserId} bound steam id {SteamId}")]
    private partial void LogSteamBound(Guid userId, string steamId);
}
