using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.SharedKernel.Base;

namespace DotNetAtlas.Domain.Alerts.ValueObjects;

/// <summary>
/// Value object representing a grouping key for weather alert subscriptions.
/// Encapsulates the logic for generating consistent group names based on city and country.
/// Used for SignalR group management and job scheduling.
/// </summary>
public sealed record AlertGroup : ValueObject
{
    /// <summary>
    /// The city for this alert grouping.
    /// </summary>
    public City City { get; }

    /// <summary>
    /// The country code for this alert grouping.
    /// </summary>
    public CountryCode CountryCode { get; }

    /// <summary>
    /// The generated group name used for SignalR groups and job identification.
    /// Format: "CITY:COUNTRYCODE" (uppercase).
    /// </summary>
    public string GroupName { get; }

    /// <summary>
    /// Creates a new AlertGrouping from a City value object and CountryCode.
    /// </summary>
    /// <param name="city">The validated city.</param>
    /// <param name="countryCode">The country code.</param>
    private AlertGroup(City city, CountryCode countryCode)
    {
        City = city;
        CountryCode = countryCode;
        GroupName = $"{city.Name.ToUpperInvariant()}:{countryCode.ToString().ToUpperInvariant()}";
    }

    /// <summary>
    /// Creates a new AlertGrouping from a location.
    /// </summary>
    /// <param name="location">The location.</param>
    /// <returns>A new AlertGrouping instance.</returns>
    public static AlertGroup From(Location location)
    {
        return new AlertGroup(location.City, location.CountryCode);
    }

    /// <summary>
    /// Creates a new AlertGrouping from a City and CountryCode.
    /// </summary>
    /// <param name="city">The validated city.</param>
    /// <param name="countryCode">The country code.</param>
    /// <returns>A new AlertGrouping instance.</returns>
    public static AlertGroup From(City city, CountryCode countryCode)
    {
        return new AlertGroup(city, countryCode);
    }
}
