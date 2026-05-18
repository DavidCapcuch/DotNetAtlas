using Platform.CQRS;

namespace Catalog.Application.Categories.ReparentCategory;

/// <summary>
/// Admin command to reparent a category. M3 ships the self-parent guard only — the cycle check
/// (via <c>CategoryAncestryService</c>) and the descendant-path cascade (via
/// <c>CategoryPathService</c>) are deferred to a follow-up milestone.
/// </summary>
public sealed record ReparentCategoryCommand : ICommand
{
    public required Guid CategoryId { get; init; }

    public Guid? NewParentCategoryId { get; init; }
}
