using Platform.CQRS;

namespace Invoicing.Application.CreditNotes.IssueCreditNote;

/// <summary>
/// Internal command issued by the enrichment-projection consumers when both halves
/// (<c>OrderCancelledEvent</c> + <c>PaymentRefundedEvent</c>) for a given
/// <see cref="OrderId"/> have been observed and the row in <c>pending_credit_notes</c>
/// has just transitioned to <c>CompletedAtUtc</c>. Idempotent on
/// <see cref="OrderId"/>: if the projection row already carries a non-null
/// <c>IssuedCreditNoteId</c>, the handler short-circuits.
/// </summary>
public sealed record IssueCreditNoteCommand : ICommand<Guid>
{
    /// <summary>
    /// Order id; primary key into <c>pending_credit_notes</c> and idempotency key for this
    /// command. Post-ADR-0029 it is also the cross-BC convergence key threading the
    /// cancellation flow end to end.
    /// </summary>
    public required Guid OrderId { get; init; }
}
