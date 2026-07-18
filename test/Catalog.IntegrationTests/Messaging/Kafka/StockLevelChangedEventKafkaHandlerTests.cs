using Catalog.Application.Common.ReadModels;
using Catalog.Infrastructure.Messaging.Kafka.StockEvents;
using Catalog.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.Test.Framework.Kafka;
using AvroStockLevelChangedEvent = Inventory.Stock.StockLevelChangedEvent;

namespace Catalog.IntegrationTests.Messaging.Kafka;

/// <summary>
/// Acceptance for the Kafka <b>adapter</b> <see cref="StockLevelChangedEventKafkaHandler"/> — the
/// message entrance for Inventory's <c>StockLevelChangedEvent</c>. Drives the handler with the real
/// Avro contract type via <see cref="FakeKafkaMessageContext"/> and asserts the observable outcome on
/// <c>product_search_view.IsSellable</c> against real Postgres.
/// <para>
/// Scope is the adapter, not the projection decision logic (that is exhaustively covered by the unit
/// tier, <c>StockLevelChangedEventProjectionHandlerTests</c>): each case pins something only the
/// adapter can get wrong — Avro→domain unwrap of the correct fields (<c>NewAvailable</c>, not
/// <c>NewOnHand</c>), forwarding the message's <c>ProductId</c>, handler resolution/dispatch, and
/// honouring <c>ConsumerContext.WorkerStopped</c> cancellation.
/// </para>
/// <para>
/// Out of scope, by design: the Kafka transport ahead of the handler (topic subscription, Avro
/// Schema-Registry deserialization, inbox dedup) is bypassed — the handler is driven directly with the
/// real contract type, the repo's sanctioned message entrance. The 30s <c>PerMessageBudget</c> expiry
/// is also unverified: <c>CancelAfter</c> runs on the system timer, so a deterministic test needs the
/// adapter to build its CTS from an injected <c>TimeProvider</c> (follow-up).
/// </para>
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class StockLevelChangedEventKafkaHandlerTests : BaseIntegrationTest
{
    private static readonly DateTime ChangedAtUtc =
        new(2026, 5, 23, 10, 0, 0, DateTimeKind.Utc);

    public StockLevelChangedEventKafkaHandlerTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_ActiveProductAvailableGoesPositive_FlipsIsSellableTrue()
    {
        // Arrange — an Active product not yet sellable (Inventory has not reported positive stock).
        var row = ProductSearchViewRowBuilder.Active("STK-POS");
        row.IsSellable = false;
        await SeedRowsAsync(row);

        // Happy-path flip: an Active product becomes sellable once availability goes positive. Both
        // OnHand and Available are positive here, so this case alone can't distinguish the two fields —
        // the NewAvailable-vs-NewOnHand swap is test 2's job.
        var avroEvent = BuildStockLevelChanged(row.ProductId, onHand: 12, reserved: 5, available: 7);

        // Act — deliver through the message entrance.
        await DeliverAsync(avroEvent);

        // Assert
        (await IsSellableAsync(row.ProductId)).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ActiveProductAvailableGoesZero_FlipsIsSellableFalse()
    {
        // Arrange — an Active product currently sellable, so a drop to zero availability has a flip to make.
        var row = ProductSearchViewRowBuilder.Active("STK-ZERO");
        row.IsSellable = true;
        await SeedRowsAsync(row);

        // OnHand stays positive while Available hits zero (everything reserved): the row must go
        // NON-sellable, which only holds if the adapter forwards NewAvailable (0) rather than NewOnHand (5).
        var avroEvent = BuildStockLevelChanged(row.ProductId, onHand: 5, reserved: 5, available: 0);

        // Act
        await DeliverAsync(avroEvent);

        // Assert
        (await IsSellableAsync(row.ProductId)).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UnknownProductId_LeavesReadModelUnchanged()
    {
        // Arrange — one projected product; the event targets a DIFFERENT id Catalog has not projected.
        var existing = ProductSearchViewRowBuilder.Active("STK-EXISTS");
        existing.IsSellable = false;
        await SeedRowsAsync(existing);

        var unknownProductId = Guid.CreateVersion7();
        var avroEvent = BuildStockLevelChanged(unknownProductId, onHand: 99, reserved: 0, available: 99);

        // Act — graceful degradation: an unknown ProductId is a no-op, not an insert or a throw.
        await DeliverAsync(avroEvent);

        // Assert — the unknown-id delivery is a graceful no-op against real Postgres: no row inserted, no
        // FK violation thrown, and the co-resident row left untouched. (The ProductId-is-forwarded guard
        // lives in tests 1/2, which require the flip on a specifically seeded id.)
        using (new AssertionScope())
        {
            (await DbContext.ProductSearchView.AsNoTracking()
                    .AnyAsync(r => r.ProductId == unknownProductId, TestContext.Current.CancellationToken))
                .Should().BeFalse();
            (await IsSellableAsync(existing.ProductId)).Should().BeFalse();
            (await DbContext.ProductSearchView.AsNoTracking()
                    .CountAsync(TestContext.Current.CancellationToken))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task Handle_WorkerStoppedAlreadyCancelled_AbortsProjection()
    {
        // Arrange — a row the delivery WOULD flip sellable if it ran to completion.
        var row = ProductSearchViewRowBuilder.Active("STK-CANCEL");
        row.IsSellable = false;
        await SeedRowsAsync(row);

        var avroEvent = BuildStockLevelChanged(row.ProductId, onHand: 10, reserved: 0, available: 7);

        using var workerStopped = new CancellationTokenSource();
        await workerStopped.CancelAsync();

        using var scope = Fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<StockLevelChangedEventKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            origin: "Inventory", cancellationToken: workerStopped.Token);

        // Act — an already-stopped worker must abort the projection: the adapter threads
        // ConsumerContext.WorkerStopped into the token the projector observes, so the query is cancelled
        // before it writes. This pins the WorkerStopped half of the per-message CTS only — not the 30s
        // PerMessageBudget expiry (see the class summary).
        var act = () => handler.Handle(context, avroEvent);

        // Assert — cancellation propagates to the caller (in production KafkaFlow uses this to skip the
        // offset commit) and no projection write landed.
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<OperationCanceledException>();
            (await IsSellableAsync(row.ProductId)).Should().BeFalse();
        }
    }

    private async Task DeliverAsync(AvroStockLevelChangedEvent avroEvent)
    {
        using var scope = Fixture.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<StockLevelChangedEventKafkaHandler>();
        var context = FakeKafkaMessageContext.Create(
            origin: "Inventory", cancellationToken: TestContext.Current.CancellationToken);

        await handler.Handle(context, avroEvent);
    }

    private Task<bool> IsSellableAsync(Guid productId) =>
        DbContext.ProductSearchView.AsNoTracking()
            .Where(r => r.ProductId == productId)
            .Select(r => r.IsSellable)
            .SingleAsync(TestContext.Current.CancellationToken);

    private async Task SeedRowsAsync(params ProductSearchViewRow[] rows)
    {
        var seeder = new CatalogReadModelSeeder(DbContext);
        await seeder.SeedRowsAsync(TestContext.Current.CancellationToken, rows);
    }

    private static AvroStockLevelChangedEvent BuildStockLevelChanged(
        Guid productId, int onHand, int reserved, int available) =>
        new()
        {
            ProductId = productId,
            NewOnHand = onHand,
            NewReserved = reserved,
            NewAvailable = available,
            ChangedAtUtc = ChangedAtUtc,
        };
}
