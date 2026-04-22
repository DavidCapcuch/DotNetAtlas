using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.Domain.Products.Events;

/// <summary>
/// Raised when <see cref="Product.UpdatePrice"/> records a non-no-op price change.
/// </summary>
public sealed record ProductPriceChangedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }
    public required Money OldPrice { get; init; }
    public required Money NewPrice { get; init; }
}
