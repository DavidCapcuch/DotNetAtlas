using Catalog.Application.Categories.CreateCategory;
using Catalog.Infrastructure.Persistence.Database.Interceptors;
using Catalog.UnitTests.Common;
using Microsoft.Extensions.Time.Testing;

namespace Catalog.UnitTests.Categories.CreateCategory;

/// <summary>
/// Exhaustive mapping coverage for
/// <see cref="CategoryCreatedMapper.ToCategoryCreatedEvent"/> — the pure leaf projecting the
/// aggregate onto the external Avro contract.
/// </summary>
public class CategoryCreatedMapperTests
{
    [Fact]
    public async Task ToCategoryCreatedEvent_ForChildCategory_MapsEveryFieldIncludingCreatedAtUtc()
    {
        // Arrange
        // CreatedUtc is stamped by the audit interceptor on save (not by Category.Create), so drive
        // it from a FakeTimeProvider to a distinct, known instant — otherwise the assertion below
        // would only pin DateTimeOffset default and never notice a wrong-source mutation.
        var createdAt = new DateTimeOffset(2026, 5, 10, 8, 30, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(createdAt);
        await using var db = FakeCatalogDbContext.Create(
            databaseName: null,
            new UpdateAuditableEntitiesInterceptor(clock));

        var parent = CatalogFactories.RootCategory("Electronics");
        var child = CatalogFactories.ChildCategory(parent, "Laptops");
        db.Categories.AddRange(parent, child);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var avro = child.ToCategoryCreatedEvent();

        // Assert
        using (new AssertionScope())
        {
            avro.CategoryId.Should().Be(child.Id);
            avro.Name.Should().Be("Laptops");
            avro.ParentCategoryId.Should().Be(parent.Id);
            avro.Path.Should().Be(child.Path.Value);
            avro.CreatedAtUtc.Should().Be(createdAt.UtcDateTime);
        }
    }
}
