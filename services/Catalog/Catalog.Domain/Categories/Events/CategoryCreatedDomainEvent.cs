using Catalog.Domain.Categories.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Domain.Categories.Events;

/// <summary>
/// Raised when <see cref="Category.Create"/> succeeds.
/// </summary>
public sealed record CategoryCreatedDomainEvent : DomainEvent
{
    public required Guid CategoryId { get; init; }
    public required string Name { get; init; }
    public required Guid? ParentCategoryId { get; init; }
    public required CategoryPath Path { get; init; }
}
