using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.Invoices;

namespace Invoicing.Application.Pdf;

/// <summary>
/// Library-neutral PDF generation seam per [ADR-0019 § IPdfGenerator abstraction](../../../../docs/adr/0019-pdf-generation-questpdf.md).
/// The Application layer declares this interface so command handlers can produce PDFs
/// without referencing QuestPDF; the Infrastructure layer owns the QuestPDF-backed adapter
/// (<c>QuestPdfInvoiceGenerator</c>) and all QuestPDF API usage.
/// </summary>
/// <remarks>
/// Architecture rule (ADR-0019 § Implementation Notes): direct imports of <c>QuestPDF.*</c>
/// must not appear in the Application or Domain layers, enforced by an architecture test.
/// <para>
/// Determinism contract: two invocations of <see cref="GenerateInvoiceAsync"/> (or
/// <see cref="GenerateCreditNoteAsync"/>) with the same aggregate state MUST produce a
/// byte-identical PDF and therefore the same <see cref="PdfGenerationResult.ContentHash"/>.
/// Adapters rely solely on aggregate state for any timestamp embedded in the PDF; no
/// <c>DateTime.UtcNow</c>.
/// </para>
/// </remarks>
public interface IPdfGenerator
{
    /// <summary>
    /// Renders a fiscal invoice PDF from the given <paramref name="invoice"/> aggregate.
    /// </summary>
    /// <param name="invoice">Issued or draft invoice aggregate. Template uses <see cref="Invoice.InvoiceNumber"/>,
    /// <see cref="Invoice.IssueDate"/>, <see cref="Invoice.BillingAddress"/>, <see cref="Invoice.Lines"/>,
    /// <see cref="Invoice.VatLines"/>, <see cref="Invoice.Subtotal"/>, and <see cref="Invoice.Total"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rendered PDF bytes + SHA-256 content hash + size + <c>application/pdf</c> content type.</returns>
    Task<PdfGenerationResult> GenerateInvoiceAsync(Invoice invoice, CancellationToken ct);

    /// <summary>
    /// Renders a credit-note PDF from the given <paramref name="creditNote"/> aggregate.
    /// </summary>
    /// <param name="creditNote">Issued credit-note aggregate. Template uses
    /// <see cref="CreditNote.CreditNoteNumber"/>, <see cref="CreditNote.OriginalInvoiceNumber"/>,
    /// <see cref="CreditNote.IssueDate"/>, <see cref="CreditNote.Lines"/>, <see cref="CreditNote.Total"/>,
    /// and <see cref="CreditNote.Reason"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Rendered PDF bytes + SHA-256 content hash + size + <c>application/pdf</c> content type.</returns>
    Task<PdfGenerationResult> GenerateCreditNoteAsync(CreditNote creditNote, CancellationToken ct);
}
