using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Weather.Alerts;

namespace DotNetAtlas.Api.Endpoints.Dev.AlertSubscriptions.Extension.PublishSubscriptionExtendedEvent;

/// <summary>
/// Dev endpoint to publish a SubscriptionExtendedEvent for testing.
/// Simulates what the Weather Alerts service would emit when subscription extension succeeds.
/// </summary>
internal class PublishSubscriptionExtendedEventEndpoint : Endpoint<PublishSubscriptionExtendedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishSubscriptionExtendedEventEndpoint> _logger;

    public PublishSubscriptionExtendedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishSubscriptionExtendedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-subscription-extended-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a SubscriptionExtendedEvent to Kafka for dev testing. " +
                "Simulates what the Weather Alerts service would emit when subscription extension succeeds.";
            s.ExampleRequest = new PublishSubscriptionExtendedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                DurationExtendedDays = 30,
                NewExpiresAtUtc = DateTime.UtcNow.AddDays(30)
            };
        });
    }

    public override async Task HandleAsync(PublishSubscriptionExtendedEventCommand req, CancellationToken ct)
    {
        var subscriptionExtendedEvent = new AlertSubscriptionExtendedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            DurationExtendedDays = req.DurationExtendedDays,
            NewExpiresAtUtc = req.NewExpiresAtUtc,
            ExtendedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishSubscriptionExtendedEventAsync(subscriptionExtendedEvent);

        _logger.LogInformation(
            "Published SubscriptionExtendedEvent - CorrelationId: {CorrelationId}, " +
            "UserId: {UserId}, PaymentTransactionId: {PaymentTransactionId}, DurationExtendedDays: {DurationExtendedDays}",
            req.CorrelationId, req.UserId, req.PaymentTransactionId, req.DurationExtendedDays);

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
