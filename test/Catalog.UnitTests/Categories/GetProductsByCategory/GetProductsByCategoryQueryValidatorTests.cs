using Catalog.Application.Categories.GetProductsByCategory;

namespace Catalog.UnitTests.Categories.GetProductsByCategory;

public class GetProductsByCategoryQueryValidatorTests
{
    private readonly GetProductsByCategoryQueryValidator _validator = new();

    [Fact]
    public void Validate_Defaults_Pass()
    {
        // Act & Assert
        _validator.Validate(new GetProductsByCategoryQuery { CategoryId = Guid.CreateVersion7() })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyCategory_Fails()
    {
        // Act & Assert
        _validator.Validate(new GetProductsByCategoryQuery { CategoryId = Guid.Empty })
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_PageSizeOver100_Fails()
    {
        // Act & Assert
        _validator.Validate(new GetProductsByCategoryQuery
        {
            CategoryId = Guid.CreateVersion7(),
            PageSize = 101,
        }).IsValid.Should().BeFalse();
    }
}
