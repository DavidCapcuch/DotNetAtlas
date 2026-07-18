using Catalog.Application.Common.Contracts;
using Platform.CQRS;

namespace Catalog.Application.Categories.GetProductsByCategory;

public sealed record GetProductsByCategoryQuery : IQuery<SearchProductsResponse>
{
    public required Guid CategoryId { get; init; }

    public bool IncludeDescendants { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
