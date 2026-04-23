using Platform.CQRS;

namespace Ordering.Application.Orders.ConfirmOrder;

/// <summary>
/// Saga-issued command after stock reservation AND payment capture both
/// succeed. Transitions the <c>Order</c> to <c>OrderStatus.Confirmed</c>
/// and emits the external <c>OrderConfirmedEvent</c>.
/// </summary>
public sealed class ConfirmOrderCommand : ICommand
{
    public required Guid OrderId { get; init; }
}
