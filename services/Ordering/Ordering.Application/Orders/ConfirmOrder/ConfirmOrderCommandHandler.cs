using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Ordering.Domain.Orders.Specifications;
using Platform.CQRS;

namespace Ordering.Application.Orders.ConfirmOrder;

public sealed class ConfirmOrderCommandHandler : ICommandHandler<ConfirmOrderCommand>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConfirmOrderCommandHandler> _logger;

    public ConfirmOrderCommandHandler(
        IOrderingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<ConfirmOrderCommandHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(ConfirmOrderCommand command, CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .WithSpecification(new OrderByIdSpec(command.OrderId))
            .FirstOrDefaultAsync(ct);
        if (order is null)
        {
            return Result.Fail(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var transition = order.Confirm(_timeProvider.GetUtcNow());
        if (transition.IsFailed)
        {
            return transition;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Order {OrderId} confirmed", order.Id);
        return Result.Ok();
    }
}
