using Catalog.Application.Categories.ReparentCategory;

namespace Catalog.UnitTests.Categories.ReparentCategory;

public class ReparentCategoryCommandValidatorTests
{
    private readonly ReparentCategoryCommandValidator _validator = new();

    [Fact]
    public void Validate_ReparentToRoot_Passes()
    {
        // Arrange
        var cmd = new ReparentCategoryCommand
        {
            CategoryId = Guid.CreateVersion7(),
            NewParentCategoryId = null,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReparentToOtherParent_Passes()
    {
        // Arrange
        var cmd = new ReparentCategoryCommand
        {
            CategoryId = Guid.CreateVersion7(),
            NewParentCategoryId = Guid.CreateVersion7(),
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyCategory_Fails()
    {
        // Arrange
        var cmd = new ReparentCategoryCommand
        {
            CategoryId = Guid.Empty,
            NewParentCategoryId = null,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SelfParent_Fails()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var cmd = new ReparentCategoryCommand
        {
            CategoryId = id,
            NewParentCategoryId = id,
        };

        // Act & Assert
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
