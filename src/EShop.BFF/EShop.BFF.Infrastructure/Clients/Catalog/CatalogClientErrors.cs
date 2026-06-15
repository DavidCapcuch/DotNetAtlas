using Platform.SharedKernel.Errors;

namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>Typed errors the Catalog client returns (mapped to HTTP by the API layer).</summary>
internal static class CatalogClientErrors
{
    /// <summary>Catalog returned 404 — the product does not exist. Gates the page to a BFF 404.</summary>
    public static NotFoundError ProductNotFound(Guid productId) =>
        new("Product", productId, "Bff.Catalog.ProductNotFound");

    /// <summary>Catalog is unreachable / 5xx / timed out. Surfaces as 503 (or a fail-safe stale serve).</summary>
    public static ServiceUnavailableError Unavailable(string reason) =>
        new("catalog-service", reason, "Bff.Catalog.Unavailable");
}
