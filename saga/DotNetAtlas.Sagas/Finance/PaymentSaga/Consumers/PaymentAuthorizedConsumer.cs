using DotNetAtlas.Sagas.Finance.PaymentSaga.InternalSagaEvents;
using Finance.Payments;
using MassTransit;

namespace DotNetAtlas.Sagas.Finance.PaymentSaga.Consumers;

/// <summary>
/// Consumer that receives PaymentAuthorizedEvent
/// and forwards it to the PaymentSaga as internal PaymentAuthorizedSagaEvent.
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
            "Received PaymentAuthorizedEvent for correlation {CorrelationId}, authorization {AuthorizationId}",
            message.CorrelationId, message.AuthorizationId);

        var paymentAuthorizedSagaEvent = new PaymentAuthorizedSagaEvent
        {
            CorrelationId = message.CorrelationId,
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
