using Platform.CQRS;

namespace Payments.Application.Transactions.CapturePayment;

/// <summary>
/// Internal CQRS command driving the <c>Authorized → Captured → Completed</c> transition (v1
/// auto-completion per <c>payments.md § 4</c>). The aggregate is resolved by <see cref="OrderId"/>
/// (the saga business key, ADR-0029) — the Capture wire command carries no PaymentTransactionId,
/// so the handler loads via the unique <c>order_id</c> index (ADR-0030 retires the old
/// correlation-id lookup).
/// </summary>
public sealed record CapturePaymentCommand : ICommand
{
    /// <summary>
    /// Order this payment belongs to — the saga key (ADR-0029). Sourced from the Kafka correlation
    /// header (which equals the OrderId) until the dedicated correlation id is fully removed.
    /// </summary>
    public required Guid OrderId { get; init; }

    /// <summary>
    /// Authorization id sourced from the Avro wire command. The handler asserts this equals
    /// the stored <c>GatewayTransactionId</c> before contacting the gateway, catching saga
    /// bugs / stale-token replays that would otherwise call the PSP with the wrong token
    /// (H-8 closeout follow-up).
    /// </summary>
    public required string AuthorizationId { get; init; }
}
