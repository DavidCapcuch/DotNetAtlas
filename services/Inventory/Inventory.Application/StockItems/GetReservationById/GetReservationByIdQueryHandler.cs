using FluentResults;
using Inventory.Application.Common.Data;
using Inventory.Application.StockItems.Common;
using Inventory.Domain.StockItems.Errors;
using Microsoft.EntityFrameworkCore;
using Platform.CQRS;

namespace Inventory.Application.StockItems.GetReservationById;

internal sealed class GetReservationByIdQueryHandler
    : IQueryHandler<GetReservationByIdQuery, ReservationAuditResponse>
{
    private readonly IInventoryDbContext _db;

    public GetReservationByIdQueryHandler(IInventoryDbContext db)
    {
        _db = db;
    }

    public async Task<Result<ReservationAuditResponse>> HandleAsync(
        GetReservationByIdQuery query,
        CancellationToken ct)
    {
        var row = await _db.ReservationAudit
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ReservationId == query.ReservationId, ct)
            .ConfigureAwait(false);

        return row is null
            ? Result.Fail<ReservationAuditResponse>(InventoryErrors.ReservationNotFound(query.ReservationId))
            : Result.Ok(row.ToReservationAuditResponse());
    }
}
