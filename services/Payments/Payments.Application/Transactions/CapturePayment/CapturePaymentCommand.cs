using Platform.CQRS;

namespace Payments.Application.Transactions.CapturePayment;

/// <summary>
/// Internal CQRS command driving the <c>Authorized → Captured → Completed</c> transition (v1
/// auto-completion per <c>payments.md § 4</c>). The M5 Kafka consumer derives
/// <see cref="PaymentId"/> from the saga <see cref="CorrelationId"/>.
/// </summary>
public sealed record CapturePaymentCommand : ICommand
{
    public required Guid PaymentId { get; init; }

    public required Guid CorrelationId { get; init; }
}
