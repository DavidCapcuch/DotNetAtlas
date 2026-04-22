using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Products.Errors;

public static class ProductNameErrors
{
    public static ValidationError Empty()
        => new ValidationError(
            propertyName: "Name",
            errorMessage: "Product name must not be empty.",
            errorCode: "ProductName.Empty");

    public static ValidationError TooLong(int max)
        => new ValidationError(
            propertyName: "Name",
            errorMessage: $"Product name must not exceed {max} characters.",
            errorCode: "ProductName.TooLong");
}
