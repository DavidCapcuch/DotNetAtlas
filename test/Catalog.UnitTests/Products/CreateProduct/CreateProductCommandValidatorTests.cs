using Catalog.Application.Products.CreateProduct;

namespace Catalog.UnitTests.Products.CreateProduct;

public class CreateProductCommandValidatorTests
{
    private readonly CreateProductCommandValidator _validator = new();

    private static CreateProductCommand Valid() => new()
    {
        Sku = "ABC-001",
        Name = "Widget",
        Description = "A fine widget.",
        CategoryId = Guid.CreateVersion7(),
        Brand = "Acme",
        Price = new MoneyDto { Amount = 1.23m, Currency = "USD" },
        Dimensions = null,
        Images = new List<ImageReferenceDto>(),
    };

    [Fact]
    public void Valid_command_passes()
    {
        var result = _validator.Validate(Valid());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has spaces")]
    [InlineData("has/slash")]
    [InlineData("-leading-dash")]
    public void Invalid_sku_fails(string sku)
    {
        var cmd = Valid() with { Sku = sku };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_name_fails()
    {
        var cmd = Valid() with { Name = string.Empty };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Description_with_html_fails()
    {
        var cmd = Valid() with { Description = "<p>html</p>" };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Description_with_lt_but_no_tag_passes()
    {
        var cmd = Valid() with { Description = "price < 10" };
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    // CAT-SEC-004 (Wave-1 closeout): existing heuristic only matched "<letter", letting these
    // shapes through. Any downstream renderer that decodes entities or honours processing
    // instructions / CDATA would then re-expose the markup. Reject up front.
    [Theory]
    [InlineData("<!-- xss -->")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<?xml version=\"1.0\"?>")]
    [InlineData("<![CDATA[bad]]>")]
    [InlineData("</p>trailing text")]
    [InlineData("encoded &#60;script&#62;alert(1)&#60;/script&#62;")]
    [InlineData("hex &#x3c;script&#x3e;")]
    [InlineData("named &lt;script&gt;alert(1)&lt;/script&gt;")]
    public void Description_with_html_bypass_fails(string description)
    {
        var cmd = Valid() with { Description = description };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Tom & Jerry buy widgets")]
    [InlineData("price < 10 and quantity > 0")]
    public void Description_with_innocuous_lt_or_amp_passes(string description)
    {
        var cmd = Valid() with { Description = description };
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_category_fails()
    {
        var cmd = Valid() with { CategoryId = Guid.Empty };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Non_positive_price_fails()
    {
        var cmd = Valid() with { Price = new MoneyDto { Amount = 0m, Currency = "USD" } };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDX")]
    public void Malformed_currency_fails(string currency)
    {
        var cmd = Valid() with { Price = new MoneyDto { Amount = 1m, Currency = currency } };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Duplicate_image_display_order_fails()
    {
        var cmd = Valid() with
        {
            Images = new List<ImageReferenceDto>
            {
                new() { Url = "https://example.com/a.jpg", AltText = "a", DisplayOrder = 0 },
                new() { Url = "https://example.com/b.jpg", AltText = "b", DisplayOrder = 0 },
            },
        };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Non_absolute_image_url_fails()
    {
        var cmd = Valid() with
        {
            Images = new List<ImageReferenceDto>
            {
                new() { Url = "/relative.jpg", AltText = "a", DisplayOrder = 0 },
            },
        };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    // Per CAT-SEC-005 (Wave-1 closeout): scheme allow-list mirrored at the API surface so a
    // hostile image URL is rejected before the command reaches the domain factory.
    [Theory]
    [InlineData("javascript:alert('xss')")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/a.jpg")]
    public void Non_http_image_url_scheme_fails(string url)
    {
        var cmd = Valid() with
        {
            Images = new List<ImageReferenceDto>
            {
                new() { Url = url, AltText = "a", DisplayOrder = 0 },
            },
        };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Dimensions_with_unknown_unit_fails()
    {
        var cmd = Valid() with
        {
            Dimensions = new DimensionsDto { Length = 1, Width = 1, Height = 1, Unit = "yards" },
        };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
