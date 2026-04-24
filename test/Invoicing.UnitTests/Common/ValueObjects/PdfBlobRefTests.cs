using Invoicing.Domain.Common.ValueObjects;

namespace Invoicing.UnitTests.Common.ValueObjects;

public class PdfBlobRefTests
{
    private const string ValidSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private static readonly Uri ValidUri = new("https://example.com/invoices/INV.pdf?sv=sas");

    [Fact]
    public void Create_AcceptsValidInputs()
    {
        var result = PdfBlobRef.Create(ValidUri, ValidSha256, sizeBytes: 1024);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_RejectsRelativeUri()
    {
        var relative = new Uri("/invoices/INV.pdf", UriKind.Relative);

        PdfBlobRef.Create(relative, ValidSha256, 1024).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")] // uppercase
    public void Create_RejectsInvalidHash(string hash)
    {
        PdfBlobRef.Create(ValidUri, hash, 1024).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveSize(long size)
    {
        PdfBlobRef.Create(ValidUri, ValidSha256, size).IsSuccess.Should().BeFalse();
    }
}
