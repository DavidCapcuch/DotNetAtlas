using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Products.Errors;

public static class ImageReferenceErrors
{
    public static ValidationError InvalidUrl()
        => new ValidationError(
            propertyName: "Url",
            errorMessage: "Image URL must be a non-empty absolute URI.",
            errorCode: "ImageReference.InvalidUrl");

    public static ValidationError AltTextEmpty()
        => new ValidationError(
            propertyName: "AltText",
            errorMessage: "Image alt text must not be empty.",
            errorCode: "ImageReference.AltTextEmpty");

    public static ValidationError AltTextTooLong(int max)
        => new ValidationError(
            propertyName: "AltText",
            errorMessage: $"Image alt text must not exceed {max} characters.",
            errorCode: "ImageReference.AltTextTooLong");

    public static ValidationError NegativeDisplayOrder()
        => new ValidationError(
            propertyName: "DisplayOrder",
            errorMessage: "Image display order must be zero or positive.",
            errorCode: "ImageReference.NegativeDisplayOrder");
}
