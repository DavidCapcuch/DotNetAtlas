using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Ordering.Application.Common.Data;
using Ordering.Application.Orders.GetOrderById;
using Ordering.Domain.Orders;
using Ordering.Domain.Orders.Specifications;
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

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .WithSpecification(new OrdersByBuyerSpec(query.BuyerId, status, query.Skip, query.Take))
            .ToListAsync(ct);

        return Result.Ok(new GetOrdersByBuyerResponse
        {
            Orders = [.. orders.Select(OrderProjection.ToResponse)],
            Skip = query.Skip,
            Take = query.Take,
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
