using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Common.ValueObjects;
using FluentResults;

namespace DotNetAtlas.Domain.Common.Services;

/// <summary>
/// Domain interface for geographic location operations.
/// Implemented by infrastructure layer.
/// </summary>
public interface IGeocodingProvider
{
    /// <summary>
    /// Gets geographic coordinates for a validated Location entity.
    /// </summary>
    Task<Result<GeoCoordinates>> GetCoordinatesAsync(Location location, CancellationToken ct);

    /// <summary>
    /// Gets geographic coordinates for a City/CountryCode pair.
    /// </summary>
    Task<Result<GeoCoordinates>> GetCoordinatesAsync(City city, CountryCode countryCode, CancellationToken ct);

    /// <summary>
    /// Validates that a city exists and is in the specified country.
    /// </summary>
    Task<Result> ValidateLocationAsync(City city, CountryCode countryCode, CancellationToken ct);
}
