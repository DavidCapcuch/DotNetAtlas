using Platform.SharedKernel.Errors;

namespace Basket.Application.Baskets.Common.Errors;

/// <summary>
/// Anti-Corruption Layer errors produced by adapters that translate upstream
/// catalog / external-service failures into Basket-shaped
/// <see cref="ValidationError"/> results. Kept out of the Domain layer because
/// the failure modes are infrastructure-class, not invariant violations of the
/// <c>Basket</c> aggregate.
/// </summary>
public static class BasketAclErrors
{
    public static ValidationError CatalogUnavailable()
        => new ValidationError(
            propertyName: "Catalog",
            errorMessage: "Product catalog is temporarily unavailable.",
            errorCode: "Basket.CatalogUnavailable");

    public static ValidationError ProductNotFound(Guid productId)
        => new ValidationError(
            propertyName: "ProductId",
            errorMessage: $"Product '{productId}' does not exist.",
            errorCode: "Basket.ProductNotFound");
}
