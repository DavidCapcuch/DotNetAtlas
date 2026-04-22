using Catalog.Domain.Products.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.ValueObjects;

namespace Catalog.Domain.Products.Events;

/// <summary>
/// Raised when <see cref="Product.Create"/> succeeds.
/// Drives the read-view projection insert and the external <c>ProductCreatedEvent</c>
/// outbox publisher (M3).
/// </summary>
public sealed record ProductCreatedDomainEvent : DomainEvent
{
    public required Guid ProductId { get; init; }
    public required Sku Sku { get; init; }
    public required ProductName Name { get; init; }
    public required Guid CategoryId { get; init; }
    public required Money Price { get; init; }
}
