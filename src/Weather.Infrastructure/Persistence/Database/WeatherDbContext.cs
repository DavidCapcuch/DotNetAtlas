using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Inbox.Core;
using Platform.ReliableMessaging.Inbox.EFCore;
using Platform.ReliableMessaging.Inbox.EFCore.Common;
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Platform.SharedKernel.Base;
using SmartEnum.EFCore;
using Weather.Application.Common.Data;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.Entities;

namespace Weather.Infrastructure.Persistence.Database;

public class WeatherDbContext : DbContext, IWeatherDbContext, IInboxDbContext
{
    public const string DefaultSchemaName = "weather";

    public WeatherDbContext(DbContextOptions<WeatherDbContext> options)
        : base(options)
    {
    }

    public DbSet<Weather.Domain.Feedback.Feedback> Feedbacks => AggregateRootSet<Weather.Domain.Feedback.Feedback>();
    public DbSet<AlertSubscriber> AlertSubscribers => AggregateRootSet<AlertSubscriber>();
    public DbSet<MonitoredLocation> MonitoredLocations => AggregateRootSet<MonitoredLocation>();
    public DbSet<MonitoredLocationAlertsSubscription> MonitoredLocationAlertsSubscriptions => Set<MonitoredLocationAlertsSubscription>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema(DefaultSchemaName);

        // src/Weather is deprecated reference scaffolding (slated for deletion) and is intentionally
        // NOT part of the snake_case migration regeneration. Pin its outbox/inbox table names to the
        // pre-existing PascalCase identifiers so the platform default flip to snake_case
        // (outbox_messages / inbox_messages) does not desync this BC's model from its frozen migration.
        modelBuilder.ConfigureOutbox(DefaultSchemaName, "OutboxMessages");
        modelBuilder.ConfigureInbox(DefaultSchemaName, "InboxMessages");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.ConfigureSmartEnum();
    }

    private DbSet<T> AggregateRootSet<T>()
        where T : class, IAggregateRoot => Set<T>();
}
