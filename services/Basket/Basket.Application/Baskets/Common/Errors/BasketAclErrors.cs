using Platform.SharedKernel.Errors;

namespace Basket.Application.Baskets.Common.Errors;

/// <summary>
/// Anti-Corruption Layer errors produced by adapters that translate upstream
/// catalog / external-service failures into Basket-shaped results. Kept out of
/// the Domain layer because the failure modes are infrastructure-class, not
/// invariant violations of the <c>Basket</c> aggregate.
/// </summary>
public static class BasketAclErrors
{
    public static ServiceUnavailableError CatalogUnavailable()
        => new ServiceUnavailableError(
            resourceName: "Catalog",
            message: "Product catalog is temporarily unavailable.",
            errorCode: "Basket.CatalogUnavailable");

    public static NotFoundError ProductNotFound(Guid productId)
        => new NotFoundError(
            entityName: "Product",
            id: productId,
            errorCode: "Basket.ProductNotFound");
}
