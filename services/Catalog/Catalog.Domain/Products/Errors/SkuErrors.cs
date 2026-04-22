using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Products.Errors;

public static class SkuErrors
{
    public static ValidationError Empty()
        => new ValidationError(
            propertyName: "Sku",
            errorMessage: "SKU must not be empty.",
            errorCode: "Sku.Empty");

    public static ValidationError TooLong(int max)
        => new ValidationError(
            propertyName: "Sku",
            errorMessage: $"SKU must not exceed {max} characters.",
            errorCode: "Sku.TooLong");

    public static ValidationError InvalidCharacters()
        => new ValidationError(
            propertyName: "Sku",
            errorMessage: "SKU must start with an alphanumeric character and contain only letters, digits, or dashes.",
            errorCode: "Sku.InvalidCharacters");
}
