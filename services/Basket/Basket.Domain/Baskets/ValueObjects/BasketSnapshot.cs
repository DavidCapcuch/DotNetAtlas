using System.Collections.Immutable;
using Platform.SharedKernel.Base;

namespace Basket.Domain.Baskets.ValueObjects;

/// <summary>
/// Full point-in-time snapshot of a basket at the instant of checkout.
/// Carried on <c>BasketCheckedOutDomainEvent</c> so the in-process outbox-publisher
/// handler (lands in milestone M4) can map it to the external
/// <c>BasketCheckoutInitiatedEvent</c> Avro record without re-reading the aggregate.
/// </summary>
/// <remarks>
/// <see cref="Items"/> is an <see cref="ImmutableArray{T}"/> so that snapshot
/// equality is structural (element-wise) — two snapshots built from identical
/// item data compare equal — and downstream handlers cannot mutate the collection
/// through a downcast.
/// </remarks>
/// <param name="Items">All line items at the moment of checkout. Never empty (empty-basket checkout is rejected).</param>
/// <param name="Total">Sum of all line values.</param>
public sealed record BasketSnapshot(
    ImmutableArray<BasketItem> Items,
    BasketTotal Total) : ValueObject
{
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
