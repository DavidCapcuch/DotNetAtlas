using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Domain.Products.Events;

/// <summary>
/// Raised when <see cref="Product.Reactivate"/> succeeds with <c>adminReactivation: true</c>
/// (transition Discontinued → Active). Not published externally in v1.
/// </summary>
public sealed record ProductReactivatedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }
}
