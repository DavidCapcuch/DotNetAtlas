using Catalog.Application.Common.Contracts;
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
    public void Validate_ValidCommand_Passes()
    {
        // Act
        var result = _validator.Validate(Valid());

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has spaces")]
    [InlineData("has/slash")]
    [InlineData("-leading-dash")]
    public void Validate_InvalidSku_Fails(string sku)
    {
        // Arrange
        var cmd = Valid() with { Sku = sku };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyName_Fails()
    {
        // Arrange
        var cmd = Valid() with { Name = string.Empty };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "security")]
    public void Validate_DescriptionWithHtml_Fails()
    {
        // Arrange
        var cmd = Valid() with { Description = "<p>html</p>" };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_DescriptionWithLtButNoTag_Passes()
    {
        // Arrange
        var cmd = Valid() with { Description = "price < 10" };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    // CAT-SEC-004 (Wave-1 closeout): existing heuristic only matched "<letter", letting these
    // shapes through. Any downstream renderer that decodes entities or honours processing
    // instructions / CDATA would then re-expose the markup. Reject up front.
    [Theory]
    [Trait("Category", "security")]
    [InlineData("<!-- xss -->")]
    [InlineData("<!DOCTYPE html>")]
    [InlineData("<?xml version=\"1.0\"?>")]
    [InlineData("<![CDATA[bad]]>")]
    [InlineData("</p>trailing text")]
    [InlineData("encoded &#60;script&#62;alert(1)&#60;/script&#62;")]
    [InlineData("hex &#x3c;script&#x3e;")]
    [InlineData("named &lt;script&gt;alert(1)&lt;/script&gt;")]
    public void Validate_DescriptionWithHtmlBypass_Fails(string description)
    {
        // Arrange
        var cmd = Valid() with { Description = description };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Tom & Jerry buy widgets")]
    [InlineData("price < 10 and quantity > 0")]
    public void Validate_DescriptionWithInnocuousLtOrAmp_Passes(string description)
    {
        // Arrange
        var cmd = Valid() with { Description = description };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyCategory_Fails()
    {
        // Arrange
        var cmd = Valid() with { CategoryId = Guid.Empty };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NonPositivePrice_Fails()
    {
        // Arrange
        var cmd = Valid() with { Price = new MoneyDto { Amount = 0m, Currency = "USD" } };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("USDX")]
    public void Validate_MalformedCurrency_Fails(string currency)
    {
        // Arrange
        var cmd = Valid() with { Price = new MoneyDto { Amount = 1m, Currency = currency } };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_DuplicateImageDisplayOrder_Fails()
    {
        // Arrange
        var cmd = Valid() with
        {
            Images = new List<ImageReferenceDto>
            {
                new() { Url = "https://example.com/a.jpg", AltText = "a", DisplayOrder = 0 },
                new() { Url = "https://example.com/b.jpg", AltText = "b", DisplayOrder = 0 },
            },
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_NonAbsoluteImageUrl_Fails()
    {
        // Arrange
        var cmd = Valid() with
        {
            Images = new List<ImageReferenceDto>
            {
                new() { Url = "/relative.jpg", AltText = "a", DisplayOrder = 0 },
            },
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    // Per CAT-SEC-005 (Wave-1 closeout): scheme allow-list mirrored at the API surface so a
    // hostile image URL is rejected before the command reaches the domain factory.
    [Theory]
    [Trait("Category", "security")]
    [InlineData("javascript:alert('xss')")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/a.jpg")]
    public void Validate_NonHttpImageUrlScheme_Fails(string url)
    {
        // Arrange
        var cmd = Valid() with
        {
            Images = new List<ImageReferenceDto>
            {
                new() { Url = url, AltText = "a", DisplayOrder = 0 },
            },
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    // CAT-SEC-006 (Wave-1 closeout): MaximumLength counts UTF-16 code units, so a surrogate
    // pair (any non-BMP emoji) is 2 chars. MaximumRuneLength counts Unicode scalars instead,
    // so 200 emoji = 200 runes = valid even though it's 400 chars.
    [Fact]
    public void Validate_NameOf200EmojiRunes_PassesRuneCheck()
    {
        // Arrange
        // 𝓪 (U+1D4EA) is a non-BMP code point, so it occupies 2 chars per appearance.
        var name = string.Concat(Enumerable.Repeat("𝓪", 200));
        var cmd = Valid() with { Name = name };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NameOf201EmojiRunes_FailsRuneCheck()
    {
        // Arrange
        var name = string.Concat(Enumerable.Repeat("𝓪", 201));
        var cmd = Valid() with { Name = name };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_DimensionsWithUnknownUnit_Fails()
    {
        // Arrange
        var cmd = Valid() with
        {
            Dimensions = new DimensionsDto { Length = 1, Width = 1, Height = 1, Unit = "yards" },
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
