using System.Net;
using Basket.Api.Common.Extensions;
using Basket.Application.Baskets.RemoveItem;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Basket.Api.Endpoints.Baskets.RemoveItem;

internal sealed class RemoveItemFromBasketEndpoint : Endpoint<RemoveItemFromBasketRequest>
{
    private readonly Platform.CQRS.ICommandHandler<RemoveItemFromBasketCommand> _handler;

    public RemoveItemFromBasketEndpoint(Platform.CQRS.ICommandHandler<RemoveItemFromBasketCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Delete("items/{productId}");
        Version(1);
        Group<BasketGroup>();
        Summary(s =>
        {
            s.Summary = "Remove an item from the caller's basket. Idempotent — removing an absent item still returns 204.";
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
        });
    }

    public override async Task HandleAsync(RemoveItemFromBasketRequest request, CancellationToken ct)
    {
        var userId = User.GetUserIdFromSubClaim();
        var command = new RemoveItemFromBasketCommand(userId, request.ProductId);

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
