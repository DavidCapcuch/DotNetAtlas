using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderPaymentCompleted;

/// <summary>
/// Saga-issued command after Payments confirms capture. Transitions the
/// <c>Order</c> to <c>OrderStatus.PaymentCompleted</c>. Audit-only — no
/// external event (the saga already observed Payments' own
/// <c>PaymentCompletedEvent</c>; see ordering.md § 6 consumer table).
/// </summary>
public sealed record MarkOrderPaymentCompletedCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public required Guid PaymentTransactionId { get; init; }
}
