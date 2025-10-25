using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherForecast.Validators;

public class GetForecastQueryValidatorTests
{
    private readonly GetForecastQueryValidator _getForecastQueryValidator = new();

    [Fact]
    public void GivenValidQuery_ShouldPass()
    {
        // Arrange
        var query = new GetForecastQuery
        {
            Days = 5,
            City = "Prague",
            CountryCode = CountryCode.CZ
        };

        // Act
        var result = _getForecastQueryValidator.TestValidate(query);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GivenTooManyDays_ShouldFail()
    {
        // Arrange
        var getForecastQuery = new GetForecastQuery
        {
            Days = 20,
            City = "Prague",
            CountryCode = CountryCode.CZ
        };

        // Act
        var result = _getForecastQueryValidator.TestValidate(getForecastQuery);

        // Assert
        result.ShouldHaveValidationErrorFor(q => q.Days);
    }
}
