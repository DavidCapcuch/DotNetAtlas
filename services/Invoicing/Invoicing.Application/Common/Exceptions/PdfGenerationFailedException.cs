using Platform.SharedKernel.Exceptions;

namespace Invoicing.Application.Common.Exceptions;

/// <summary>
/// Bug-class exception — wraps a QuestPDF library failure (typically
/// <c>QuestPDF.Drawing.Exceptions.DocumentLayoutException</c>) raised while
/// rendering an invoice or credit-note PDF. Thrown by the
/// <c>QuestPdfInvoiceGenerator</c> adapter; surfaces through the consumer
/// middleware to the DLT.
/// </summary>
/// <remarks>
/// <para>
/// Inherits <see cref="DataIntegrityException"/> so the consumer middleware's
/// existing <c>catch (CriticalException)</c> branch DLTs it without change and
/// downstream logging gets a typed <see cref="Detail"/> field instead of having
/// to parse <see cref="Exception.Message"/>. The original QuestPDF exception is
/// preserved as <see cref="Exception.InnerException"/> for diagnostics.
/// </para>
/// <para>
/// The adapter narrows its catch to QuestPDF-namespace exceptions only — any
/// other exception (OOM, cancellation, etc.) propagates raw.
/// </para>
/// </remarks>
public sealed class PdfGenerationFailedException(string detail, Exception innerException)
    : DataIntegrityException(
        "Invoicing.PdfGenerationFailed",
        $"PDF generation failed: {detail}",
        innerException)
{
    public string Detail { get; } = detail;
}
