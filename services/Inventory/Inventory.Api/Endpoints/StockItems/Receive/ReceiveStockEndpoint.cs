using System.Net;
using FastEndpoints;
using Inventory.Api.Common.Authorization;
using Inventory.Api.Common.Extensions;
using Inventory.Application.StockItems.Common;
using Inventory.Application.StockItems.ReceiveStock;

namespace Inventory.Api.Endpoints.StockItems.Receive;

internal sealed class ReceiveStockEndpoint : Endpoint<ReceiveStockRequest, StockLevelResponse>
{
    private readonly Platform.CQRS.ICommandHandler<ReceiveStockCommand, StockLevelResponse> _handler;
    private readonly TimeProvider _timeProvider;

    public ReceiveStockEndpoint(
        Platform.CQRS.ICommandHandler<ReceiveStockCommand, StockLevelResponse> handler,
        TimeProvider timeProvider)
    {
        _handler = handler;
        _timeProvider = timeProvider;
    }

    public override void Configure()
    {
        Post("stock-items/{productId:guid}/receive");
        Version(1);
        Group<InventoryGroup>();
        Policies(InventoryAuthorizationPolicies.CommandsPolicy);
        Summary(s =>
        {
            s.Summary = "Records an inbound stock movement for the given ProductId.";
            s.Description =
                "Admin endpoint. Appends a StockReceivedEvent to the stream and " +
                "returns the post-mutation projection snapshot. Requires the " +
                "inventory.commands.reserve scope (per inventory.md M1 design " +
                "decision — admin write actions reuse the saga reserve scope " +
                "in v1).";
        });
        Description(b =>
        {
            b.Produces<StockLevelResponse>((int)HttpStatusCode.OK);
            b.Produces((int)HttpStatusCode.BadRequest);
            b.Produces((int)HttpStatusCode.Unauthorized);
            b.Produces((int)HttpStatusCode.Forbidden);
            b.Produces((int)HttpStatusCode.Conflict);
        });
    }

    public override async Task HandleAsync(ReceiveStockRequest request, CancellationToken ct)
    {
        var command = new ReceiveStockCommand
        {
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            Source = request.Source,
            ReceivedByUserId = request.ReceivedByUserId,
            OccurredOnUtc = _timeProvider.GetUtcNow(),
            CorrelationId = request.CorrelationId,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
