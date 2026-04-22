namespace Catalog.Application.Categories.GetCategoryTree;

public sealed class GetCategoryTreeResponse
{
    public required IReadOnlyList<CategoryTreeNode> Nodes { get; set; }
}

public sealed class CategoryTreeNode
{
    public required Guid CategoryId { get; set; }

    public required string Name { get; set; }

    public required string Path { get; set; }

    public Guid? ParentCategoryId { get; set; }

    public required int Depth { get; set; }

    public required int ProductCount { get; set; }
}
