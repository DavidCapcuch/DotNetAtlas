using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Common.ValueObjects;
using FluentResults;

namespace DotNetAtlas.Domain.Common.Services;

/// <summary>
/// Domain service for creating validated Location entities.
/// Validates location via an external geo provider before creation.
/// </summary>
public class LocationFactory
{
    private readonly IGeocodingProvider _geocodingProvider;

    public LocationFactory(IGeocodingProvider geocodingProvider)
    {
        _geocodingProvider = geocodingProvider;
    }

    /// <summary>
    /// Creates a new Location entity after validating it exists via geo provider.
    /// </summary>
    /// <param name="cityName">The city name.</param>
    /// <param name="countryCode">The country code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A validated Location entity or validation errors.</returns>
    public async Task<Result<Location>> CreateAsync(
        string cityName,
        CountryCode countryCode,
        CancellationToken ct)
    {
        var cityResult = City.Create(cityName);
        if (cityResult.IsFailed)
        {
            return Result.Fail(cityResult.Errors);
        }

        var validationResult = await _geocodingProvider.ValidateLocationAsync(cityResult.Value, countryCode, ct);
        if (validationResult.IsFailed)
        {
            return Result.Fail(validationResult.Errors);
        }

        return Location.From(cityResult.Value, countryCode);
    }
}
