using System.Net;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.AvroExtensions;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Order.AlertSubscriptions;
using Ordering.Application.AlertSubscriptions.ExtendAlertSubscription;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.AlertSubscriptionOrders;
using Serilog.Context;

namespace Ordering.API.AlertSubscriptionOrders.ExtendAlertSubscription;

/// <summary>
/// Endpoint for initiating an alert subscription extension.
/// Creates a SubscriptionOrder entity and publishes an AlertSubscriptionExtensionInitiatedEvent
/// via the transactional outbox to trigger the Extend Alert Subscription Saga.
/// </summary>
internal class ExtendAlertSubscriptionEndpoint : Endpoint<ExtendAlertSubscriptionCommand>
{
    private readonly IOrderingDbContext _orderingDbContext;
    private readonly ITransactionalOutbox<IOrderingDbContext> _transactionalOutbox;
    private readonly TimeProvider _timeProvider;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<ExtendAlertSubscriptionEndpoint> _logger;

    public ExtendAlertSubscriptionEndpoint(
        IOrderingDbContext orderingDbContext,
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        TimeProvider timeProvider,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<ExtendAlertSubscriptionEndpoint> logger)
    {
        _orderingDbContext = orderingDbContext;
        _transactionalOutbox = transactionalOutbox;
        _timeProvider = timeProvider;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
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
                Currency = "USD",
                IdempotencyKey = Guid.CreateVersion7().ToString()
            };
        });
        Description(b =>
        {
            b.Produces((int)HttpStatusCode.Created);
            b.Produces((int)HttpStatusCode.BadRequest);
        });
    }

    public override async Task HandleAsync(ExtendAlertSubscriptionCommand req, CancellationToken ct)
    {
        var order = new AlertSubscriptionOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = req.UserId,
            AlertSubscriptionOrderType = AlertSubscriptionOrderType.Extension,
            PaymentMethodId = req.PaymentMethodId,
            Tier = null,
            DurationDays = req.DurationDays,
            Amount = req.Amount,
            Currency = req.Currency,
            IdempotencyKey = req.IdempotencyKey,
            Status = AlertSubscriptionOrderStatus.Initiated,
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            _orderingDbContext.AlertSubscriptionOrders.Add(order);

            _transactionalOutbox.AddOutboxMessage(
                _topicsOptions.OrderAlertSubscriptions,
                order.Id.ToString(),
                new AlertSubscriptionExtensionInitiatedEvent
                {
                    UserId = req.UserId,
                    PaymentMethodId = req.PaymentMethodId,
                    DurationDays = req.DurationDays,
                    Amount = req.Amount.ToAvroDecimal(4),
                    Currency = req.Currency,
                    IdempotencyKey = req.IdempotencyKey,
                    InitiatedAtUtc = order.CreatedAtUtc
                });

            await _transactionalOutbox.SaveChangesAsync(ct);
        }, ct);

        _logger.LogInformation(
            "Alert subscription extension initiated - OrderId: {OrderId}, " +
            "UserId: {UserId}, Amount: {Amount} {Currency}",
            order.Id, req.UserId, req.Amount, req.Currency);

        using var _ = LogContext.PushProperty("FeedbackId", sendFeedbackCommand.Id.ToString());

        var changeFeedbackResult = await _changeFeedbackHandler.HandleAsync(sendFeedbackCommand, ct);

        await changeFeedbackResult.MatchAsync(
            () => Send.NoContentAsync(ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
