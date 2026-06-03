using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentAuthorizedEvent"/> from <c>payments.transactions</c>
/// (shared topic; <c>saga-checkout</c> consumer group is offset-isolated from
/// <c>saga-payment-processing</c> per ADR-0001) and forwards it to the
/// <see cref="CheckoutSagaOrchestrator"/> as <see cref="PaymentAuthorizedCheckoutSagaEvent"/>
/// (ADR-0026 capture pivot). Both the sub-saga and the Checkout saga subscribe to Payments'
/// authorization event independently; the Checkout saga reacts by confirming stock + order before
/// approving capture. The <c>Checkout</c> suffix disambiguates from PaymentProcessingSaga's own
/// consumer of the same Avro event.
/// </summary>
public sealed class PaymentAuthorizedCheckoutConsumer : IConsumer<PaymentAuthorizedEvent>
{
    private readonly ILogger<PaymentAuthorizedCheckoutConsumer> _logger;

    public PaymentAuthorizedCheckoutConsumer(ILogger<PaymentAuthorizedCheckoutConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentAuthorizedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, authorization {AuthorizationId}",
            nameof(PaymentAuthorizedCheckoutConsumer), nameof(PaymentAuthorizedEvent),
            message.OrderId, message.AuthorizationId);

        await context.Publish(new PaymentAuthorizedCheckoutSagaEvent
        {
            OrderId = message.OrderId,
            AuthorizationId = message.AuthorizationId,
            AuthorizedAtUtc = message.AuthorizedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
