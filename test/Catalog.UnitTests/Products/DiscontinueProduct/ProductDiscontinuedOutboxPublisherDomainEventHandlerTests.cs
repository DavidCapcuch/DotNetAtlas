using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Application.Products.DiscontinueProduct;
using Catalog.Domain.Products.Events;
using Catalog.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using AvroProductDiscontinuedEvent = Catalog.Products.ProductDiscontinuedEvent;

namespace Catalog.UnitTests.Products.DiscontinueProduct;

/// <summary>
/// Orchestration coverage for <see cref="ProductDiscontinuedOutboxPublisherDomainEventHandler"/>:
/// it loads the tracked aggregate and enqueues the mapped event on the correct topic keyed by the
/// product id. Field-level mapping is owned by <see cref="ProductDiscontinuedMapperTests"/>.
/// </summary>
public class ProductDiscontinuedOutboxPublisherDomainEventHandlerTests
{
    [Fact]
    public async Task Handle_Discontinuation_EnqueuesDiscontinuedEventOnProductsTopicKeyedById()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outbox = Substitute.For<ITransactionalOutbox<ICatalogDbContext>>();
        var publisher = new ProductDiscontinuedOutboxPublisherDomainEventHandler(
            db,
            outbox,
            Options.Create(new TopicsOptions
            {
                CatalogProducts = "catalog.products",
                CatalogCategories = "catalog.categories",
                InventoryStockEvents = "inventory.stock-events",
                DltTopicSuffix = ".DLT",
            }),
            NullLogger<ProductDiscontinuedOutboxPublisherDomainEventHandler>.Instance);

        // Act
        await publisher.Handle(
            new ProductDiscontinuedDomainEvent
            {
                ProductId = product.Id,
                Reason = "Supplier EOL",
                OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
            },
            TestContext.Current.CancellationToken);

        // Assert
        var args = outbox.ReceivedCalls().Single().GetArguments();
        using (new AssertionScope())
        {
            args[0].Should().Be("catalog.products");
            args[1].Should().Be(product.Id.ToString());
            var avro = args[2].Should().BeOfType<AvroProductDiscontinuedEvent>().Subject;
            avro.ProductId.Should().Be(product.Id);
        }
    }
}
