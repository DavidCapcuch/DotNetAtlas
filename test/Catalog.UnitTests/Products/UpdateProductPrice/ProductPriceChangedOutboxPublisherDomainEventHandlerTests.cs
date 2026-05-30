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
    private static TopicsOptions DefaultTopics() => new()
    {
        CatalogProducts = "catalog.products",
        CatalogCategories = "catalog.categories",
        StockLevelChanged = "inventory.stock-events",
        DltTopicSuffix = ".DLT",
    };

    [Fact]
    public async Task Given_PriceChange_Then_EnqueuesAvroWithOldAndNewAmounts()
    {
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

        await publisher.Handle(
            new ProductPriceChangedDomainEvent
            {
                ProductId = product.Id,
                OldPrice = Money.Create(9.99m, "USD").Value,
                NewPrice = Money.Create(14.50m, "USD").Value,
                OccurredOnUtc = occurredOn,
            },
            TestContext.Current.CancellationToken);

        var args = outbox.ReceivedCalls().Single().GetArguments();
        args[0].Should().Be("catalog.products");
        args[1].Should().Be(product.Id.ToString());
        var avro = args[2].Should().BeOfType<AvroProductPriceChanged>().Subject;

        using (new AssertionScope())
        {
            avro.ProductId.Should().Be(product.Id);
            avro.Sku.Should().Be(product.Sku.Value);
            avro.Currency.Should().Be("USD");
            ((decimal)avro.OldPriceAmount).Should().Be(9.99m);
            ((decimal)avro.NewPriceAmount).Should().Be(14.50m);
            avro.ChangedAtUtc.Should().Be(occurredOn.UtcDateTime);
        }
    }
}
