using System.Net;
using Basket.Api.Common;
using Basket.Application.Baskets.ChangeItemQuantity;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Basket.Api.Endpoints.Baskets.ChangeItemQuantity;

internal sealed class ChangeItemQuantityEndpoint : Endpoint<ChangeItemQuantityRequest>
{
    private readonly Platform.CQRS.ICommandHandler<ChangeItemQuantityCommand> _handler;

    public ChangeItemQuantityEndpoint(Platform.CQRS.ICommandHandler<ChangeItemQuantityCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Put("items/{productId}/quantity");
        Version(1);
        Group<BasketGroup>();
        Summary(s =>
        {
            s.Summary = "Change the quantity of an existing basket item.";
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
        });
    }

    public override async Task HandleAsync(ChangeItemQuantityRequest request, CancellationToken ct)
    {
        var userId = User.GetUserIdFromSubClaim();
        var command = new ChangeItemQuantityCommand(userId, request.ProductId, request.NewQuantity);

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
