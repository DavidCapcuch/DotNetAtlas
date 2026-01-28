using DotNetAtlas.Domain.Alerts;
using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Feedback;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Application.Common.Data;

public interface IWeatherDbContext : IOutboxDbContext
{
    DbSet<Feedback> Feedbacks { get; }
    DbSet<AlertSubscriber> AlertSubscribers { get; }
    DbSet<Location> Locations { get; }
    DbSet<MonitoredLocation> MonitoredLocations { get; }
}
