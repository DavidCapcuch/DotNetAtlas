using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.Common;
using Inventory.Domain.StockItems.Errors;
using Inventory.Domain.StockItems.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.SharedKernel.Exceptions;

namespace Inventory.Application.StockItems.ReceiveStock;

internal sealed class ReceiveStockCommandHandler : ICommandHandler<ReceiveStockCommand, StockLevelResponse>
{
    private readonly IEventStore _eventStore;
    private readonly IInventoryDbContext _db;
    private readonly ILogger<ReceiveStockCommandHandler> _logger;

    public ReceiveStockCommandHandler(
        IEventStore eventStore,
        IInventoryDbContext db,
        ILogger<ReceiveStockCommandHandler> logger)
    {
        _eventStore = eventStore;
        _db = db;
        _logger = logger;
    }

    public async Task<Result<StockLevelResponse>> HandleAsync(ReceiveStockCommand command, CancellationToken ct)
    {
        var sourceResult = StockSource.Create(command.Source);
        if (sourceResult.IsFailed)
        {
            return sourceResult.ToResult<StockLevelResponse>();
        }

        var appendResult = await _eventStore.AppendAsync(
            streamId: command.ProductId,
            command: aggregate => aggregate.ReceiveStock(
                command.Quantity,
                sourceResult.Value,
                command.ReceivedByUserId,
                command.OccurredOnUtc),
            correlationId: command.CorrelationId,
            ct: ct).ConfigureAwait(false);

        if (appendResult.IsFailed)
        {
            return appendResult.ToResult<StockLevelResponse>();
        }

        _logger.LogInformation(
            "Received {Quantity} units of Product {ProductId} from {Source} (version after append: {Version})",
            command.Quantity, command.ProductId, sourceResult.Value.Value, appendResult.Value.Version);

        // Same DbContext scope, same transaction just committed: the projection
        // row is durable. Re-read with AsNoTracking to bypass identity-map
        // hits that would return the pre-append snapshot. A missing row means
        // CurrentStockLevelsProjectionHandler silently failed — bug-class.
        var row = await _db.CurrentStockLevels
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProductId == command.ProductId, ct)
            .ConfigureAwait(false)
            ?? throw new DataIntegrityException(
                "Inventory.CurrentStockLevels.RowMissingAfterAppend",
                $"current_stock_levels missing row for ProductId {command.ProductId} after successful event-store append.");

        return Result.Ok(row.ToStockLevelResponse());
    }
}
