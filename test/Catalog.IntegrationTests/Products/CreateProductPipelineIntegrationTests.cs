using Catalog.Application.Categories.CreateCategory;
using Catalog.Application.Common.ReadModels;
using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products.ValueObjects;
using Catalog.Infrastructure.Persistence.Database;
using Catalog.IntegrationTests.Common;
using FluentResults.Extensions.FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS;
using Platform.ReliableMessaging.Outbox.Core;

namespace Catalog.IntegrationTests.Products;

/// <summary>
/// Full-pipeline integration test for the CQRS-on-Postgres pattern that defines Catalog's
/// teaching story (catalog.md § 9): a single <c>SaveChangesAsync</c> commits the
/// <c>Product</c> aggregate write, the <c>product_search_view</c> projection upsert,
/// AND the outbox row atomically. Hits the real <see cref="CatalogDbContext"/> on a
/// Postgres Testcontainer with all the EF mappings (OwnsOne VOs, owned image
/// collection, Money + currency converter, jsonb columns, xmin concurrency) exercised
/// end-to-end.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class CreateProductPipelineIntegrationTests : BaseIntegrationTest
{
    public CreateProductPipelineIntegrationTests(IntegrationTestFixture app)
        : base(app)
    {
    }

    [Fact]
    public async Task CreateProduct_PersistsProductAndProjectionAndOutboxAtomically()
    {
        // Arrange — first create a category so the Product has somewhere to live.
        Guid categoryId;
        using (var setupScope = Fixture.CreateScope())
        {
            var categoryHandler = setupScope.ServiceProvider
                .GetRequiredService<ICommandHandler<CreateCategoryCommand, Guid>>();
            var categoryResult = await categoryHandler.HandleAsync(
                new CreateCategoryCommand { Name = $"Electronics-{Guid.CreateVersion7():N}" },
                TestContext.Current.CancellationToken);
            categoryResult.Should().BeSuccess();
            categoryId = categoryResult.Value;
        }

        var sku = $"SKU-{Guid.CreateVersion7():N}".ToUpperInvariant()[..32];
        var command = new CreateProductCommand
        {
            Sku = sku,
            Name = "Integration-Test Widget",
            Description = "Round-trips through Postgres OwnsOne VOs.",
            CategoryId = categoryId,
            Brand = "TestBrand",
            Price = new MoneyDto { Amount = 12.34m, Currency = "USD" },
            Dimensions = new DimensionsDto { Length = 10m, Width = 5m, Height = 2m, Unit = "cm" },
            Images =
            [
                new ImageReferenceDto
                {
                    Url = "https://example.com/img.png",
                    AltText = "alt",
                    DisplayOrder = 0,
                },
            ],
        };

        // Act
        Guid productId;
        using (var actScope = Fixture.CreateScope())
        {
            var handler = actScope.ServiceProvider
                .GetRequiredService<ICommandHandler<CreateProductCommand, Guid>>();
            var result = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

            using (new AssertionScope())
            {
                result.Should().BeSuccess();
                result.Value.Should().NotBe(Guid.Empty);
            }

            productId = result.Value;
        }

        // Assert — re-resolve the DbContext from a fresh scope so we read what was
        // actually committed to Postgres, not what's still in the prior scope's change tracker.
        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        // 1. Write-model row landed with VOs flattened into single columns.
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, TestContext.Current.CancellationToken);

        // 2. Projection row landed atomically with the write.
        var projection = await db.ProductSearchView
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);

        // 3. Outbox row landed with the right topic + key + Avro CLR type name.
        var outboxRows = await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.KafkaKey == productId.ToString())
            .ToListAsync(TestContext.Current.CancellationToken);

        using (new AssertionScope())
        {
            // Write-model — every OwnsOne VO round-trips correctly.
            product.Should().NotBeNull();
            product!.Sku.Value.Should().Be(sku);
            product.Name.Value.Should().Be(command.Name);
            product.Description.Value.Should().Be(command.Description);
            product.Brand.Value.Should().Be(command.Brand);
            product.Price.Amount.Should().Be(command.Price.Amount);
            product.Price.Currency.Name.Should().Be(command.Price.Currency);
            product.Status.Should().Be(ProductStatus.Active);
            product.Dimensions.Should().NotBeNull();
            product.Dimensions!.Length.Should().Be(command.Dimensions!.Length);
            product.Images.Should().HaveCount(1);

            // Projection — populated by the in-process domain-event handler atomically.
            projection.Should().NotBeNull();
            projection!.Sku.Should().Be(sku);
            projection.Name.Should().Be(command.Name);
            projection.CategoryId.Should().Be(categoryId);
            projection.PriceAmount.Should().Be(command.Price.Amount);
            projection.PriceCurrency.Should().Be(command.Price.Currency);
            projection.Status.Should().Be(ProductStatus.Active.Name);
            projection.IsSellable.Should().BeTrue(
                "post-#177 products are Active on create and therefore sellable");

            // Outbox — exactly one row, on the products topic, with the Avro CLR type name.
            outboxRows.Should().ContainSingle()
                .Which.TopicName.Should().Be("catalog.products");
            outboxRows[0].Type.Should().Be(typeof(Catalog.Products.ProductCreatedEvent).FullName);
        }
    }
}
