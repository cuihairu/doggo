using Doggo.Domain.Users;
using Doggo.Infrastructure.Auth;
using Doggo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Doggo.Infrastructure.Tests;

public class AuthServiceTests
{
    private const string Account = "13800000001";
    private const string Password = "correct-horse";

    private static DoggoDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<DoggoDbContext>()
            .UseInMemoryDatabase($"auth-tests-{Guid.NewGuid():N}")
            .Options);

    private sealed class StubWechatClient(WechatSession? session) : IWechatSessionClient
    {
        public Task<WechatSession> ExchangeAsync(string code, CancellationToken cancellationToken = default) =>
            session is null
                ? throw new WechatLoginNotConfiguredException("WeChat login is not configured on this server.")
                : Task.FromResult(session);
    }

    private static AuthOptions CreateAuthOptions() => new()
    {
        SecretKey = "unit-test-signing-secret-that-is-long-enough",
        Issuer = "doggo-test",
        Audience = "doggo-api-test",
        AccessTokenMinutes = 30,
        RefreshTokenDays = 14
    };

    private static AuthService CreateService(DoggoDbContext db, WechatSession? session = null)
    {
        var options = Options.Create(CreateAuthOptions());
        var wechat = new StubWechatClient(session ?? new WechatSession("oX-1-test-openid", null, "sk"));
        return new AuthService(db, options, new TokenService(options), wechat, NullLogger<AuthService>.Instance);
    }

    private static Task<AuthResult> RegisterAsync(
        AuthService service,
        string account = Account,
        string password = Password,
        string? nickname = null,
        string? inviteCode = null) =>
        service.RegisterAsync(new RegisterCommand(account, password, nickname, inviteCode), "127.0.0.1", "unit");

    [Fact]
    public async Task Register_CreatesAccountWithInviteCodeAndTokens()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var result = await RegisterAsync(service);

        Assert.True(result.UserId != Guid.Empty);
        Assert.True(result.AccessToken.Length > 0);
        Assert.True(result.AccessTokenExpiresAtUtc > DateTime.UtcNow);
        Assert.True(result.RefreshToken.Length > 0);
        Assert.Equal(1, await db.UserAccounts.CountAsync());

        var user = await db.UserAccounts.SingleAsync();
        Assert.Equal(Account, user.Phone);
        Assert.False(string.IsNullOrEmpty(user.InviteCode));
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(1, await db.UserRefreshTokens.CountAsync());
        Assert.Null((await db.UserRefreshTokens.FirstAsync(t => t.TokenHash == Hash(result.RefreshToken))).RevokedAtUtc);
    }

    [Theory]
    [InlineData("ab", Password)] // too short account
    [InlineData(Account, "short")] // weak password
    [InlineData("not a phone or email", Password)]
    public async Task Register_RejectsInvalidInput(string account, string password)
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<AuthValidationException>(() => RegisterAsync(service, account, password));
    }

    [Theory]
    [InlineData("13800000002")]
    [InlineData("user@example.com")]
    public async Task Register_AcceptsPhoneOrEmailAccount(string account)
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var result = await RegisterAsync(service, account);

        Assert.Equal(1, await db.UserAccounts.CountAsync());
        Assert.Equal(result.UserId, (await db.UserAccounts.SingleAsync()).Id);
    }

    [Fact]
    public async Task Register_DuplicateAccount_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        await RegisterAsync(service);

        await Assert.ThrowsAsync<AccountExistsException>(() => RegisterAsync(service));
    }

    [Fact]
    public async Task Register_WithValidInviteCode_BindsInviter()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var inviter = await RegisterAsync(service, "13800000002");
        var inviterCode = (await db.UserAccounts.FirstAsync(u => u.Id == inviter.UserId)).InviteCode;

        var invitee = await RegisterAsync(service, "13800000003", inviteCode: inviterCode);

        var bound = await db.UserAccounts.SingleAsync(u => u.Id == invitee.UserId);
        Assert.Equal(inviter.UserId, bound.InvitedByUserId);
    }

    [Fact]
    public async Task Register_WithUnknownInviteCode_IgnoresIt()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var result = await RegisterAsync(service, "13800000003", inviteCode: "NOPE-NOPE");

        Assert.Null((await db.UserAccounts.SingleAsync(u => u.Id == result.UserId)).InvitedByUserId);
    }

    [Fact]
    public async Task Login_Success_IssuesTokensUpdatesLoginTimeAndLogs()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var registered = await RegisterAsync(service);
        await db.SaveChangesAsync();

        var result = await service.LoginAsync(Account, Password, "127.0.0.1", "unit");

        Assert.Equal(registered.UserId, result.UserId);
        Assert.NotEqual(registered.RefreshToken, result.RefreshToken);
        Assert.True(result.AccessTokenExpiresAtUtc > DateTime.UtcNow);
        Assert.NotNull((await db.UserAccounts.FirstAsync()).LastLoginAtUtc);
        var successLogs = await db.UserLoginLogs.CountAsync(l => l.UserId == registered.UserId && l.Success);
        Assert.Equal(2, successLogs);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsAndWritesFailureLog()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var registered = await RegisterAsync(service);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<AuthException>(() => service.LoginAsync(Account, "wrong-password", "127.0.0.1", "unit"));

        Assert.Equal(1, await db.UserLoginLogs.CountAsync(l => l.UserId == registered.UserId && !l.Success));
    }

    [Fact]
    public async Task Login_UnknownAccount_ThrowsAndWritesFailureLog()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<AuthException>(() => service.LoginAsync("13800009999", Password, "127.0.0.1", "unit"));

        Assert.Equal(1, await db.UserLoginLogs.CountAsync(l => l.UserId == null && !l.Success));
    }

    [Fact]
    public async Task Login_DisabledAccount_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        await RegisterAsync(service);
        var user = await db.UserAccounts.SingleAsync();
        user.Status = UserStatus.Disabled;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<AuthException>(() => service.LoginAsync(Account, Password, "127.0.0.1", "unit"));
    }

    [Fact]
    public async Task Refresh_RotatesTokenAndInvalidatesTheOldOne()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var registered = await RegisterAsync(service);
        await db.SaveChangesAsync();

        var refreshed = await service.RefreshAsync(registered.RefreshToken);

        Assert.NotEqual(registered.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(2, await db.UserRefreshTokens.CountAsync());
        Assert.Single(await db.UserRefreshTokens.Where(t => t.RevokedAtUtc != null).ToListAsync());

        await Assert.ThrowsAsync<AuthException>(() => service.RefreshAsync(registered.RefreshToken));
    }

    [Fact]
    public async Task Refresh_UnknownToken_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        await Assert.ThrowsAsync<AuthException>(() => service.RefreshAsync("not-a-real-token"));
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var registered = await RegisterAsync(service);
        var token = await db.UserRefreshTokens.SingleAsync();
        token.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<AuthException>(() => service.RefreshAsync(registered.RefreshToken));
    }

    [Fact]
    public async Task Logout_RevokesTokenAndIsIdempotent()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var registered = await RegisterAsync(service);

        await service.LogoutAsync(registered.RefreshToken);
        Assert.Single(await db.UserRefreshTokens.Where(t => t.RevokedAtUtc != null).ToListAsync());

        // Idempotent: logging out an unknown or already-revoked token does not throw.
        await service.LogoutAsync(registered.RefreshToken);
        await service.LogoutAsync("not-a-real-token");
    }

    [Fact]
    public async Task Logout_ThenRefresh_Throws()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var registered = await RegisterAsync(service);

        await service.LogoutAsync(registered.RefreshToken);

        await Assert.ThrowsAsync<AuthException>(() => service.RefreshAsync(registered.RefreshToken));
    }

    [Fact]
    public async Task GetProfile_ReturnsTheUserView()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var registered = await RegisterAsync(service, "user@example.com", Password, "Ada");

        var profile = await service.GetProfileAsync(registered.UserId);

        Assert.Equal("user@example.com", profile.Email);
        Assert.Equal("Ada", profile.Nickname);
        Assert.Equal("Active", profile.Status);

        await Assert.ThrowsAsync<AuthException>(() => service.GetProfileAsync(Guid.CreateVersion7()));
    }

    [Fact]
    public async Task BindSteam_BindsOnce_RejectsAnotherOwner()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var first = await RegisterAsync(service, "13800000011");
        var second = await RegisterAsync(service, "13800000012");

        var bound = await service.BindSteamAsync(first.UserId, "76561198000000001");
        Assert.Equal(first.UserId, bound.UserId);

        // Idempotent for the same user.
        var again = await service.BindSteamAsync(first.UserId, "76561198000000001");
        Assert.Equal(bound.Id, again.Id);
        Assert.Equal(1, await db.UserSteamProfiles.CountAsync());

        // Another user cannot take an already-bound steamid.
        await Assert.ThrowsAsync<AuthValidationException>(() =>
            service.BindSteamAsync(second.UserId, "76561198000000001"));
    }

    [Fact]
    public async Task WechatLogin_CreatesTheAccountOnceAndReturnsTokens()
    {
        await using var db = CreateDb();
        var service = CreateService(db);

        var first = await service.LoginWithWechatAsync("wx-code-1", "wx-player", "127.0.0.1", "taro");
        Assert.False(string.IsNullOrEmpty(first.AccessToken));
        Assert.False(string.IsNullOrEmpty(first.RefreshToken));

        var account = await db.UserAccounts.SingleAsync();
        Assert.Equal("oX-1-test-openid", account.WechatOpenId);
        Assert.Equal("wx-player", account.Nickname);
        Assert.False(string.IsNullOrEmpty(account.InviteCode));
        // Miniapp accounts get a random unusable password, so account login cannot work for them.
        Assert.False(string.IsNullOrEmpty(account.PasswordHash));

        // The same openid is the same account; a new openid is a new account.
        var again = await service.LoginWithWechatAsync("wx-code-2", null, "127.0.0.1", "taro");
        Assert.Equal(first.UserId, again.UserId);
        Assert.Equal(1, await db.UserAccounts.CountAsync());
        Assert.Equal(2, await db.UserLoginLogs.CountAsync(l => l.UserId == first.UserId));
    }

    [Fact]
    public async Task WechatLogin_RejectsAnEmptyCodeAndAnUnconfiguredServer()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<AuthValidationException>(
            () => CreateService(db).LoginWithWechatAsync(" ", null, "127.0.0.1", "taro"));

        await using var unconfigured = CreateDb();
        var options = Options.Create(CreateAuthOptions());
        var service = new AuthService(
            unconfigured, options, new TokenService(options),
            new StubWechatClient(null), NullLogger<AuthService>.Instance);
        await Assert.ThrowsAsync<WechatLoginNotConfiguredException>(
            () => service.LoginWithWechatAsync("wx-code", null, "127.0.0.1", "taro"));
    }

    private static string Hash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
