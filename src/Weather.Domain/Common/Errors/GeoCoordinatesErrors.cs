using Platform.SharedKernel.Errors;

namespace Weather.Domain.Common.Errors;

public static class GeoCoordinatesErrors
{
    public static ValidationError InvalidLatitude(double latitude)
        => new ValidationError(
            propertyName: "Latitude",
            errorMessage: $"Latitude must be between -90 and 90 degrees. Provided: {latitude}",
            errorCode: "GeoCoordinates.InvalidLatitude");

    public static ValidationError InvalidLongitude(double longitude)
        => new ValidationError(
            propertyName: "Longitude",
            errorMessage: $"Longitude must be between -180 and 180 degrees. Provided: {longitude}",
            errorCode: "GeoCoordinates.InvalidLongitude");
}
