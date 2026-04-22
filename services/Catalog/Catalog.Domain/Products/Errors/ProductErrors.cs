using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Products.Errors;

public static class ProductErrors
{
    public static ValidationError CategoryIdRequired()
        => new ValidationError(
            propertyName: "CategoryId",
            errorMessage: "CategoryId is required.",
            errorCode: "Product.CategoryIdRequired");

    public static ValidationError CannotRepriceDiscontinued()
        => new ValidationError(
            propertyName: "Status",
            errorMessage: "A discontinued product cannot be re-priced.",
            errorCode: "Product.CannotRepriceDiscontinued");

    public static ValidationError CannotModifyDiscontinued()
        => new ValidationError(
            propertyName: "Status",
            errorMessage: "A discontinued product cannot be modified.",
            errorCode: "Product.CannotModifyDiscontinued");

    public static ValidationError ReasonRequired()
        => new ValidationError(
            propertyName: "Reason",
            errorMessage: "Discontinue reason must not be empty.",
            errorCode: "Product.ReasonRequired");

    public static ValidationError ReactivationRequiresAdminFlag()
        => new ValidationError(
            propertyName: "AdminReactivation",
            errorMessage: "Reactivating a discontinued product requires the admin reactivation flag.",
            errorCode: "Product.ReactivationRequiresAdminFlag");

    public static ValidationError NotFound(Guid productId)
        => new ValidationError(
            propertyName: "ProductId",
            errorMessage: $"Product '{productId}' does not exist.",
            errorCode: "Product.NotFound");

    public static ValidationError SkuAlreadyExists(string sku)
        => new ValidationError(
            propertyName: "Sku",
            errorMessage: $"A product with SKU '{sku}' already exists.",
            errorCode: "Product.SkuAlreadyExists");
}
