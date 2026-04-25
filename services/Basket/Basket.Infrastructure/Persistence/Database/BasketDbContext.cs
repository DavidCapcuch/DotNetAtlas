using Basket.Application.Common.Data;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Basket.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Basket bounded context's SQL side-car.
/// Implements the <see cref="IBasketDbContext"/> application port (which
/// extends <c>IOutboxDbContext</c>) and the <see cref="IInboxDbContext"/>
/// interface so that future consumers (e.g. a Catalog price-invalidation
/// inbox) can share the same EF scope + transaction as outbox writes.
/// </summary>
/// <remarks>
/// <para>
/// Per [ADR-0003](../../../../docs/adr/0003-basket-as-technical-bc.md), Basket
/// is a technical bounded context whose aggregate lives in <c>redis-basket</c>,
/// not Postgres. The sole purpose of this DbContext is the transactional
/// outbox + inbox; it carries the <c>OutboxMessages</c> and <c>InboxMessages</c>
/// tables and nothing else. An architecture test in M7 asserts that the type
/// never exposes a <c>DbSet&lt;Basket&gt;</c>.
/// </para>
/// <para>
/// The <c>basket</c> schema name matches the relay configuration pinned in
/// <c>docker-compose.yaml</c> (<c>OutboxRelay__SchemaName=basket</c>); the
/// relay container tails the <c>basket.OutboxMessages</c> table and republishes
/// the Avro payload to the <c>basket.sessions</c> Kafka topic.
/// </para>
/// </remarks>
public sealed class BasketDbContext : DbContext, IBasketDbContext, IInboxDbContext
{
    /// <summary>Default Postgres schema for all Basket SQL tables.</summary>
    public const string DefaultSchemaName = "basket";

    public BasketDbContext(DbContextOptions<BasketDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultSchemaName);

        modelBuilder.ConfigureOutbox(DefaultSchemaName);
        modelBuilder.ConfigureInbox(DefaultSchemaName);
    }
}
