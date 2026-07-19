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
using AvroProductStatus = Catalog.Products.ProductStatus;

namespace Catalog.UnitTests.Products.CreateProduct;

public class ProductCreatedOutboxPublisherDomainEventHandlerTests
{
    /// <summary>Scale pinned by <c>PriceAmount</c> in <c>ProductCreatedEvent.avsc</c>.</summary>
    private const int MoneyScale = 4;

    private static TopicsOptions DefaultTopics() => new()
    {
        CatalogProducts = "catalog.products",
        CatalogCategories = "catalog.categories",
        InventoryStockEvents = "inventory.stock-events",
        DltTopicSuffix = ".DLT",
    };

    [Fact]
    public async Task Handle_TrackedProductAndCategory_EnqueuesAvroWithEnrichedFields()
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
        var received = outbox.ReceivedCalls().Single();
        var args = received.GetArguments();
        args[0].Should().Be("catalog.products");
        args[1].Should().Be(product.Id.ToString());
        var avro = args[2].Should().BeOfType<AvroProductCreatedEvent>().Subject;

        using (new AssertionScope())
        {
            avro.ProductId.Should().Be(product.Id);
            avro.Sku.Should().Be(product.Sku.Value);
            avro.Name.Should().Be(product.Name.Value);
            avro.Description.Should().Be(product.Description.Value);
            avro.CategoryId.Should().Be(product.CategoryId);
            avro.CategoryPath.Should().Be(category.Path.Value);
            avro.BrandName.Should().Be(product.Brand.Value);
            // Value via the cast, scale asserted separately: the (decimal) cast erases scale, so
            // it alone would not notice the amount being emitted at the input's scale instead of
            // the schema's 4 — which Avro rejects at encode time.
            ((decimal)avro.PriceAmount).Should().Be(product.Price.Amount);
            avro.PriceAmount.Scale.Should().Be(MoneyScale);
            avro.PriceCurrency.Should().Be(product.Price.Currency.Name);
            avro.Status.Should().Be(AvroProductStatus.Active);
        }
    }

    [Fact]
    public async Task Handle_LongDescription_TruncatesTo1000Chars()
    {
        // Arrange
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category, description: new string('x', 2_500));
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
        var avro = (AvroProductCreatedEvent)outbox.ReceivedCalls().Single().GetArguments()[2]!;
        avro.Description.Length.Should().Be(1000);
    }
}
