using Catalog.Application.Products.SearchProducts;

namespace Catalog.UnitTests.Products.SearchProducts;

public class SearchProductsQueryValidatorTests
{
    private readonly SearchProductsQueryValidator _validator = new();

    [Fact]
    public void Defaults_are_valid()
    {
        _validator.Validate(new SearchProductsQuery()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Page_number_under_1_fails()
    {
        _validator.Validate(new SearchProductsQuery { PageNumber = 0 }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Page_size_out_of_range_fails(int pageSize)
    {
        _validator.Validate(new SearchProductsQuery { PageSize = pageSize }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Min_price_without_currency_fails()
    {
        _validator.Validate(new SearchProductsQuery { MinPrice = 10m }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Max_price_less_than_min_fails()
    {
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
    public void Valid_category_path_prefix_passes(string prefix)
    {
        _validator.Validate(new SearchProductsQuery { CategoryPathPrefix = prefix })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Invalid_category_path_prefix_fails()
    {
        _validator.Validate(new SearchProductsQuery { CategoryPathPrefix = "no-slash" })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Invalid_status_fails()
    {
        _validator.Validate(new SearchProductsQuery { Status = "Bogus" }).IsValid.Should().BeFalse();
    }
}
