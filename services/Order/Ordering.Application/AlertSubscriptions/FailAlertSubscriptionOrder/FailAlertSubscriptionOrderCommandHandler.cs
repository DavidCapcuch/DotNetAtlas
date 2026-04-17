using Ardalis.Specification.EntityFrameworkCore;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Domain.AlertSubscriptionOrders.Errors;
using Ordering.Domain.AlertSubscriptionOrders.Specifications;
using Platform.CQRS;

namespace Ordering.Application.AlertSubscriptions.FailAlertSubscriptionOrder;

/// <summary>
/// Handles <see cref="FailAlertSubscriptionOrderCommand"/> by loading the aggregate
/// and transitioning it to the Failed status.
/// </summary>
public sealed class FailAlertSubscriptionOrderCommandHandler
    : ICommandHandler<FailAlertSubscriptionOrderCommand>
{
    private readonly ILogger<FailAlertSubscriptionOrderCommandHandler> _logger;
    private readonly IOrderingDbContext _orderingDbContext;

    public FailAlertSubscriptionOrderCommandHandler(
        ILogger<FailAlertSubscriptionOrderCommandHandler> logger,
        IOrderingDbContext orderingDbContext)
    {
        _logger = logger;
        _orderingDbContext = orderingDbContext;
    }

    public async Task<Result> HandleAsync(
        FailAlertSubscriptionOrderCommand command,
        CancellationToken ct)
    {
        var order = await _orderingDbContext.AlertSubscriptionOrders
            .WithSpecification(new AlertSubscriptionOrderByIdSpec(command.OrderId))
            .FirstOrDefaultAsync(ct);

        if (order is null)
        {
            return Result.Fail(AlertSubscriptionOrderErrors.NotFound(command.OrderId));
        }

        order.Fail();
        await _orderingDbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Alert subscription order failed - OrderId: {OrderId}",
            command.OrderId);

        return Result.Ok();
    }
}
