using Doggo.Domain.Common;

namespace Doggo.Domain.System;

public class AdminAccount : Entity, ISoftDeletable
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
    public string? LastLoginIp { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public class AdminRole : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class AdminPermission : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class AdminUserRole : Entity
{
    public Guid AdminAccountId { get; set; }
    public Guid RoleId { get; set; }
}

public class AdminRolePermission : Entity
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
}

public class AdminIpWhitelist : Entity
{
    public string IpOrCidr { get; set; } = string.Empty;
    public string? Note { get; set; }
    public bool Enabled { get; set; } = true;
}

public class AdminLoginLog : Entity
{
    public Guid AdminAccountId { get; set; }
    public string Ip { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public bool Success { get; set; }
}

public class SiteSetting : Entity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ValueType { get; set; } = "text";
    public string? Note { get; set; }
}

public class SupportQrcode : Entity
{
    public string Title { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int DaysOfWeekMask { get; set; } = 127;
    public TimeSpan StartAt { get; set; }
    public TimeSpan EndAt { get; set; }
    public bool Enabled { get; set; } = true;
}

public class ShortUrl : Entity
{
    public string Key { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public long ClickCount { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}

public class NotificationChannel : Entity
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public class NotificationEventSubscription : Entity
{
    public Guid ChannelId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
