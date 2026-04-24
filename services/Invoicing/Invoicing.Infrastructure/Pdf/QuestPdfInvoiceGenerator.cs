using System.Security.Cryptography;
using Invoicing.Application.Pdf;
using Invoicing.Domain.CreditNotes;
using Invoicing.Domain.Invoices;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Invoicing.Infrastructure.Pdf;

/// <summary>
/// QuestPDF-backed <see cref="IPdfGenerator"/> per ADR-0019. Delegates layout to
/// <see cref="InvoiceDocument"/> / <see cref="CreditNoteDocument"/> and wraps the result
/// with the SHA-256 content hash (lowercase hex) that <c>PdfBlobRef</c> ingests after upload.
/// </summary>
/// <remarks>
/// Stateless + thread-safe: the generator holds only the options snapshot, and QuestPDF's
/// document composition is pure-functional. Registered as a singleton — see
/// <c>InfrastructureDependencyInjection.AddPdfGeneration</c>.
/// </remarks>
internal sealed class QuestPdfInvoiceGenerator : IPdfGenerator
{
    private const string PdfContentType = "application/pdf";

    private readonly PdfGenerationOptions _options;

    static QuestPdfInvoiceGenerator()
    {
        // QuestPDF refuses to render without an explicit license selection. Community is
        // MIT-licensed below the revenue threshold (ADR-0019 § License posture). The
        // assignment is idempotent and safe under the CLR's single-threaded static-ctor
        // guarantee — multiple generator instances setting it produce the same outcome.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public QuestPdfInvoiceGenerator(IOptions<PdfGenerationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public Task<PdfGenerationResult> GenerateInvoiceAsync(Invoice invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ct.ThrowIfCancellationRequested();

        var document = new InvoiceDocument(invoice, _options);
        return Task.FromResult(Render(document));
    }

    public Task<PdfGenerationResult> GenerateCreditNoteAsync(CreditNote creditNote, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(creditNote);
        ct.ThrowIfCancellationRequested();

        var document = new CreditNoteDocument(creditNote, _options);
        return Task.FromResult(Render(document));
    }

    private static PdfGenerationResult Render(IDocument document)
    {
        // DocumentExtensions.GeneratePdf() is synchronous; IPdfGenerator stays async to keep
        // the seam open for a future HTML-to-PDF adapter (ADR-0019 § Considered Options 4).
        var bytes = document.GeneratePdf();
        var hash = ComputeSha256Hex(bytes);
        return new PdfGenerationResult(bytes, hash, bytes.LongLength, PdfContentType);
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hashBuffer = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(data, hashBuffer);
        return Convert.ToHexStringLower(hashBuffer);
    }
}
