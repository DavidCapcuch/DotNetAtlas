using Catalog.Application.Categories.CreateCategory;

namespace Catalog.UnitTests.Categories.CreateCategory;

public class CreateCategoryCommandValidatorTests
{
    private readonly CreateCategoryCommandValidator _validator = new();

    [Fact]
    public void Valid_root_category_passes()
    {
        var cmd = new CreateCategoryCommand { Name = "Electronics", ParentCategoryId = null };
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Valid_child_category_passes()
    {
        var cmd = new CreateCategoryCommand
        {
            Name = "Laptops",
            ParentCategoryId = Guid.CreateVersion7(),
        };
        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_name_fails()
    {
        var cmd = new CreateCategoryCommand { Name = string.Empty };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Name_over_100_chars_fails()
    {
        var cmd = new CreateCategoryCommand { Name = new string('x', 101) };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_guid_parent_fails()
    {
        var cmd = new CreateCategoryCommand
        {
            Name = "x",
            ParentCategoryId = Guid.Empty,
        };
        _validator.Validate(cmd).IsValid.Should().BeFalse();
    }
}
