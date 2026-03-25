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
using AppExtendSubscriptionCommand = Weather.Application.WeatherAlerts.ExtendSubscription.ExtendSubscriptionCommand;

namespace Weather.Infrastructure.Messaging.Kafka.Subscriptions;

/// <summary>
/// Handles ExtendSubscriptionCommand from the Extension Saga.
/// Extends subscription in the domain and emits:
/// - SubscriptionExtendedEvent on success (via domain event handler + outbox)
/// - SubscriptionExtensionActivationFailedEvent on failure for saga compensation.
/// </summary>
/// <remarks>
/// Idempotent processing is handled by InboxMiddleware in the KafkaFlow pipeline.
/// Success events are published by domain event handlers to maintain DDD separation.
/// </remarks>
public class ExtendSubscriptionCommandKafkaHandler : IMessageHandler<ExtendAlertSubscriptionCommand>
{
    private readonly ICommandHandler<AppExtendSubscriptionCommand> _extendSubscriptionHandler;
    private readonly ITransactionalOutbox<IWeatherDbContext> _transactionalOutbox;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExtendSubscriptionCommandKafkaHandler> _logger;
    private readonly TopicsOptions _topicsOptions;

    public ExtendSubscriptionCommandKafkaHandler(
        ICommandHandler<AppExtendSubscriptionCommand> extendSubscriptionHandler,
        TimeProvider timeProvider,
        ILogger<ExtendSubscriptionCommandKafkaHandler> logger,
        ITransactionalOutbox<IWeatherDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _extendSubscriptionHandler = extendSubscriptionHandler;
        _timeProvider = timeProvider;
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(IMessageContext context, ExtendAlertSubscriptionCommand message)
    {
        var origin = context.ExtractOrigin();
        _logger.LogDebug(
            "Received ExtendSubscriptionCommand from origin: {Origin}, CorrelationId: {CorrelationId}",
            origin ?? "unknown", message.CorrelationId);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            var extendSubscriptionCommand = message.ToExtendSubscriptionCommand();
            var extendSubscriptionResult = await _extendSubscriptionHandler
                .HandleAsync(extendSubscriptionCommand, cancellationToken);

            if (extendSubscriptionResult.IsFailed)
            {
                _logger.LogWarning(
                    "Subscription extension failed for CorrelationId {CorrelationId}, UserId {UserId}: {Errors}",
                    message.CorrelationId, message.UserId,
                    string.Join(", ", extendSubscriptionResult.Errors));

                var failedEvent = message.ToSubscriptionExtensionActivationFailedEvent(
                    extendSubscriptionResult.Errors.ToAvroErrorDetails(),
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
                    "Published SubscriptionExtensionActivationFailedEvent for CorrelationId {CorrelationId}, " +
                    "UserId {UserId}, PaymentTransactionId {PaymentTransactionId}, ErrorCodes: [{ErrorCodes}]",
                    message.CorrelationId, message.UserId, message.PaymentTransactionId, errorCodes);
            }
            else
            {
                // Success: Domain event handlers will publish SubscriptionExtendedEvent via outbox
                await _transactionalOutbox.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Subscription extension succeeded for CorrelationId {CorrelationId}, UserId {UserId}",
                    message.CorrelationId, message.UserId);
            }
        }, cancellationToken);
    }
}
