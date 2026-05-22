using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Ordering.Domain.Orders.Specifications;
using Platform.CQRS;

namespace Ordering.Application.Orders.CancelOrder;

public sealed class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CancelOrderCommandHandler> _logger;

    public CancelOrderCommandHandler(
        IOrderingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<CancelOrderCommandHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(CancelOrderCommand command, CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .WithSpecification(new OrderByIdSpec(command.OrderId))
            .FirstOrDefaultAsync(ct);
        if (order is null)
        {
            return Result.Fail(OrderingErrors.OrderNotFound(command.OrderId));
        }

        // Authorization: buyers cannot see / cancel other buyers' orders.
        // Return NotFound (not Forbidden) to avoid leaking existence.
        // Logged at Warning so SecOps can probe for credential-stuffing
        // patterns; ordering.authz.cross_buyer_attempt counter is a
        // follow-up (see ordering-followups summary).
        if (!command.IsAdmin && order.BuyerId != command.BuyerId)
        {
            _logger.LogWarning(
                "Buyer {BuyerId} attempted to cancel order {OrderId} owned by a different buyer — returning NotFound",
                command.BuyerId, command.OrderId);
            return Result.Fail(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var transition = order.Cancel(command.Reason, _timeProvider.GetUtcNow());
        if (transition.IsFailed)
        {
            return transition;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Order {OrderId} cancelled by {BuyerId} (admin={IsAdmin})",
            order.Id, command.BuyerId, command.IsAdmin);

        return Result.Ok();
    }
}
