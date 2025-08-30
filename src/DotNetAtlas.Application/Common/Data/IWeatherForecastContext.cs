using DotNetAtlas.Domain.Entities.Weather;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Application.Common.Data
{
    public interface IWeatherForecastContext
    {
        DbSet<WeatherFeedback> WeatherFeedbacks { get; set; }
        Task<int> SaveChangesAsync(CancellationToken cancellationToken);
        Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken);
    }
}