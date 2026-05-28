using Platform.SharedKernel.Errors;

namespace Basket.Domain.Baskets.Errors;

/// <summary>
/// Aggregate-rule errors raised by the Basket aggregate. Each factory returns
/// the canonical <see cref="DomainError"/> subclass whose error code is the
/// single source of truth consumed by callers, tests, and the
/// Platform.Api problem-details dispatch. ACL adapter failures (catalog
/// availability, product existence) live in
/// <c>Basket.Application.Baskets.Common.Errors.BasketAclErrors</c>.
/// </summary>
public static class BasketErrors
{
    public static ConflictError EmptyBasket()
        => new ConflictError(
            entityName: "Basket",
            message: "Basket must contain at least one item to checkout.",
            errorCode: "Basket.Empty");

    public static ConflictError MaxItemsReached(int max)
        => new ConflictError(
            entityName: "Basket",
            message: $"Basket cannot hold more than {max} items.",
            errorCode: "Basket.MaxItemsReached");

    public static ValidationError InvalidQuantity()
        => new ValidationError(
            propertyName: "Quantity",
            errorMessage: "Item quantity must be at least 1.",
            errorCode: "Basket.InvalidQuantity");

    public static ValidationError CurrencyMismatch()
        => new ValidationError(
            propertyName: "Currency",
            errorMessage: "All basket items must share the same currency.",
            errorCode: "Basket.CurrencyMismatch");

    public static NotFoundError ItemNotFound(Guid productId)
        => new NotFoundError(
            entityName: "BasketItem",
            id: productId,
            errorCode: "Basket.ItemNotFound");

    /// <summary>
    /// Raised when a persisted basket state cannot be rehydrated — e.g. its stored
    /// currency code is no longer present in the <c>CurrencyCode</c> SmartEnum.
    /// Persistent data-integrity failure, not a transient outage; surfaced as
    /// 422 (<see cref="ValidationError"/>) so clients distinguish it from the
    /// transient 503 emitted by ACL-layer <c>CatalogUnavailable</c>.
    /// </summary>
    public static ValidationError Corruption(Guid userId)
        => new ValidationError(
            propertyName: "Basket",
            errorMessage: $"Stored basket state for user '{userId}' could not be rehydrated.",
            errorCode: "Basket.Corruption");
}
