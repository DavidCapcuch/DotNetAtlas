using System.Net;
using EShop.BFF.Api.Common;
using EShop.BFF.Api.Composition;
using EShop.BFF.Api.Endpoints;
using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Clients.Basket;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;
using EShop.BFF.Infrastructure.Common.Observability;
using FastEndpoints;
using FluentResults;
using Platform.Api.Extensions;
using Platform.SharedKernel.Errors;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Api.Endpoints.BasketPage;

/// <summary>
/// <c>GET /api/v1/bff/basket</c> (bff.md § 3.2). <b>Required auth</b> — the buyer is the JWT <c>sub</c>.
/// Reads the buyer's basket (via the <c>basket.read</c> RFC 8693 token exchange, preserving <c>sub</c>),
/// then enriches it in parallel with current Catalog price (drift flags) + Inventory availability
/// (out-of-stock flags), behind a per-user redis-cache FusionCache (15 s TTL, 2 m fail-safe). Basket gates
/// the page (404 → empty; transport failure → fail-safe stale page or 503); Catalog / Inventory enrich it
/// (failure → null fields + <c>X-BFF-PartialData</c>, never a failed page).
/// </summary>
internal sealed class GetBasketEndpoint : EndpointWithoutRequest<BasketPageResponse>
{
    private readonly IBasketClient _basket;
    private readonly ICatalogClient _catalog;
    private readonly IInventoryClient _inventory;
    private readonly IFusionCache _cache;
    private readonly TimeProvider _timeProvider;

