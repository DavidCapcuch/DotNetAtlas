using Platform.CQRS;

namespace Basket.Application.Baskets.RemoveItem;

/// <summary>
/// Idempotently removes <paramref name="ProductId"/> from the basket owned by
/// <paramref name="UserId"/>. Missing basket or missing item ⇒ <see cref="FluentResults.Result.Ok"/>
/// with no state change and no event. See <c>Basket.RemoveItem</c>.
/// </summary>
public sealed record RemoveItemFromBasketCommand(Guid UserId, Guid ProductId) : ICommand;
