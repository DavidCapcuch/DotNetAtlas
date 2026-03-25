using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.AlertSubscriptionOrders.Errors;
using Ordering.Domain.AlertSubscriptionOrders.Specifications;
using Platform.CQS;

namespace Ordering.Application.AlertSubscriptions.CompleteAlertSubscriptionOrder;

/// <summary>
/// Handles <see cref="CompleteAlertSubscriptionOrderCommand"/> by loading the aggregate
/// and transitioning it to the Completed status.
/// </summary>
public sealed class CompleteAlertSubscriptionOrderCommandHandler
    : ICommandHandler<CompleteAlertSubscriptionOrderCommand>
{
    private readonly ILogger<CompleteAlertSubscriptionOrderCommandHandler> _logger;
    private readonly IOrderingDbContext _orderingDbContext;

    public CompleteAlertSubscriptionOrderCommandHandler(
        ILogger<CompleteAlertSubscriptionOrderCommandHandler> logger,
        IOrderingDbContext orderingDbContext)
    {
        _logger = logger;
        _orderingDbContext = orderingDbContext;
    }

    public async Task<Result> HandleAsync(
        CompleteAlertSubscriptionOrderCommand command,
        CancellationToken ct)
    {
        var order = await _orderingDbContext.AlertSubscriptionOrders
            .WithSpecification(new AlertSubscriptionOrderByIdSpec(command.OrderId))
            .FirstOrDefaultAsync(ct);

        if (order is null)
        {
            return Result.Fail(AlertSubscriptionOrderErrors.NotFound(command.OrderId));
        }

        order.Complete();
        await _orderingDbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Alert subscription order completed - OrderId: {OrderId}",
            command.OrderId);

        return Result.Ok();
    }
}
