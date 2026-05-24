using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// Content-addressed reference to a stored PDF artifact per ADR-0017.
/// <para>
/// Immutable once set on the aggregate (I-4) — PDFs are write-once.
/// <see cref="BlobName"/> is the canonical immutable identifier of the blob
/// (e.g., <c>"2026/05/INV-2026-000142.pdf"</c>). Callers compute fresh SAS URLs
/// on demand via <c>IBlobStore.GetSasUrlAsync</c>; the aggregate never persists
/// a bearer credential (issue #131).
/// </para>
/// </summary>
public sealed record PdfBlobRef : ValueObject
{
    public const int ContentHashLength = 64;
    public const int BlobNameMaxLength = 1024;
    private const string PdfExtension = ".pdf";

    public string BlobName { get; private init; } = null!;
    public string ContentHash { get; private init; } = null!;
    public long SizeBytes { get; private init; }

    private PdfBlobRef()
    {
    }

    public static Result<PdfBlobRef> Create(string blobName, string contentHash, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(blobName) || blobName.Length > BlobNameMaxLength)
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(BlobName),
                $"BlobName must be a non-empty path (max {BlobNameMaxLength} chars).",
                "Invoicing.InvalidBlobName"));
        }

        if (blobName.StartsWith('/') || blobName.StartsWith('\\'))
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(BlobName), "BlobName must be a relative path (no leading slash).", "Invoicing.InvalidBlobName"));
        }

        if (!blobName.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(BlobName), "BlobName must end with '.pdf'.", "Invoicing.InvalidBlobName"));
        }

        if (string.IsNullOrWhiteSpace(contentHash) || contentHash.Length != ContentHashLength)
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(ContentHash),
                $"ContentHash must be {ContentHashLength} hex chars (SHA-256).",
                "Invoicing.InvalidContentHash"));
        }

        foreach (var ch in contentHash)
        {
            if (!IsLowerHex(ch))
            {
                return Result.Fail<PdfBlobRef>(new ValidationError(
                    nameof(ContentHash), "ContentHash must be lowercase hex.", "Invoicing.InvalidContentHash"));
            }
        }

        if (sizeBytes <= 0)
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(SizeBytes), "SizeBytes must be strictly positive.", "Invoicing.InvalidBlobSize"));
        }

        return Result.Ok(new PdfBlobRef
        {
            BlobName = blobName,
            ContentHash = contentHash,
            SizeBytes = sizeBytes,
        });
    }

    private static bool IsLowerHex(char ch) =>
        (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
}
