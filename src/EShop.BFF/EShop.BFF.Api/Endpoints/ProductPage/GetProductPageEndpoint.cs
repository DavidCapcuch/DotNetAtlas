using System.Net;
using EShop.BFF.Api.Common;
using EShop.BFF.Api.Composition;
using EShop.BFF.Api.Endpoints;
using EShop.BFF.Api.Responses;
using EShop.BFF.Infrastructure.Caching;
using EShop.BFF.Infrastructure.Clients.Catalog;
using EShop.BFF.Infrastructure.Clients.Inventory;
using FastEndpoints;
using FluentResults;
using Platform.Api.Extensions;
using Platform.SharedKernel.Errors;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Api.Endpoints.ProductPage;

/// <summary>
/// <c>GET /api/v1/bff/product-page/{productId}</c> (bff.md § 3.1). Public. Composes Catalog product
/// info + Inventory availability in parallel, behind the redis-cache FusionCache. Catalog gates the
/// page (404 → 404; transport failure → fail-safe stale page or 503); Inventory enriches it (failure →
/// null availability + <c>X-BFF-PartialData: inventory</c>, never a failed page).
/// </summary>
internal sealed class GetProductPageEndpoint : Endpoint<GetProductPageRequest, ProductPageResponse>
{
    // A degraded compose (Catalog 404 → absent, or Inventory unavailable → null availability) is cached
    // only briefly so recovery surfaces fast, instead of being pinned for the healthy 5-minute TTL.
    private static readonly TimeSpan DegradedEntryDuration = TimeSpan.FromSeconds(15);

    private readonly ICatalogClient _catalog;
    private readonly IInventoryClient _inventory;
    private readonly IFusionCache _cache;
    private readonly TimeProvider _timeProvider;

    public GetProductPageEndpoint(
        ICatalogClient catalog,
        IInventoryClient inventory,
        IFusionCache cache,
        TimeProvider timeProvider)
    {
        _catalog = catalog;
        _inventory = inventory;
        _cache = cache;
        _timeProvider = timeProvider;
    }

    public override void Configure()
    {
        Get("product-page/{productId:guid}");
        Version(1);
        Group<BffGroup>();
        AllowAnonymous();
        Summary(s => s.Summary =
            "Public product page: Catalog product composed with Inventory availability (bff.md § 3.1).");
        Description(b =>
        {
            b.Produces<ProductPageResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.ServiceUnavailable);
        });
    }

    public override async Task HandleAsync(GetProductPageRequest request, CancellationToken ct)
    {
        var productId = request.ProductId;

        ProductPageResponse? page;
        try
        {
            page = await _cache.GetOrSetAsync<ProductPageResponse?>(
                BffCacheConstants.ProductPageKey(productId), // bff.md § 3.1.1
                (ctx, factoryCt) => ComposeAsync(ctx, productId, factoryCt),
                token: ct);
        }
        catch (UpstreamUnavailableException)
        {
            // Catalog is down and no stale page was cached to fail-safe to.
            await Send.SendErrorResponseAsync(
                Result.Fail(new ServiceUnavailableError(
                    "product-page",
                    "an upstream dependency is unavailable",
                    "Bff.ProductPage.Unavailable")),
                ct);
            return;
        }

        if (page is null)
        {
            await Send.SendErrorResponseAsync(
                Result.Fail(new NotFoundError("Product", productId, "Bff.ProductPage.ProductNotFound")),
                ct);
            return;
        }

        // FusionCache's native fail-safe serves the last-good page (with its compose-time flags) when
        // Catalog is down. It exposes no "served stale" signal, so flag it from the page's age: a page
        // older than its fresh window can only have come from fail-safe (bff.md § 3.1 / § 2.4).
        if (StaleServePolicy.WasServedStale(
                page.GeneratedAtUtc, _timeProvider.GetUtcNow(), BffCacheDependencyInjection.StaleServeFreshWindow))
        {
            page = page with { HasStaleData = true };
        }

        if (page.InStock is null)
        {
            // Inventory was unavailable at composition time (bff.md § 3.1 failure table).
            HttpContext.Response.Headers["X-BFF-PartialData"] = "inventory";
        }

        // A fail-safe stale serve or a partial-degraded compose both carry HasStaleData (bff.md § 2.4).
        HttpContext.Response.SignalStale(page.HasStaleData);

        await Send.OkAsync(page, ct);
    }

    private async Task<ProductPageResponse?> ComposeAsync(
        FusionCacheFactoryExecutionContext<ProductPageResponse?> ctx, Guid productId, CancellationToken ct)
    {
        var catalogTask = _catalog.GetProductByIdAsync(productId, ct);
        var inventoryTask = _inventory.GetStockLevelAsync(productId, ct);
        await Task.WhenAll(catalogTask, inventoryTask);

        var catalogResult = await catalogTask;
        if (catalogResult.IsFailed)
        {
            // 404 → cache "absent" briefly (degraded) and let the endpoint return 404.
            if (catalogResult.HasError<NotFoundError>())
            {
                ctx.Options.SetDuration(DegradedEntryDuration);
                return null;
            }

            // Transport failure / 5xx → don't cache a failure; let fail-safe serve a stale page if any
            // (else this surfaces and the endpoint maps it to 503).
            throw new UpstreamUnavailableException("catalog");
        }

        var inventoryResult = await inventoryTask;
        var stockOrNull = inventoryResult.IsSuccess ? inventoryResult.Value : null;
        var composed = ProductPageComposer.Compose(catalogResult.Value, stockOrNull, _timeProvider.GetUtcNow());

        // Inventory unavailable → the page carries null availability; cache briefly so it isn't pinned.
        if (stockOrNull is null)
        {
            ctx.Options.SetDuration(DegradedEntryDuration);
        }

        return composed;
    }
}
