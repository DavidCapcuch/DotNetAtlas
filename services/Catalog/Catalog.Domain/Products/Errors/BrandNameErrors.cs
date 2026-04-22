using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Products.Errors;

public static class BrandNameErrors
{
    public static ValidationError Empty()
        => new ValidationError(
            propertyName: "Brand",
            errorMessage: "Brand name must not be empty.",
            errorCode: "BrandName.Empty");

    public static ValidationError TooLong(int max)
        => new ValidationError(
            propertyName: "Brand",
            errorMessage: $"Brand name must not exceed {max} characters.",
            errorCode: "BrandName.TooLong");
}
