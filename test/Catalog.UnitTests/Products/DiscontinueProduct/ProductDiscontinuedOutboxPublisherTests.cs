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

public class ProductDiscontinuedOutboxPublisherTests
{
    [Fact]
    public async Task Given_Discontinuation_Then_EnqueuesAvroWithReasonAndSku()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.ActiveProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outbox = Substitute.For<ITransactionalOutbox<ICatalogDbContext>>();
        var publisher = new ProductDiscontinuedOutboxPublisher(
            db,
            outbox,
            Options.Create(new CatalogTopicsOptions
            {
                CatalogProducts = "catalog.products",
                CatalogCategories = "catalog.categories",
                DltTopicSuffix = ".DLT",
            }),
            NullLogger<ProductDiscontinuedOutboxPublisher>.Instance);

        var occurredOn = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero);

        await publisher.Handle(
            new ProductDiscontinuedDomainEvent
            {
                ProductId = product.Id,
                Reason = "Supplier EOL",
                OccurredOnUtc = occurredOn,
            },
            TestContext.Current.CancellationToken);

        var args = outbox.ReceivedCalls().Single().GetArguments();
        args[0].Should().Be("catalog.products");
        var avro = args[2].Should().BeOfType<AvroProductDiscontinuedEvent>().Subject;

        using (new AssertionScope())
        {
            avro.ProductId.Should().Be(product.Id);
            avro.Sku.Should().Be(product.Sku.Value);
            avro.Reason.Should().Be("Supplier EOL");
            avro.DiscontinuedAtUtc.Should().Be(occurredOn.UtcDateTime);
        }
    }
}
