using Invoicing.Domain.CreditNotes.ValueObjects;
using Invoicing.Domain.Invoices.ValueObjects;

namespace Invoicing.Application.Common.Blobs;

/// <summary>
/// Canonical blob-name layout for invoice / credit-note PDFs in the shared <c>invoices</c>
/// container, per <c>docs/bc-design/invoicing.md</c> § 10. Centralising the format here lets
/// read-side query handlers mint fresh SAS URLs for the same blobs the command handlers
/// uploaded — without coupling either side to the other's private helper.
/// </summary>
/// <remarks>
/// <para>
/// The month dimension is fixed at <c>01</c> for v1 (the spec's <c>YYYY/MM/</c> prefix is a
/// catalogue partition reserved for v2 sharding; not derived from <see cref="DateTimeOffset"/>).
/// Single source of truth — <c>IssueInvoiceCommandHandler</c> + <c>IssueCreditNoteCommandHandler</c>
/// both call <see cref="For(InvoiceNumber)"/> / <see cref="For(CreditNoteNumber)"/> directly; v2's
/// partition story will land here.
/// </para>
/// </remarks>
internal static class InvoicePdfBlobName
{
    /// <summary>
    /// Builds the blob name for an invoice PDF: <c>{YYYY}/01/{InvoiceNumber}.pdf</c>.
    /// </summary>
    public static string For(InvoiceNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);
        return FormattableString.Invariant($"{number.Year:D4}/01/{number.Value}.pdf");
    }

    /// <summary>
    /// Builds the blob name for a credit-note PDF:
    /// <c>credit-notes/{YYYY}/01/{CreditNoteNumber}.pdf</c>. Credit notes share the
    /// <c>invoices</c> container with the matching 10-year immutable retention policy;
    /// a key prefix separates them.
    /// </summary>
    public static string For(CreditNoteNumber number)
    {
        ArgumentNullException.ThrowIfNull(number);
        return FormattableString.Invariant($"credit-notes/{number.Year:D4}/01/{number.Value}.pdf");
    }
}
