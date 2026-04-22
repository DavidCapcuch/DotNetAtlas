using Catalog.Domain.Products.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Domain.Products.Events;

/// <summary>
/// Raised when <see cref="Product.Describe"/> succeeds.
/// </summary>
public sealed record ProductDescribedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }
    public required ProductDescription NewDescription { get; init; }
}
