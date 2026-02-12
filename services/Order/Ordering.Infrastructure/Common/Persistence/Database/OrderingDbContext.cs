using DotNetAtlas.ReliableMessaging.Inbox.Core;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore.Common;
using DotNetAtlas.ReliableMessaging.Outbox.Core;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using DotNetAtlas.SharedKernel.Base;
using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Data;
using Ordering.Domain.AlertSubscriptionOrders;
using SmartEnum.EFCore;

namespace Ordering.Infrastructure.Common.Persistence.Database;

public class OrderingDbContext : DbContext, IOrderingDbContext, IInboxDbContext
{
    public const string DefaultSchemaName = "ordering";

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options)
        : base(options)
    {
    }

    public DbSet<AlertSubscriptionOrder> AlertSubscriptionOrders => AggregateRootSet<AlertSubscriptionOrder>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
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
