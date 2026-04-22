using Catalog.Domain.Categories.Errors;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Categories.Errors;

public class CategoryErrorsTests
{
    [Fact]
    public void NotFound_PopulatesPropertyNameMessageAndCode()
    {
        var categoryId = Guid.CreateVersion7();

        var error = CategoryErrors.NotFound(categoryId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("CategoryId");
            error.ErrorCode.Should().Be("Category.NotFound");
            error.Message.Should().Contain(categoryId.ToString());
        }
    }

    [Fact]
    public void ParentNotFound_PopulatesPropertyNameMessageAndCode()
    {
        var parentId = Guid.CreateVersion7();

        var error = CategoryErrors.ParentNotFound(parentId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<ValidationError>();
            error.PropertyName.Should().Be("ParentCategoryId");
            error.ErrorCode.Should().Be("Category.ParentNotFound");
            error.Message.Should().Contain(parentId.ToString());
        }
    }
}
