using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Ordering.Domain.Orders.Specifications;
using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderFailed;

public sealed class MarkOrderFailedCommandHandler : ICommandHandler<MarkOrderFailedCommand>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkOrderFailedCommandHandler> _logger;

    public MarkOrderFailedCommandHandler(
        IOrderingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<MarkOrderFailedCommandHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(MarkOrderFailedCommand command, CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .WithSpecification(new OrderByIdSpec(command.OrderId))
            .FirstOrDefaultAsync(ct);
        if (order is null)
        {
            return Result.Fail(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var transition = order.Fail(command.ErrorCode, command.ErrorMessage, _timeProvider.GetUtcNow());
        if (transition.IsFailed)
        {
            return transition;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Order {OrderId} marked Failed (ErrorCode {ErrorCode})",
            order.Id, command.ErrorCode);

        return Result.Ok();
    }
}
