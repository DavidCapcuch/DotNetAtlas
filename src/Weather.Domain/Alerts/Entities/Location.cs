using System.Runtime.CompilerServices;
using FluentResults;
using Platform.SharedKernel.Base;
using Weather.Domain.Common.ValueObjects;

[assembly: InternalsVisibleTo("Weather.UnitTests")]
[assembly: InternalsVisibleTo("Weather.IntegrationTests")]
[assembly: InternalsVisibleTo("Weather.FunctionalTests")]

namespace Weather.Domain.Alerts.Entities;

/// <summary>
/// Represents a geographic location with validated city and country information.
/// Implemented as an entity, so it can be shared across aggregates (Forecast, Alerts).
/// </summary>
public sealed class Location : Entity<Guid>, IAuditableEntity
{
    public City City { get; private set; } = null!;

    public CountryCode CountryCode { get; private set; }

    private Location()
    {
    }

    /// <summary>
    /// Creates a Location from a city name string and CountryCode.
    /// Internal - use LocationFactory.CreateAsync() for validated creation with geo provider.
    /// </summary>
    internal static Result<Location> Create(
        string city,
        CountryCode countryCode)
    {
        var cityResult = City.Create(city);
        if (cityResult.IsFailed)
        {
            return Result.Fail(cityResult.Errors);
        }

        return From(cityResult.Value, countryCode);
    }

    /// <summary>
    /// Creates a Location from validated City and CountryCode.
    /// Internal - use LocationFactory.CreateAsync() for validated creation.
    /// </summary>
    internal static Location From(
        City city,
        CountryCode countryCode)
    {
        return new Location
        {
            Id = Guid.CreateVersion7(),
            City = city,
            CountryCode = countryCode
        };
    }

    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset LastModifiedUtc { get; private set; }
}
