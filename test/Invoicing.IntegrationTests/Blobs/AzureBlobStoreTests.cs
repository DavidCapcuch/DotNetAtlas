using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Invoicing.IntegrationTests.Blobs;

/// <summary>
/// Integration tests for <c>AzureBlobStore</c> against a real Azurite container.
/// Covers ADR-0017's core contract: upload produces a content-addressed <c>PdfBlobRef</c>,
/// SAS URL grants time-bounded GET, re-uploading the same blob is a no-op on hash.
/// </summary>
[Collection(nameof(AzuriteCollection))]
public sealed class AzureBlobStoreTests(AzuriteFixture fixture)
{
    private const string ContentType = "application/pdf";
    private const string BlobName = "2026/04/INV-2026-000142.pdf";

    private static readonly byte[] SamplePdf = Encoding.UTF8.GetBytes(
        "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n1 0 obj<</Type/Catalog>>endobj\ntrailer<<>>\n%%EOF\n");

    [Fact]
    public async Task UploadAsync_ReturnsPdfBlobRefWithSha256HexAndExpectedSize()
    {
        var ct = TestContext.Current.CancellationToken;

        var pdfRef = await fixture.BlobStore.UploadAsync(
            containerName: fixture.ContainerName,
            blobName: BlobName,
            content: SamplePdf,
            contentType: ContentType,
            metadata: null,
            ct: ct);

        pdfRef.ContentHash.Should().Be(ComputeSha256Hex(SamplePdf));
        pdfRef.SizeBytes.Should().Be(SamplePdf.Length);
        var sasUri = await fixture.BlobStore.GetSasUrlAsync(fixture.ContainerName, pdfRef.BlobName, TimeSpan.FromMinutes(5), ct);
        sasUri.IsAbsoluteUri.Should().BeTrue();
        sasUri.Query.Should().Contain("sig=");
    }

