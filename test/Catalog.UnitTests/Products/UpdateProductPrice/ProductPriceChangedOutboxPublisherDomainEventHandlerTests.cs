using Avro;
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

public class ProductPriceChangedOutboxPublisherDomainEventHandlerTests
{
    /// <summary>Scale pinned by the money fields in <c>ProductPriceChangedEvent.avsc</c>.</summary>
    private const int MoneyScale = 4;

    private static TopicsOptions DefaultTopics() => new()
    {
        CatalogProducts = "catalog.products",
        CatalogCategories = "catalog.categories",
        InventoryStockEvents = "inventory.stock-events",
        DltTopicSuffix = ".DLT",
    };

    [Fact]
    public async Task Handle_PriceChange_EnqueuesAvroWithOldAndNewAmounts()
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

        var occurredOn = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        // Act
        await publisher.Handle(
            new ProductPriceChangedDomainEvent
            {
                ProductId = product.Id,
                OldPrice = Money.Create(9.99m, "USD").Value,
                NewPrice = Money.Create(14.50m, "USD").Value,
                OccurredOnUtc = occurredOn,
            },
            TestContext.Current.CancellationToken);

        // Assert
        var args = outbox.ReceivedCalls().Single().GetArguments();
        args[0].Should().Be("catalog.products");
        args[1].Should().Be(product.Id.ToString());
        var avro = args[2].Should().BeOfType<AvroProductPriceChanged>().Subject;

        using (new AssertionScope())
        {
            avro.ProductId.Should().Be(product.Id);
            avro.Sku.Should().Be(product.Sku.Value);
            avro.Currency.Should().Be("USD");
            // Scale-comparing oracle, not a (decimal) cast — the cast erases scale, hiding an
            // amount emitted at the input's own scale rather than the schema's 4.
            avro.OldPriceAmount.Should().Be(new AvroDecimal(9.9900m));
            avro.OldPriceAmount.Scale.Should().Be(MoneyScale);
            avro.NewPriceAmount.Should().Be(new AvroDecimal(14.5000m));
            avro.NewPriceAmount.Scale.Should().Be(MoneyScale);
            avro.ChangedAtUtc.Should().Be(occurredOn.UtcDateTime);
        }
    }
}
