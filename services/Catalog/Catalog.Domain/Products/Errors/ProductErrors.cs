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

    // CAT-RV-M03 (Wave-1 closeout): user-actionable state-transition rejections surface as
    // 409 Result.Fail rather than 500 DataIntegrityException. The exception path remains for
    // genuinely impossible states (UI-bug paths), but client-driven retries against a product
    // whose status has changed concurrently should not produce an internal error.
    public static ValidationError CannotDiscontinueInStatus(string currentStatus)
        => new ValidationError(
            propertyName: "Status",
            errorMessage: $"Cannot discontinue product in status '{currentStatus}'.",
            errorCode: "Product.CannotDiscontinueInStatus");

    public static ValidationError CannotReactivateInStatus(string currentStatus)
        => new ValidationError(
            propertyName: "Status",
            errorMessage: $"Cannot reactivate product in status '{currentStatus}'. Only discontinued products may be reactivated.",
            errorCode: "Product.CannotReactivateInStatus");
}
