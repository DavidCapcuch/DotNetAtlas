using Platform.CQRS;

namespace Payments.Application.Transactions.RequestRefund;

/// <summary>
/// Internal CQRS command for the <c>Captured/Completed → Refunded</c> compensation path. The
/// aggregate is resolved by its primary key — <see cref="PaymentId"/> is the saga-issued
/// <c>PaymentTransactionId</c> from the wire command, which a refund explicitly references.
/// </summary>
public sealed record RequestRefundCommand : ICommand
{
    /// <summary>
    /// Target payment transaction id (the aggregate primary key), taken from the wire
    /// <c>RequestRefundCommand.PaymentTransactionId</c>.
    /// </summary>
    public required Guid PaymentId { get; init; }

    public required string Reason { get; init; }
}
