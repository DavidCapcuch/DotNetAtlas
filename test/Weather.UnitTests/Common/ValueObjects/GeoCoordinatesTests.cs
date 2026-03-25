using FluentResults.Extensions.FluentAssertions;
using Platform.SharedKernel.Errors;
using Weather.Domain.Common.ValueObjects;

namespace Weather.UnitTests.Common.ValueObjects;

public class GeoCoordinatesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50.05, 14.5)]
    [InlineData(-33, 150.2)]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    public void Create_WhenValidCoordinates_ReturnsSuccess(double latitude, double longitude)
    {
        // Arrange & Act
        var geoCoordinatesResult = GeoCoordinates.Create(latitude, longitude);

        // Assert
        using (new AssertionScope())
        {
            geoCoordinatesResult.Should().BeSuccess();
            geoCoordinatesResult.Value.Latitude.Should().Be(latitude);
            geoCoordinatesResult.Value.Longitude.Should().Be(longitude);
        }
    }

    [Theory]
    [InlineData(90.1)]
    [InlineData(91)]
    [InlineData(-90.1)]
    [InlineData(-91)]
    [InlineData(180)]
    public void Create_WhenLatitudeOutOfRange_ReturnsValidationError(double latitude)
    {
        // Arrange & Act
        var geoCoordinatesResult = GeoCoordinates.Create(latitude, 0);

        // Assert
        using (new AssertionScope())
        {
            geoCoordinatesResult.Should().BeFailure();
            var validationError = geoCoordinatesResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("GeoCoordinates.InvalidLatitude");
        }
    }

    [Theory]
    [InlineData(180.1)]
    [InlineData(181)]
    [InlineData(-180.1)]
    [InlineData(-181)]
    [InlineData(360)]
    public void Create_WhenLongitudeOutOfRange_ReturnsValidationError(double longitude)
    {
        // Arrange & Act
        var geoCoordinatesResult = GeoCoordinates.Create(0, longitude);

        // Assert
        using (new AssertionScope())
        {
            geoCoordinatesResult.Should().BeFailure();
            var validationError = geoCoordinatesResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("GeoCoordinates.InvalidLongitude");
        }
    }

    [Fact]
    public void Create_WhenBothCoordinatesOutOfRange_ReturnsBothValidationErrors()
    {
        // Arrange & Act
        var geoCoordinatesResult = GeoCoordinates.Create(91, 181);

        // Assert
        using (new AssertionScope())
        {
            geoCoordinatesResult.Should().BeFailure();
            geoCoordinatesResult.Errors.Should().HaveCount(2);
            geoCoordinatesResult.Errors.Should().AllBeAssignableTo<ValidationError>();
            var errors = geoCoordinatesResult.Errors.OfType<ValidationError>().ToList();
            errors.Should()
                .ContainSingle(err => err.ErrorCode == "GeoCoordinates.InvalidLatitude")
                .And.ContainSingle(err => err.ErrorCode == "GeoCoordinates.InvalidLongitude");
        }
    }

    [Theory]
    [InlineData(90, 0)]
    [InlineData(-90, 0)]
    public void Create_WhenAtLatitudeBoundary_ReturnsSuccess(double latitude, double longitude)
    {
        // Arrange & Act
        var geoCoordinatesResult = GeoCoordinates.Create(latitude, longitude);

        // Assert
        geoCoordinatesResult.Should().BeSuccess();
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(0, -180)]
    public void Create_WhenAtLongitudeBoundary_ReturnsSuccess(double latitude, double longitude)
    {
        // Arrange & Act
        var geoCoordinatesResult = GeoCoordinates.Create(latitude, longitude);

        // Assert
        geoCoordinatesResult.Should().BeSuccess();
    }
}
