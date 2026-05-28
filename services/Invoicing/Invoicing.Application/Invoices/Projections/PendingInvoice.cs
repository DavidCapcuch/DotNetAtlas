namespace Invoicing.Application.Invoices.Projections;

/// <summary>
/// Async-multi-source enrichment row: buffers <c>OrderConfirmedEvent</c> and
/// <c>PaymentCapturedEvent</c> for the same <c>CorrelationId</c> until both halves
/// arrive, at which point <c>IssueInvoiceCommandHandler</c> reads the row
/// (keyed on <see cref="CorrelationId"/>) and constructs the <c>Invoice</c>
/// aggregate. Schema mirrors <c>docs/bc-design/invoicing.md § 8.1</c>.
/// </summary>
/// <remarks>
/// <para>
/// Convergence is signalled by <see cref="CompletedAtUtc"/> being non-null AND
/// <see cref="IssuedInvoiceId"/> being null — i.e. both event payloads are
/// captured but issuance has not yet run. The consumer that observes the second
/// half writes <see cref="CompletedAtUtc"/> and raises an
/// <c>InvoiceIssuanceReadyDomainEvent</c> from that same code path so the
/// command handler subscribes once and runs once.
/// </para>
/// <para>
/// This is a projection POCO, NOT an aggregate — it has no domain events,
/// invariants, or factory. The two enrichment consumers in
/// <c>Invoicing.Infrastructure.Messaging.Kafka.Projections</c> own the row's
/// state machine via read-modify-write under the inbox-middleware transaction.
/// </para>
/// </remarks>
public sealed class PendingInvoice
{
    /// <summary>Primary key. Derived from the inbound Avro event's <c>CorrelationId</c> field (Ordering / Payments both carry it).</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Set once <c>OrderConfirmedEvent</c> has been observed; null until then.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>Set once <c>PaymentCapturedEvent</c> has been observed; null until then.</summary>
    public Guid? PaymentId { get; set; }

    /// <summary>Buyer extracted from <c>OrderConfirmedEvent.BuyerId</c>; null until the order half arrives. The allocator partitions by buyer and the issued event keys on this.</summary>
    public Guid? BuyerId { get; set; }

    /// <summary>Full <c>OrderConfirmedEvent</c> serialised to JSON; null until the order half arrives.</summary>
    /// <remarks>PII per ADR-0011 — do not log. The aggregate is hydrated from this column; logging the raw envelope would leak the buyer name + (future) billing address.</remarks>
    public string? OrderPayload { get; set; }

    /// <summary>Full <c>PaymentCapturedEvent</c> serialised to JSON; null until the payment half arrives.</summary>
    /// <remarks>PII per ADR-0011 — do not log. Capture amounts are non-PII but the envelope is treated uniformly with <see cref="OrderPayload"/> to keep the convention single-rule.</remarks>
    public string? PaymentPayload { get; set; }

    /// <summary>Wall-clock at first observation (whichever half arrived first). Never overwritten on subsequent updates — the row's birth time, not its last-touch time.</summary>
    public DateTimeOffset FirstSeenAtUtc { get; set; }

    /// <summary>Set when both halves are present. Stays null on duplicate inbound events for an already-converged row (no-op semantics).</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Set by <c>IssueInvoiceCommandHandler</c> after a successful issuance, atomically with the <c>Invoice</c> aggregate insert. The enrichment consumers never write here.</summary>
    public Guid? IssuedInvoiceId { get; set; }
}
