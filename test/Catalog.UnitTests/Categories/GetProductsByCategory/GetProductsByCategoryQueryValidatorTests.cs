using Catalog.Application.Categories.GetProductsByCategory;

namespace Catalog.UnitTests.Categories.GetProductsByCategory;

public class GetProductsByCategoryQueryValidatorTests
{
    private readonly GetProductsByCategoryQueryValidator _validator = new();

    [Fact]
    public void Valid_defaults_pass()
    {
        _validator.Validate(new GetProductsByCategoryQuery { CategoryId = Guid.CreateVersion7() })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_category_fails()
    {
        _validator.Validate(new GetProductsByCategoryQuery { CategoryId = Guid.Empty })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_over_100_fails()
    {
        _validator.Validate(new GetProductsByCategoryQuery
        {
            CategoryId = Guid.CreateVersion7(),
            PageSize = 101,
        }).IsValid.Should().BeFalse();
    }
}
