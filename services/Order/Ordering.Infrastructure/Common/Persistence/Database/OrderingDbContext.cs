using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Data;
using Ordering.Domain.AlertSubscriptionOrders;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Base;
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
