using FastEndpoints;

namespace Inventory.Api.Endpoints.StockItems.Adjust;

/// <summary>
/// Body for <c>POST /api/v1/inventory/stock-items/{productId}/adjust</c>
/// (idempotent per ADR-0013 — clients MUST send <c>Idempotency-Key</c>).
/// <c>ProductId</c> is bound from the route token; the rest from the body.
/// </summary>
/// <remarks>
/// <c>AdjustedByUserId</c> is body-bound (not extracted from the JWT
/// <c>sub</c> claim) intentionally. Inventory is called server-to-server
/// per ADR-0010 with a Keycloak service-account token whose <c>sub</c>
/// is the calling service's account, not a human operator. Upstream
/// admin tooling (e.g., a future AdminPanel BFF) authenticates the
/// human operator on its own surface and forwards the operator id in
/// the body. The audit row therefore records the human, not the
/// service account. If/when Inventory exposes a user-facing endpoint
/// it will switch to <c>User.GetUserIdFromSubClaim()</c> per
/// <c>services/Basket/Basket.Api/Endpoints/Baskets/Checkout/CheckoutBasketEndpoint.cs:66</c>.
/// </remarks>
internal sealed class AdjustStockRequest
{
    [BindFrom("productId")]
    public required Guid ProductId { get; init; }

    /// <summary>Signed delta; must not be zero (validator rejects).</summary>
    public required int Delta { get; init; }

    public required string Reason { get; init; }

    public required Guid AdjustedByUserId { get; init; }

    public Guid? CorrelationId { get; init; }
}
