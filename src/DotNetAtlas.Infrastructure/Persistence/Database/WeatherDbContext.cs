using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Domain.Alerts;
using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Feedback;
using DotNetAtlas.ReliableMessaging.Inbox.Core;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore.Common;
using DotNetAtlas.ReliableMessaging.Outbox.Core;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using DotNetAtlas.SharedKernel.Base;
using Microsoft.EntityFrameworkCore;
using SmartEnum.EFCore;

namespace DotNetAtlas.Infrastructure.Persistence.Database;

public class WeatherDbContext : DbContext, IWeatherDbContext, IInboxDbContext
{
    public const string DefaultSchemaName = "weather";

    public WeatherDbContext(DbContextOptions<WeatherDbContext> options)
        : base(options)
    {
    }

    public DbSet<Feedback> Feedbacks => AggregateRootSet<Feedback>();
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
