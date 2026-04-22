using Platform.CQRS;

namespace Catalog.Application.Categories.GetCategoryTree;

/// <summary>
/// Public query returning the taxonomy tree (whole tree or a subtree rooted at
/// <see cref="RootCategoryId"/>) with per-node counts of Active products.
/// </summary>
public class GetCategoryTreeQuery : IQuery<GetCategoryTreeResponse>
{
    public Guid? RootCategoryId { get; set; }
}
