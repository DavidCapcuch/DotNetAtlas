using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Products.Errors;

public static class ProductDescriptionErrors
{
    public static ValidationError TooLong(int max)
        => new ValidationError(
            propertyName: "Description",
            errorMessage: $"Product description must not exceed {max} characters.",
            errorCode: "ProductDescription.TooLong");
}