    public GetBasketEndpoint(
        IBasketClient basket,
        ICatalogClient catalog,
        IInventoryClient inventory,
        IFusionCache cache,
        TimeProvider timeProvider)
    {
        _basket = basket;
        _catalog = catalog;
        _inventory = inventory;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public override void Configure()
    {
        Get("basket");
        Version(1);
        Group<BffGroup>();

        // Required auth (bff.md § 3.2): NOT AllowAnonymous → the default policy requires an authenticated
        // user. No scope policy — the BFF validates the inbound user token (aud: bff); the basket.read scope
        // rides the *outbound* token exchange, not the inbound check.
        Summary(s => s.Summary =
            "Authenticated buyer's basket enriched with current price + availability (bff.md § 3.2).");
        Description(b =>
        {
            b.Produces<BasketPageResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.ServiceUnavailable);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!BffUser.TryGetBuyerId(User, out var userId))
        {
            // Authenticated but no parseable sub — a malformed token; fail closed.
            await Send.UnauthorizedAsync(ct);
            return;
        }

        // The factory runs only on a miss; capture whether it ran to attribute the read as hit vs miss
        // (bff.md § 2.4). A fail-safe stale serve runs the factory (it threw) → counted a miss.
        var factoryRan = false;

        BasketPageResponse page;
        try
        {
            page = await _cache.GetOrSetAsync<BasketPageResponse>(
                BffCacheConstants.BasketPageKey(userId),
                (_, factoryCt) =>
                {
                    factoryRan = true;
                    return ComposeAsync(userId, factoryCt);
                },
                options: BffBasketCache.EntryOptions(),
                tags: BffBasketCache.Tags(userId),
                token: ct);
        }
        catch (UpstreamUnavailableException)
        {
            // Basket is down and no stale basket was cached to fail-safe to.
            await Send.SendErrorResponseAsync(
                Result.Fail(new ServiceUnavailableError(
                    "basket",
                    "an upstream dependency is unavailable",
                    "Bff.Basket.Unavailable")),
                ct);
            return;
        }

        var cacheHit = !factoryRan;
        BffMetrics.RecordCache(BffMetrics.BasketEndpoint, cacheHit);

        // FusionCache's native fail-safe serves the last-good basket (with its compose-time flags) when
        // Basket is down. It exposes no "served stale" signal, so flag it from the page's age: a basket
        // older than its fresh window can only have come from fail-safe (bff.md § 3.2 / § 2.4).
        if (StaleServePolicy.WasServedStale(
                page.GeneratedAtUtc, _timeProvider.GetUtcNow(), BffBasketCache.StaleServeFreshWindow))
        {
            page = page with { HasStaleData = true };
        }

        SignalPartialData(page);

        // A fail-safe stale serve or a partial-degraded compose both carry HasStaleData (bff.md § 2.4).
        HttpContext.Response.SignalStale(page.HasStaleData);
        BffMetrics.TagRequest(BffMetrics.BasketEndpoint, cacheHit, page.HasStaleData);

        await Send.OkAsync(page, ct);
    }

    private async Task<BasketPageResponse> ComposeAsync(Guid userId, CancellationToken ct)
    {
        // INVARIANT: this factory runs synchronously on the request thread, so the Basket call's
        // TokenExchangeHandler can read the inbound user JWT + sub from IHttpContextAccessor. Do NOT give the
        // basket cache entry a FactorySoftTimeout + background completion — a factory completing on a
        // background thread would see a null HttpContext and the exchange would fail closed.
        var basketResult = await _basket.GetBasketAsync(ct);

        if (basketResult.IsFailed)
        {
            // 404 → the buyer has no basket yet: render an empty page (200, not stale). bff.md § 3.2.
            if (basketResult.HasError<NotFoundError>())
            {
                return BasketPageComposer.Compose(EmptyBasket(userId), null, null, _timeProvider.GetUtcNow());
            }

            // Transport failure / 5xx → don't cache a failure; let fail-safe serve a stale basket if any
            // (else this surfaces and the endpoint maps it to 503).
            throw new UpstreamUnavailableException("basket");
        }

        var basket = basketResult.Value;

        // Empty basket → no enrichment (bff.md § 3.2): empty items + zero totals, not stale.
        if (basket.Items.Count == 0)
        {
            return BasketPageComposer.Compose(basket, null, null, _timeProvider.GetUtcNow());
        }

        // Parallel batch enrichment: current price (Catalog by-ids) + current availability (Inventory bulk).
        var productIds = basket.Items.Select(item => item.ProductId).ToList();
        var catalogTask = _catalog.GetProductsByIdsAsync(productIds, ct);
        var inventoryTask = _inventory.GetStockLevelsBulkAsync(productIds, ct);
        await Task.WhenAll(catalogTask, inventoryTask);

        var catalogResult = await catalogTask;
        var inventoryResult = await inventoryTask;
        var catalogOrNull = catalogResult.IsSuccess ? catalogResult.Value : null;
        var inventoryOrNull = inventoryResult.IsSuccess ? inventoryResult.Value : null;

        return BasketPageComposer.Compose(basket, catalogOrNull, inventoryOrNull, _timeProvider.GetUtcNow());
    }

    // bff.md § 3.2 failure table: a null CurrentPrice / AvailableQty surfaces as X-BFF-PartialData. The
    // page's own null fields carry this on a cache hit too (no need to re-know which upstream failed).
    private void SignalPartialData(BasketPageResponse page)
    {
        var partial = new List<string>(capacity: 2);
        if (page.Items.Any(item => item.CurrentPrice is null))
        {
            partial.Add("catalog");
        }

        if (page.Items.Any(item => item.AvailableQty is null))
        {
            partial.Add("inventory");
        }

        if (partial.Count > 0)
        {
            HttpContext.Response.Headers["X-BFF-PartialData"] = string.Join(", ", partial);

            // Every partial 200 (cache hit or miss) counts — the rate is the degraded-UX signal (bff.md § 2.4).
            BffMetrics.RecordPartialResponse(BffMetrics.BasketEndpoint);
        }
    }

    private static BasketDto EmptyBasket(Guid userId) =>
        new(userId, Version: 0, Items: [], Total: null, CreatedAtUtc: default, LastModifiedAtUtc: default);
}
