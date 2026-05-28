using Platform.CQRS;

namespace Payments.Application.Transactions.VoidPayment;

/// <summary>
/// Internal CQRS command for the <c>Authorized → Voided</c> compensation path (saga
/// pre-capture compensation). The Kafka consumer derives <see cref="PaymentId"/> from the
/// saga <see cref="CorrelationId"/>.
/// </summary>
public sealed record VoidPaymentCommand : ICommand
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

    /// <summary>
    /// Saga-supplied reason for the void. Persisted on the aggregate (<c>void_reason</c>
    /// column) and surfaced on <c>PaymentVoidedEvent.Reason</c> for downstream audit
    /// (H-5 closeout follow-up).
    /// </summary>
    public required string Reason { get; init; }
}
