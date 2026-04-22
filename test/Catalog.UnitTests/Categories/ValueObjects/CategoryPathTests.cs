using Catalog.Domain.Categories.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Categories.ValueObjects;

public class CategoryPathTests
{
    [Theory]
    [InlineData("/a")]
    [InlineData("/a-b")]
    [InlineData("/electronics")]
    [InlineData("/electronics/computers")]
    [InlineData("/electronics/computers/laptops")]
    [InlineData("/electronics/computers/peripherals/mice/wireless")]
    public void Create_AtValidDepth_ReturnsSuccess(string path)
    {
        // Act
        var result = CategoryPath.Create(path);

        // Assert
        result.Should().BeSuccess();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noslash")]
    [InlineData("/")]
    [InlineData("/UPPER")]
    [InlineData("//double")]
    [InlineData("/a/b/c/d/e/f")] // depth 6
    public void Create_WhenMalformedOrDepthExceeded_ReturnsFailure(string? path)
    {
        // Act
        var result = CategoryPath.Create(path);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "CategoryPath.Malformed");
        }
    }

    [Theory]
    [InlineData("/a", 1)]
    [InlineData("/a/b", 2)]
    [InlineData("/a/b/c/d/e", 5)]
    public void Depth_CountsSegmentsCorrectly(string path, int expectedDepth)
    {
        // Arrange
        var categoryPath = CategoryPath.Create(path).Value;

        // Act
        var depth = categoryPath.Depth();

        // Assert
        depth.Should().Be(expectedDepth);
    }

    [Fact]
    public void Append_WhenResultingDepthIs5_ReturnsSuccess()
    {
        // Arrange
        var path = CategoryPath.Create("/a/b/c/d").Value;

        // Act
        var result = path.Append("e");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Value.Should().Be("/a/b/c/d/e");
        }
    }

    [Fact]
    public void Append_WhenResultingDepthWouldExceed5_ReturnsMaxDepthExceeded()
    {
        // Arrange
        var path = CategoryPath.Create("/a/b/c/d/e").Value;

        // Act
        var result = path.Append("f");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "CategoryPath.MaxDepthExceeded");
        }
    }

    [Fact]
    public void Breadcrumb_ComposesFromSlugToNameMap()
    {
        // Arrange
        var path = CategoryPath.Create("/electronics/computers/laptops").Value;
        var slugToName = new Dictionary<string, string>
        {
            ["electronics"] = "Electronics",
            ["computers"] = "Computers",
            ["laptops"] = "Laptops"
        };

        // Act
        var breadcrumb = path.Breadcrumb(slugToName);

        // Assert
        breadcrumb.Should().Be("Electronics > Computers > Laptops");
    }

    [Fact]
    public void Breadcrumb_UnknownSlugsFallBackToSlug()
    {
        // Arrange
        var path = CategoryPath.Create("/electronics/unknown").Value;
        var slugToName = new Dictionary<string, string>
        {
            ["electronics"] = "Electronics"
        };

        // Act
        var breadcrumb = path.Breadcrumb(slugToName);

        // Assert
        breadcrumb.Should().Be("Electronics > unknown");
    }

    [Theory]
    [InlineData("Electronics", "electronics")]
    [InlineData("Sound & Vision", "sound-vision")]
    [InlineData("  Laptops  ", "laptops")]
    [InlineData("Multi   Space", "multi-space")]
    [InlineData("Café", "caf")]
    [InlineData("Pokémon", "pok-mon")]
    public void Slugify_ReturnsLowercaseAsciiDashSeparatedToken(string input, string expected)
    {
        // Act
        var slug = CategoryPath.Slugify(input);

        // Assert
        slug.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    public void Slugify_WhenEmpty_ReturnsNull(string? input)
    {
        // Act
        var slug = CategoryPath.Slugify(input);

        // Assert
        slug.Should().BeNull();
    }
}
