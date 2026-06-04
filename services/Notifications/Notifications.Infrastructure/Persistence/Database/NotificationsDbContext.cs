using Microsoft.EntityFrameworkCore;
using Notifications.Application.Common.Data;
using Notifications.Domain.Deliveries;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Infrastructure.Persistence.Database;

public sealed class NotificationsDbContext : DbContext, INotificationsDbContext, IInboxDbContext
{
    public const string DefaultSchemaName = "notifications";

    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : base(options)
    {
    }

    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // v2 (#312) introduced the first EF-persisted domain table (notification_deliveries),
        // so the assembly scan is back (mirrors Catalog/Invoicing/etc.). The platform Inbox /
        // Outbox tables are configured via their dedicated extension methods below.
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        modelBuilder.ConfigureOutbox(DefaultSchemaName);
        modelBuilder.ConfigureInbox(DefaultSchemaName);
    }
}
