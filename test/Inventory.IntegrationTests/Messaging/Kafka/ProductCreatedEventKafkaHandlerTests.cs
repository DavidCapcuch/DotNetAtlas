using Avro;
using Inventory.Infrastructure.Messaging.Kafka.StockInit;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Test.Framework.Kafka;
using AvroProductCreatedEvent = Catalog.Products.ProductCreatedEvent;
using AvroProductStatus = Catalog.Products.ProductStatus;

namespace Inventory.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Acceptance for <see cref="ProductCreatedEventKafkaHandler"/>. The
/// first delivery initializes a fresh event-sourced stream
/// (<c>StockItemInitializedEvent</c> at version 1); a duplicate delivery is
/// a no-op (Application-layer guard <c>Version &gt; 0</c> returns
/// <c>Result.Ok</c> without appending).
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class ProductCreatedEventKafkaHandlerTests : BaseIntegrationTest
{
    private static readonly DateTime UtcNow =
        new(2026, 4, 25, 13, 0, 0, DateTimeKind.Utc);

    public ProductCreatedEventKafkaHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task NewProduct_StreamInitialized()
    {
        var productId = Guid.NewGuid();
        var avroEvent = BuildAvroProductCreated(productId);

        using var scope = Fixture.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProductCreatedEventKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            origin: "Catalog",
            cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(context, avroEvent);

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var streamRows = await db.StockEvents
            .AsNoTracking()
            .Where(r => r.StreamId == productId)
            .OrderBy(r => r.Version)
            .ToListAsync(TestContext.Current.CancellationToken);
        streamRows.Should().ContainSingle()
            .Which.EventType.Should().Be("StockItemInitializedEvent");

        var levels = await db.CurrentStockLevels
            .AsNoTracking()
            .FirstAsync(r => r.ProductId == productId, TestContext.Current.CancellationToken);
        levels.OnHand.Should().Be(0);
        levels.Reserved.Should().Be(0);
        levels.Available.Should().Be(0);
    }

    [Fact]
    public async Task DuplicateDelivery_IsIdempotentNoOp()
    {
        var productId = Guid.NewGuid();
        var avroEvent = BuildAvroProductCreated(productId);

        // First delivery -> stream initialized.
        using (var scope = Fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ProductCreatedEventKafkaHandler>();
            await handler.Handle(
                FakeKafkaMessageContext.Create(origin: "Catalog", cancellationToken: TestContext.Current.CancellationToken),
                avroEvent);
        }

        // Second delivery -> aggregate's Version > 0 guard kicks in;
        // application handler returns Result.Ok with no event appended.
        using (var scope = Fixture.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<ProductCreatedEventKafkaHandler>();
            await handler.Handle(
                FakeKafkaMessageContext.Create(origin: "Catalog", cancellationToken: TestContext.Current.CancellationToken),
                avroEvent);
        }

        using var verifyScope = Fixture.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var rowCount = await db.StockEvents
            .AsNoTracking()
            .CountAsync(r => r.StreamId == productId, TestContext.Current.CancellationToken);
        rowCount.Should().Be(1);
    }

    private static AvroProductCreatedEvent BuildAvroProductCreated(Guid productId) =>
        new()
        {
            ProductId = productId,
            Sku = "SKU-M5-TEST",
            Name = "M5 test product",
            Description = "M5 test description",
            CategoryId = Guid.NewGuid(),
            CategoryPath = "/test",
            BrandName = "TestBrand",
            PriceAmount = new AvroDecimal(99.99m),
            PriceCurrency = "USD",
            Status = AvroProductStatus.Active,
            CreatedAtUtc = UtcNow,
        };
}
