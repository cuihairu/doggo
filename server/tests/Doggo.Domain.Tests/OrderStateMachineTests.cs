using Doggo.Domain.Orders;
using Xunit;

namespace Doggo.Domain.Tests;

public class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.PendingSettlement)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.PendingSettlement, OrderStatus.PendingRetrieval)]
    [InlineData(OrderStatus.PendingSettlement, OrderStatus.Refunded)]
    [InlineData(OrderStatus.PendingRetrieval, OrderStatus.Retrieving)]
    [InlineData(OrderStatus.PendingRetrieval, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Retrieving, OrderStatus.Completed)]
    [InlineData(OrderStatus.Retrieving, OrderStatus.PendingRetrieval)]
    [InlineData(OrderStatus.Retrieving, OrderStatus.Refunded)]
    public void CanTransition_allows_documented_moves(OrderStatus from, OrderStatus to) =>
        Assert.True(OrderStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Completed)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Retrieving)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.Refunded)]
    [InlineData(OrderStatus.PendingSettlement, OrderStatus.Retrieving)]
    [InlineData(OrderStatus.PendingSettlement, OrderStatus.Completed)]
    [InlineData(OrderStatus.PendingRetrieval, OrderStatus.Completed)]
    [InlineData(OrderStatus.Completed, OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.Completed, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.PendingSettlement)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.Refunded, OrderStatus.PendingRetrieval)]
    [InlineData(OrderStatus.PendingPayment, OrderStatus.PendingPayment)]
    public void CanTransition_rejects_everything_else(OrderStatus from, OrderStatus to) =>
        Assert.False(OrderStateMachine.CanTransition(from, to));

    [Fact]
    public void RequireTransition_throws_for_illegal_moves() =>
        Assert.Throws<InvalidOperationException>(() => OrderStateMachine.RequireTransition(OrderStatus.Completed, OrderStatus.PendingPayment));

    [Fact]
    public void RequireTransition_allows_legal_moves_without_throwing() =>
        OrderStateMachine.RequireTransition(OrderStatus.Retrieving, OrderStatus.Completed);

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public void IsTerminal_is_true_for_end_states(OrderStatus status) => Assert.True(OrderStateMachine.IsTerminal(status));

    [Theory]
    [InlineData(OrderStatus.PendingPayment)]
    [InlineData(OrderStatus.PendingSettlement)]
    [InlineData(OrderStatus.PendingRetrieval)]
    [InlineData(OrderStatus.Retrieving)]
    public void IsTerminal_is_false_for_working_states(OrderStatus status) => Assert.False(OrderStateMachine.IsTerminal(status));
}
