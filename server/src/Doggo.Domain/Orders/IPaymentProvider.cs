using System.Text.Json;

namespace Doggo.Domain.Orders;

public sealed record PaymentCreation(string PayUrl, string ProviderTradeNo, long AmountCents);

public sealed record PaymentCallbackResult(
    Guid OrderId,
    string IdempotencyKey,
    string ProviderTradeNo,
    long AmountCents,
    bool Success,
    string? Message);

/// <summary>
/// One integration point per payment channel. Implementations own channel auth:
/// <see cref="CreatePaymentAsync"/> returns what the client needs to pay, and
/// <see cref="VerifyCallbackAsync"/> decides whether an inbound callback is genuine.
/// </summary>
public interface IPaymentProvider
{
    PaymentProvider Provider { get; }

    Task<PaymentCreation> CreatePaymentAsync(UserOrder order, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the callback cannot be trusted; otherwise the parsed result.</summary>
    Task<PaymentCallbackResult?> VerifyCallbackAsync(
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default);

    Task<bool> RefundAsync(string providerTradeNo, long amountCents, string reason, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-process fake for development, tests and the mock acceptance flow: payments succeed
/// immediately, callbacks are plain JSON, refunds always succeed. Replaced by real
/// Alipay/WeChat providers in the deployment milestone.
/// </summary>
public sealed class MockPaymentProvider : IPaymentProvider
{
    public PaymentProvider Provider => PaymentProvider.Alipay;

    public Task<PaymentCreation> CreatePaymentAsync(UserOrder order, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentCreation(
            $"https://mock-pay.doggo.local/checkout/{order.OrderNo}",
            $"MOCK-{order.OrderNo}",
            order.TotalCostCents));

    public Task<PaymentCallbackResult?> VerifyCallbackAsync(
        string payload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var orderId = root.GetProperty("orderId").GetGuid();
            var amountCents = root.GetProperty("amountCents").GetInt64();
            var success = root.TryGetProperty("success", out var successElement) && successElement.GetBoolean();
            var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            return Task.FromResult<PaymentCallbackResult?>(
                new PaymentCallbackResult(orderId, $"mock-{orderId}-{amountCents}", $"MOCK-{orderId}", amountCents, success, message));
        }
        catch (JsonException)
        {
            return Task.FromResult<PaymentCallbackResult?>(null);
        }
    }

    public Task<bool> RefundAsync(string providerTradeNo, long amountCents, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
