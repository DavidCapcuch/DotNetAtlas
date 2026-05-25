using FluentResults;
using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Data;
using Ordering.Domain.Orders;
using Platform.CQRS;

namespace Ordering.Application.Orders.GetOrdersByBuyer;

public sealed class GetOrdersByBuyerQueryHandler
    : IQueryHandler<GetOrdersByBuyerQuery, GetOrdersByBuyerResponse>
{
    private readonly IOrderingDbContext _dbContext;

    public GetOrdersByBuyerQueryHandler(IOrderingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetOrdersByBuyerResponse>> HandleAsync(
        GetOrdersByBuyerQuery query,
        CancellationToken ct)
    {
        var status = ParseStatus(query.Status);

        // SQL-side projection (#238, ADR-0021) — the list endpoint deliberately
        // returns a summary shape that's narrower than GetOrderByIdResponse
        // (use-cases.md § 3.4.2). LastStatusChangeAtUtc is computed in SQL via
        // EF's `??` → COALESCE translation; ItemCount via a correlated count
        // on the owned `ordering.order_items` table.
        var filtered = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == query.BuyerId)
            .Where(o => status == null || o.Status == status);

        var total = await filtered
            .TagWith($"{nameof(GetOrdersByBuyerQueryHandler)}:Count")
            .CountAsync(ct);

        var items = await filtered
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .TagWith(nameof(GetOrdersByBuyerQueryHandler))
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.Status.Name,
                o.Total.Amount,
                o.Total.Currency.Name,
                o.Items.Count,
                o.CreatedAtUtc,
                // COALESCE chain matches use-cases.md § 3.4.2 verbatim — pick
                // the most-recent non-null lifecycle timestamp. `o.Shipment`
                // is an owned nullable VO; the conditional + cast keeps the
                // expression's element type DateTimeOffset? so the chain stays
                // null-coalesceable. Final `o.CreatedAtUtc` is non-nullable —
                // the chain terminates here and the result type is
                // DateTimeOffset, not DateTimeOffset?.
                o.DeliveredAtUtc
                    ?? (o.Shipment == null ? (DateTimeOffset?)null : o.Shipment.ShippedAtUtc)
                    ?? o.ConfirmedAtUtc
                    ?? o.PaymentCompletedAtUtc
                    ?? o.StockReservedAtUtc
                    ?? o.CreatedAtUtc))
            .ToListAsync(ct);

        return Result.Ok(new GetOrdersByBuyerResponse
        {
            Items = items,
            Total = total,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
        });
    }

    private static OrderStatus? ParseStatus(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        // The validator is the front-line guard (see
        // GetOrdersByBuyerQueryValidator rule on Status). An unparseable
        // name reaching the handler is bug-class: validation was bypassed.
        if (!OrderStatus.TryFromName(name, out var status))
        {
            throw new Platform.SharedKernel.Exceptions.DataIntegrityException(
                "OrdersByBuyer.InvalidStatus",
                $"OrderStatus '{name}' did not parse; validator should have rejected this upstream.");
        }

        return status;
    }
}
