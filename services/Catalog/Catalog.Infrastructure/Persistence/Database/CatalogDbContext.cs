using Catalog.Application.Common.Data;
using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Categories;
using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Base;
using SmartEnum.EFCore;

namespace Catalog.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Catalog bounded context. Implements the
/// <see cref="ICatalogDbContext"/> application port and <see cref="IInboxDbContext"/> so the
/// <c>StockLevelChangedEvent</c> Kafka consumer can dedup messages atomically with the projection
/// update (ADR-0008 + reliable messaging).
/// </summary>
/// <remarks>
/// The materialized read view <see cref="ProductSearchViewRow"/> is a separate
/// <see cref="DbSet{TEntity}"/> on the SAME context as the write-model aggregates. This is the
/// CQRS-projection-on-Postgres pattern from <c>catalog.md § 9</c>: a single
/// <c>SaveChangesAsync</c> commits the aggregate change AND the projection upsert in the same
/// transaction; downstream BCs see eventually-consistent Kafka events but Catalog's own search
/// view is immediately consistent.
/// </remarks>
public sealed class CatalogDbContext : DbContext, ICatalogDbContext, IInboxDbContext
{
    /// <summary>Default Postgres schema for all Catalog tables.</summary>
    public const string DefaultSchemaName = "catalog";

    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public DbSet<Product> Products => AggregateRootSet<Product>();

    /// <inheritdoc />
    public DbSet<Category> Categories => AggregateRootSet<Category>();

    /// <inheritdoc />
    public DbSet<ProductSearchViewRow> ProductSearchView => Set<ProductSearchViewRow>();

    /// <inheritdoc />
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <inheritdoc />
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        modelBuilder.ConfigureOutbox(DefaultSchemaName);
        modelBuilder.ConfigureInbox(DefaultSchemaName);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureSmartEnum();
    }

    private DbSet<T> AggregateRootSet<T>()
        where T : class, IAggregateRoot => Set<T>();
}
