using System.Diagnostics;
using DotNetAtlas.CQS;
using FluentResults;
using Microsoft.Extensions.Logging;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Observability.Tracing;
using Ordering.Domain.AlertSubscriptionOrders;
using Ordering.Domain.ValueObjects;

namespace Ordering.Application.AlertSubscriptions.PurchaseAlertSubscription;

public sealed class PurchaseAlertSubscriptionCommandHandler
    : ICommandHandler<PurchaseAlertSubscriptionCommand, Guid>
{
    private readonly ILogger<PurchaseAlertSubscriptionCommandHandler> _logger;
    private readonly IOrderingDbContext _orderingDbContext;

    public PurchaseAlertSubscriptionCommandHandler(
        ILogger<PurchaseAlertSubscriptionCommandHandler> logger,
        IOrderingDbContext orderingDbContext)
    {
        _logger = logger;
        _orderingDbContext = orderingDbContext;
    }

    public async Task<Result<Guid>> HandleAsync(
        PurchaseAlertSubscriptionCommand command,
        CancellationToken ct)
    {
        var priceResult = Money.Create(command.Amount, command.Currency);
        if (priceResult.IsFailed)
        {
            return Result.Fail(priceResult.Errors);
        }

        var purchaseOrder = AlertSubscriptionOrder.CreatePurchaseOrder(
            command.UserId, command.PaymentMethodId, command.Tier, command.DurationDays,
            priceResult.Value);

        _orderingDbContext.AlertSubscriptionOrders.Add(purchaseOrder);
        await _orderingDbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Alert subscription purchase order created - OrderId: {OrderId}, UserId: {UserId}, " +
            "Tier: {Tier}, Amount: {Amount} {Currency}",
            purchaseOrder.Id, command.UserId, command.Tier, command.Amount, command.Currency);

        Activity.Current?.SetTag(TraceTags.AlertSubscriptionOrder, purchaseOrder.Id.ToString());

        return Result.Ok(purchaseOrder.Id);
    }
}
