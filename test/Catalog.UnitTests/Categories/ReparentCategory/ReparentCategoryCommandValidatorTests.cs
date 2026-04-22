using Catalog.Application.Categories.ReparentCategory;

namespace Catalog.UnitTests.Categories.ReparentCategory;

public class ReparentCategoryCommandValidatorTests
{
    private readonly ReparentCategoryCommandValidator _validator = new();

    [Fact]
    public void Valid_reparent_to_root_passes()
    {
        var cmd = new ReparentCategoryCommand
        {
            CategoryId = Guid.CreateVersion7(),
            NewParentCategoryId = null,
        };
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_reparent_to_other_parent_passes()
    {
        var cmd = new ReparentCategoryCommand
        {
            CategoryId = Guid.CreateVersion7(),
            NewParentCategoryId = Guid.CreateVersion7(),
        };
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_category_fails()
    {
        var cmd = new ReparentCategoryCommand
        {
            CategoryId = Guid.Empty,
            NewParentCategoryId = null,
        };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Self_parent_fails()
    {
        var id = Guid.CreateVersion7();
        var cmd = new ReparentCategoryCommand
        {
            CategoryId = id,
            NewParentCategoryId = id,
        };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
