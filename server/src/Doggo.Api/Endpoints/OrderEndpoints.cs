using Doggo.Domain.Orders;
using Doggo.Infrastructure.Orders;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Doggo.Api.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/orders");

        group.MapPost("/", async (
            [FromBody] CreateOrderRequest request,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await orders.CreateOrderAsync(new CreateOrderCommand(
                    request.UserId,
                    request.ProductId,
                    request.PlanId,
                    request.OptionId,
                    request.Entry,
                    request.ServiceMode,
                    request.FulfillmentType,
                    request.PayType,
                    request.CoinDeduction,
                    request.CouponDeductionCents), cancellationToken);

                return Results.Created($"/api/orders/{result.OrderId}", result);
            }
            catch (OrderNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (OrderValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var order = await orders.FindOrderAsync(id, cancellationToken);
                return Results.Ok(OrderView.From(order));
            }
            catch (OrderNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });

        group.MapPost("/{id:guid}/pay", async (
            Guid id,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var creation = await orders.CreatePaymentAsync(id, cancellationToken);
                return Results.Ok(creation);
            }
            catch (OrderNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (OrderValidationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapPost("/{id:guid}/cancel", async (
            Guid id,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var cancelled = await orders.CancelUnpaidAsync(id, "cancelled by user", cancellationToken);
                return Results.Ok(new { cancelled });
            }
            catch (OrderNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });

        group.MapPost("/{id:guid}/refund", async (
            Guid id,
            [FromBody] RefundRequest request,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var refunded = await orders.RefundOrderAsync(id, request.Reason, cancellationToken);
                return Results.Ok(new { refunded });
            }
            catch (OrderNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });

        group.MapPost("/{id:guid}/retrieval", async (
            Guid id,
            [FromBody] RetrievalRequest request,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var (accepted, offerId, error) = await orders.SubmitRetrievalAsync(
                    id,
                    request.TradeUrlId,
                    [.. request.ItemsToGive.Select(item => new VaporTradeAsset(item.AssetId, item.AppId, item.ClassId, item.InstanceId))],
                    cancellationToken);

                return accepted
                    ? Results.Ok(new { accepted, offerId })
                    : Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (OrderNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        });

        group.MapPost("/{id:guid}/retrieval/complete", async (
            Guid id,
            [FromBody] RetrievalCompleteRequest request,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            var completed = await orders.CompleteRetrievalAsync(id, request.OfferId, cancellationToken);
            return Results.Ok(new { completed });
        });

        group.MapPost("/{id:guid}/retrieval/fail", async (
            Guid id,
            [FromBody] RetrievalFailRequest request,
            [FromServices] OrderService orders,
            CancellationToken cancellationToken) =>
        {
            var rolledBack = await orders.FailRetrievalAsync(id, request.Reason, cancellationToken);
            return Results.Ok(new { rolledBack });
        });

        app.MapPost("/api/payments/callback", async (
            HttpRequest request,
            [FromServices] OrderService orders,
            [FromServices] IPaymentProvider payment,
            CancellationToken cancellationToken) =>
        {
            var payload = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

            var callback = await payment.VerifyCallbackAsync(payload, headers, cancellationToken);
            if (callback is null)
            {
                return Results.Problem("payment callback could not be verified", statusCode: StatusCodes.Status400BadRequest);
            }

            var applied = await orders.ApplyPaymentCallbackAsync(callback.OrderId, callback, payload, cancellationToken);
            return Results.Ok(new { applied, callback.Success });
        });
    }
}

public sealed record CreateOrderRequest(
    Guid UserId,
    Guid ProductId,
    Guid? PlanId,
    Guid? OptionId,
    OrderEntry Entry,
    ServiceMode ServiceMode,
    FulfillmentType FulfillmentType,
    PaymentProvider PayType,
    long CoinDeduction = 0,
    long CouponDeductionCents = 0);

public sealed record RefundRequest(string Reason);

public sealed record RetrievalRequest(Guid TradeUrlId, List<RetrievalItem> ItemsToGive);

public sealed record RetrievalItem(string AssetId, string AppId, string ClassId, string InstanceId);

public sealed record RetrievalCompleteRequest(string OfferId);

public sealed record RetrievalFailRequest(string Reason);

public sealed record OrderView(
    Guid Id,
    string OrderNo,
    Guid UserId,
    OrderEntry Entry,
    Guid ProductId,
    Guid? PlanId,
    Guid? PromotionId,
    long RegularPriceCents,
    long PriceCents,
    long TotalPriceCents,
    long TotalCostCents,
    long CoinDeduction,
    long CouponDeductionCents,
    long CashbackCents,
    OrderStatus Status,
    ServiceMode ServiceMode,
    FulfillmentType FulfillmentType,
    Guid? TradeUrlId,
    string? TradeOfferId,
    string? Error,
    PaymentProvider PayType,
    string? PayTradeNo,
    DateTime? PaidAtUtc,
    DateTime? PayDeadlineUtc,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc)
{
    public static OrderView From(UserOrder order) => new(
        order.Id,
        order.OrderNo,
        order.UserId,
        order.Entry,
        order.ProductId,
        order.PlanId,
        order.PromotionId,
        order.RegularPriceCents,
        order.PriceCents,
        order.TotalPriceCents,
        order.TotalCostCents,
        order.CoinDeduction,
        order.CouponDeductionCents,
        order.CashbackCents,
        order.Status,
        order.ServiceMode,
        order.FulfillmentType,
        order.TradeUrlId,
        order.TradeOfferId,
        order.Error,
        order.PayType,
        order.PayTradeNo,
        order.PaidAtUtc,
        order.PayDeadlineUtc,
        order.CompletedAtUtc,
        order.CreatedAtUtc,
        order.UpdatedAtUtc);
}
