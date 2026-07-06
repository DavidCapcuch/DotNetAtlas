using System.Net;
using FluentResults;

namespace EShop.BFF.Infrastructure.Clients.Basket;

/// <summary>
/// Typed client for Basket's buyer-scoped <b>write</b> surface (bff.md § 3.6) — the four item mutations the
/// BFF fronts as thin forwarders. Reached via the RFC 8693 token exchange on the <c>basket.write</c> scope
/// (separate from <see cref="IBasketClient"/>'s <c>basket.read</c>: the platform pins one exchange scope per
/// <see cref="HttpClient"/>, and least-privilege keeps read and write audiences distinct).
/// </summary>
/// <remarks>
/// Each method relays Basket's own verdict status verbatim as <c>Result.Ok(status)</c> for any response
/// &lt; 500 except 401/403 — those reject the BFF's exchanged token (infra failure, not the buyer's
/// credential) and are shielded like an unreachable Basket (≥ 500 / transport / circuit-open) as a
/// <see cref="Platform.SharedKernel.Errors.ServiceUnavailableError"/> the endpoint maps to a 503 through the
/// unified <c>SendErrorResponseAsync</c>. The BFF authors no other status — there is no response composition
/// (bff.md § 3.6).
/// </remarks>
internal interface IBasketWriteClient
{
    /// <summary>Forwards <c>POST /api/v1/basket/items</c>, relaying the inbound <c>Idempotency-Key</c> unchanged.</summary>
    Task<Result<BasketWriteVerdict>> AddItemAsync(AddItemDto item, string? idempotencyKey, CancellationToken ct);

    /// <summary>Forwards <c>PUT /api/v1/basket/items/{productId}/quantity</c> (idempotent by method).</summary>
    Task<Result<BasketWriteVerdict>> ChangeItemQuantityAsync(Guid productId, int quantity, CancellationToken ct);

    /// <summary>Forwards <c>DELETE /api/v1/basket/items/{productId}</c> (idempotent by method).</summary>
    Task<Result<BasketWriteVerdict>> RemoveItemAsync(Guid productId, CancellationToken ct);

    /// <summary>Forwards <c>DELETE /api/v1/basket/items</c> — empties the basket (idempotent by method).</summary>
    Task<Result<BasketWriteVerdict>> ClearAsync(CancellationToken ct);
}
