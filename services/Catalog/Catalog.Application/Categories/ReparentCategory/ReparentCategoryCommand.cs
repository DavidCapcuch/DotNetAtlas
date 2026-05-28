using Platform.CQRS;

namespace Catalog.Application.Categories.ReparentCategory;

/// <summary>
/// Admin command to reparent a category. The handler guards against self-parenting,
/// rejects descendant cycles via <c>CategoryAncestryService</c>, and cascades the
/// resulting path rewrite to descendants via <c>CategoryPathService</c>.
/// </summary>
public sealed record ReparentCategoryCommand : ICommand
{
    public required Guid CategoryId { get; init; }

    public Guid? NewParentCategoryId { get; init; }
}
