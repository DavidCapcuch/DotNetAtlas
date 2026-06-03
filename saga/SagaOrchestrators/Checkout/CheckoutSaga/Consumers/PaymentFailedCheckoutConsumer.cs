using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentFailedEvent"/> from <c>payments.transactions</c> and
/// forwards it to the <see cref="CheckoutSagaOrchestrator"/> as
/// <see cref="PaymentFailedSagaEvent"/> per docs/bc-design/checkout-saga.md § 8 row 11. The
/// <c>Checkout</c> suffix disambiguates from any future PaymentProcessingSaga consumer of the
/// same Avro event (§ 8 line 361). Drives transition <c>AwaitingPayment</c> -&gt;
/// <c>CompensatingStockReservations</c>; no refund is needed because payment never captured.
/// </summary>
public sealed class PaymentFailedCheckoutConsumer : IConsumer<PaymentFailedEvent>
{
    private readonly ILogger<PaymentFailedCheckoutConsumer> _logger;

    public PaymentFailedCheckoutConsumer(ILogger<PaymentFailedCheckoutConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, error {ErrorCode}",
            nameof(PaymentFailedCheckoutConsumer), nameof(PaymentFailedEvent),
            message.OrderId, message.ErrorCode);

        await context.Publish(new PaymentFailedSagaEvent
        {
            OrderId = message.OrderId,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            FailedAtUtc = message.FailedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
