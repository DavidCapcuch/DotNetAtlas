using FluentResults;
using FluentResults.Extensions.FluentAssertions;
using NSubstitute;
using Platform.SharedKernel.Errors;
using Weather.Domain.Common.Services;
using Weather.Domain.Common.ValueObjects;
using Weather.Domain.Forecast.Errors;

namespace Weather.UnitTests.WeatherAlerts.Services;

public class LocationFactoryTests
{
    private readonly IGeocodingProvider _geocodingProvider = Substitute.For<IGeocodingProvider>();
    private readonly LocationFactory _locationFactory;

    public LocationFactoryTests()
    {
        _locationFactory = new LocationFactory(_geocodingProvider);
    }

    [Fact]
    public async Task CreateAsync_WhenValidCityAndCountry_ReturnsLocation()
    {
        // Arrange
        var cityName = "Prague";
        var countryCode = CountryCode.CZ;
        _geocodingProvider.ValidateLocationAsync(Arg.Any<City>(), countryCode, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Act
        var locationResult = await _locationFactory.CreateAsync(cityName, countryCode, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            locationResult.Should().BeSuccess();
            locationResult.Value.City.Name.Should().Be(cityName);
            locationResult.Value.CountryCode.Should().Be(countryCode);
        }
    }

    [Fact]
    public async Task CreateAsync_WhenGeoProviderValidationFails_ReturnsFailure()
    {
        // Arrange
        var cityName = "NonExistentCity";
        var countryCode = CountryCode.CZ;
        var expectedError = ForecastErrors.CityNotFoundError(cityName, countryCode);
        _geocodingProvider.ValidateLocationAsync(Arg.Any<City>(), countryCode, Arg.Any<CancellationToken>())
            .Returns(Result.Fail(expectedError));

        // Act
        var locationResult = await _locationFactory.CreateAsync(cityName, countryCode, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            locationResult.Should().BeFailure();
            locationResult.Errors.Should().ContainSingle();
            locationResult.Errors[0].Should().BeOfType<NotFoundError>();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateAsync_WhenCityNameInvalid_ReturnsValidationError(string? invalidCityName)
    {
        // Arrange
        var countryCode = CountryCode.CZ;

        // Act
        var locationResult = await _locationFactory.CreateAsync(invalidCityName!, countryCode, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            locationResult.Should().BeFailure();
            locationResult.Errors.Should().NotBeEmpty();
            locationResult.Errors.Should().AllBeOfType<ValidationError>();
        }

        // Verify geo provider was never called for invalid city name
        await _geocodingProvider.DidNotReceive()
            .ValidateLocationAsync(Arg.Any<City>(), Arg.Any<CountryCode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WhenCityNameTooShort_ReturnsValidationError()
    {
        // Arrange
        var cityName = "A"; // Min length is 2
        var countryCode = CountryCode.CZ;

        // Act
        var locationResult = await _locationFactory.CreateAsync(cityName, countryCode, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            locationResult.Should().BeFailure();
            locationResult.Errors.Should().ContainSingle();
            locationResult.Errors[0].Should().BeOfType<ValidationError>();
        }

        // Verify geo provider was never called for invalid city name
        await _geocodingProvider.DidNotReceive()
            .ValidateLocationAsync(Arg.Any<City>(), Arg.Any<CountryCode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WhenCityNameTooLong_ReturnsValidationError()
    {
        // Arrange
        var cityName = new string('A', City.MaxLength + 1);
        var countryCode = CountryCode.CZ;

        // Act
        var locationResult = await _locationFactory.CreateAsync(cityName, countryCode, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            locationResult.Should().BeFailure();
            locationResult.Errors.Should().ContainSingle();
            locationResult.Errors[0].Should().BeOfType<ValidationError>();
        }

        // Verify geo provider was never called for invalid city name
        await _geocodingProvider.DidNotReceive()
            .ValidateLocationAsync(Arg.Any<City>(), Arg.Any<CountryCode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_WhenValidInput_CallsGeoProviderWithCorrectParameters()
    {
        // Arrange
        var cityName = "Berlin";
        var countryCode = CountryCode.DE;
        _geocodingProvider.ValidateLocationAsync(Arg.Any<City>(), countryCode, Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        // Act
        await _locationFactory.CreateAsync(cityName, countryCode, CancellationToken.None);

        // Assert
        await _geocodingProvider.Received(1)
            .ValidateLocationAsync(
                Arg.Is<City>(c => c.Name == cityName),
                countryCode,
                Arg.Any<CancellationToken>());
    }
}
