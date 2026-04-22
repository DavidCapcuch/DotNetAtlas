using Platform.SharedKernel.Base;
using Platform.SharedKernel.ValueObjects;

namespace Basket.Domain.Baskets.ValueObjects;

/// <summary>
/// Projected sum of all basket line values: <c>Sum(item.Snapshot.Price * item.Quantity)</c>.
/// Computed on demand by the aggregate — not persisted. Wraps a strictly-positive
/// <see cref="Money"/> and is never constructed for an empty basket — callers rely
/// on the nullable <c>Basket.Total</c> getter, which returns <c>null</c> when
/// <c>Items.Count == 0</c> (invariant 7 blocks empty-basket checkout anyway).
/// </summary>
/// <param name="Amount">Total monetary amount of the basket.</param>
public sealed record BasketTotal(Money Amount) : ValueObject;
