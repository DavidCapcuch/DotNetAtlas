using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderShipped;

public sealed class MarkOrderShippedCommandHandler : ICommandHandler<MarkOrderShippedCommand>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkOrderShippedCommandHandler> _logger;

    public MarkOrderShippedCommandHandler(
        IOrderingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<MarkOrderShippedCommandHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(MarkOrderShippedCommand command, CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, ct);
        if (order is null)
        {
            return Result.Fail(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var transition = order.MarkShipped(command.Carrier, command.TrackingNumber, _timeProvider.GetUtcNow());
        if (transition.IsFailed)
        {
            return transition;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Order {OrderId} marked Shipped (Carrier {Carrier}, Tracking {TrackingNumber})",
            order.Id, command.Carrier, command.TrackingNumber);

        return Result.Ok();
    }
}