    [Fact]
    public async Task UploadAsync_SasUriIsGettable_AndReturnsIdenticalBytes()
    {
        var ct = TestContext.Current.CancellationToken;

        var pdfRef = await fixture.BlobStore.UploadAsync(
            fixture.ContainerName,
            "2026/04/INV-2026-000143.pdf",
            SamplePdf,
            ContentType,
            metadata: null,
            ct: ct);

        var sasUri = await fixture.BlobStore.GetSasUrlAsync(fixture.ContainerName, pdfRef.BlobName, TimeSpan.FromMinutes(5), ct);
        using var response = await fixture.Http.GetAsync(sasUri, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var returned = await response.Content.ReadAsByteArrayAsync(ct);
        returned.Should().Equal(SamplePdf);
    }

    [Fact]
    public async Task UploadAsync_PreservesContentDispositionForBrowserDownload()
    {
        var ct = TestContext.Current.CancellationToken;
        const string blobName = "2026/04/INV-2026-000144.pdf";

        var pdfRef = await fixture.BlobStore.UploadAsync(
            fixture.ContainerName,
            blobName,
            SamplePdf,
            ContentType,
            metadata: null,
            ct: ct);

        var sasUri = await fixture.BlobStore.GetSasUrlAsync(fixture.ContainerName, pdfRef.BlobName, TimeSpan.FromMinutes(5), ct);
        using var response = await fixture.Http.GetAsync(sasUri, ct);
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        // HttpHeaders parses quoted-string and exposes the inner value (RFC 6266).
        response.Content.Headers.ContentDisposition.FileName.Should().Be("INV-2026-000144.pdf");
    }

    [Fact]
    public async Task GetSasUrlAsync_GrantsReadAccessToAlreadyUploadedBlob()
    {
        var ct = TestContext.Current.CancellationToken;
        const string blobName = "2026/04/INV-2026-000145.pdf";

        await fixture.BlobStore.UploadAsync(
            fixture.ContainerName,
            blobName,
            SamplePdf,
            ContentType,
            metadata: null,
            ct: ct);

        var freshUri = await fixture.BlobStore.GetSasUrlAsync(
            fixture.ContainerName,
            blobName,
            TimeSpan.FromMinutes(5),
            ct);

        using var response = await fixture.Http.GetAsync(freshUri, ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DownloadAsync_StreamsOriginalBytes()
    {
        var ct = TestContext.Current.CancellationToken;
        const string blobName = "2026/04/INV-2026-000146.pdf";

        await fixture.BlobStore.UploadAsync(
            fixture.ContainerName,
            blobName,
            SamplePdf,
            ContentType,
            metadata: null,
            ct: ct);

        await using var stream = await fixture.BlobStore.DownloadAsync(fixture.ContainerName, blobName, ct);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);

        ms.ToArray().Should().Equal(SamplePdf);
    }

    [Fact]
    public async Task UploadAsync_IdenticalContent_ProducesIdenticalHash()
    {
        var ct = TestContext.Current.CancellationToken;

        var first = await fixture.BlobStore.UploadAsync(
            fixture.ContainerName,
            "2026/04/INV-2026-000147.pdf",
            SamplePdf,
            ContentType,
            metadata: null,
            ct: ct);

        var second = await fixture.BlobStore.UploadAsync(
            fixture.ContainerName,
            "2026/04/INV-2026-000148.pdf",
            SamplePdf,
            ContentType,
            metadata: null,
            ct: ct);

        second.ContentHash.Should().Be(first.ContentHash);
    }

    [Fact]
    public async Task UploadAsync_EmptyContent_ThrowsArgumentException()
    {
        var ct = TestContext.Current.CancellationToken;

        var act = async () => await fixture.BlobStore.UploadAsync(
            fixture.ContainerName,
            "empty.pdf",
            ReadOnlyMemory<byte>.Empty,
            ContentType,
            metadata: null,
            ct: ct);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetSasUrlAsync_DerivesSeFromInjectedTimeProvider_NotWallClock()
    {
        // Pin against ADR-0015: SAS expiry is sourced from the injected TimeProvider so
        // FakeTimeProvider-driven handler-side metadata (`sasExpiresAtUtc`) stays in lock-step
        // with the signed `se` parameter. Before the H1 fix this asserted against
        // DateTimeOffset.UtcNow and drifted from the FakeTimeProvider any test could read.
        var ct = TestContext.Current.CancellationToken;
        const string blobName = "2026/04/INV-2026-000149.pdf";
        var fixedNow = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(fixedNow);
        var expiry = TimeSpan.FromMinutes(10);
        var expectedSe = fixedNow.Add(expiry).UtcDateTime;

        // Upload via the system-clock store so the blob exists, then mint the SAS via the
        // FakeTimeProvider-bound store. GetSasUrlAsync does not call the storage service, so
        // running the two against the same Azurite container is safe.
        await fixture.BlobStore.UploadAsync(
            fixture.ContainerName,
            blobName,
            SamplePdf,
            ContentType,
            metadata: null,
            ct: ct);

        var fakeClockStore = fixture.CreateBlobStoreWithClock(clock);
        var sasUri = await fakeClockStore.GetSasUrlAsync(
            fixture.ContainerName,
            blobName,
            expiry,
            ct);

        var seValue = HttpUtility.ParseQueryString(sasUri.Query)["se"];
        seValue.Should().NotBeNull(
            "Azure SAS URIs always carry an `se` (signed-expiry) parameter — its absence indicates the URI is not a SAS-signed URL");
        var seParsed = DateTimeOffset.Parse(seValue!, CultureInfo.InvariantCulture);
        seParsed.UtcDateTime.Should().Be(expectedSe,
            "the BlobSasBuilder must use _timeProvider.GetUtcNow() rather than wall-clock UtcNow");
    }

    private static string ComputeSha256Hex(byte[] data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));
}
