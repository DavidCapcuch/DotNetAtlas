using Catalog.Domain.Categories.ValueObjects;
using Platform.SharedKernel.Base.DomainEvents;

namespace Catalog.Domain.Categories.Events;

/// <summary>
/// Raised when <see cref="Category.Rename"/> or <see cref="Category.Reparent"/> succeeds.
/// On <c>Rename</c>, <see cref="OldParentId"/> equals <see cref="NewParentId"/> and only the
/// final segment of the path differs.
/// </summary>
public sealed record CategoryReparentedDomainEvent : DomainEvent
{
    public required Guid CategoryId { get; init; }
    public required Guid? OldParentId { get; init; }
    public required Guid? NewParentId { get; init; }
    public required CategoryPath OldPath { get; init; }
    public required CategoryPath NewPath { get; init; }
}
