using Catalog.Application.Common.Contracts;
using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Catalog.Domain.Products.ValueObjects;
using Catalog.UnitTests.Common;
using EntityFramework.Exceptions.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.SharedKernel.Errors;

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
    public async Task Handle_ValidCommand_PersistsActiveProductAndReturnsId()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateProductCommandHandler(
            db, TimeProvider.System, NullLogger<CreateProductCommandHandler>.Instance);

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
            persisted.Status.Should().Be(ProductStatus.Active);
            persisted.PopDomainEvents().Should()
                .ContainSingle()
                .Which.Should().BeOfType<ProductCreatedDomainEvent>();
        }
    }

    [Fact]
    public async Task Handle_DuplicateSku_FailsWithSkuAlreadyExists()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        db.Products.Add(CatalogFactories.ActiveProduct(category, sku: "ABC-001"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateProductCommandHandler(
            db, TimeProvider.System, NullLogger<CreateProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            ValidCommand(category.Id), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e =>
            ((DomainError)e).ErrorCode == "Product.SkuAlreadyExists"
            && e.Message.Contains("ABC-001"));
    }

    [Fact]
    public async Task Handle_MissingCategory_FailsWithCategoryNotFound()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var handler = new CreateProductCommandHandler(
            db, TimeProvider.System, NullLogger<CreateProductCommandHandler>.Instance);
        var unknownCategory = Guid.CreateVersion7();

        // Act
        var result = await handler.HandleAsync(
            ValidCommand(unknownCategory), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(unknownCategory.ToString()));
    }

    /// <summary>
    /// CAT-RV-H04: the AnyAsync precheck is racy. Under concurrency two
    /// commands can both pass the check before either SaveChanges; the unique index
    /// (UX_Products_Sku) then surfaces a UniqueConstraintException from the second call.
    /// The handler must translate it to the contract-documented 409 ProductErrors.SkuAlreadyExists.
    /// </summary>
    [Fact]
    [Trait("Category", "regression")]
    public async Task Handle_UniqueConstraintRaceOnSku_FailsWithSkuAlreadyExists()
    {
        // Arrange — derived FakeCatalogDbContext that lets the AnyAsync precheck pass
        // (no row tracked) and then throws UniqueConstraintException on SaveChanges,
        // mimicking the production interceptor's behaviour when a concurrent commit
        // races us at the unique index.
        var category = CatalogFactories.RootCategory();
        await using var db = ThrowOnSaveCatalogDbContext.CreateThrowing(
            new UniqueConstraintException(
                "23505: duplicate key value violates unique constraint UX_Products_Sku",
                new InvalidOperationException("ux_products_sku")));
        db.Categories.Add(category);
        await db.SaveChangesViaBaseAsync(TestContext.Current.CancellationToken);

        var handler = new CreateProductCommandHandler(
            db, TimeProvider.System, NullLogger<CreateProductCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(
            ValidCommand(category.Id), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeFailure();
        result.Errors.Should().ContainSingle(e =>
            ((DomainError)e).ErrorCode == "Product.SkuAlreadyExists"
            && e.Message.Contains("ABC-001"));
    }

    [Fact]
    public async Task Handle_SkuNormalization_StoredAsUpperInvariant()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new CreateProductCommandHandler(
            db, TimeProvider.System, NullLogger<CreateProductCommandHandler>.Instance);
        var cmd = ValidCommand(category.Id) with { Sku = "abc-002" };

        // Act
        var result = await handler.HandleAsync(cmd, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeSuccess();
        var persisted = await db.Products.FirstAsync(
            p => p.Id == result.Value, TestContext.Current.CancellationToken);
        persisted.Sku.Value.Should().Be("ABC-002");
    }
}
