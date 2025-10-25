using DotNetAtlas.Application.Common.Validators;
using FluentValidation.TestHelper;

namespace DotNetAtlas.UnitTests.WeatherForecast.Validators;

public class CityValidatorTests
{
    private readonly CityValidator _validator = new();

    [Fact]
    public void GivenValidCity_ShouldPass()
    {
        // Arrange
        var city = "Prague";

        // Act
        var result = _validator.TestValidate(city);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void GivenEmptyCity_ShouldFail()
    {
        // Arrange
        var city = string.Empty;

        // Act
        var result = _validator.TestValidate(city);

        // Assert
        result.ShouldHaveValidationErrors();
    }
}
