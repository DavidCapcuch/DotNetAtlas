using Platform.CQRS;

namespace Payments.Application.Transactions.VoidPayment;

/// <summary>
/// Internal CQRS command for the <c>Authorized → Voided</c> compensation path (saga pre-capture
/// compensation). The aggregate is resolved by <see cref="OrderId"/> (the saga business key,
/// ADR-0029) — the Void wire command carries no PaymentTransactionId, so the handler loads via the
/// unique <c>order_id</c> index.
/// </summary>
public sealed record VoidPaymentCommand : ICommand
{
    /// <summary>
    /// Order this payment belongs to — the saga key (ADR-0029). Sourced from the inbound
    /// Avro Void wire command's <c>OrderId</c> field.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Authorization id sourced from the Avro wire command. The handler asserts this equals
    /// the stored <c>GatewayTransactionId</c> before contacting the gateway, catching saga
    /// bugs / stale-token replays that would otherwise call the PSP with the wrong token
    /// (H-8).
    /// </summary>
    public required string AuthorizationId { get; init; }

    /// <summary>
    /// Saga-supplied reason for the void. Persisted on the aggregate (<c>void_reason</c>
    /// column) and surfaced on <c>PaymentVoidedEvent.Reason</c> for downstream audit
    /// (H-5).
    /// </summary>
    public required string Reason { get; init; }
}
