using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Ordering.Domain.Orders.Specifications;
using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderDelivered;

public sealed class MarkOrderDeliveredCommandHandler : ICommandHandler<MarkOrderDeliveredCommand>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkOrderDeliveredCommandHandler> _logger;

    public MarkOrderDeliveredCommandHandler(
        IOrderingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<MarkOrderDeliveredCommandHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(MarkOrderDeliveredCommand command, CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .WithSpecification(new OrderByIdSpec(command.OrderId))
            .FirstOrDefaultAsync(ct);
        if (order is null)
        {
            return Result.Fail(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var transition = order.MarkDelivered(_timeProvider.GetUtcNow());
        if (transition.IsFailed)
        {
            return transition;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Order {OrderId} marked Delivered", order.Id);
        return Result.Ok();
    }
}
