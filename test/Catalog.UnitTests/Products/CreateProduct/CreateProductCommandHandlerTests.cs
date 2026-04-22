using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using Catalog.UnitTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Catalog.UnitTests.Products.CreateProduct;

public class CreateProductCommandHandlerTests
{
    private static CreateProductCommand ValidCommand(Guid categoryId) => new()
    {
        Sku = "ABC-001",
        Name = "Widget",
        Description = "Nice widget.",
        CategoryId = categoryId,
        Brand = "Acme",
        Price = new MoneyDto { Amount = 12.50m, Currency = "USD" },
        Dimensions = new DimensionsDto { Length = 10m, Width = 5m, Height = 2m, Unit = "cm" },
        Images = new List<ImageReferenceDto>
        {
            new() { Url = "https://cdn.example.com/w.jpg", AltText = "widget", DisplayOrder = 0 },
        },
    };

    [Fact]
    public async Task Given_ValidCommand_When_Handling_Then_PersistsProductInDraftAndReturnsId()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateProductCommandHandler(
            db, NullLogger<CreateProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            ValidCommand(category.Id), TestContext.Current.CancellationToken);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeSuccess();
            result.Value.Should().NotBe(Guid.Empty);
            var persisted = await db.Products.FirstAsync(
                p => p.Id == result.Value, TestContext.Current.CancellationToken);
            persisted.Sku.Value.Should().Be("ABC-001");
            persisted.Status.Should().Be(ProductStatus.Draft);
            persisted.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductCreatedDomainEvent>();
        }
    }

    [Fact]
    public async Task Given_DuplicateSku_When_Handling_Then_FailsWithSkuAlreadyExists()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        db.Products.Add(CatalogFactories.DraftProduct(category, sku: "ABC-001"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateProductCommandHandler(
            db, NullLogger<CreateProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            ValidCommand(category.Id), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure()
            .And.HaveReason("A product with SKU 'ABC-001' already exists.");
    }

    [Fact]
    public async Task Given_MissingCategory_When_Handling_Then_FailsWithCategoryNotFound()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var handler = new CreateProductCommandHandler(
            db, NullLogger<CreateProductCommandHandler>.Instance);
        var unknownCategory = Guid.CreateVersion7();

        // Act
        var result = await handler.HandleAsync(
            ValidCommand(unknownCategory), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(unknownCategory.ToString()));
    }

    [Fact]
    public async Task Given_SkuNormalization_When_Handling_Then_StoredAsUpperInvariant()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateProductCommandHandler(
            db, NullLogger<CreateProductCommandHandler>.Instance);
        var cmd = ValidCommand(category.Id);
        cmd.Sku = "abc-002";

        // Act
        var result = await handler.HandleAsync(cmd, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
        var persisted = await db.Products.FirstAsync(
            p => p.Id == result.Value, TestContext.Current.CancellationToken);
        persisted.Sku.Value.Should().Be("ABC-002");
    }
}
