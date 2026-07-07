using Catalog.Application.Products.SearchProducts;

namespace Catalog.UnitTests.Products.SearchProducts;

public class SearchProductsQueryValidatorTests
{
    private readonly SearchProductsQueryValidator _validator = new();

    // CAT-TST-M01 (Wave-1 closeout): the previous "Defaults_are_valid" test was misleading —
    // a default-constructed SearchProductsQuery has PageNumber=0 and PageSize=0 (record
    // defaults), which the validator (correctly) rejects. The test only passed by hitting
    // the .When(...) guards on every rule; it tested nothing meaningful. The endpoint binding
    // layer is the one that supplies `?? 1` / `?? 20` defaults, not the validator.

    [Fact]
    public void Validate_PageNumberUnder1_Fails()
    {
        // Act & Assert
        _validator.Validate(new SearchProductsQuery { PageNumber = 0 }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_PageSizeOutOfRange_Fails(int pageSize)
    {
        // Act & Assert
        _validator.Validate(new SearchProductsQuery { PageSize = pageSize }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MinPriceWithoutCurrency_Fails()
    {
        // Act & Assert
        _validator.Validate(new SearchProductsQuery { MinPrice = 10m }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MaxPriceLessThanMin_Fails()
    {
        // Act & Assert
        _validator.Validate(new SearchProductsQuery
        {
            MinPrice = 10m,
            MaxPrice = 5m,
            Currency = "USD",
        }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("/electronics")]
    [InlineData("/electronics/laptops")]
    [InlineData("/a/b/c/d/e")]
    public void Validate_ValidCategoryPathPrefix_Passes(string prefix)
    {
        // Act & Assert
        _validator.Validate(new SearchProductsQuery { CategoryPathPrefix = prefix })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidCategoryPathPrefix_Fails()
    {
        // Act & Assert
        _validator.Validate(new SearchProductsQuery { CategoryPathPrefix = "no-slash" })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_InvalidStatus_Fails()
    {
        // Act & Assert
        _validator.Validate(new SearchProductsQuery { Status = "Bogus" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TextLongerThan100Chars_Fails()
    {
        // Arrange
        var text = new string('a', 101);

        // Act & Assert
        _validator.Validate(new SearchProductsQuery { Text = text }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_TextExactly100Chars_Passes()
    {
        // Arrange
        var text = new string('a', 100);

        // Act & Assert
        _validator.Validate(new SearchProductsQuery { Text = text }).IsValid.Should().BeTrue();
    }
}
