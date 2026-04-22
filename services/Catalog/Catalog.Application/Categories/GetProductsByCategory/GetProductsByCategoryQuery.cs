using Catalog.Application.Products.SearchProducts;
using Platform.CQRS;

namespace Catalog.Application.Categories.GetProductsByCategory;

public class GetProductsByCategoryQuery : IQuery<SearchProductsResponse>
{
    public required Guid CategoryId { get; set; }

    public bool IncludeDescendants { get; set; }

    public int PageNumber { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}
