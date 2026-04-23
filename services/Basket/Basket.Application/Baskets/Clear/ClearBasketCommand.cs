using Platform.CQRS;

namespace Basket.Application.Baskets.Clear;

/// <summary>
/// Removes every line from the basket owned by <paramref name="UserId"/>. The
/// basket itself is NOT deleted — it persists at <c>Version + 1</c> with empty
/// items (only <c>CheckoutBasketCommand</c> deletes the Redis entry). Missing
/// basket or already-empty basket ⇒ <see cref="FluentResults.Result.Ok"/> with
/// no state change.
/// </summary>
public sealed record ClearBasketCommand(Guid UserId) : ICommand;
