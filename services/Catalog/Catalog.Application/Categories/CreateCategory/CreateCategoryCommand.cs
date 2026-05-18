using Platform.CQRS;

namespace Catalog.Application.Categories.CreateCategory;

/// <summary>
/// Admin command to create a new <see cref="Catalog.Domain.Categories.Category"/>. Root
/// categories pass <see cref="ParentCategoryId"/> == <c>null</c>; children supply the parent's id.
/// Returns the new category's identity on success.
/// </summary>
public sealed record CreateCategoryCommand : ICommand<Guid>
{
    public required string Name { get; init; }

    public Guid? ParentCategoryId { get; init; }
}
