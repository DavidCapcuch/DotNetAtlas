using Platform.SharedKernel.Errors;

namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>Typed errors the Basket client returns (mapped by the endpoint).</summary>
internal static class BasketClientErrors
{
    /// <summary>
    /// Basket returned 404 — the buyer has no basket yet (lazily created). The endpoint renders an empty
    /// basket page (200), not a failure (bff.md § 3.2 failure table).
    /// </summary>
    public static NotFoundError BasketNotFound() =>
        new("Basket", "current-user", "Bff.Basket.NotFound");

    /// <summary>Basket is unreachable / 5xx / timed out. Gates the page (fail-safe stale serve or 503).</summary>
    public static ServiceUnavailableError Unavailable(string reason) =>
        new("basket-service", reason, "Bff.Basket.Unavailable");
}
