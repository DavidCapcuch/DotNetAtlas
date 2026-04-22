using Catalog.Application.Products.GetProductById;

namespace Catalog.Application.Products.GetProductsByIds;

public sealed class GetProductsByIdsResponse
{
    public required IReadOnlyList<GetProductByIdResponse> Products { get; set; }

    public required IReadOnlyList<Guid> MissingProductIds { get; set; }
}
