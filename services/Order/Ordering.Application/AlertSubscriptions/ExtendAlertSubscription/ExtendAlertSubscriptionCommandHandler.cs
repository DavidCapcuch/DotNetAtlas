using System.Diagnostics;
using DotNetAtlas.CQS;
using FluentResults;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Observability.Tracing;
using Ordering.Domain.AlertSubscriptionOrders;
using Ordering.Domain.ValueObjects;

namespace Ordering.Application.AlertSubscriptions.ExtendAlertSubscription;

public sealed class ExtendAlertSubscriptionCommandHandler
    : ICommandHandler<ExtendAlertSubscriptionCommand, Guid>
{
    private readonly ILogger<ExtendAlertSubscriptionCommandHandler> _logger;
    private readonly IOrderingDbContext _orderingDbContext;

    public ExtendAlertSubscriptionCommandHandler(
        ILogger<ExtendAlertSubscriptionCommandHandler> logger,
        IOrderingDbContext orderingDbContext)
    {
        _logger = logger;
        _orderingDbContext = orderingDbContext;
    }

    public async Task<Result<Guid>> HandleAsync(
        ExtendAlertSubscriptionCommand command,
        CancellationToken ct)
    {
        var priceResult = Money.Create(command.Amount, command.Currency);
        if (priceResult.IsFailed)
        {
            return Result.Fail(priceResult.Errors);
        }

        var extensionOrder = AlertSubscriptionOrder.CreateExtensionOrder(
            command.UserId, command.PaymentMethodId, command.DurationDays,
            priceResult.Value);

        _orderingDbContext.AlertSubscriptionOrders.Add(extensionOrder);
        await _orderingDbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Alert subscription extension order created - OrderId: {OrderId}, UserId: {UserId}, Amount: {Amount} {Currency}",
            extensionOrder.Id, command.UserId, command.Amount, command.Currency);

        Activity.Current?.SetTag(TraceTags.AlertSubscriptionOrder, extensionOrder.Id.ToString());

        return Result.Ok(extensionOrder.Id);
    }
}
