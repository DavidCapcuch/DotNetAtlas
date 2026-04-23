using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderDelivered;

/// <summary>
/// Admin-triggered terminal-happy transition. Transitions the order to
/// <c>OrderStatus.Delivered</c> and emits the external
/// <c>OrderDeliveredEvent</c>. v2 may replace this with a carrier-webhook
/// adapter (ordering.md Appendix B.6).
/// </summary>
public sealed class MarkOrderDeliveredCommand : ICommand
{
    public required Guid OrderId { get; init; }
}
