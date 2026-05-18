using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Platform.CQRS;
using Platform.SharedKernel.Exceptions;

namespace Inventory.Application.StockItems.AdjustStock;

internal sealed class AdjustStockCommandHandler : ICommandHandler<AdjustStockCommand, StockLevelResponse>
{
    private readonly IEventStore _eventStore;
    private readonly IInventoryDbContext _db;
    private readonly ILogger<AdjustStockCommandHandler> _logger;

    public AdjustStockCommandHandler(
        IEventStore eventStore,
        IInventoryDbContext db,
        ILogger<AdjustStockCommandHandler> logger)
    {
        _eventStore = eventStore;
        _db = db;
        _logger = logger;
    }

    public async Task<Result<StockLevelResponse>> HandleAsync(AdjustStockCommand command, CancellationToken ct)
    {
        var appendResult = await _eventStore.AppendAsync(
            streamId: command.ProductId,
            command: aggregate => aggregate.AdjustStock(
                command.Delta,
                command.Reason,
                command.AdjustedByUserId,
                command.OccurredOnUtc),
            correlationId: command.CorrelationId,
            ct: ct).ConfigureAwait(false);

        if (appendResult.IsFailed)
        {
            return appendResult.ToResult<StockLevelResponse>();
        }

        _logger.LogInformation(
            "Adjusted stock for Product {ProductId} by {Delta} (version after append: {Version})",
            command.ProductId, command.Delta, appendResult.Value.Version);

        // AdjustedByUserId is caller-supplied in the request body (not derived
        // from the JWT sub claim) so it must not be logged at Information
        // level as an audit signal — a caller could spoof any Guid. Demoted
        // to Debug so it stays in dev/diagnostic traces but isn't ingested
        // into the audit log. JWT-sub-vs-body validation is the alternative;
        // see #155 for design context.
        _logger.LogDebug(
            "AdjustStock for Product {ProductId} attributed to AdjustedByUserId {UserId} (request-body value, not authenticated)",
            command.ProductId, command.AdjustedByUserId);

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
