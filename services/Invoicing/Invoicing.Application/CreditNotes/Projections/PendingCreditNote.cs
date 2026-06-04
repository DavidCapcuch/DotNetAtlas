namespace Invoicing.Application.CreditNotes.Projections;

/// <summary>
/// Async-multi-source enrichment row: buffers <c>OrderCancelledEvent</c> and
/// <c>PaymentRefundedEvent</c> for the same <c>OrderId</c> until both
/// halves arrive, at which point <c>IssueCreditNoteCommandHandler</c>
/// reads the row (keyed on <see cref="OrderId"/>) and constructs the
/// <c>CreditNote</c> aggregate. Mirrors
/// <see cref="Invoices.Projections.PendingInvoice"/> per
/// <c>docs/bc-design/invoicing.md § 8.3</c>.
/// </summary>
public sealed class PendingCreditNote
{
    /// <summary>Primary key. The <c>OrderId</c> both Avro halves (OrderCancelledEvent / PaymentRefundedEvent) carry; post-ADR-0029 it is the cross-BC convergence key.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Set once <c>PaymentRefundedEvent</c> has been observed; null until then. Maps to <c>PaymentRefundedEvent.PaymentTransactionId</c> (the original captured payment, not the refund txn id — the credit note compensates the original capture).</summary>
    public Guid? PaymentId { get; set; }

    /// <summary>Buyer extracted from <c>OrderCancelledEvent.BuyerId</c>; null until the order-cancel half arrives.</summary>
    public Guid? BuyerId { get; set; }

    /// <summary>Full <c>OrderCancelledEvent</c> serialised to JSON; null until the order-cancel half arrives.</summary>
    /// <remarks>PII per ADR-0011 — do not log.</remarks>
    public string? OrderPayload { get; set; }

    /// <summary>Full <c>PaymentRefundedEvent</c> serialised to JSON; null until the refund half arrives.</summary>
    /// <remarks>PII per ADR-0011 — do not log.</remarks>
    public string? PaymentPayload { get; set; }

    /// <summary>Wall-clock at first observation. Never overwritten.</summary>
    public DateTimeOffset FirstSeenAtUtc { get; set; }

    /// <summary>Set when both halves are present.</summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>Set by <c>IssueCreditNoteCommandHandler</c> after issuance.</summary>
    public Guid? IssuedCreditNoteId { get; set; }
}
