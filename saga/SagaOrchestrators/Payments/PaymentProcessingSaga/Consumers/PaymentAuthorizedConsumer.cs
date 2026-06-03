using MassTransit;
using Payments.Transactions;
using SagaOrchestrators.Payments.PaymentProcessingSaga.InternalSagaEvents;

namespace SagaOrchestrators.Payments.PaymentProcessingSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="PaymentAuthorizedEvent"/> from the payment provider
/// and forwards it to the <see cref="PaymentProcessingSagaOrchestrator"/> as an internal
/// <see cref="PaymentAuthorizedSagaEvent"/>.
/// </summary>
public sealed class PaymentAuthorizedConsumer : IConsumer<PaymentAuthorizedEvent>
{
    private readonly ILogger<PaymentAuthorizedConsumer> _logger;

    public PaymentAuthorizedConsumer(ILogger<PaymentAuthorizedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentAuthorizedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, authorization {AuthorizationId}",
            nameof(PaymentAuthorizedConsumer), nameof(PaymentAuthorizedEvent),
            message.OrderId, message.AuthorizationId);

        var paymentAuthorizedSagaEvent = new PaymentAuthorizedSagaEvent
        {
            OrderId = message.OrderId,
            UserId = message.UserId,
            AuthorizationId = message.AuthorizationId,
            Amount = (decimal)message.Amount,
            Currency = message.Currency,
            AuthorizedAtUtc = message.AuthorizedAtUtc,
            ExpiresAtUtc = message.ExpiresAtUtc
        };

        await context.Publish(paymentAuthorizedSagaEvent);
    }
}
