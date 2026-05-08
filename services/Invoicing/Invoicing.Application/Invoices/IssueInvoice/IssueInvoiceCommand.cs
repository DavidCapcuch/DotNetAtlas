using Platform.CQRS;

namespace Invoicing.Application.Invoices.IssueInvoice;

/// <summary>
/// Internal command issued by the M6 enrichment-projection consumers when both halves
/// (<c>OrderConfirmedEvent</c> + <c>PaymentCapturedEvent</c>) for a given
/// <see cref="CorrelationId"/> have been observed and the row in <c>pending_invoices</c>
/// has just transitioned to <c>CompletedAtUtc</c>. Idempotent on
/// <see cref="CorrelationId"/>: if the projection row already carries a non-null
/// <c>IssuedInvoiceId</c>, the handler short-circuits.
/// </summary>
/// <remarks>
/// The single-field shape is deliberate — convergence is keyed on the projection row that
/// the handler loads, so passing the full payloads through the command would just
/// re-serialise data that's already on disk. See <c>IssueInvoiceCommandHandler</c> for the
/// load-allocate-render-upload-persist sequence.
/// </remarks>
public sealed record IssueInvoiceCommand : ICommand<Guid>
{
    /// <summary>
    /// Saga / cross-BC correlation id; primary key into <c>pending_invoices</c> and
    /// idempotency key for this command.
    /// </summary>
    public required Guid CorrelationId { get; init; }
}
