using System.Net;
using FastEndpoints;
using Ordering.API.AlertSubscriptionOrders.GetAlertSubscriptionOrderStatus;
using Ordering.API.Common.Extensions;
using Ordering.Application.AlertSubscriptions.ExtendAlertSubscription;
using Ordering.Application.AlertSubscriptions.GetAlertSubscriptionOrderStatus;
using CQS = Platform.CQS;

namespace Ordering.API.AlertSubscriptionOrders.ExtendAlertSubscription;

/// <summary>
/// Endpoint for initiating an alert subscription extension.
/// Delegates to <see cref="ExtendAlertSubscriptionCommandHandler"/> which creates the order,
/// raises domain events, and publishes the integration event via the transactional outbox.
/// </summary>
internal sealed class ExtendAlertSubscriptionEndpoint : Endpoint<ExtendAlertSubscriptionCommand>
{
    private readonly CQS.ICommandHandler<ExtendAlertSubscriptionCommand, Guid> _extendAlertSubscriptionHandler;

    public ExtendAlertSubscriptionEndpoint(
        CQS.ICommandHandler<ExtendAlertSubscriptionCommand, Guid> extendAlertSubscriptionHandler)
    {
        _extendAlertSubscriptionHandler = extendAlertSubscriptionHandler;
    }

    public override void Configure()
    {
        Post("/extend");
        Version(1);
        Group<AlertSubscriptionOrdersGroup>();
        Summary(s =>
        {
            s.Summary = "Initiate an alert subscription extension.";
            s.Description =
                "Creates a subscription order and publishes an initiation event " +
                "to trigger the Extend Alert Subscription Saga.";
            s.ExampleRequest = new ExtendAlertSubscriptionCommand
            {
                PaymentMethodId = Guid.CreateVersion7(),
                DurationDays = 30,
                Amount = 4.99m,
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

    public override async Task HandleAsync(ExtendAlertSubscriptionCommand req, CancellationToken ct)
    {
        var extendResult = await _extendAlertSubscriptionHandler.HandleAsync(req, ct);

        await extendResult.MatchAsync(
            orderId => Send.CreatedAtAsync<GetAlertSubscriptionOrderStatusEndpoint>(
                new GetAlertSubscriptionOrderStatusQuery
                {
                    Id = orderId
                },
                cancellation: ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
