using System.Net;
using Basket.Api.Common.Extensions;
using Basket.Application.Baskets.GetByUserId;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Basket.Api.Endpoints.Baskets.GetByUserId;

internal sealed class GetBasketEndpoint : EndpointWithoutRequest<GetBasketResponse>
{
    private readonly Platform.CQRS.IQueryHandler<GetBasketByUserIdQuery, GetBasketResponse> _handler;

    public GetBasketEndpoint(Platform.CQRS.IQueryHandler<GetBasketByUserIdQuery, GetBasketResponse> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Get(string.Empty);
        Version(1);
        Group<BasketGroup>();
        Summary(s =>
        {
            s.Summary = "Get the caller's basket. Empty basket is 200 with no items, never 404.";
        });
        Description(b =>
        {
            b.Produces<GetBasketResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.Unauthorized);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.GetUserIdFromSubClaim();
        var query = new GetBasketByUserIdQuery(userId);

        var result = await _handler.HandleAsync(query, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
