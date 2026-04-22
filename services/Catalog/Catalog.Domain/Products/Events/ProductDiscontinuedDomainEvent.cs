using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Domain.Products.Events;

/// <summary>
/// Raised when <see cref="Product.Discontinue"/> succeeds (transition Active → Discontinued).
/// </summary>
public sealed record ProductDiscontinuedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }
    public required string Reason { get; init; }
}
