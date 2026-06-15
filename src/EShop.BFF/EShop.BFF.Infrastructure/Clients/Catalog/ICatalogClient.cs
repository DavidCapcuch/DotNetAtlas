using FluentResults;

namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>
/// Typed client for Catalog's read surface (bff.md § 4.1). Returns <see cref="Result{T}"/> so callers
/// distinguish "product does not exist" (a gating <c>NotFoundError</c>) from "Catalog is unavailable"
/// (a <c>ServiceUnavailableError</c>).
/// </summary>
internal interface ICatalogClient
{
    Task<Result<CatalogProductDto>> GetProductByIdAsync(Guid productId, CancellationToken ct);
}
