using KafkaFlow;
using Microsoft.Extensions.Options;
using Payments.Common.Config.Kafka;
using Payments.Common.Persistence.Database;
using Payments.Transactions;
using Platform.KafkaFlow.Inbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

namespace Payments.Transactions.RequestRefund;

/// <summary>
/// Handles RequestRefundCommand from the Payment Processing Saga.
/// Refunds a captured payment and emits:
/// - PaymentRefundedEvent on success (via outbox).
/// </summary>
/// <remarks>
/// Idempotent processing is handled by InboxMiddleware in the KafkaFlow pipeline.
/// This is a fake/stub handler for development and testing purposes.
/// </remarks>
public class RequestRefundCommandKafkaHandler : IMessageHandler<RequestRefundCommand>
{
    private readonly ITransactionalOutbox<IPaymentDbContext> _transactionalOutbox;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestRefundCommandKafkaHandler> _logger;
    private readonly TopicsOptions _topicsOptions;

    public RequestRefundCommandKafkaHandler(
        TimeProvider timeProvider,
        ILogger<RequestRefundCommandKafkaHandler> logger,
        ITransactionalOutbox<IPaymentDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(IMessageContext context, RequestRefundCommand message)
    {
        var origin = context.ExtractOrigin();
        _logger.LogDebug(
            "Received RequestRefundCommand from origin: {Origin}, CorrelationId: {CorrelationId}",
            origin ?? "unknown", message.CorrelationId);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            _logger.LogInformation(
                "Payment Service: Fake Refunding payment for TransactionId: {PaymentTransactionId}, Reason: {Reason}",
                message.PaymentTransactionId, message.Reason);
            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            _logger.LogInformation("Payment Service: Fake Refunded payment");

            _transactionalOutbox.AddOutboxMessage(_topicsOptions.Payments, message.CorrelationId.ToString(),
                new PaymentRefundedEvent
                {
                    CorrelationId = message.CorrelationId,
                    UserId = message.UserId,
                    PaymentTransactionId = message.PaymentTransactionId,
                    RefundTransactionId = Guid.CreateVersion7(),
                    RefundedAmount = new Avro.AvroDecimal(0, 4),
                    Currency = "USD",
                    RefundedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                });
            await _transactionalOutbox.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payment Service: Fake PaymentRefundedEvent published");
        }, cancellationToken);
    }
}
