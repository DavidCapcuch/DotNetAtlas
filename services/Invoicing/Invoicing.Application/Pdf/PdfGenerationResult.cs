namespace Invoicing.Application.Pdf;

/// <summary>
/// Result of a PDF generation call — raw bytes plus the integrity metadata used to stamp
/// <c>PdfBlobRef</c> on the aggregate after upload (ADR-0017 + ADR-0019 § IPdfGenerator abstraction).
/// </summary>
/// <param name="Content">Rendered PDF bytes (non-empty, starts with <c>%PDF-</c>).</param>
/// <param name="ContentHash">SHA-256 digest of <paramref name="Content"/>, encoded as 64 lowercase hex chars.
/// Matches <see cref="Domain.Common.ValueObjects.PdfBlobRef.ContentHashLength"/>.</param>
/// <param name="SizeBytes">Length of <paramref name="Content"/> in bytes (strictly positive).</param>
/// <param name="ContentType">MIME type — always <c>application/pdf</c> for this generator.</param>
public readonly record struct PdfGenerationResult(
    byte[] Content,
    string ContentHash,
    long SizeBytes,
    string ContentType);
