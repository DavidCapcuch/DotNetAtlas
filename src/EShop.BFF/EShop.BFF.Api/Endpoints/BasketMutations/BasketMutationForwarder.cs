using System.Net;
using System.Security.Claims;
using EShop.BFF.Api.Common;
using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Clients.Basket;
using FastEndpoints;
using FluentResults;
using Platform.Api.Extensions;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Api.Endpoints.BasketMutations;

/// <summary>
/// Shared response logic for the four basket-mutation forwarders (bff.md § 3.6): resolve the buyer, forward
/// to Basket, and — the value the BFF adds over a direct path — on a 2xx <b>synchronously</b> invalidate the
/// buyer's <c>basket-bff-{userId}</c> read cache before responding, so the next <c>GET /basket</c> reflects
/// the change with no stale window. Verdict relay vs 503 shielding is owned by <c>IBasketWriteClient</c>.
/// </summary>
internal static class BasketMutationForwarder
{
    public static async Task ForwardAndRespondAsync(
        HttpContext http,
        IResponseSender send,
        ClaimsPrincipal user,
        IFusionCache cache,
        ILogger logger,
        Func<CancellationToken, Task<Result<BasketWriteVerdict>>> forward,
        CancellationToken ct)
    {
        if (!BffUser.TryGetBuyerId(user, out var userId))
        {
            // Authenticated but no parseable sub — a malformed token; fail closed.
            await http.Response.SendUnauthorizedAsync(ct);
            return;
        }

        var outcome = await forward(ct);

        if (outcome.IsFailed)
        {
            // Basket unreachable (≥ 500 / transport / circuit-open) → unified 503 from the typed
            // ServiceUnavailableError. No hand-mapped status; the BFF authors only this one verdict.
            await send.SendErrorResponseAsync(outcome, ct);
            return;
        }

        var verdict = outcome.Value;
        if (IsSuccess(verdict.Status))
        {
            // Synchronous, before responding: the next GET /basket must see the mutation (bff.md § 3.6).
            // Best-effort + post-commit — the write already committed in Basket, so a cache hiccup must not
            // 5xx a succeeded mutation and a client disconnect must not abort the eviction (TTL + the
            // basket.sessions invalidator backstop a missed eviction).
            await BffCacheInvalidation.TryRemoveByTagAsync(
                cache, BffCacheConstants.BasketPageTag(userId), logger);
        }

        // Relay Basket's verdict verbatim — the status and, when Basket wrote one (RFC 9457 problem details
        // on a decline), its body; a thin forwarder composes no response of its own (bff.md § 3.6).
        await http.Response.SendResultAsync(verdict.Body is null
            ? Results.StatusCode((int)verdict.Status)
            : Results.Content(verdict.Body, verdict.ContentType, statusCode: (int)verdict.Status));
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;
}
