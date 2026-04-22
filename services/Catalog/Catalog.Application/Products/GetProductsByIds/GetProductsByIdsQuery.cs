using Platform.CQRS;

namespace Catalog.Application.Products.GetProductsByIds;

/// <summary>
/// BFF-facing bulk lookup used by basket/order enrichment. Partial-tolerant: missing products
/// are returned in <see cref="GetProductsByIdsResponse.MissingProductIds"/> rather than failing
/// the whole call.
/// </summary>
public class GetProductsByIdsQuery : IQuery<GetProductsByIdsResponse>
{
    public required IReadOnlyList<Guid> Ids { get; set; }
}
