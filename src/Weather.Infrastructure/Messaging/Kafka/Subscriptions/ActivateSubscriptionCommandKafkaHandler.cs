using KafkaFlow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.CQS;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;
using Weather.Alerts;
using Weather.Application.Common.Data;
using Weather.Application.Common.Messaging;
using Weather.Application.WeatherAlerts.PurchaseSubscription;

namespace Weather.Infrastructure.Messaging.Kafka.Subscriptions;

/// <summary>
/// Handles ActivateSubscriptionCommand from the Purchase Saga.
/// Activates subscription in the domain and emits:
/// - SubscriptionActivatedEvent on success (via domain event handler + outbox)
/// - SubscriptionActivationFailedEvent on failure for saga compensation.
/// </summary>
/// <remarks>
/// Idempotent processing is handled by InboxMiddleware in the KafkaFlow pipeline.
/// Success events are published by domain event handlers to maintain DDD separation.
/// </remarks>
public class ActivateSubscriptionCommandKafkaHandler : IMessageHandler<ActivateAlertSubscriptionCommand>
{
    private readonly ICommandHandler<PurchaseSubscriptionCommand> _purchaseSubscriptionCommandHandler;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ActivateSubscriptionCommandKafkaHandler> _logger;
    private readonly TopicsOptions _topicsOptions;

    public ActivateSubscriptionCommandKafkaHandler(
        ICommandHandler<PurchaseSubscriptionCommand> purchaseSubscriptionCommandHandler,
        TimeProvider timeProvider,
        ILogger<ActivateSubscriptionCommandKafkaHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _purchaseSubscriptionCommandHandler = purchaseSubscriptionCommandHandler;
        _timeProvider = timeProvider;
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(IMessageContext context, ActivateAlertSubscriptionCommand message)
    {
        var origin = context.ExtractOrigin();
        _logger.LogDebug(
            "Received ActivateSubscriptionCommand from origin: {Origin}, CorrelationId: {CorrelationId}",
            origin ?? "unknown", message.CorrelationId);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            var purchaseSubscriptionCommand = message.ToPurchaseSubscriptionCommand();
            var purchaseSubscriptionResult =
                await _purchaseSubscriptionCommandHandler.HandleAsync(purchaseSubscriptionCommand, cancellationToken);

            if (purchaseSubscriptionResult.IsFailed)
            {
                _logger.LogWarning(
                    "Subscription activation failed for CorrelationId {CorrelationId}, UserId {UserId}: {Errors}",
                    message.CorrelationId, message.UserId,
                    string.Join(", ", purchaseSubscriptionResult.Errors));

                var failedEvent = message.ToSubscriptionActivationFailedEvent(
                    purchaseSubscriptionResult.Errors.ToAvroErrorDetails(),
                    _timeProvider.GetUtcNow().UtcDateTime);

                // Publish failure event for saga compensation (refund trigger)
                // Key must be CorrelationId so saga can correlate the failure event
                _transactionalOutbox.AddOutboxMessage(
                    _topicsOptions.WeatherAlertSubscriptions,
                    message.CorrelationId.ToString(),
                    failedEvent);
                await _transactionalOutbox.SaveChangesAsync(cancellationToken);

                var errorCodes = string.Join(", ", failedEvent.Errors.Select(e => e.ErrorCode));
                _logger.LogInformation(
                    "Published SubscriptionActivationFailedEvent for CorrelationId {CorrelationId}, " +
                    "UserId {UserId}, PaymentTransactionId {PaymentTransactionId}, ErrorCodes: [{ErrorCodes}]",
                    message.CorrelationId, message.UserId, message.PaymentTransactionId, errorCodes);
            }
            else
            {
                // Success: Domain event handlers will publish SubscriptionActivatedEvent via outbox
                await _transactionalOutbox.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Subscription activation succeeded for CorrelationId {CorrelationId}, UserId {UserId}",
                    message.CorrelationId, message.UserId);
            }
        }, cancellationToken);
    }
}
