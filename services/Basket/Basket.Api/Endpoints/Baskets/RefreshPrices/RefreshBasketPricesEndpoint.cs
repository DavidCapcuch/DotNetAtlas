using System.Net;
using Basket.Api.Common.Extensions;
using Basket.Application.Baskets.RefreshPrices;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Basket.Api.Endpoints.Baskets.RefreshPrices;

internal sealed class RefreshBasketPricesEndpoint : EndpointWithoutRequest
{
    private readonly Platform.CQRS.ICommandHandler<RefreshBasketPricesCommand> _handler;

    public RefreshBasketPricesEndpoint(Platform.CQRS.ICommandHandler<RefreshBasketPricesCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("refresh-prices");
        Version(1);
        Group<BasketGroup>();
        Summary(s =>
        {
            s.Summary = "Re-snapshot every basket line against current Catalog prices.";
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.ServiceUnavailable);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetUserIdFromSubClaim();
        var command = new RefreshBasketPricesCommand(userId);

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
