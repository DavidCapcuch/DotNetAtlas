using Platform.CQRS;

namespace Basket.Application.Baskets.ChangeItemQuantity;

/// <summary>
/// Replaces the quantity of <paramref name="ProductId"/> in the basket owned by
/// <paramref name="UserId"/> with <paramref name="NewQuantity"/>. Fails with
/// <c>BasketErrors.ItemNotFound</c> when the product is not present. If the new
/// quantity equals the existing quantity the aggregate short-circuits (no event,
/// no version bump).
/// </summary>
public sealed record ChangeItemQuantityCommand(Guid UserId, Guid ProductId, int NewQuantity) : ICommand;
