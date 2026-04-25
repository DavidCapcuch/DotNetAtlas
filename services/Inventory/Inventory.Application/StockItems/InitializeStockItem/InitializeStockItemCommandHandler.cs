using FluentResults;
using Inventory.Application.Common.Data;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Inventory.Application.StockItems.InitializeStockItem;

/// <summary>
/// Handles <see cref="InitializeStockItemCommand"/>. Delegates to
/// <see cref="IEventStore.AppendAsync"/> which rehydrates the stream, invokes
/// <c>aggregate.Initialize</c>, and commits the <c>StockItemInitializedEvent</c>
/// + projection upsert in one transaction. If the stream is already initialized
/// (<c>Version &gt; 0</c>), returns <see cref="Result.Ok"/> without appending —
/// idempotent against Catalog re-delivery.
/// </summary>
internal sealed class InitializeStockItemCommandHandler : ICommandHandler<InitializeStockItemCommand>
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<InitializeStockItemCommandHandler> _logger;

    public InitializeStockItemCommandHandler(
        IEventStore eventStore,
        ILogger<InitializeStockItemCommandHandler> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(InitializeStockItemCommand command, CancellationToken ct)
    {
        var result = await _eventStore.AppendAsync(
            streamId: command.ProductId,
            command: aggregate =>
            {
                if (aggregate.Version > 0)
                {
                    // Already initialized — idempotent no-op. Append nothing.
                    return Result.Ok();
                }

                return aggregate.Initialize(command.ProductId, command.OccurredOnUtc);
            },
            correlationId: command.CorrelationId,
            ct: ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Initialized stock-item stream for Product {ProductId} (version after append: {Version})",
                command.ProductId, result.Value.Version);
        }

        return result.ToResult();
    }
}
