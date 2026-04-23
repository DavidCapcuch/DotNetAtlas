using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderShipped;

/// <summary>
/// Admin/warehouse-operator command. Transitions the order to
/// <c>OrderStatus.Shipped</c> and emits the external
/// <c>OrderShippedEvent</c>. Endpoint is M5 (admin-authenticated per
/// <c>AuthPolicies.OrderingAdmin</c>).
/// </summary>
public sealed class MarkOrderShippedCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public required string Carrier { get; init; }

    public required string TrackingNumber { get; init; }
}
