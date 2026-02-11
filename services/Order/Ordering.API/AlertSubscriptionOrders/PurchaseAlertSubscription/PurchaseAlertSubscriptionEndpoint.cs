using System.Net;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.AvroExtensions;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Order.AlertSubscriptions;
using Ordering.API.AlertSubscriptionOrders.GetAlertSubscriptionOrderStatus;
using Ordering.Application.AlertSubscriptions.PurchaseAlertSubscription;
using Ordering.Application.Common.Data;
using Ordering.Application.Common.Messaging;
using Ordering.Domain.AlertSubscriptionOrders;

namespace Ordering.API.AlertSubscriptionOrders.PurchaseAlertSubscription;

/// <summary>
/// Endpoint for initiating a new alert subscription purchase.
/// Creates a SubscriptionOrder entity and publishes an AlertSubscriptionPurchaseInitiatedEvent
/// via the transactional outbox to trigger the Purchase Alert Subscription Saga.
/// </summary>
internal class PurchaseAlertSubscriptionEndpoint : Endpoint<PurchaseAlertSubscriptionCommand>
{
    private readonly IOrderingDbContext _orderingDbContext;
    private readonly ITransactionalOutbox<IOrderingDbContext> _transactionalOutbox;
    private readonly TimeProvider _timeProvider;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PurchaseAlertSubscriptionEndpoint> _logger;

    public PurchaseAlertSubscriptionEndpoint(
        IOrderingDbContext orderingDbContext,
        ITransactionalOutbox<IOrderingDbContext> transactionalOutbox,
        TimeProvider timeProvider,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PurchaseAlertSubscriptionEndpoint> logger)
    {
        _orderingDbContext = orderingDbContext;
        _transactionalOutbox = transactionalOutbox;
        _timeProvider = timeProvider;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
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

    public override async Task HandleAsync(PurchaseAlertSubscriptionCommand req, CancellationToken ct)
    {
        var order = new AlertSubscriptionOrder
        {
            Id = Guid.CreateVersion7(),
            UserId = req.UserId,
            AlertSubscriptionOrderType = AlertSubscriptionOrderType.Purchase,
            PaymentMethodId = req.PaymentMethodId,
            Tier = req.Tier.ToString(),
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
                new AlertSubscriptionPurchaseInitiatedEvent
                {
                    UserId = req.UserId,
                    PaymentMethodId = req.PaymentMethodId,
                    Tier = req.Tier,
                    DurationDays = req.DurationDays,
                    Amount = req.Amount.ToAvroDecimal(4),
                    Currency = req.Currency,
                    IdempotencyKey = req.IdempotencyKey,
                    InitiatedAtUtc = order.CreatedAtUtc
                });

            await _transactionalOutbox.SaveChangesAsync(ct);
        }, ct);

        _logger.LogInformation(
            "Alert subscription purchase initiated - OrderId: {OrderId}, " +
            "UserId: {UserId}, Tier: {Tier}, Amount: {Amount} {Currency}",
            order.Id, req.UserId, req.Tier, req.Amount, req.Currency);

        var sendFeedbackResult = await _sendFeedbackHandler.HandleAsync(sendFeedbackCommand, ct);

        await sendFeedbackResult.MatchAsync(
            id => Send.CreatedAtAsync<GetFeedbackByIdEndpoint>(
                new GetFeedbackByIdQuery
                {
                    Id = id
                },
                cancellation: ct),
            failureResult => Send.SendErrorResponseAsync(failureResult, ct));
    }
}
