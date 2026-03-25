using Ardalis.Specification;
using Weather.Domain.Alerts.Entities;
using Weather.Domain.Common.ValueObjects;

namespace Weather.Domain.Alerts.Specifications;

/// <summary>
/// Specification to find a Location by city name and country code.
/// </summary>
public sealed class LocationByCityAndCountrySpec : Specification<Location>,
    ISingleResultSpecification<Location>
{
    public LocationByCityAndCountrySpec(string city, CountryCode countryCode)
    {
        Query
            .Where(l => l.City.Name == city && l.CountryCode == countryCode)
            .TagWith(nameof(LocationByCityAndCountrySpec));
    }
}
