using FastEndpoints;
using Microsoft.Extensions.Options;
using Payments.Transactions;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentAuthorizationFailedEvent;

/// <summary>
/// Dev endpoint to publish PaymentAuthorizationFailedEvent for testing.
/// </summary>
internal class PublishPaymentAuthorizationFailedEventEndpoint : Endpoint<PublishPaymentAuthorizationFailedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentAuthorizationFailedEventEndpoint> _logger;

    public PublishPaymentAuthorizationFailedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentAuthorizationFailedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-authorization-failed-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentAuthorizationFailedEvent to Kafka for dev testing. " +
                "Simulates what the Payment service would emit when payment authorization fails.";
            s.ExampleRequest = new PublishPaymentAuthorizationFailedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                ErrorCode = "CARD_DECLINED",
                ErrorMessage = "The card was declined.",
                IsRetryable = false
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentAuthorizationFailedEventCommand req, CancellationToken ct)
    {
        var paymentAuthorizationFailedEvent = new PaymentAuthorizationFailedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            ErrorCode = req.ErrorCode,
            ErrorMessage = req.ErrorMessage,
            IsRetryable = req.IsRetryable,
            FailedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishPaymentAuthorizationFailedEventAsync(paymentAuthorizationFailedEvent);

        _logger.LogInformation(
            "Published PaymentAuthorizationFailedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
