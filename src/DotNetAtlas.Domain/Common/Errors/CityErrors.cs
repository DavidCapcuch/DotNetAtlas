using DotNetAtlas.SharedKernel.Errors;

namespace DotNetAtlas.Domain.Common.Errors;

public static class CityErrors
{
    public static ValidationError InvalidCity()
        => new ValidationError(
            propertyName: "City",
            errorMessage: "City name cannot be null or empty.",
            errorCode: "City.Invalid");

    public static ValidationError CityOutOfRange(int minInclusive, int maxInclusive)
        => new ValidationError(
            propertyName: "City",
            errorMessage: $"City name length must be between {minInclusive} and {maxInclusive}.",
            errorCode: "City.LengthOutOfRange");
}
