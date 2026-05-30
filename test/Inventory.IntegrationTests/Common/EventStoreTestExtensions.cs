using EntityFramework.Exceptions.PostgreSQL;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.Infrastructure.Persistence.EventStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Platform.SharedKernel.Base.DomainEvents;

namespace Inventory.IntegrationTests.Common;

/// <summary>
/// Shared helpers for event-store concurrency tests. Extracts the
/// "build an intercepted <see cref="InventoryDbContext"/> wired to the test
/// container" and "insert a raw competing row at a given version" patterns
/// that were originally duplicated across <c>EventStoreRepositoryTests</c>,
/// <c>Session2CannotOversellTests</c>, and <c>Session3ConfirmIdempotencyTests</c>.
/// </summary>
internal static class EventStoreTestExtensions
{
    /// <summary>
    /// Builds an <see cref="InventoryDbContext"/> wired to the fixture's
    /// Postgres connection with a single <see cref="OneShotConflictInterceptor"/>
    /// attached. Used by concurrency tests that need to inject a competing row
    /// between rehydrate and save on the same DbContext instance the repository
    /// is using.
    /// </summary>
    public static InventoryDbContext CreateInterceptedDbContext(
        this IntegrationTestFixture fixture,
        OneShotConflictInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(fixture.ConnectionString, npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .UseExceptionProcessor()
            .AddInterceptors(interceptor)
            .Options;

        return new InventoryDbContext(options);
    }

    /// <summary>
    /// Inserts a single <c>stock_events</c> row at the given
    /// <paramref name="streamId"/> / <paramref name="version"/> using a fresh
    /// scope's <see cref="InventoryDbContext"/>. Serializes
    /// <paramref name="event"/> via <see cref="StockEventSerializer"/> so the
    /// row is byte-identical to one produced by the production write path —
    /// used to simulate a "competing writer" that won the version race in
    /// concurrency tests.
    /// </summary>
    public static async Task InsertEventStoreRowAsync(
        this IntegrationTestFixture fixture,
        Guid streamId,
        int version,
        DomainEvent @event,
        CancellationToken ct)
    {
        using var scope = fixture.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var (eventType, payload) = StockEventSerializer.Serialize(@event);
        var row = StockEventRow.Create(
            streamId: streamId,
            version: version,
            eventType: eventType,
            payload: payload,
            occurredAtUtc: @event.OccurredOnUtc,
            correlationId: null);

        ctx.StockEvents.Add(row);
        await ctx.SaveChangesAsync(ct);
    }
}
