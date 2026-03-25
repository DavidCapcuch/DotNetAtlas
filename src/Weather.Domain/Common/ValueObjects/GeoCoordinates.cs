using FluentResults;
using Platform.SharedKernel.Base;
using Weather.Domain.Common.Errors;

namespace Weather.Domain.Common.ValueObjects;

/// <summary>
/// Represents geographic coordinates with validation.
/// </summary>
public sealed record GeoCoordinates : ValueObject
{
    public double Latitude { get; private init; }

    public double Longitude { get; private init; }

    private GeoCoordinates()
    {
    }

    public static Result<GeoCoordinates> Create(double latitude, double longitude)
    {
        var mergedResults = Result.Merge(
            Result.FailIf(latitude is < -90 or > 90, GeoCoordinatesErrors.InvalidLatitude(latitude)),
            Result.FailIf(longitude is < -180 or > 180, GeoCoordinatesErrors.InvalidLongitude(longitude)));

        if (mergedResults.IsFailed)
        {
            return mergedResults;
        }

        return new GeoCoordinates
        {
            Latitude = latitude,
            Longitude = longitude
        };
    }
}
