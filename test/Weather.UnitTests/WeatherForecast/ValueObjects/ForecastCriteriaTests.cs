using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;
using Weather.Domain.Common.ValueObjects;
using Weather.Domain.Forecast.ValueObjects;

namespace Weather.UnitTests.WeatherForecast.ValueObjects;

public class ForecastCriteriaTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(14)]
    public void Create_WhenValidInput_ReturnsSuccess(int days)
    {
        // Arrange
        var startDate = new DateOnly(2024, 6, 1);
        var dateRange = DateRange.Create(startDate, days).Value;

        // Act
        var forecastCriteriaResult = ForecastCriteria.Create("Prague", CountryCode.CZ, dateRange);

        // Assert
        using (new AssertionScope())
        {
            forecastCriteriaResult.Should().BeSuccess();
            forecastCriteriaResult.Value.City.Name.Should().Be("Prague");
            forecastCriteriaResult.Value.CountryCode.Should().Be(CountryCode.CZ);
            forecastCriteriaResult.Value.DateRange.Should().Be(dateRange);
            forecastCriteriaResult.Value.Days.Should().Be(days);
        }
    }

    [Fact]
    public void Create_WhenMinimumDays_ReturnsSuccess()
    {
        // Arrange
        var startDate = new DateOnly(2024, 6, 1);
        var dateRange = DateRange.Create(startDate, ForecastCriteria.MinDays).Value;

        // Act
        var forecastCriteriaResult = ForecastCriteria.Create("Berlin", CountryCode.DE, dateRange);

        // Assert
        using (new AssertionScope())
        {
            forecastCriteriaResult.Should().BeSuccess();
            forecastCriteriaResult.Value.Days.Should().Be(ForecastCriteria.MinDays);
        }
    }

    [Fact]
    public void Create_WhenMaximumDays_ReturnsSuccess()
    {
        // Arrange
        var startDate = new DateOnly(2024, 6, 1);
        var dateRange = DateRange.Create(startDate, ForecastCriteria.MaxDays).Value;

        // Act
        var forecastCriteriaResult = ForecastCriteria.Create("London", CountryCode.GB, dateRange);

        // Assert
        using (new AssertionScope())
        {
            forecastCriteriaResult.Should().BeSuccess();
            forecastCriteriaResult.Value.Days.Should().Be(ForecastCriteria.MaxDays);
        }
    }

    [Fact]
    public void Create_WhenDaysExceedsMax_ReturnsValidationError()
    {
        // Arrange
        var startDate = new DateOnly(2024, 6, 1);
        var dateRange = DateRange.Create(startDate, ForecastCriteria.MaxDays + 1).Value;

        // Act
        var forecastCriteriaResult = ForecastCriteria.Create("Paris", CountryCode.FR, dateRange);

        // Assert
        using (new AssertionScope())
        {
            forecastCriteriaResult.Should().BeFailure();
            var validationError = forecastCriteriaResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("Forecast.InvalidDaysRange");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenInvalidCity_ReturnsValidationError(string? city)
    {
        // Arrange
        var startDate = new DateOnly(2024, 6, 1);
        var dateRange = DateRange.Create(startDate, 7).Value;

        // Act
        var forecastCriteriaResult = ForecastCriteria.Create(city, CountryCode.CZ, dateRange);

        // Assert
        using (new AssertionScope())
        {
            forecastCriteriaResult.Should().BeFailure();
            var validationError = forecastCriteriaResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("City.Invalid");
        }
    }

    [Fact]
    public void Create_WhenBothCityAndDaysInvalid_ReturnsBothValidationErrors()
    {
        // Arrange
        var startDate = new DateOnly(2024, 6, 1);
        var dateRange = DateRange.Create(startDate, ForecastCriteria.MaxDays + 1).Value;

        // Act
        var forecastCriteriaResult = ForecastCriteria.Create("", CountryCode.CZ, dateRange);

        // Assert
        using (new AssertionScope())
        {
            forecastCriteriaResult.Should().BeFailure();
            forecastCriteriaResult.Errors.Should().HaveCountGreaterThanOrEqualTo(2);
            forecastCriteriaResult.Errors.Should().AllBeAssignableTo<ValidationError>();
        }
    }
}
