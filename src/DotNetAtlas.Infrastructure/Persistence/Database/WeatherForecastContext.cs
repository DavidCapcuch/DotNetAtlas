using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.Domain.Entities.Weather.Feedback;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Infrastructure.Persistence.Database;

public class WeatherForecastContext : DbContext, IWeatherForecastContext
{
    public WeatherForecastContext(DbContextOptions<WeatherForecastContext> options)
        : base(options)
    {
    }

    public DbSet<WeatherFeedback> WeatherFeedbacks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly)
            .HasDefaultSchema("weather");
    }
}
