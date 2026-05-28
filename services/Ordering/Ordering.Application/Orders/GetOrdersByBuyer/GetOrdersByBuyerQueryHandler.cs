using FluentResults;
using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Data;
using Ordering.Domain.Orders;
using Platform.CQRS;
using Platform.SharedKernel.Exceptions;

namespace Ordering.Application.Orders.GetOrdersByBuyer;

public sealed class GetOrdersByBuyerQueryHandler
    : IQueryHandler<GetOrdersByBuyerQuery, GetOrdersByBuyerResponse>
{
    private const int MaxPageSize = 100;

    private readonly IOrderingDbContext _dbContext;

    public GetOrdersByBuyerQueryHandler(IOrderingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<GetOrdersByBuyerResponse>> HandleAsync(
        GetOrdersByBuyerQuery query,
        CancellationToken ct)
    {
        // Defence-in-depth — GetOrdersByBuyerQueryValidator is the front-line
        // guard for PageNumber / PageSize. This catches the bug-class case
        // where the ValidationBehavior pipeline is bypassed (e.g. handler
        // constructed directly outside the CQRS scope): PageSize=0 would
        // silently return an empty page, PageNumber<1 would push the EF
        // offset (PageNumber-1)*PageSize to <= 0 (undefined across providers),
        // and PageSize > MaxPageSize would defeat the wave-1 100-row cap.
        if (query.PageNumber < 1 || query.PageSize <= 0 || query.PageSize > MaxPageSize)
        {
            throw new DataIntegrityException(
                "OrdersByBuyer.OutOfRange",
                $"PageNumber / PageSize out of range (PageNumber={query.PageNumber}, PageSize={query.PageSize}, MaxPageSize={MaxPageSize}); validator should have rejected this upstream.");
        }

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
                // the timestamp of the order's current state. Cancellation and
                // Failure sit at the FRONT: they are terminal alternative
                // states whose timestamp supersedes any retained happy-path
                // timestamp (a Shipped-then-Cancelled row's Status is
                // "Cancelled", so its LastStatusChangeAtUtc must be
                // CancelledAtUtc, not the now-superseded ShippedAtUtc).
                // `o.Cancellation` / `o.Failure` / `o.Shipment` are owned
                // nullable VOs; the conditional + cast keeps the expression's
                // element type DateTimeOffset? so the chain stays
                // null-coalesceable. Final `o.CreatedAtUtc` is non-nullable —
                // the chain terminates here and the result type is
                // DateTimeOffset, not DateTimeOffset?.
                (o.Cancellation == null ? (DateTimeOffset?)null : o.Cancellation.CancelledAtUtc)
                    ?? (o.Failure == null ? (DateTimeOffset?)null : o.Failure.FailedAtUtc)
                    ?? o.DeliveredAtUtc
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
            throw InvalidStatus(name);
        }

        return status;
    }

    private static DataIntegrityException InvalidStatus(string name) =>
        new(
            "OrdersByBuyer.InvalidStatus",
            $"OrderStatus '{name}' did not parse; validator should have rejected this upstream.");
}
