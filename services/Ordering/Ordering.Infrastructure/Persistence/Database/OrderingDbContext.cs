using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Data;
using Ordering.Domain.Orders;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Base;
using SmartEnum.EFCore;

namespace Ordering.Infrastructure.Persistence.Database;

/// <summary>
/// EF Core DbContext for the Ordering bounded context. Implements the
/// <see cref="IOrderingDbContext"/> application port and <see cref="IInboxDbContext"/>
/// so saga-command consumers can participate in the inbox-dedup + outbox-write
/// transaction (reliable messaging).
/// </summary>
public sealed class OrderingDbContext : DbContext, IOrderingDbContext, IInboxDbContext
{
    /// <summary>Default Postgres schema for all Ordering tables.</summary>
    public const string DefaultSchemaName = "ordering";

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    public DbSet<Order> Orders => AggregateRootSet<Order>();

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
