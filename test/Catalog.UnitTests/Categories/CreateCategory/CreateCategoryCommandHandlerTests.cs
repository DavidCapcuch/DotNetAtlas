using Catalog.Application.Categories.CreateCategory;
using Catalog.Domain.Categories.Events;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Categories.CreateCategory;

public class CreateCategoryCommandHandlerTests
{
    [Fact]
    public async Task Given_RootCategory_When_Handling_Then_Persists()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new CreateCategoryCommandHandler(
            db, TimeProvider.System, NullLogger<CreateCategoryCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new CreateCategoryCommand { Name = "Electronics", ParentCategoryId = null },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().NotBe(Guid.Empty);
            var persisted = await db.Categories.FirstAsync(
                c => c.Id == result.Value, TestContext.Current.CancellationToken);
            persisted.Name.Should().Be("Electronics");
            persisted.Path.Value.Should().Be("/electronics");
            persisted.PopDomainEvents().OfType<CategoryCreatedDomainEvent>()
                .Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Given_ChildCategory_When_ParentExists_Then_Persists()
    {
        await using var db = FakeCatalogDbContext.Create();
        var root = CatalogFactories.RootCategory();
        db.Categories.Add(root);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateCategoryCommandHandler(
            db, TimeProvider.System, NullLogger<CreateCategoryCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new CreateCategoryCommand { Name = "Laptops", ParentCategoryId = root.Id },
            TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            var persisted = await db.Categories.FirstAsync(
                c => c.Id == result.Value, TestContext.Current.CancellationToken);
            persisted.Path.Value.Should().Be("/electronics/laptops");
            persisted.ParentCategoryId.Should().Be(root.Id);
        }
    }

    [Fact]
    public async Task Given_ChildCategory_When_ParentMissing_Then_FailsParentNotFound()
    {
        await using var db = FakeCatalogDbContext.Create();
        var handler = new CreateCategoryCommandHandler(
            db, TimeProvider.System, NullLogger<CreateCategoryCommandHandler>.Instance);
        var unknownParent = Guid.CreateVersion7();

        var result = await handler.HandleAsync(
            new CreateCategoryCommand { Name = "Laptops", ParentCategoryId = unknownParent },
            TestContext.Current.CancellationToken);

        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("Parent category") && e.Message.Contains(unknownParent.ToString()));
    }
}
