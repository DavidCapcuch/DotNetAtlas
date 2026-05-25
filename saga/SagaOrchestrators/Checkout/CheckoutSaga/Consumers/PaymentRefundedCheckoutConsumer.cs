using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentRefundedEvent"/> from <c>payments.transactions</c> and
/// forwards it to the <see cref="CheckoutSagaOrchestrator"/> as
/// <see cref="PaymentRefundedSagaEvent"/> per docs/bc-design/checkout-saga.md § 8 row 12. The
/// <c>Checkout</c> suffix disambiguates from any future PaymentProcessingSaga consumer of the
/// same Avro event (§ 8 line 361). Drives transition <c>CompensatingPayment</c> -&gt;
/// <c>CompensatingStockReservations</c>. The internal saga event's <c>Amount</c> is sourced
/// from the Avro <c>RefundedAmount</c> field.
/// </summary>
public sealed class PaymentRefundedCheckoutConsumer : IConsumer<PaymentRefundedEvent>
{
    private readonly ILogger<PaymentRefundedCheckoutConsumer> _logger;

    public PaymentRefundedCheckoutConsumer(ILogger<PaymentRefundedCheckoutConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentRefundedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, transaction {PaymentTransactionId}, refund amount {Amount} {Currency}",
            nameof(PaymentRefundedCheckoutConsumer), nameof(PaymentRefundedEvent),
            message.CorrelationId, message.PaymentTransactionId, (decimal)message.RefundedAmount, message.Currency);

        await context.Publish(new PaymentRefundedSagaEvent
        {
            CorrelationId = message.CorrelationId,
            PaymentTransactionId = message.PaymentTransactionId,
            Amount = (decimal)message.RefundedAmount,
            Currency = message.Currency,
            RefundedAtUtc = message.RefundedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
