using DotNetAtlas.ReliableMessaging.Inbox.Core;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore.Common;
using DotNetAtlas.ReliableMessaging.Outbox.Core;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using Microsoft.EntityFrameworkCore;

namespace Notifications.Common.Persistence.Database;

public class NotificationDbContext : DbContext, INotificationDbContext, IInboxDbContext
{
    public const string DefaultSchemaName = "payment";

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
