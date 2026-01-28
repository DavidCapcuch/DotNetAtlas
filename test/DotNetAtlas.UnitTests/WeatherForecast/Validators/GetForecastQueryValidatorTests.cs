using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Domain.Common.ValueObjects;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherForecast.Validators;

public class GetForecastQueryValidatorTests
{
    private readonly GetForecastQueryValidator _getForecastQueryValidator = new();

    [Fact]
    public void WhenValidQuery_ShouldPass()
    {
        // Arrange
        var getForecastQuery = new GetForecastQuery
        {
            Days = 5,
            City = "Prague",
            CountryCode = CountryCode.CZ
        };

        // Act
        var getForecastQueryValidationResult = _getForecastQueryValidator.TestValidate(getForecastQuery);

        // Assert
        getForecastQueryValidationResult.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void WhenTooManyDays_ShouldFail()
    {
        // Arrange
        var getForecastQuery = new GetForecastQuery
        {
            Days = 20,
            City = "Prague",
            CountryCode = CountryCode.CZ
        };

        // Act
        var getForecastQueryValidationResult = _getForecastQueryValidator.TestValidate(getForecastQuery);

        // Assert
        getForecastQueryValidationResult.ShouldHaveValidationErrorFor(q => q.Days);
    }
}
