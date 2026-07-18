using Catalog.Application.Common.Contracts;

namespace Catalog.Application.Products.GetProductsByIds;

public sealed class GetProductsByIdsResponse
{
    public required IReadOnlyList<ProductDetailResponse> Products { get; set; }

    public required IReadOnlyList<Guid> MissingProductIds { get; set; }
}
