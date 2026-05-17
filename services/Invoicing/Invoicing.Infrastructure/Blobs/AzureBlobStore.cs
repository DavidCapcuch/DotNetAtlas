using System.Globalization;
using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Invoicing.Application.Blobs;
using Invoicing.Domain.Common.ValueObjects;
using Microsoft.Extensions.Options;
using Platform.SharedKernel.Exceptions;

namespace Invoicing.Infrastructure.Blobs;

/// <summary>
/// <c>Azure.Storage.Blobs</c>-backed implementation of <see cref="IBlobStore"/>.
/// Runs unchanged against Azurite (local) and real Azure Blob (production) per ADR-0017.
/// SDK-level retries with exponential backoff are used instead of an application-level
/// Polly pipeline (ADR-0017 \u00a7 design_open \u2014 cross-service HTTP resilience is handled by
/// YARP at the edge; SDK retries cover the storage-client path).
/// </summary>
internal sealed class AzureBlobStore : IBlobStore
{
    private readonly BlobServiceClient _serviceClient;
    private readonly BlobStorageOptions _options;
    private readonly TimeProvider _timeProvider;

    public AzureBlobStore(
        BlobServiceClient serviceClient,
        IOptions<BlobStorageOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _serviceClient = serviceClient;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<PdfBlobRef> UploadAsync(
        string containerName,
        string blobName,
        ReadOnlyMemory<byte> content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata,
        TimeSpan sasTtl,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (content.IsEmpty)
        {
            throw new ArgumentException("Blob content must not be empty.", nameof(content));
        }

        if (sasTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(sasTtl), "SAS TTL must be strictly positive.");
        }

        var container = _serviceClient.GetBlobContainerClient(containerName);
        var blob = container.GetBlobClient(blobName);

        var contentHash = ComputeSha256Hex(content.Span);

        using var stream = new MemoryStream(content.Length);
        await stream.WriteAsync(content, ct).ConfigureAwait(false);
        stream.Position = 0;

        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType,
                ContentDisposition = BuildContentDisposition(blobName),
            },
            Metadata = metadata?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        };

        try
        {
            await blob.UploadAsync(stream, uploadOptions, ct).ConfigureAwait(false);
        }
        catch (Azure.RequestFailedException ex)
        {
            throw new CriticalInfrastructureException(
                "Invoicing.BlobUploadFailed",
                $"Blob upload failed for '{containerName}/{blobName}'.",
                ex);
        }

        var sasUri = BuildSasUri(blob, sasTtl, blobName);
        var refResult = PdfBlobRef.Create(sasUri, contentHash, content.Length);

        // PdfBlobRef.Create validates URI shape + hash + size \u2014 a failure here would indicate
        // a bug in this adapter (e.g. a non-absolute SAS URI slipped through). Bug-class.
        if (refResult.IsFailed)
        {
            throw new DataIntegrityException(
                "Invoicing.InvalidBlobRefAfterUpload",
                $"Computed PdfBlobRef failed validation after a successful upload: "
                + string.Join("; ", refResult.Errors.Select(e => e.Message)));
        }

        return refResult.Value;
    }

    public Task<Uri> GetSasUrlAsync(string containerName, string blobName, TimeSpan expiry, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiry), "Expiry must be strictly positive.");
        }

        var blob = _serviceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        var uri = BuildSasUri(blob, expiry, blobName);
        _ = ct; // No async work in this path (Azure SDK generates SAS client-side).
        return Task.FromResult(uri);
    }

    public async Task<Stream> DownloadAsync(string containerName, string blobName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        var blob = _serviceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);
        var response = await blob.DownloadStreamingAsync(cancellationToken: ct).ConfigureAwait(false);
        return response.Value.Content;
    }

    private Uri BuildSasUri(BlobClient blob, TimeSpan expiry, string blobName)
    {
        if (!blob.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "BlobServiceClient is not configured with account credentials; cannot generate SAS URIs. "
                + "Ensure BlobStorageOptions.ConnectionString includes the account key or the BlobServiceClient "
                + "is constructed with TokenCredential + delegation-key for managed-identity mode.");
        }

        // ADR-0015: SAS expiry is derived from the injected TimeProvider so FakeTimeProvider-driven
        // tests can pin the `se` window deterministically against the handler-side `sasExpiresAtUtc`
        // metadata (otherwise the two clocks can drift on the same machine).
        var builder = new BlobSasBuilder(BlobSasPermissions.Read, _timeProvider.GetUtcNow().Add(expiry))
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            ContentDisposition = BuildContentDisposition(blobName),
            Resource = "b",
        };

        var sasUri = blob.GenerateSasUri(builder);

        // Optional CDN rewrite (nginx-cdn locally / Front Door in prod) \u2014 preserve path + query,
        // replace scheme + authority.
        if (_options.PublicBaseUri is null)
        {
            return sasUri;
        }

        var rebuilt = new UriBuilder(_options.PublicBaseUri)
        {
            Path = sasUri.AbsolutePath,
            Query = sasUri.Query.TrimStart('?'),
        };
        return rebuilt.Uri;
    }

    private static string BuildContentDisposition(string blobName)
    {
        var fileName = Path.GetFileName(blobName);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"attachment; filename=\"{fileName}\"");
    }

    private static string ComputeSha256Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hashBuffer = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(data, hashBuffer);
        return Convert.ToHexStringLower(hashBuffer);
    }
}

