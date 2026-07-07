using Invoicing.Domain.Common.ValueObjects;
using Platform.SharedKernel.Errors;

namespace Invoicing.UnitTests.Common.ValueObjects;

public class PdfBlobRefTests
{
    private const string ValidSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string ValidBlobName = "2026/05/INV-2026-000142.pdf";

    [Fact]
    public void Create_WithValidInputs_ReturnsOk()
    {
        var result = PdfBlobRef.Create(
            blobName: ValidBlobName,
            contentHash: new string('a', 64),
            sizeBytes: 12345L);

        using (new AssertionScope())
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.BlobName.Should().Be(ValidBlobName);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/leading-slash/INV.pdf")]
    [InlineData("INV-2026-000142.txt")]
    public void Create_WithInvalidBlobName_FailsWithInvalidBlobName(string? blobName)
    {
        var result = PdfBlobRef.Create(
            blobName: blobName!,
            contentHash: new string('a', 64),
            sizeBytes: 12345L);

        result.IsFailed.Should().BeTrue();
        result.Errors.OfType<ValidationError>()
            .Should().Contain(e => e.ErrorCode == "Invoicing.InvalidBlobName");
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")] // uppercase
    public void Create_RejectsInvalidHash(string hash)
    {
        PdfBlobRef.Create(ValidBlobName, hash, 1024).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveSize(long size)
    {
        PdfBlobRef.Create(ValidBlobName, ValidSha256, size).IsSuccess.Should().BeFalse();
    }
}
