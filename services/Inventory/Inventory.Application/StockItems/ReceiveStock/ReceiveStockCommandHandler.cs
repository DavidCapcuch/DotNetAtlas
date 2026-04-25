using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Domain.StockItems.ValueObjects;
using Microsoft.Extensions.Logging;
using Platform.CQRS;

namespace Inventory.Application.StockItems.ReceiveStock;

internal sealed class ReceiveStockCommandHandler : ICommandHandler<ReceiveStockCommand>
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<ReceiveStockCommandHandler> _logger;

    public ReceiveStockCommandHandler(
        IEventStore eventStore,
        ILogger<ReceiveStockCommandHandler> logger)
    {
        _eventStore = eventStore;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ReceiveStockCommand command, CancellationToken ct)
    {
        var sourceResult = StockSource.Create(command.Source);
        if (sourceResult.IsFailed)
        {
            return sourceResult.ToResult();
        }

        var result = await _eventStore.AppendAsync(
            streamId: command.ProductId,
            command: aggregate => aggregate.ReceiveStock(
                command.Quantity,
                sourceResult.Value,
                command.ReceivedByUserId,
                command.OccurredOnUtc),
            correlationId: command.CorrelationId,
            ct: ct).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Received {Quantity} units of Product {ProductId} from {Source} (version after append: {Version})",
                command.Quantity, command.ProductId, sourceResult.Value.Value, result.Value.Version);
        }

        return result.ToResult();
    }
}
