using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Infrastructure.Persistence.Database;

public class WeatherContext : DbContext, IWeatherContext
{
    public WeatherContext(DbContextOptions<WeatherContext> options)
        : base(options)
    {
    }

    public DbSet<Feedback> Feedbacks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema("weather");
    }
}
