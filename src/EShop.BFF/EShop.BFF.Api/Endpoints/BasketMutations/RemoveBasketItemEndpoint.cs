using System.Net;
using EShop.BFF.Api.Endpoints;
using EShop.BFF.Infrastructure.Clients.Basket;
using FastEndpoints;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Api.Endpoints.BasketMutations;

/// <summary>
/// <c>DELETE /api/v1/bff/basket/items/{productId}</c> (bff.md § 3.6). Required auth. Thin forwarder to
/// Basket's remove-item via the <c>basket.write</c> token exchange; on a 2xx, synchronously invalidates the
/// buyer's basket read cache. Idempotent by HTTP-method semantics (removing an absent item still 204s).
/// </summary>
internal sealed class RemoveBasketItemEndpoint : Endpoint<RemoveBasketItemRequest>
{
    private readonly IBasketWriteClient _basket;
    private readonly IFusionCache _cache;

    public RemoveBasketItemEndpoint(IBasketWriteClient basket, IFusionCache cache)
    {
        _basket = basket;
        _cache = cache;
    }

    public override void Configure()
    {
        Delete("basket/items/{productId:guid}");
        Version(1);
        Group<BffGroup>();
        Summary(s => s.Summary = "Remove an item from the buyer's basket (forwards to Basket, bff.md § 3.6).");
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.ServiceUnavailable);
        });
    }

    public override Task HandleAsync(RemoveBasketItemRequest request, CancellationToken ct) =>
        BasketMutationForwarder.ForwardAndRespondAsync(
            HttpContext,
            Send,
            User,
            _cache,
            Logger,
            c => _basket.RemoveItemAsync(request.ProductId, c),
            ct);
}
