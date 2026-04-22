using Avro.Specific;
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

public class ProductCreatedOutboxPublisherTests
{
    private static CatalogTopicsOptions DefaultTopics() => new()
    {
        CatalogProducts = "catalog.products",
        CatalogCategories = "catalog.categories",
        DltTopicSuffix = ".DLT",
    };

    [Fact]
    public async Task Given_TrackedProductAndCategory_When_Handling_Then_EnqueuesAvroWithEnrichedFields()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DraftProduct(category);
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outbox = Substitute.For<ITransactionalOutbox<ICatalogDbContext>>();
        var publisher = new ProductCreatedOutboxPublisher(
            db,
            outbox,
            Options.Create(DefaultTopics()),
            NullLogger<ProductCreatedOutboxPublisher>.Instance);

        await publisher.Handle(
            new ProductCreatedDomainEvent
            {
                ProductId = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                CategoryId = product.CategoryId,
                Price = product.Price,
            },
            TestContext.Current.CancellationToken);

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
            ((decimal)avro.PriceAmount).Should().Be(product.Price.Amount);
            avro.PriceCurrency.Should().Be(product.Price.Currency.Name);
            avro.Status.Should().Be(AvroProductStatus.Draft);
            ((ISpecificRecord)avro).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Given_LongDescription_When_Handling_Then_TruncatesTo1000Chars()
    {
        await using var db = FakeCatalogDbContext.Create();
        var category = CatalogFactories.RootCategory();
        db.Categories.Add(category);
        var product = CatalogFactories.DraftProduct(category, description: new string('x', 2_500));
        db.Products.Add(product);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outbox = Substitute.For<ITransactionalOutbox<ICatalogDbContext>>();
        var publisher = new ProductCreatedOutboxPublisher(
            db,
            outbox,
            Options.Create(DefaultTopics()),
            NullLogger<ProductCreatedOutboxPublisher>.Instance);

        await publisher.Handle(
            new ProductCreatedDomainEvent
            {
                ProductId = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                CategoryId = product.CategoryId,
                Price = product.Price,
            },
            TestContext.Current.CancellationToken);

        var avro = (AvroProductCreatedEvent)outbox.ReceivedCalls().Single().GetArguments()[2]!;
        avro.Description.Length.Should().Be(1000);
    }
}
