using Catalog.Domain.Categories;
using Catalog.Domain.Categories.Events;
using Catalog.Domain.Categories.ValueObjects;
using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;

namespace Catalog.UnitTests.Categories.Aggregates;

public class CategoryTests
{
    [Fact]
    public void Create_AsRoot_BuildsPathFromSlugAndRaisesEvent()
    {
        // Act
        var result = Category.Create("Electronics", parentCategoryId: null, parentPath: null);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var category = result.Value;
            category.Name.Should().Be("Electronics");
            category.ParentCategoryId.Should().BeNull();
            category.Path.Value.Should().Be("/electronics");
            var created = category.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<CategoryCreatedDomainEvent>()
                .Subject;
            created.CategoryId.Should().Be(category.Id);
            created.Path.Value.Should().Be("/electronics");
            created.ParentCategoryId.Should().BeNull();
        }
    }

    [Fact]
    public void Create_AsChild_AppendsSlugToParentPathAndRaisesEvent()
    {
        // Arrange
        var parentId = Guid.CreateVersion7();
        var parentPath = CategoryPath.Create("/electronics/computers").Value;

        // Act
        var result = Category.Create("Laptops", parentCategoryId: parentId, parentPath: parentPath);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Path.Value.Should().Be("/electronics/computers/laptops");
            result.Value.ParentCategoryId.Should().Be(parentId);
        }
    }

    [Fact]
    public void Create_WhenResultingDepthWouldExceed5_ReturnsAggregateLevelMaxDepthExceeded()
    {
        // Arrange
        var parentId = Guid.CreateVersion7();
        var parentPath = CategoryPath.Create("/a/b/c/d/e").Value;

        // Act
        var result = Category.Create("f", parentCategoryId: parentId, parentPath: parentPath);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Category.MaxDepthExceeded");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameEmpty_ReturnsFailureWithNameRequired(string? name)
    {
        // Act
        var result = Category.Create(name!, parentCategoryId: null, parentPath: null);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Category.NameRequired");
        }
    }

    [Fact]
    public void Create_WhenNameTooLong_ReturnsFailureWithNameTooLong()
    {
        // Arrange
        var longName = new string('A', 101);

        // Act
        var result = Category.Create(longName, parentCategoryId: null, parentPath: null);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Category.NameTooLong");
        }
    }

    [Fact]
    public void Rename_UpdatesFinalPathSegmentAndRaisesReparentedWithSameParent()
    {
        // Arrange
        var parentId = Guid.CreateVersion7();
        var parentPath = CategoryPath.Create("/electronics").Value;
        var category = Category.Create("Computers", parentId, parentPath).Value;
        var oldPath = category.Path;
        _ = category.PopDomainEvents();

        // Act
        var result = category.Rename("Workstations");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            category.Name.Should().Be("Workstations");
            category.Path.Value.Should().Be("/electronics/workstations");
            category.ParentCategoryId.Should().Be(parentId);
            var evt = category.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<CategoryReparentedDomainEvent>()
                .Subject;
            evt.OldParentId.Should().Be(parentId);
            evt.NewParentId.Should().Be(parentId);
            evt.OldPath.Should().Be(oldPath);
            evt.NewPath.Value.Should().Be("/electronics/workstations");
        }
    }

    [Fact]
    public void Rename_WhenRootCategory_UpdatesPathAndKeepsNullParent()
    {
        // Arrange
        var category = Category.Create("Electronics", parentCategoryId: null, parentPath: null).Value;
        _ = category.PopDomainEvents();

        // Act
        var result = category.Rename("Gadgets");

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            category.Name.Should().Be("Gadgets");
            category.Path.Value.Should().Be("/gadgets");
            category.ParentCategoryId.Should().BeNull();
        }
    }

    [Fact]
    public void Reparent_Valid_RaisesReparentedWithOldAndNewPath()
    {
        // Arrange
        var oldParentId = Guid.CreateVersion7();
        var oldParentPath = CategoryPath.Create("/electronics").Value;
        var category = Category.Create("Laptops", oldParentId, oldParentPath).Value;
        var oldPath = category.Path;
        _ = category.PopDomainEvents();

        var newParentId = Guid.CreateVersion7();
        var newParentPath = CategoryPath.Create("/portable-devices").Value;

        // Act
        var result = category.Reparent(newParentId, newParentPath);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            category.ParentCategoryId.Should().Be(newParentId);
            category.Path.Value.Should().Be("/portable-devices/laptops");
            var evt = category.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<CategoryReparentedDomainEvent>()
                .Subject;
            evt.OldParentId.Should().Be(oldParentId);
            evt.NewParentId.Should().Be(newParentId);
            evt.OldPath.Should().Be(oldPath);
            evt.NewPath.Should().Be(category.Path);
        }
    }

    [Fact]
    public void Reparent_WhenNewPathExceedsDepth5_ReturnsAggregateLevelMaxDepthExceeded()
    {
        // Arrange
        var oldParentId = Guid.CreateVersion7();
        var oldParentPath = CategoryPath.Create("/a").Value;
        var category = Category.Create("leaf", oldParentId, oldParentPath).Value;
        _ = category.PopDomainEvents();

        var newParentId = Guid.CreateVersion7();
        var newParentPath = CategoryPath.Create("/a/b/c/d/e").Value;

        // Act
        var result = category.Reparent(newParentId, newParentPath);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Category.MaxDepthExceeded");
            category.ParentCategoryId.Should().Be(oldParentId);
        }
    }

    [Fact]
    public void Reparent_ToRoot_UpdatesPathAndSetsNullParent()
    {
        // Arrange
        var oldParentId = Guid.CreateVersion7();
        var oldParentPath = CategoryPath.Create("/electronics").Value;
        var category = Category.Create("Laptops", oldParentId, oldParentPath).Value;
        _ = category.PopDomainEvents();

        // Act
        var result = category.Reparent(newParentCategoryId: null, newParentPath: null);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            category.ParentCategoryId.Should().BeNull();
            category.Path.Value.Should().Be("/laptops");
        }
    }

    [Fact]
    public void Reparent_WhenNewParentIdEqualsSelf_ReturnsCannotParentToSelf()
    {
        // Arrange
        var category = Category.Create("Electronics", parentCategoryId: null, parentPath: null).Value;
        var selfPath = CategoryPath.Create("/electronics").Value;
        _ = category.PopDomainEvents();

        // Act
        var result = category.Reparent(newParentCategoryId: category.Id, newParentPath: selfPath);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFailure();
            result.Errors.Should().ContainSingle(e =>
                ((DomainError)e).ErrorCode == "Category.CannotParentToSelf");
        }
    }
}
