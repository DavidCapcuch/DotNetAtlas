using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.Errors;
using Platform.CQRS;

namespace Ordering.Application.Orders.MarkOrderPaymentCompleted;

/// <summary>
/// Handles <see cref="MarkOrderPaymentCompletedCommand"/>. Same shape as
/// the stock-reserved handler — load, transition, save. FSM-violation is
/// bug-class.
/// </summary>
public sealed class MarkOrderPaymentCompletedCommandHandler : ICommandHandler<MarkOrderPaymentCompletedCommand>
{
    private readonly IOrderingDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MarkOrderPaymentCompletedCommandHandler> _logger;

    public MarkOrderPaymentCompletedCommandHandler(
        IOrderingDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<MarkOrderPaymentCompletedCommandHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(MarkOrderPaymentCompletedCommand command, CancellationToken ct)
    {
        var order = await _dbContext.Orders
            .FirstOrDefaultAsync(o => o.Id == command.OrderId, ct);
        if (order is null)
        {
            return Result.Fail(OrderingErrors.OrderNotFound(command.OrderId));
        }

        var transition = order.MarkPaymentCompleted(command.PaymentTransactionId, _timeProvider.GetUtcNow());
        if (transition.IsFailed)
        {
            return transition;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Order {OrderId} marked PaymentCompleted (PaymentTransactionId {PaymentTransactionId})",
            order.Id, command.PaymentTransactionId);

        return Result.Ok();
    }
}
