using KafkaFlow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notifications.Application.Common.Data;
using Notifications.Infrastructure.Common.Config;
using Payments.Transactions;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Notifications.Infrastructure.AuthorizePayment;

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
public class AuthorizePaymentCommandKafkaHandler : IMessageHandler<AuthorizePaymentCommand>
{
    private readonly ITransactionalOutbox<INotificationDbContext> _transactionalOutbox;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthorizePaymentCommandKafkaHandler> _logger;
    private readonly TopicsOptions _topicsOptions;

    public AuthorizePaymentCommandKafkaHandler(
        TimeProvider timeProvider,
        ILogger<AuthorizePaymentCommandKafkaHandler> logger,
        ITransactionalOutbox<INotificationDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(IMessageContext context, AuthorizePaymentCommand message)
    {
        var origin = context.ExtractOrigin();
        _logger.LogDebug(
            "Received ExtendSubscriptionCommand from origin: {Origin}, CorrelationId: {CorrelationId}",
            origin ?? "unknown", message.CorrelationId);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            _logger.LogInformation("Payment Service: Fake Authorizing payment");
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            _logger.LogInformation("Payment Service: Fake Authorized payment");

            _transactionalOutbox.AddOutboxMessage(_topicsOptions.Payments, message.CorrelationId.ToString(),
                new PaymentAuthorizedEvent
                {
                    CorrelationId = message.CorrelationId,
                    Currency = message.Currency,
                    Amount = message.Amount,
                    AuthorizationId = Guid.CreateVersion7().ToString(),
                    UserId = message.UserId,
                    AuthorizedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
                    ExpiresAtUtc = _timeProvider.GetUtcNow().AddDays(7).UtcDateTime
                });
            await _transactionalOutbox.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payment Service: Fake PaymentAuthorizedEvent published");
        }, cancellationToken);
    }
}
