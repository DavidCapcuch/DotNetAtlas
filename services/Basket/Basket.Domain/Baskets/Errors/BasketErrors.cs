using Platform.SharedKernel.Errors;

namespace Basket.Domain.Baskets.Errors;

/// <summary>
/// User-actionable validation errors raised by the Basket aggregate and its
/// Anti-Corruption Layer. Each factory returns a <see cref="ValidationError"/>
/// whose error code is the single source of truth consumed by callers, tests,
/// and the HTTP problem-details pipeline.
/// </summary>
public static class BasketErrors
{
    public static ValidationError EmptyBasket()
        => new ValidationError(
            propertyName: "Basket",
            errorMessage: "Basket must contain at least one item to checkout.",
            errorCode: "Basket.Empty");

    public static ValidationError MaxItemsReached(int max)
        => new ValidationError(
            propertyName: "Items",
            errorMessage: $"Basket cannot hold more than {max} items.",
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

    public static ValidationError ItemNotFound(Guid productId)
        => new ValidationError(
            propertyName: "ProductId",
            errorMessage: $"Product '{productId}' is not in the basket.",
            errorCode: "Basket.ItemNotFound");

    /// <summary>
    /// Raised when a persisted basket state cannot be rehydrated — e.g. its stored
    /// currency code is no longer present in the <c>CurrencyCode</c> SmartEnum.
    /// Treated as a transient infrastructure-class failure (503 at the API
    /// boundary) so callers can retry or fall back rather than surface a 5xx.
    /// </summary>
    public static ValidationError Corruption(Guid userId)
        => new ValidationError(
            propertyName: "Basket",
            errorMessage: $"Stored basket state for user '{userId}' could not be rehydrated.",
            errorCode: "Basket.Corruption");
}
