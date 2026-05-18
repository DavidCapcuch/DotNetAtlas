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

    /// <summary>
    /// Authorization id sourced from the Avro wire command. The handler asserts this equals
    /// the stored <c>GatewayTransactionId</c> before contacting the gateway, catching saga
    /// bugs / stale-token replays that would otherwise call the PSP with the wrong token
    /// (H-8 closeout follow-up).
    /// </summary>
    public required string AuthorizationId { get; init; }
}
