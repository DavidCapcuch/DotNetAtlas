using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Ordering.Domain.Orders.Specifications;
using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderStockReserved;

/// <summary>
/// Handles <see cref="MarkOrderStockReservedCommand"/> — loads the order and
/// calls <c>Order.MarkStockReserved</c>. FSM-violation is bug-class
/// (<c>DataIntegrityException</c> from the aggregate); user-visible failures
/// are limited to NotFound.
/// </summary>
public sealed class MarkOrderStockReservedCommandHandler : ICommandHandler<MarkOrderStockReservedCommand>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkOrderStockReservedCommandHandler> _logger;

    public MarkOrderStockReservedCommandHandler(
        IOrderingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<MarkOrderStockReservedCommandHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(MarkOrderStockReservedCommand command, CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .WithSpecification(new OrderByIdSpec(command.OrderId))
            .FirstOrDefaultAsync(ct);
        if (order is null)
        {
            return Result.Fail(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var transition = order.MarkStockReserved(command.ReservationId, _timeProvider.GetUtcNow());
        if (transition.IsFailed)
        {
            return transition;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Order {OrderId} marked StockReserved (ReservationId {ReservationId})",
            order.Id, command.ReservationId);

        return Result.Ok();
    }
}
