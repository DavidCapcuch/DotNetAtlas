using System.Net;
using FastEndpoints;
using Inventory.API.Common.Authorization;
using Inventory.API.Common.Extensions;
using Inventory.Application.StockItems.AdjustStock;
using Inventory.Application.StockItems.Common;

namespace Inventory.API.Endpoints.StockItems.Adjust;

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
        Policies(InventoryAuthorizationPolicies.CommandsPolicy);
        Idempotency(opts =>
        {
            // ADR-0013: 24h TTL, header `Idempotency-Key`, redis-cache backing
            // (configured at the platform level via AddIdempotencyKeyOutputCache).
            // FastEndpoints 7.0.1's IdempotencyOptions.AdditionalHeaders defaults
            // include `Authorization` so two callers reusing the same UUID don't
            // share responses.
            opts.HeaderName = "Idempotency-Key";
            opts.CacheDuration = TimeSpan.FromHours(24);
        });
        Summary(s =>
        {
            s.Summary = "Records a signed correction to OnHand for the given ProductId.";
            s.Description =
                "Admin endpoint. Appends a StockAdjustedEvent and returns the " +
                "post-mutation projection snapshot. Idempotency-Key header (24h " +
                "TTL) deduplicates retries per ADR-0013. Requires the " +
                "inventory.commands.reserve scope.";
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
            CorrelationId = request.CorrelationId,
        };

        var result = await _handler.HandleAsync(command, ct);

        await result.MatchAsync(
            response => Send.OkAsync(response, ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
