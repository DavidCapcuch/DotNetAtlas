using DotNetAtlas.Domain.Common.ValueObjects;
using DotNetAtlas.SharedKernel.Errors;
using FluentResults.Extensions.FluentAssertions;

namespace DotNetAtlas.UnitTests.Common.ValueObjects;

public class CityTests
{
    [Theory]
    [InlineData("Prague")]
    [InlineData("New York")]
    [InlineData("AB")]
    public void Create_WhenValidCity_ReturnsSuccess(string cityName)
    {
        // Arrange & Act
        var cityResult = City.Create(cityName);

        // Assert
        using (new AssertionScope())
        {
            cityResult.Should().BeSuccess();
            cityResult.Value.Name.Should().Be(cityName);
        }
    }

    [Fact]
    public void Create_WhenMaxLengthCity_ReturnsSuccess()
    {
        // Arrange
        var cityName = new string('a', City.MaxLength);

        // Act
        var cityResult = City.Create(cityName);

        // Assert
        using (new AssertionScope())
        {
            cityResult.Should().BeSuccess();
            cityResult.Value.Name.Should().Be(cityName);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_WhenEmpty_ReturnsValidationError(string? cityName)
    {
        // Arrange & Act
        var cityResult = City.Create(cityName);

        // Assert
        using (new AssertionScope())
        {
            cityResult.Should().BeFailure();
            var validationError = cityResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("City.Invalid");
        }
    }

    [Fact]
    public void Create_WhenTooShort_ReturnsValidationError()
    {
        // Arrange
        var cityName = "A"; // Min length is 2

        // Act
        var cityResult = City.Create(cityName);

        // Assert
        using (new AssertionScope())
        {
            cityResult.Should().BeFailure();
            var validationError = cityResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("City.LengthOutOfRange");
        }
    }

    [Fact]
    public void Create_WhenTooLong_ReturnsValidationError()
    {
        // Arrange
        var cityName = new string('a', City.MaxLength + 1);

        // Act
        var cityResult = City.Create(cityName);

        // Assert
        using (new AssertionScope())
        {
            cityResult.Should().BeFailure();
            var validationError = cityResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("City.LengthOutOfRange");
        }
    }

    [Fact]
    public void Create_WhenCityHasLeadingAndTrailingSpaces_TrimsThem()
    {
        // Arrange
        const string cityName = "  Prague  ";

        // Act
        var cityResult = City.Create(cityName);

        // Assert
        using (new AssertionScope())
        {
            cityResult.Should().BeSuccess();
            cityResult.Value.Name.Should().Be("Prague");
        }
    }
}
