using Platform.CQRS;

namespace Invoicing.Application.CreditNotes.IssueCreditNote;

/// <summary>
/// Internal command issued by the M6 enrichment-projection consumers when both halves
/// (<c>OrderCancelledEvent</c> + <c>PaymentRefundedEvent</c>) for a given
/// <see cref="CorrelationId"/> have been observed and the row in <c>pending_credit_notes</c>
/// has just transitioned to <c>CompletedAtUtc</c>. Idempotent on
/// <see cref="CorrelationId"/>: if the projection row already carries a non-null
/// <c>IssuedCreditNoteId</c>, the handler short-circuits.
/// </summary>
public sealed record IssueCreditNoteCommand : ICommand<Guid>
{
    /// <summary>
    /// Cancellation flow correlation id; primary key into <c>pending_credit_notes</c> and
    /// idempotency key for this command. Identical to the cancelled order's
    /// <c>OrderConfirmedEvent.CorrelationId</c> — it threads the saga end to end.
    /// </summary>
    public required Guid CorrelationId { get; init; }
}
