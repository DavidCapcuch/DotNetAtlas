using FluentResults;
using Inventory.Application.Common.Data;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Inventory.Application.StockItems.AdjustStock;

internal sealed class AdjustStockCommandHandler : ICommandHandler<AdjustStockCommand>
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<AdjustStockCommandHandler> _logger;

    public AdjustStockCommandHandler(
        IEventStore eventStore,
        ILogger<AdjustStockCommandHandler> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(AdjustStockCommand command, CancellationToken ct)
    {
        var result = await _eventStore.AppendAsync(
            streamId: command.ProductId,
            command: aggregate => aggregate.AdjustStock(
                command.Delta,
                command.Reason,
                command.AdjustedByUserId,
                command.OccurredOnUtc),
            correlationId: command.CorrelationId,
            ct: ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Adjusted stock for Product {ProductId} by {Delta} (by {UserId}, version after append: {Version})",
                command.ProductId, command.Delta, command.AdjustedByUserId, result.Value.Version);
        }

        return result.ToResult();
    }
}
