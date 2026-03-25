using System.Net;
using FastEndpoints;
using Ordering.API.AlertSubscriptionOrders.GetAlertSubscriptionOrderStatus;
using Ordering.API.Common.Extensions;
using Ordering.Application.AlertSubscriptions.GetAlertSubscriptionOrderStatus;
using Ordering.Application.AlertSubscriptions.PurchaseAlertSubscription;
using Ordering.Domain.AlertSubscriptionOrders;
using CQS = Platform.CQS;

namespace Ordering.API.AlertSubscriptionOrders.PurchaseAlertSubscription;

/// <summary>
/// Endpoint for initiating a new alert subscription purchase.
/// Delegates to <see cref="PurchaseAlertSubscriptionCommandHandler"/> which creates the order,
/// raises domain events, and publishes the integration event via the transactional outbox.
/// </summary>
internal sealed class PurchaseAlertSubscriptionEndpoint : Endpoint<PurchaseAlertSubscriptionCommand>
{
    private readonly CQS.ICommandHandler<PurchaseAlertSubscriptionCommand, Guid> _purchaseHandler;

    public PurchaseAlertSubscriptionEndpoint(
        CQS.ICommandHandler<PurchaseAlertSubscriptionCommand, Guid> purchaseHandler)
    {
        _purchaseHandler = purchaseHandler;
    }

    public override void Configure()
    {
        Post("/purchase");
        Version(1);
        Group<AlertSubscriptionOrdersGroup>();
        Summary(s =>
        {
            s.Summary = "Initiate a new alert subscription purchase.";
            s.Description =
                "Creates a subscription order and publishes an initiation event " +
                "to trigger the Purchase Alert Subscription Saga.";
            s.ExampleRequest = new PurchaseAlertSubscriptionCommand
            {
                PaymentMethodId = Guid.CreateVersion7(),
                Tier = AlertSubscriptionTier.Pro,
                DurationDays = 30,
                Amount = 9.99m,
                Currency = "USD"
            };
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.Created);
            b.Produces((int)HttpStatusCode.BadRequest);
            b.Produces((int)HttpStatusCode.Conflict);
        });
    }

    public override async Task HandleAsync(PurchaseAlertSubscriptionCommand req, CancellationToken ct)
    {
        var purchaseResult = await _purchaseHandler.HandleAsync(req, ct);

        await purchaseResult.MatchAsync(
            orderId => Send.CreatedAtAsync<GetAlertSubscriptionOrderStatusEndpoint>(
                new GetAlertSubscriptionOrderStatusQuery
                {
                    Id = orderId
                },
                cancellation: ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
