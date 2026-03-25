using FluentValidation.TestHelper;
using Weather.Application.Common.Validators;

namespace Weather.UnitTests.WeatherForecast.Validators;

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
