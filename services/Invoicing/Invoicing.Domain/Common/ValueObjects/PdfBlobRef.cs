using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

namespace Invoicing.Domain.Common.ValueObjects;

/// <summary>
/// Content-addressed reference to a stored PDF artifact per ADR-0017.
/// <para>
/// Immutable once set on the aggregate (I-4) — PDFs are write-once.
/// <see cref="BlobUri"/> is the presigned (SAS) GET URL the aggregate hands back to consumers;
/// <see cref="ContentHash"/> is the SHA-256 hex digest used for integrity verification.
/// </para>
/// </summary>
/// <param name="BlobUri">Presigned SAS URL with limited TTL.</param>
/// <param name="ContentHash">SHA-256 hex digest (64 lowercase hex chars).</param>
/// <param name="SizeBytes">Total PDF size in bytes (strictly positive).</param>
public sealed record PdfBlobRef(Uri BlobUri, string ContentHash, long SizeBytes) : ValueObject
{
    public const int ContentHashLength = 64;

    public static Result<PdfBlobRef> Create(Uri blobUri, string contentHash, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(blobUri);

        if (!blobUri.IsAbsoluteUri)
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(BlobUri), "Blob URI must be absolute.", "Invoicing.InvalidBlobUri"));
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
                    nameof(ContentHash),
                    "ContentHash must be lowercase hex.",
                    "Invoicing.InvalidContentHash"));
            }
        }

        if (sizeBytes <= 0)
        {
            return Result.Fail<PdfBlobRef>(new ValidationError(
                nameof(SizeBytes), "SizeBytes must be strictly positive.", "Invoicing.InvalidBlobSize"));
        }

        return Result.Ok(new PdfBlobRef(blobUri, contentHash, sizeBytes));
    }

    private static bool IsLowerHex(char ch) =>
        (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f');
}
