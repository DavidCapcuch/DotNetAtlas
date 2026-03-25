using Microsoft.EntityFrameworkCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Weather.Domain.Alerts;
using Weather.Domain.Alerts.Entities;

namespace Weather.Application.Common.Data;

public interface IWeatherDbContext : IOutboxDbContext
{
    DbSet<Domain.Feedback.Feedback> Feedbacks { get; }
    DbSet<AlertSubscriber> AlertSubscribers { get; }
    DbSet<Location> Locations { get; }
    DbSet<MonitoredLocation> MonitoredLocations { get; }
}
