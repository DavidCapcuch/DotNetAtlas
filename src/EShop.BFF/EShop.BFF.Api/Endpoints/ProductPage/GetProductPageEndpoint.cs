using System.Net;
using EShop.BFF.Api.Composition;
using EShop.BFF.Api.Endpoints;
using EShop.BFF.Api.Responses;
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
                $"product-page:{productId}", // bff.md § 3.1.1
                async (ctx, factoryCt) =>
                {
                    var (composed, isDegraded) = await ComposeAsync(productId, factoryCt);

                    // A 404 (null) or an Inventory-unavailable partial must not be pinned for the full
                    // 5-minute TTL — shorten its lifetime so a recovered upstream (or a freshly-created
                    // product) surfaces quickly. Mirrors Inventory's FusionStockLevelCache posture.
                    if (isDegraded)
                    {
                        ctx.Options.SetDuration(DegradedEntryDuration);
                    }

                    return composed;
                },
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

        if (page.InStock is null)
        {
            // Inventory was unavailable at composition time (bff.md § 3.1 failure table).
            HttpContext.Response.Headers["X-BFF-PartialData"] = "inventory";
        }

        await Send.OkAsync(page, ct);
    }

    private async Task<(ProductPageResponse? Page, bool IsDegraded)> ComposeAsync(Guid productId, CancellationToken ct)
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
                return (Page: null, IsDegraded: true);
            }

            // Transport failure / 5xx → don't cache a failure; let fail-safe serve a stale page if any.
            throw new UpstreamUnavailableException("catalog");
        }

        var inventoryResult = await inventoryTask;
        var stockOrNull = inventoryResult.IsSuccess ? inventoryResult.Value : null;
        var composed = ProductPageComposer.Compose(catalogResult.Value, stockOrNull, _timeProvider.GetUtcNow());

        // Inventory unavailable → the page carries null availability; treat as degraded so it isn't pinned.
        return (Page: composed, IsDegraded: stockOrNull is null);
    }
}
