using DotNetAtlas.Application.Common.Validators;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherForecast.Validators;

public class CityValidatorTests
{
    private readonly CityValidator _cityValidator = new();

    [Fact]
    public void WhenValidCity_ShouldPass()
    {
        // Arrange
        const string cityName = "Prague";

        // Act
        var cityValidationResult = _cityValidator.TestValidate(cityName);

        // Assert
        cityValidationResult.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void WhenEmptyCity_ShouldFail()
    {
        // Arrange
        var cityName = string.Empty;

        // Act
        var cityValidationResult = _cityValidator.TestValidate(cityName);

        // Assert
        cityValidationResult.ShouldHaveValidationErrors();
    }
}
