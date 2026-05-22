using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Ordering.Domain.Orders.Specifications;
using Platform.CQRS;

namespace Ordering.Application.Orders.GetOrderById;

public sealed class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, GetOrderByIdResponse>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly ILogger<GetOrderByIdQueryHandler> _logger;

    public GetOrderByIdQueryHandler(
        IOrderingDbContext dbContext,
        ILogger<GetOrderByIdQueryHandler> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<GetOrderByIdResponse>> HandleAsync(
        GetOrderByIdQuery query,
        CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .WithSpecification(new OrderByIdSpec(query.OrderId))
            .FirstOrDefaultAsync(ct);

        if (order is null)
        {
            return Result.Fail<GetOrderByIdResponse>(OrderingErrors.OrderNotFound(query.OrderId));
        }

        // Ownership enforcement: buyer may read only their own order. Return NotFound
        // (not Forbidden) for a cross-buyer lookup so existence is not leaked.
        // Logged at Warning so SecOps can probe for credential-stuffing
        // patterns; ordering.authz.cross_buyer_attempt counter is a
        // follow-up (see ordering-followups summary).
        if (!query.IsAdmin && order.BuyerId != query.BuyerId)
        {
            _logger.LogWarning(
                "Buyer {BuyerId} requested order {OrderId} owned by a different buyer — returning NotFound",
                query.BuyerId, query.OrderId);
            return Result.Fail<GetOrderByIdResponse>(OrderingErrors.OrderNotFound(query.OrderId));
        }

        return Result.Ok(OrderProjection.ToResponse(order));
    }
}
