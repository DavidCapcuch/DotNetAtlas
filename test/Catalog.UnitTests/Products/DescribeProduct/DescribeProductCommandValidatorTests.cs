using Catalog.Application.Products.DescribeProduct;

namespace Catalog.UnitTests.Products.DescribeProduct;

public class DescribeProductCommandValidatorTests
{
    private readonly DescribeProductCommandValidator _validator = new();

    [Fact]
    public void Valid_description_passes()
    {
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = "plain text description",
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_product_id_fails()
    {
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.Empty,
            NewDescription = "desc",
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Html_description_fails()
    {
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = "has <b>bold</b>",
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_description_passes()
    {
        // Per use-cases spec: 0-4000 chars allowed; empty string clears the description.
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = string.Empty,
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    // CAT-SEC-004 (Wave-1 closeout): the original "<letter" heuristic let comments, doctypes,
    // processing instructions, CDATA, and encoded entities through. Mirror the hardened
    // CreateProduct heuristic.
    [Theory]
    [InlineData("<!-- xss -->")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<?xml version=\"1.0\"?>")]
    [InlineData("<![CDATA[bad]]>")]
    [InlineData("</p>")]
    [InlineData("&#60;script&#62;")]
    [InlineData("&#x3c;script&#x3e;")]
    [InlineData("&lt;script&gt;")]
    public void Description_with_html_bypass_fails(string description)
    {
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = description,
        };

        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Tom & Jerry")]
    [InlineData("price < 10 USD")]
    public void Innocuous_lt_or_amp_passes(string description)
    {
        var cmd = new DescribeProductCommand
        {
            ProductId = Guid.CreateVersion7(),
            NewDescription = description,
        };

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }
}
