using Catalog.Domain.Categories.Errors;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Categories.Errors;

public class CategoryErrorsTests
{
    [Fact]
    public void NotFound_ReturnsNotFoundErrorWithEntityNameAndCode()
    {
        var categoryId = Guid.CreateVersion7();

        var error = CategoryErrors.NotFound(categoryId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<NotFoundError>();
            error.EntityName.Should().Be("Category");
            error.Id.Should().Be(categoryId);
            error.ErrorCode.Should().Be("Category.NotFound");
            error.Message.Should().Contain(categoryId.ToString());
        }
    }

    [Fact]
    public void ParentNotFound_ReturnsNotFoundErrorWithEntityNameAndCode()
    {
        var parentId = Guid.CreateVersion7();

        var error = CategoryErrors.ParentNotFound(parentId);

        using (new AssertionScope())
        {
            error.Should().BeOfType<NotFoundError>();
            error.EntityName.Should().Be("Category");
            error.Id.Should().Be(parentId);
            error.ErrorCode.Should().Be("Category.ParentNotFound");
            error.Message.Should().Contain(parentId.ToString());
        }
    }
}
