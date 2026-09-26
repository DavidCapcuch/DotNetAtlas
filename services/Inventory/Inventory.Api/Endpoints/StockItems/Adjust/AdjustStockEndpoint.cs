using System.Net;
using FastEndpoints;
using Inventory.Api.Common.Authorization;
using Inventory.Application.StockItems.AdjustStock;
using Inventory.Application.StockItems.Common;
using Platform.Api.Extensions;

namespace Inventory.Api.Endpoints.StockItems.Adjust;

internal sealed class AdjustStockEndpoint : Endpoint<AdjustStockRequest, StockLevelResponse>
{
    private readonly Platform.CQRS.ICommandHandler<AdjustStockCommand, StockLevelResponse> _handler;
    private readonly TimeProvider _timeProvider;

    public AdjustStockEndpoint(
        Platform.CQRS.ICommandHandler<AdjustStockCommand, StockLevelResponse> handler,
        TimeProvider timeProvider)
    {
        _handler = handler;
        _timeProvider = timeProvider;
    }

    public override void Configure()
    {
        Post("stock-items/{productId:guid}/adjust");
        Version(1);
        Group<InventoryGroup>();
        Policies(AuthPolicies.WritePolicy);
        Idempotency(opts =>
        {
            // ADR-0013. A request without the header is rejected with 400 before the handler runs.
            // IdempotencyOptions.AdditionalHeaders includes `Authorization` by default, so a cached
            // response is keyed to the caller's bearer token and never served to another caller.
            opts.HeaderName = "Idempotency-Key";
            opts.CacheDuration = TimeSpan.FromHours(24);
        });
        Summary(s =>
        {
            s.Summary = "Records a signed correction to OnHand for the given ProductId.";
            s.Description =
                "Admin endpoint. Appends a StockAdjustedDomainEvent and returns the " +
                "post-mutation projection snapshot. Idempotency-Key header (24h " +
                "TTL) deduplicates retries per ADR-0013. Requires the " +
                "inventory.write scope.";
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

    public override async Task HandleAsync(AdjustStockRequest request, CancellationToken ct)
    {
        var command = new AdjustStockCommand
        {
            ProductId = request.ProductId,
            Delta = request.Delta,
            Reason = request.Reason,
            AdjustedByUserId = request.AdjustedByUserId,
            OccurredOnUtc = _timeProvider.GetUtcNow(),
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
