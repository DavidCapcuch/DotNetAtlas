using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentCompletedEvent"/> from <c>payments.payments</c>
/// (shared topic; <c>saga-checkout</c> consumer group is offset-isolated from
/// <c>saga-payment-processing</c> per ADR-0001) and forwards it to the
/// <see cref="CheckoutSagaOrchestrator"/> as <see cref="PaymentCompletedSagaEvent"/> per
/// docs/bc-design/checkout-saga.md § 8 row 10. The <c>Checkout</c> suffix disambiguates from
/// any future PaymentProcessingSaga consumer of the same Avro event (§ 8 line 361).
/// </summary>
public sealed class PaymentCompletedCheckoutConsumer : IConsumer<PaymentCompletedEvent>
{
    private readonly ILogger<PaymentCompletedCheckoutConsumer> _logger;

    public PaymentCompletedCheckoutConsumer(ILogger<PaymentCompletedCheckoutConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentCompletedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, transaction {PaymentTransactionId}, amount {Amount} {Currency}",
            nameof(PaymentCompletedCheckoutConsumer), nameof(PaymentCompletedEvent),
            message.CorrelationId, message.PaymentTransactionId, (decimal)message.Amount, message.Currency);

        await context.Publish(new PaymentCompletedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            PaymentTransactionId = message.PaymentTransactionId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            CompletedAtUtc = message.CompletedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
