using System.Net;
using EShop.BFF.Api.Endpoints;
using EShop.BFF.Infrastructure.Clients.Basket;
using FastEndpoints;
using ZiggyCreatures.Caching.Fusion;

namespace EShop.BFF.Api.Endpoints.BasketMutations;

/// <summary>
/// <c>POST /api/v1/bff/basket/items</c> (bff.md § 3.6). Required auth. Thin forwarder to Basket's add-item
/// via the <c>basket.write</c> token exchange; relays the inbound <c>Idempotency-Key</c> unchanged (Basket
/// owns the replay) and, on a 2xx, synchronously invalidates the buyer's basket read cache.
/// </summary>
internal sealed class AddBasketItemEndpoint : Endpoint<AddBasketItemRequest>
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IBasketWriteClient _basket;
    private readonly IFusionCache _cache;

    public AddBasketItemEndpoint(IBasketWriteClient basket, IFusionCache cache)
    {
        _basket = basket;
        _cache = cache;
    }

    public override void Configure()
    {
        Post("basket/items");
        Version(1);
        Group<BffGroup>();

        // Required auth (no AllowAnonymous): the buyer is the inbound user JWT's sub; the basket.write scope
        // rides the outbound token exchange, not the inbound check.
        Summary(s => s.Summary = "Add an item to the buyer's basket (forwards to Basket, bff.md § 3.6).");
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.BadRequest);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Conflict);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
            b.Produces((int)HttpStatusCode.ServiceUnavailable);
        });
    }

    public override Task HandleAsync(AddBasketItemRequest request, CancellationToken ct)
    {
        var idempotencyKey = HttpContext.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var value)
            ? value.ToString()
            : null;

        return BasketMutationForwarder.ForwardAndRespondAsync(
            HttpContext,
            Send,
            User,
            _cache,
            Logger,
            c => _basket.AddItemAsync(new AddItemDto(request.ProductId, request.Quantity), idempotencyKey, c),
            ct);
    }
}
