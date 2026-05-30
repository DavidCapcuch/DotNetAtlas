using System.Text.Json;
using System.Text.Json.Serialization;
using FluentResults.Extensions.FluentAssertions;
using Inventory.Domain.StockItems.Events;
using Inventory.Domain.StockItems.ValueObjects;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.Infrastructure.Persistence.EventStore;
using Inventory.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Platform.SharedKernel.Exceptions;

namespace Inventory.IntegrationTests.Persistence;

/// <summary>
/// Pre/post-migration replay parity test for the rename from <c>*Event</c> to
/// <c>*DomainEvent</c> on <c>inventory.stock_events.event_type</c>. Proves that:
/// (1) a row carrying an OLD discriminator string cannot be rehydrated by the
///     current <see cref="StockEventSerializer"/> (registry rejects unknown names),
/// (2) the V004 SQL UPDATE statements that constitute the data migration
///     successfully rewrite those rows to the NEW discriminator strings,
/// (3) post-migration rehydration produces aggregate state byte-identical to a
///     pristine stream seeded via the production command handlers
///     (<see cref="StockItemSeed.ActiveReservationAsync"/>).
/// The JSON payload format is identical pre/post-rename (records' fields didn't
/// change, only the CLR type name did), so the same payload that
/// <see cref="StockEventSerializer.Serialize"/> produces against the renamed
/// type would also have been produced against the pre-rename type.
/// </summary>
[Collection<IntegrationTestCollection>]
public sealed class RenameMigrationReplayTests : BaseIntegrationTest
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero);

    // Field-shape JSON must match exactly what StockEventSerializer.Serialize emits
    // for the renamed types. The serializer uses JsonSerializerDefaults.Web (camelCase)
    // with JsonStringEnumConverter — mirror that here.
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public RenameMigrationReplayTests(IntegrationTestFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task LegacyDiscriminators_FailDeserialization_ThenMigrationRestoresReplayParity()
    {
        var ct = TestContext.Current.CancellationToken;

        // ---- Arrange ----
        // Two independent streams: A is seeded with LEGACY discriminator strings
        // (Init + Receive + Reserve), simulating a pre-migration event_store row.
        // B is seeded via the production command handlers, producing the NEW
        // discriminator strings — this is the parity baseline.
        var productIdA = Guid.NewGuid();
        var reservationIdA = Guid.NewGuid();
        var orderIdA = Guid.NewGuid();

        var productIdB = Guid.NewGuid();
        var reservationIdB = Guid.NewGuid();
        var orderIdB = Guid.NewGuid();

        await Seed.ActiveReservationAsync(
            productIdB, reservationIdB, orderIdB, quantity: 3, anchorUtc: T0, ct, onHand: 10);

        await InsertLegacyRowAsync(
            streamId: productIdA, version: 1, legacyDiscriminator: "StockItemInitializedEvent",
            payload: SerializePayload(new StockItemInitializedDomainEvent
            {
                ProductId = productIdA,
                OccurredOnUtc = T0,
            }),
            occurredAtUtc: T0,
            ct);

        await InsertLegacyRowAsync(
            streamId: productIdA, version: 2, legacyDiscriminator: "StockReceivedEvent",
            payload: SerializePayload(new StockReceivedDomainEvent
            {
                ProductId = productIdA,
                Quantity = 10,
                Source = StockSource.ReceivingDock.Value,
                ReceivedByUserId = null,
                OccurredOnUtc = T0.AddMinutes(1),
            }),
            occurredAtUtc: T0.AddMinutes(1),
            ct);

        await InsertLegacyRowAsync(
            streamId: productIdA, version: 3, legacyDiscriminator: "StockReservedEvent",
            payload: SerializePayload(new StockReservedDomainEvent
            {
                ProductId = productIdA,
                ReservationId = reservationIdA,
                Quantity = 3,
                OrderId = orderIdA,
                ExpiresAtUtc = T0.AddMinutes(2) + StockItemSeed.DefaultReservationTtl,
                OccurredOnUtc = T0.AddMinutes(2),
            }),
            occurredAtUtc: T0.AddMinutes(2),
            ct);

        // ---- Assert (1): pre-migration rehydration fails on the legacy discriminators ----
        using (var preScope = Fixture.CreateScope())
        {
            var preRepo = preScope.ServiceProvider.GetRequiredService<EventStoreRepository>();
            var act = async () => await preRepo.RehydrateAsync(productIdA, ct);

            await act.Should().ThrowAsync<DataIntegrityException>()
                .Where(ex => ex.ErrorCode == "Inventory.UnknownEventType",
                    "the StockEventSerializer.EventTypeRegistry is keyed on the new *DomainEvent names; legacy rows must fail loudly");
        }

        // ---- Act: apply the V004 data UPDATE statements ----
        // Mirrors V004__RenameInventoryEventTypesToDomainEvent.sql exactly. The
        // statements are idempotent by definition (UPDATE...WHERE old → new),
        // so re-running on a fully-migrated DB is a no-op.
        await ApplyV004UpdatesAsync(ct);

        // ---- Assert (2): post-migration rehydration succeeds with the parity state ----
        using var postScope = Fixture.CreateScope();
        var postRepo = postScope.ServiceProvider.GetRequiredService<EventStoreRepository>();

        var aggregateA = await postRepo.RehydrateAsync(productIdA, ct);
        var aggregateB = await postRepo.RehydrateAsync(productIdB, ct);

        aggregateA.Version.Should().Be(aggregateB.Version, "both streams have Init + Receive + Reserve at V=3");
        aggregateA.OnHand.Should().Be(aggregateB.OnHand);
        aggregateA.Reserved.Should().Be(aggregateB.Reserved);
        aggregateA.Available.Should().Be(aggregateB.Available);
        aggregateA.Reservations.Should().HaveCount(aggregateB.Reservations.Count);

        // ---- Assert (3): discriminators on the migrated rows now match the registry ----
        using var verifyScope = Fixture.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var typesA = await verifyCtx.StockEvents
            .AsNoTracking()
            .Where(r => r.StreamId == productIdA)
            .OrderBy(r => r.Version)
            .Select(r => r.EventType)
            .ToListAsync(ct);
        typesA.Should().Equal(
            nameof(StockItemInitializedDomainEvent),
            nameof(StockReceivedDomainEvent),
            nameof(StockReservedDomainEvent));
    }

    private async Task InsertLegacyRowAsync(
        Guid streamId,
        int version,
        string legacyDiscriminator,
        string payload,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        // Direct INSERT bypassing StockEventSerializer so the legacy discriminator
        // string survives — the EF-mapped path would write nameof(...) and we want
        // the OLD name in the row to simulate a pre-migration event_store record.
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO inventory.stock_events (stream_id, version, event_type, payload, occurred_at_utc, correlation_id) "
            + "VALUES (@stream_id, @version, @event_type, @payload::jsonb, @occurred_at_utc, NULL);",
            connection);
        cmd.Parameters.AddWithValue("stream_id", streamId);
        cmd.Parameters.AddWithValue("version", version);
        cmd.Parameters.AddWithValue("event_type", legacyDiscriminator);
        cmd.Parameters.AddWithValue("payload", payload);
        cmd.Parameters.AddWithValue("occurred_at_utc", occurredAtUtc);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task ApplyV004UpdatesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(ct);

        var pairs = new[]
        {
            ("StockItemInitializedEvent", nameof(StockItemInitializedDomainEvent)),
            ("StockReceivedEvent", nameof(StockReceivedDomainEvent)),
            ("StockReservedEvent", nameof(StockReservedDomainEvent)),
            ("ReservationConfirmedEvent", nameof(ReservationConfirmedDomainEvent)),
            ("ReservationReleasedEvent", nameof(ReservationReleasedDomainEvent)),
            ("StockAdjustedEvent", nameof(StockAdjustedDomainEvent)),
        };

        foreach (var (oldName, newName) in pairs)
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE inventory.stock_events SET event_type = @new WHERE event_type = @old;",
                connection);
            cmd.Parameters.AddWithValue("new", newName);
            cmd.Parameters.AddWithValue("old", oldName);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static string SerializePayload<T>(T @event)
        where T : notnull =>
        JsonSerializer.Serialize(@event, @event.GetType(), PayloadJsonOptions);
}
