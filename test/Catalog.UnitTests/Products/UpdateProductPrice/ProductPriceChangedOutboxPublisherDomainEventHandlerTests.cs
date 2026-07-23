using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Application.Products.UpdateProductPrice;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.SharedKernel.ValueObjects;
using AvroProductPriceChanged = Catalog.Products.ProductPriceChangedEvent;

namespace Catalog.UnitTests.Products.UpdateProductPrice;

/// <summary>
/// Orchestration coverage for <see cref="ProductPriceChangedOutboxPublisherDomainEventHandler"/>:
/// it loads the tracked aggregate and enqueues the mapped event on the correct topic keyed by the
/// product id. Field-level mapping is owned by <see cref="ProductPriceChangedMapperTests"/>.
/// </summary>
public class ProductPriceChangedOutboxPublisherDomainEventHandlerTests
{
    private static TopicsOptions DefaultTopics() => new()
    {
        CatalogProducts = "catalog.products",
        CatalogCategories = "catalog.categories",
        InventoryStockEvents = "inventory.stock-events",
        DltTopicSuffix = ".DLT",
    };

    [Fact]
    public async Task Handle_PriceChange_EnqueuesPriceChangedEventOnProductsTopicKeyedById()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outbox = Substitute.For<ITransactionalOutbox<ICatalogDbContext>>();
        var publisher = new ProductPriceChangedOutboxPublisherDomainEventHandler(
            db,
            outbox,
            Options.Create(DefaultTopics()),
            NullLogger<ProductPriceChangedOutboxPublisherDomainEventHandler>.Instance);

        // Act
        await publisher.Handle(
            new ProductPriceChangedDomainEvent
            {
                ProductId = product.Id,
                OldPrice = Money.Create(9.99m, "USD").Value,
                NewPrice = Money.Create(14.50m, "USD").Value,
                OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
            },
            TestContext.Current.CancellationToken);

        // Assert
        var args = outbox.ReceivedCalls().Single().GetArguments();
        using (new AssertionScope())
        {
            args[0].Should().Be("catalog.products");
            args[1].Should().Be(product.Id.ToString());
            var avro = args[2].Should().BeOfType<AvroProductPriceChanged>().Subject;
            avro.ProductId.Should().Be(product.Id);
        }
    }
}
