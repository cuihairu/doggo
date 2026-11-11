namespace Doggo.Domain.Orders;

/// <summary>
/// Selects the payment channel bound to <see cref="IPaymentProvider"/>. Only "Mock" ships
/// today (the deployment milestone adds Alipay/WeChat); a production environment must not
/// run on the mock provider, so a misconfigured deployment fails fast at startup instead of
/// silently accepting forged payment callbacks.
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payment";

    /// <summary>Provider name; case-insensitive. Empty defaults to the mock for dev/test.</summary>
    public string Provider { get; set; } = "Mock";

    public bool IsMockProvider =>
        string.IsNullOrWhiteSpace(Provider)
        || string.Equals(Provider, "Mock", StringComparison.OrdinalIgnoreCase);
}
