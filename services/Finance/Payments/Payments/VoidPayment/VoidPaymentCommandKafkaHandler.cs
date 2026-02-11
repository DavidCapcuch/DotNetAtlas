using DotNetAtlas.KafkaFlow.Inbox.EFCore;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore;
using DotNetAtlas.ReliableMessaging.Outbox.EFCore.Common;
using Finance.Payments;
using KafkaFlow;
using Microsoft.Extensions.Options;
using Payments.Common.Config;
using Payments.Common.Config.Kafka;
using Payments.Common.Persistence.Database;
using Payments.Persistence.Database;

namespace Payments.Payments.VoidPayment;

/// <summary>
/// Handles VoidPaymentCommand from the Payment Processing Saga.
/// Voids (cancels) an authorized payment that has not yet been captured and emits:
/// - PaymentVoidedEvent on success (via outbox).
/// </summary>
/// <remarks>
/// Idempotent processing is handled by InboxMiddleware in the KafkaFlow pipeline.
/// This is a fake/stub handler for development and testing purposes.
/// </remarks>
public class VoidPaymentCommandKafkaHandler : IMessageHandler<VoidPaymentCommand>
{
    private readonly ITransactionalOutbox<IPaymentDbContext> _transactionalOutbox;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VoidPaymentCommandKafkaHandler> _logger;
    private readonly TopicsOptions _topicsOptions;

    public VoidPaymentCommandKafkaHandler(
        TimeProvider timeProvider,
        ILogger<VoidPaymentCommandKafkaHandler> logger,
        ITransactionalOutbox<IPaymentDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(IMessageContext context, VoidPaymentCommand message)
    {
        var origin = context.ExtractOrigin();
        _logger.LogDebug(
            "Received VoidPaymentCommand from origin: {Origin}, CorrelationId: {CorrelationId}",
            origin ?? "unknown", message.CorrelationId);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            _logger.LogInformation(
                "Payment Service: Fake Voiding payment for AuthorizationId: {AuthorizationId}, Reason: {Reason}",
                message.AuthorizationId, message.Reason);
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            _logger.LogInformation("Payment Service: Fake Voided payment");

            _transactionalOutbox.AddOutboxMessage(_topicsOptions.Payments, message.CorrelationId.ToString(),
                new PaymentVoidedEvent
                {
                    CorrelationId = message.CorrelationId,
                    UserId = message.UserId,
                    AuthorizationId = message.AuthorizationId,
                    VoidedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                });
            await _transactionalOutbox.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payment Service: Fake PaymentVoidedEvent published");
        }, cancellationToken);
    }
}
