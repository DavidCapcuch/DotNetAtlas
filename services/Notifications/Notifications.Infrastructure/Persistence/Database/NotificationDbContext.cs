using Microsoft.EntityFrameworkCore;
using Notifications.Application.Common.Data;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Infrastructure.Persistence.Database;

public class NotificationDbContext : DbContext, INotificationDbContext, IInboxDbContext
{
    public const string DefaultSchemaName = "notifications";

    public NotificationDbContext(DbContextOptions<NotificationDbContext> options)
        : base(options)
    {
    }

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        modelBuilder.ConfigureOutbox(DefaultSchemaName);
        modelBuilder.ConfigureInbox(DefaultSchemaName);
    }
}
