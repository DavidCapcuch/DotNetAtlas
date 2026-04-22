using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Domain.Products.Events;

/// <summary>
/// Raised when <see cref="Product.Activate"/> succeeds (transition Draft → Active).
/// </summary>
public sealed record ProductActivatedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }
}
