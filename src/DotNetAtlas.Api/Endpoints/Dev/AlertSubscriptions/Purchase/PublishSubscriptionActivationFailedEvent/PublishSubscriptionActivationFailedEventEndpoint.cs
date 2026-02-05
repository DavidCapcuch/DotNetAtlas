using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Weather.Alerts;

namespace DotNetAtlas.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishSubscriptionActivationFailedEvent;

/// <summary>
/// Dev endpoint to publish a SubscriptionActivationFailedEvent for testing.
/// Simulates what the Weather Alerts service would emit when subscription activation fails.
/// </summary>
internal class
    PublishSubscriptionActivationFailedEventEndpoint : Endpoint<PublishSubscriptionActivationFailedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishSubscriptionActivationFailedEventEndpoint> _logger;

    public PublishSubscriptionActivationFailedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishSubscriptionActivationFailedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-subscription-activation-failed-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a SubscriptionActivationFailedEvent to Kafka for dev testing. " +
                "Simulates what the Weather Alerts service would emit when subscription activation fails.";
            s.ExampleRequest = new PublishSubscriptionActivationFailedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                RequestedTier = SubscriptionTier.Pro,
                RequestedDurationDays = 30,
                Errors =
                [
                    new ErrorDetailsDto
                    {
                        ErrorCode = "PAYMENT_FAILED",
                        ErrorMessage = "Payment could not be processed"
                    }
                ]
            };
        });
    }

    public override async Task HandleAsync(PublishSubscriptionActivationFailedEventCommand req, CancellationToken ct)
    {
        var avroEvent = new AlertSubscriptionActivationFailedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            RequestedTier = req.RequestedTier,
            RequestedDurationDays = req.RequestedDurationDays,
            Errors =
            [
                .. req.Errors.Select(e => new ErrorDetails
                {
                    ErrorCode = e.ErrorCode,
                    ErrorMessage = e.ErrorMessage
                })
            ],
            OccurredOnUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishSubscriptionActivationFailedEventAsync(avroEvent);

        _logger.LogInformation(
            "Published SubscriptionActivationFailedEvent - CorrelationId: {CorrelationId}, " +
            "UserId: {UserId}, PaymentTransactionId: {PaymentTransactionId}, ErrorCount: {ErrorCount}",
            req.CorrelationId, req.UserId, req.PaymentTransactionId, req.Errors.Count);

        await Send.OkAsync(new
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            Topic = _topicsOptions.WeatherAlertSubscriptions,
            Message = "Event published successfully"
        }, ct);
    }
}
