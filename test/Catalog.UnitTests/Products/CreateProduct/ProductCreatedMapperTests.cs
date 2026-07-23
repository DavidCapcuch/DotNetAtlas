using Avro;
using Catalog.Application.Products.CreateProduct;
using Catalog.Infrastructure.Persistence.Database.Interceptors;
using Catalog.UnitTests.Common;
using Microsoft.Extensions.Time.Testing;
using AvroProductStatus = Catalog.Products.ProductStatus;

namespace Catalog.UnitTests.Products.CreateProduct;

/// <summary>
/// Exhaustive mapping coverage for
/// <see cref="ProductCreatedMapper.ToProductCreatedEvent"/> — the pure leaf projecting the
/// aggregate + its category onto the external Avro contract.
/// </summary>
public class ProductCreatedMapperTests
{
    /// <summary>Scale pinned by <c>PriceAmount</c> in <c>ProductCreatedEvent.avsc</c>.</summary>
    private const int MoneyScale = 4;

    [Fact]
    public async Task ToProductCreatedEvent_WhenFullyPopulated_MapsEveryFieldIncludingCreatedAtUtc()
    {
        // Arrange
        // CreatedUtc is stamped by the audit interceptor on save (not by Product.Create), so drive
        // it from a FakeTimeProvider to a distinct, known instant — otherwise the assertion below
        // would only pin DateTimeOffset default and never notice a wrong-source mutation.
        var createdAt = new DateTimeOffset(2026, 5, 10, 8, 30, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(createdAt);
        await using var db = FakeCatalogDbContext.Create(
            databaseName: null,
            new UpdateAuditableEntitiesInterceptor(clock));

        var category = CatalogFactories.RootCategory("Electronics");
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(
            category,
            sku: "SKU-42",
            name: "Widget",
            description: "A widget",
            brand: "Acme",
            amount: 12.50m,
            currency: "USD");
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        var avro = product.ToProductCreatedEvent(category);

        // Assert
        using (new AssertionScope())
        {
            avro.ProductId.Should().Be(product.Id);
            avro.Sku.Should().Be("SKU-42");
            avro.Name.Should().Be("Widget");
            avro.Description.Should().Be("A widget");
            avro.CategoryId.Should().Be(category.Id);
            avro.CategoryPath.Should().Be(category.Path.Value);
            avro.BrandName.Should().Be("Acme");
            // Scale-comparing oracle, not a (decimal) cast — the cast erases scale, hiding an amount
            // emitted at the input's own scale (2) rather than the schema's 4.
            avro.PriceAmount.Should().Be(new AvroDecimal(12.5000m));
            avro.PriceAmount.Scale.Should().Be(MoneyScale);
            avro.PriceCurrency.Should().Be("USD");
            avro.Status.Should().Be(AvroProductStatus.Active);
            avro.CreatedAtUtc.Should().Be(createdAt.UtcDateTime);
        }
    }

    [Fact]
    public void ToProductCreatedEvent_WhenDescriptionExceedsMax_TruncatesTo1000Chars()
    {
        // Arrange
        var category = CatalogFactories.RootCategory();
        var product = CatalogFactories.ActiveProduct(category, description: new string('x', 2_500));

        // Act
        var avro = product.ToProductCreatedEvent(category);

        // Assert
        avro.Description.Length.Should().Be(1000);
    }
}
