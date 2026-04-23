using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderFailed;

/// <summary>
/// Saga-issued command on compensation or timeout. Transitions the
/// <c>Order</c> to terminal <c>OrderStatus.Failed</c> and emits the
/// external <c>OrderFailedEvent</c>.
/// </summary>
public sealed class MarkOrderFailedCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public required string ErrorCode { get; init; }

    public required string ErrorMessage { get; init; }
}
