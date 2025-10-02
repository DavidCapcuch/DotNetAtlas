using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Domain.Entities.Weather.Forecast;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.Forecast.Validators;

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
            CountryCode = CountryCode.Cz
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
            CountryCode = CountryCode.Cz
        };

        // Act
        var result = _getForecastQueryValidator.TestValidate(getForecastQuery);

        // Assert
        result.ShouldHaveValidationErrorFor(q => q.Days);
    }
}
