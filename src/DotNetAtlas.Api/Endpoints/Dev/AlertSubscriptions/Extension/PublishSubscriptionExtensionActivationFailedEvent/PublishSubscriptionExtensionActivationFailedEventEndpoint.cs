using DotNetAtlas.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishSubscriptionActivationFailedEvent;
using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Weather.Alerts;

namespace DotNetAtlas.Api.Endpoints.Dev.AlertSubscriptions.Extension.PublishSubscriptionExtensionActivationFailedEvent;

/// <summary>
/// Dev endpoint to publish a SubscriptionExtensionActivationFailedEvent for testing.
/// Simulates what the Weather Alerts service would emit when subscription extension activation fails.
/// </summary>
internal class
    PublishSubscriptionExtensionActivationFailedEventEndpoint : Endpoint<
    PublishSubscriptionExtensionActivationFailedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishSubscriptionExtensionActivationFailedEventEndpoint> _logger;

    public PublishSubscriptionExtensionActivationFailedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishSubscriptionExtensionActivationFailedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-subscription-extension-activation-failed-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a SubscriptionExtensionActivationFailedEvent to Kafka for dev testing. " +
                "Simulates what the Weather Alerts service would emit when subscription extension activation fails.";
            s.ExampleRequest = new PublishSubscriptionExtensionActivationFailedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                RequestedDurationExtendedDays = 30,
                Errors =
                [
                    new ErrorDetailsDto
                    {
                        ErrorCode = "EXTENSION_FAILED",
                        ErrorMessage = "Subscription extension could not be processed"
                    }
                ]
            };
        });
    }

    public override async Task HandleAsync(PublishSubscriptionExtensionActivationFailedEventCommand req,
        CancellationToken ct)
    {
        var subscriptionExtensionActivationFailedEvent = new AlertSubscriptionExtensionActivationFailedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            RequestedDurationExtendedDays = req.RequestedDurationExtendedDays,
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

        await _devEventsProducer.PublishSubscriptionExtensionActivationFailedEventAsync(
            subscriptionExtensionActivationFailedEvent);

        _logger.LogInformation(
            "Published SubscriptionExtensionActivationFailedEvent - CorrelationId: {CorrelationId}, " +
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
