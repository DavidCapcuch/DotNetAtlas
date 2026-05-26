using Microsoft.EntityFrameworkCore;
using Notifications.Application.Common.Data;
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

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Notifications has no domain aggregates persisted via EF — the only tables
        // owned by this BC are the platform Inbox / Outbox below, configured via the
        // dedicated extension methods. Skipping ApplyConfigurationsFromAssembly avoids
        // the noisy "No instantiatable types implementing `IEntityTypeConfiguration` were
        // found" warning on every cold start. When the first domain entity lands, add
        // an `EntityConfigurations/` folder mirroring Catalog/Invoicing/etc. and
        // reintroduce the scan call here.
        modelBuilder.HasDefaultSchema(DefaultSchemaName);

        modelBuilder.ConfigureOutbox(DefaultSchemaName);
        modelBuilder.ConfigureInbox(DefaultSchemaName);
    }
}
