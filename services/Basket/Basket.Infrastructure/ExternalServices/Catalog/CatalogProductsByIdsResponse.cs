namespace Basket.Infrastructure.ExternalServices.Catalog;

/// <summary>
/// Private projection of Catalog's <c>GetProductsByIdsResponse</c> HTTP contract.
/// Catalog's batch endpoint is partial-tolerant: the <c>Products</c> array
/// contains the products it could resolve, and <c>MissingProductIds</c> lists
/// the ids it could not. The adapter uses <c>Products</c> to build its result
/// pairs; <c>MissingProductIds</c> is deserialized for future diagnostics /
/// logging but is not propagated upward, because the
/// <see cref="Basket.Application.Abstractions.IProductCatalogQueryPort.GetManyAsync"/>
/// contract says missing ids are silently dropped.
/// </summary>
internal sealed record CatalogProductsByIdsResponse(
    IReadOnlyList<CatalogProductResponse> Products,
    IReadOnlyList<Guid> MissingProductIds);
