using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Domain.Common;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using DotNetAtlas.Outbox.Core;
using DotNetAtlas.Outbox.EntityFrameworkCore.EntityConfiguration;
using DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Infrastructure.Persistence.Database;

public class WeatherDbContext : DbContext, IWeatherDbContext, IOutboxDbContext
{
    public WeatherDbContext(DbContextOptions<WeatherDbContext> options)
        : base(options)
    {
    }

    public DbSet<Feedback> Feedbacks => AggregateRootSet<Feedback>();
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema("weather");

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration("weather"));
    }

    private DbSet<T> AggregateRootSet<T>()
        where T : class, IAggregateRoot => Set<T>();
}
