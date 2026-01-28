using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Domain.Alerts.Entities;
using DotNetAtlas.Domain.Alerts.Specifications;
using DotNetAtlas.Domain.Common.ValueObjects;

namespace DotNetAtlas.UnitTests.WeatherAlerts.Specifications;

public class LocationByCityAndCountrySpecTests
{
    [Fact]
    public void WhenApplied_ShouldFilterByCityAndCountry()
    {
        // Arrange
        var parisFranceLocation = Location.Create("Paris", CountryCode.FR).Value;
        var berlinGermanyLocation = Location.Create("Berlin", CountryCode.DE).Value;
        var parisUsLocation = Location.Create("Paris", CountryCode.US).Value;

        var locations = new List<Location>
        {
            parisFranceLocation,
            berlinGermanyLocation,
            parisUsLocation
        };

        var locationByCityAndCountrySpec = new LocationByCityAndCountrySpec("Paris", CountryCode.FR);

        // Act
        var filteredLocations = locations
            .AsQueryable()
            .WithSpecification(locationByCityAndCountrySpec)
            .ToList();

        // Assert
        using (new AssertionScope())
        {
            filteredLocations.Should().ContainSingle();
            filteredLocations.Single().Should().Be(parisFranceLocation);
        }
    }

    [Fact]
    public void WhenNoMatchingLocation_ShouldReturnEmpty()
    {
        // Arrange
        var berlinGermanyLocation = Location.Create("Berlin", CountryCode.DE).Value;
        var londonUkLocation = Location.Create("London", CountryCode.GB).Value;

        var locations = new List<Location>
        {
            berlinGermanyLocation,
            londonUkLocation
        };
        var locationByCityAndCountrySpec = new LocationByCityAndCountrySpec("Prague", CountryCode.CZ);

        // Act
        var filteredLocations = locations
            .AsQueryable()
            .WithSpecification(locationByCityAndCountrySpec)
            .ToList();

        // Assert
        filteredLocations.Should().BeEmpty();
    }

    [Fact]
    public void WhenSameCityDifferentCountry_ShouldNotMatch()
    {
        // Arrange
        var parisFranceLocation = Location.Create("Paris", CountryCode.FR).Value;

        var locations = new List<Location>
        {
            parisFranceLocation
        };
        var locationByCityAndCountrySpec = new LocationByCityAndCountrySpec("Paris", CountryCode.US);

        // Act
        var filteredLocations = locations
            .AsQueryable()
            .WithSpecification(locationByCityAndCountrySpec)
            .ToList();

        // Assert
        filteredLocations.Should().BeEmpty();
    }
}
