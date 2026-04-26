using Platform.CQRS;

namespace Payments.Application.Transactions.RequestRefund;

/// <summary>
/// Internal CQRS command for the <c>Captured/Completed → Refunded</c> compensation path. The
/// M5 Kafka consumer derives <see cref="PaymentId"/> from the wire-shape Avro
/// <c>RequestRefundCommand.PaymentTransactionId</c>.
/// </summary>
public sealed record RequestRefundCommand : ICommand
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }

    public required string Reason { get; init; }
}
