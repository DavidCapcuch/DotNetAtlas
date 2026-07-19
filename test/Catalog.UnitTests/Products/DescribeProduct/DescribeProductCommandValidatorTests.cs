using Catalog.Application.Products.DescribeProduct;

namespace Catalog.UnitTests.Products.DescribeProduct;

public class DescribeProductCommandValidatorTests
{
    private readonly DescribeProductCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidDescription_Passes()
    {
        // Arrange
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = "plain text description",
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyProductId_Fails()
    {
        // Arrange
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.Empty,
            NewDescription = "desc",
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "security")]
    public void Validate_HtmlDescription_Fails()
    {
        // Arrange
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = "has <b>bold</b>",
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyDescription_Passes()
    {
        // Arrange
        // Per use-cases spec: 0-4000 chars allowed; empty string clears the description.
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = string.Empty,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    // CAT-SEC-004: a naive "<letter" heuristic would let comments, doctypes, processing
    // instructions, CDATA, and encoded entities through. Mirror the CreateProduct heuristic.
    [Theory]
    [Trait("Category", "security")]
    [InlineData("<!-- xss -->")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<?xml version=\"1.0\"?>")]
    [InlineData("<![CDATA[bad]]>")]
    [InlineData("</p>")]
    [InlineData("&#60;script&#62;")]
    [InlineData("&#x3c;script&#x3e;")]
    [InlineData("&lt;script&gt;")]
    public void Validate_DescriptionWithHtmlBypass_Fails(string description)
    {
        // Arrange
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = description,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Tom & Jerry")]
    [InlineData("price < 10 USD")]
    public void Validate_InnocuousLtOrAmp_Passes(string description)
    {
        // Arrange
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = description,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }
}
