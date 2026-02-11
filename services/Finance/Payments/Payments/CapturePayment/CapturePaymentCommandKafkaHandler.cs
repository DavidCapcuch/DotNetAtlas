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

namespace Payments.Payments.CapturePayment;

/// <summary>
/// Handles CapturePaymentCommand from the Payment Processing Saga.
/// Captures an authorized payment and emits:
/// - PaymentCapturedEvent on success (via outbox).
/// </summary>
/// <remarks>
/// Idempotent processing is handled by InboxMiddleware in the KafkaFlow pipeline.
/// This is a fake/stub handler for development and testing purposes.
/// </remarks>
public class CapturePaymentCommandKafkaHandler : IMessageHandler<CapturePaymentCommand>
{
    private readonly ITransactionalOutbox<IPaymentDbContext> _transactionalOutbox;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CapturePaymentCommandKafkaHandler> _logger;
    private readonly TopicsOptions _topicsOptions;

    public CapturePaymentCommandKafkaHandler(
        TimeProvider timeProvider,
        ILogger<CapturePaymentCommandKafkaHandler> logger,
        ITransactionalOutbox<IPaymentDbContext> transactionalOutboxWriter,
        IOptions<TopicsOptions> topicsOptions)
    {
        _timeProvider = timeProvider;
        _logger = logger;
        _transactionalOutbox = transactionalOutboxWriter;
        _topicsOptions = topicsOptions.Value;
    }

    public async Task Handle(IMessageContext context, CapturePaymentCommand message)
    {
        var origin = context.ExtractOrigin();
        _logger.LogDebug(
            "Received CapturePaymentCommand from origin: {Origin}, CorrelationId: {CorrelationId}",
            origin ?? "unknown", message.CorrelationId);

        var cancellationToken = context.ConsumerContext.WorkerStopped;

        await _transactionalOutbox.Database.EnsureTransactionAsync(async () =>
        {
            _logger.LogInformation("Payment Service: Fake Capturing payment for AuthorizationId: {AuthorizationId}",
                message.AuthorizationId);
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            _logger.LogInformation("Payment Service: Fake Captured payment");

            _transactionalOutbox.AddOutboxMessage(_topicsOptions.Payments, message.CorrelationId.ToString(),
                new PaymentCapturedEvent
                {
                    CorrelationId = message.CorrelationId,
                    UserId = message.UserId,
                    PaymentTransactionId = Guid.CreateVersion7(),
                    AuthorizationId = message.AuthorizationId,
                    Amount = message.Amount,
                    Currency = "USD",
                    CapturedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
                });
            await _transactionalOutbox.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Payment Service: Fake PaymentCapturedEvent published");
        }, cancellationToken);
    }
}
