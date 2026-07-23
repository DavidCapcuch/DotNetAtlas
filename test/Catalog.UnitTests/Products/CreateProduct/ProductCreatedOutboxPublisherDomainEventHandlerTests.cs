using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Application.Products.CreateProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using AvroProductCreatedEvent = Catalog.Products.ProductCreatedEvent;

namespace Catalog.UnitTests.Products.CreateProduct;

/// <summary>
/// Orchestration coverage for <see cref="ProductCreatedOutboxPublisherDomainEventHandler"/>: it
/// loads the tracked product and its category, then enqueues the mapped event on the correct topic
/// keyed by the product id. Field-level mapping is owned by <see cref="ProductCreatedMapperTests"/>.
/// </summary>
public class ProductCreatedOutboxPublisherDomainEventHandlerTests
{
    private static TopicsOptions DefaultTopics() => new()
    {
        CatalogProducts = "catalog.products",
        CatalogCategories = "catalog.categories",
        InventoryStockEvents = "inventory.stock-events",
        DltTopicSuffix = ".DLT",
    };

    [Fact]
    public async Task Handle_TrackedProductAndCategory_EnqueuesCreatedEventOnProductsTopicKeyedById()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outbox = Substitute.For<ITransactionalOutbox<ICatalogDbContext>>();
        var publisher = new ProductCreatedOutboxPublisherDomainEventHandler(
            db,
            outbox,
            Options.Create(DefaultTopics()),
            NullLogger<ProductCreatedOutboxPublisherDomainEventHandler>.Instance);

        // Act
        await publisher.Handle(
            new ProductCreatedDomainEvent
            {
                OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
                ProductId = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                CategoryId = product.CategoryId,
                Price = product.Price,
            },
            TestContext.Current.CancellationToken);

        // Assert
        var args = outbox.ReceivedCalls().Single().GetArguments();
        using (new AssertionScope())
        {
            args[0].Should().Be("catalog.products");
            args[1].Should().Be(product.Id.ToString());
            var avro = args[2].Should().BeOfType<AvroProductCreatedEvent>().Subject;
            avro.ProductId.Should().Be(product.Id);
            avro.CategoryPath.Should().Be(category.Path.Value);
        }
    }
}
