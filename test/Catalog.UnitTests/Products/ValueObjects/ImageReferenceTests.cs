using Catalog.Domain.Products.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Products.ValueObjects;

public class ImageReferenceTests
{
    [Fact]
    public void Create_WhenValid_ReturnsImageReference()
    {
        // Act
        var result = ImageReference.Create("https://example.com/img.png", "Alt text", 0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Url.Should().Be("https://example.com/img.png");
            result.Value.AltText.Should().Be("Alt text");
            result.Value.DisplayOrder.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-absolute")]
    [InlineData("/relative/path.png")]
    public void Create_WhenUrlNotAbsolute_ReturnsFailure(string? url)
    {
        // Act
        var result = ImageReference.Create(url, "Alt", 0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ImageReference.InvalidUrl");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenAltTextEmpty_ReturnsFailure(string? altText)
    {
        // Act
        var result = ImageReference.Create("https://example.com/img.png", altText, 0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ImageReference.AltTextEmpty");
        }
    }

    [Fact]
    public void Create_WhenAltTextTooLong_ReturnsFailureWithAltTextTooLong()
    {
        // Arrange
        var tooLong = new string('A', 201);

        // Act
        var result = ImageReference.Create("https://example.com/img.png", tooLong, 0);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ImageReference.AltTextTooLong");
        }
    }

    [Fact]
    public void Create_WhenDisplayOrderNegative_ReturnsFailure()
    {
        // Act
        var result = ImageReference.Create("https://example.com/img.png", "Alt", -1);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "ImageReference.NegativeDisplayOrder");
        }
    }
}
