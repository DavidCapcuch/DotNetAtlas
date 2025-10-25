using DotNetAtlas.Domain.Entities.Weather.Feedback;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.Application.Common.Data;

public interface IWeatherContext
{
    DbSet<Feedback> Feedbacks { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken);
}
