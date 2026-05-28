using Invoicing.Domain.Common.ValueObjects;

namespace Invoicing.Application.Blobs;

/// <summary>
/// Abstraction over blob storage per ADR-0017. Production targets Azure Blob Storage;
/// local dev + integration tests run against Azurite. The implementation in
/// <c>Invoicing.Infrastructure.Blobs.AzureBlobStore</c> uses the <c>Azure.Storage.Blobs</c>
/// SDK directly (single infrastructure consumer; promoted to <c>Platform.BlobStorage.*</c>
/// only when a second BC needs it per ADR-0017 \u00a7 Implementation Notes).
/// </summary>
/// <remarks>
/// Architecture rule (ADR-0017 \u00a7 IBlobStore abstraction): Application and Domain layers
/// must go through this interface and never reference <c>Azure.Storage.Blobs</c> directly,
/// enforced by an architecture test. The interface lives in the Application layer so
/// command handlers can inject it without depending on Infrastructure-namespace types.
/// </remarks>
public interface IBlobStore
{
    /// <summary>
    /// Uploads <paramref name="content"/> to the given container under
    /// <paramref name="blobName"/>. Computes SHA-256 of the content as the integrity
    /// digest, uploads the bytes, and returns a <see cref="PdfBlobRef"/> keyed on the
    /// canonical immutable <c>BlobName</c>. SAS URLs are minted on demand via
    /// <see cref="GetSasUrlAsync"/>; no presigned URL is returned from upload (issue #131).
    /// </summary>
    /// <param name="containerName">Azure Blob container (e.g., <c>invoices</c>).</param>
    /// <param name="blobName">Relative blob path within the container (e.g., <c>2026/04/INV-2026-000142.pdf</c>).</param>
    /// <param name="content">PDF bytes (or any binary payload).</param>
    /// <param name="contentType">MIME type (e.g., <c>application/pdf</c>).</param>
    /// <param name="metadata">Optional custom blob metadata key/value pairs.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PdfBlobRef> UploadAsync(
        string containerName,
        string blobName,
        ReadOnlyMemory<byte> content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken ct);

    /// <summary>
    /// Produces a presigned read URL for an existing blob (without re-uploading).
    /// Used by query handlers to return a fresh SAS URL whenever a buyer re-fetches
    /// their invoice metadata.
    /// </summary>
    Task<Uri> GetSasUrlAsync(
        string containerName,
        string blobName,
        TimeSpan expiry,
        CancellationToken ct);

    /// <summary>
    /// Streams the blob content. Primarily used by the byte-deterministic PDF test
    /// to hash-compare two regenerations.
    /// </summary>
    Task<Stream> DownloadAsync(
        string containerName,
        string blobName,
        CancellationToken ct);
}
