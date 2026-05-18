using System.Collections.Immutable;
using Platform.SharedKernel.Base;

namespace Basket.Domain.Baskets.ValueObjects;

/// <summary>
/// Full point-in-time snapshot of a basket at the instant of checkout.
/// Carried on <c>BasketCheckedOutDomainEvent</c> so the in-process outbox-publisher
/// handler maps it to the external <c>BasketCheckoutInitiatedEvent</c> Avro record
/// without re-reading the aggregate.
/// </summary>
/// <remarks>
/// <see cref="Items"/> is an <see cref="ImmutableArray{T}"/> so that snapshot
/// equality is structural (element-wise) — two snapshots built from identical
/// item data compare equal — and downstream handlers cannot mutate the collection
/// through a downcast.
/// </remarks>
public sealed record BasketSnapshot : ValueObject
{
    /// <summary>All line items at the moment of checkout. Never empty (empty-basket checkout is rejected).</summary>
    public ImmutableArray<BasketItem> Items { get; private init; } = ImmutableArray<BasketItem>.Empty;

    /// <summary>Sum of all line values.</summary>
    public BasketTotal Total { get; private init; } = null!;

    private BasketSnapshot()
    {
    }

    /// <summary>
    /// Creates a basket snapshot wrapping the given items and total.
    /// </summary>
    public static BasketSnapshot Create(ImmutableArray<BasketItem> items, BasketTotal total) =>
        new()
        {
            Items = items,
            Total = total,
        };

    // EqualityContract check is intentionally elided — the type is sealed,
    // so no derived type can ever be passed in.
    public bool Equals(BasketSnapshot? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Total == other.Total && Items.SequenceEqual(other.Items);
    }

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Total);
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}
