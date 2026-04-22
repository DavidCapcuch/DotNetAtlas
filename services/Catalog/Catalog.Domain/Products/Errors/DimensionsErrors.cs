using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Products.Errors;

public static class DimensionsErrors
{
    public static ValidationError NonPositiveDimension()
        => new ValidationError(
            propertyName: "Dimensions",
            errorMessage: "Length, width, and height must all be strictly positive.",
            errorCode: "Dimensions.NonPositiveDimension");

    public static ValidationError UnsupportedUnit()
        => new ValidationError(
            propertyName: "Unit",
            errorMessage: "Unit must be one of: cm, mm, in.",
            errorCode: "Dimensions.UnsupportedUnit");
}
