using Catalog.Application.Categories.CreateCategory;
using Catalog.Application.Common.Data;
using Catalog.Application.Common.Messaging;
using Catalog.Domain.Categories.Events;
using Catalog.UnitTests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using AvroCategoryCreatedEvent = Catalog.Categories.CategoryCreatedEvent;

namespace Catalog.UnitTests.Categories.CreateCategory;

public class CategoryCreatedOutboxPublisherDomainEventHandlerTests
{
    [Fact]
    public async Task Given_TrackedCategory_Then_EnqueuesAvroWithFullFieldSet()
    {
        await using var db = FakeCatalogDbContext.Create();
        var parent = CatalogFactories.RootCategory("Electronics");
        var child = CatalogFactories.ChildCategory(parent, "Laptops");
        db.Categories.AddRange(parent, child);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var outbox = Substitute.For<ITransactionalOutbox<ICatalogDbContext>>();
        var publisher = new CategoryCreatedOutboxPublisherDomainEventHandler(
            db,
            outbox,
            Options.Create(new TopicsOptions
            {
                CatalogProducts = "catalog.products",
                CatalogCategories = "catalog.categories",
                StockLevelChangedEvent = "inventory.stock-events",
                DltTopicSuffix = ".DLT",
            }),
            NullLogger<CategoryCreatedOutboxPublisherDomainEventHandler>.Instance);

        await publisher.Handle(
            new CategoryCreatedDomainEvent
            {
                OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero),
                CategoryId = child.Id,
                Name = child.Name,
                ParentCategoryId = child.ParentCategoryId,
                Path = child.Path,
            },
            TestContext.Current.CancellationToken);

        var args = outbox.ReceivedCalls().Single().GetArguments();
        args[0].Should().Be("catalog.categories");
        args[1].Should().Be(child.Id.ToString());
        var avro = args[2].Should().BeOfType<AvroCategoryCreatedEvent>().Subject;

        using (new AssertionScope())
        {
            avro.CategoryId.Should().Be(child.Id);
            avro.Name.Should().Be("Laptops");
            avro.ParentCategoryId.Should().Be(parent.Id);
            avro.Path.Should().Be(child.Path.Value);
        }
    }
}
