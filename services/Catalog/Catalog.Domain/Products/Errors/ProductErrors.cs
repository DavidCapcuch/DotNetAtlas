using Platform.SharedKernel.Errors;

namespace Catalog.Domain.Products.Errors;

public static class ProductErrors
{
    public static ValidationError CategoryIdRequired()
        => new ValidationError(
            propertyName: "CategoryId",
            errorMessage: "CategoryId is required.",
            errorCode: "Product.CategoryIdRequired");

    public static ValidationError PriceMustBePositive()
        => new ValidationError(
            propertyName: "Price",
            errorMessage: "Price amount must be strictly positive.",
            errorCode: "Product.PriceMustBePositive");

    public static ConflictError CannotRepriceDiscontinued()
        => new ConflictError(
            entityName: "Product",
            message: "A discontinued product cannot be re-priced.",
            errorCode: "Product.CannotRepriceDiscontinued");

    public static ConflictError CannotModifyDiscontinued()
        => new ConflictError(
            entityName: "Product",
            message: "A discontinued product cannot be modified.",
            errorCode: "Product.CannotModifyDiscontinued");

    public static ValidationError ReasonRequired()
        => new ValidationError(
            propertyName: "Reason",
            errorMessage: "Discontinue reason must not be empty.",
            errorCode: "Product.ReasonRequired");

    public static ForbiddenError ReactivationRequiresAdminFlag()
        => new ForbiddenError(
            entityName: "Product",
            id: "(any)",
            errorCode: "Product.ReactivationRequiresAdminFlag");

    public static NotFoundError NotFound(Guid productId)
        => new NotFoundError(
            entityName: "Product",
            id: productId,
            errorCode: "Product.NotFound");

    public static ConflictError SkuAlreadyExists(string sku)
        => new ConflictError(
            entityName: "Product",
            message: $"A product with SKU '{sku}' already exists.",
            errorCode: "Product.SkuAlreadyExists");

    // CAT-RV-M03: user-actionable state-transition rejections surface as
    // 409 Result.Fail rather than 500 DataIntegrityException. The exception path remains for
    // genuinely impossible states (UI-bug paths), but client-driven retries against a product
    // whose status has changed concurrently should not produce an internal error.
    public static ConflictError CannotDiscontinueInStatus(string currentStatus)
        => new ConflictError(
            entityName: "Product",
            message: $"Cannot discontinue product in status '{currentStatus}'.",
            errorCode: "Product.CannotDiscontinueInStatus");

    public static ConflictError CannotReactivateInStatus(string currentStatus)
        => new ConflictError(
            entityName: "Product",
            message: $"Cannot reactivate product in status '{currentStatus}'. Only discontinued products may be reactivated.",
            errorCode: "Product.CannotReactivateInStatus");
}
