using System.Net;
using Basket.Api.Common.Extensions;
using Basket.Application.Baskets.AddItem;
using FastEndpoints;
using Platform.Api.Extensions;

namespace Basket.Api.Endpoints.Baskets.AddItem;

internal sealed class AddItemToBasketEndpoint : Endpoint<AddItemToBasketRequest>
{
    private readonly Platform.CQRS.ICommandHandler<AddItemToBasketCommand> _handler;

    public AddItemToBasketEndpoint(Platform.CQRS.ICommandHandler<AddItemToBasketCommand> handler)
    {
        _handler = handler;
    }

    public override void Configure()
    {
        Post("items");
        Version(1);
        Group<BasketGroup>();
        Idempotency();
        Summary(s =>
        {
            s.Summary = "Add an item to the caller's basket. Frozen-pricing snapshot is captured at add-time.";
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.NoContent);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.NotFound);
            b.Produces((int)HttpStatusCode.Conflict);
            b.Produces((int)HttpStatusCode.UnprocessableEntity);
            b.Produces((int)HttpStatusCode.ServiceUnavailable);
        });
    }

    public override async Task HandleAsync(AddItemToBasketRequest request, CancellationToken ct)
    {
        var userId = User.GetUserIdFromSubClaim();
        var command = new AddItemToBasketCommand(userId, request.ProductId, request.Quantity);

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
