using System.Net;
using Basket.Api.Common.Extensions;
using Basket.Application.Baskets.Clear;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Basket.Api.Endpoints.Baskets.Clear;

internal sealed class ClearBasketEndpoint : EndpointWithoutRequest
{
    private readonly Platform.CQRS.ICommandHandler<ClearBasketCommand> _handler;

    public ClearBasketEndpoint(Platform.CQRS.ICommandHandler<ClearBasketCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Delete("items");
        Version(1);
        Group<BasketGroup>();
        Summary(s =>
        {
            s.Summary = "Empty the caller's basket. Aggregate stays — only Checkout deletes the Redis key.";
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetUserIdFromSubClaim();
        var command = new ClearBasketCommand(userId);

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
