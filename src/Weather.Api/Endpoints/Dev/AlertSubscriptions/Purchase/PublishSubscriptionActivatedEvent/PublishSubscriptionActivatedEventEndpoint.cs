using FastEndpoints;
using Microsoft.Extensions.Options;
using Weather.Alerts;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishSubscriptionActivatedEvent;

/// <summary>
/// Dev endpoint to publish a SubscriptionActivatedEvent for testing.
/// Simulates what the Weather Alerts service would emit when subscription activation succeeds.
/// </summary>
internal class PublishSubscriptionActivatedEventEndpoint : Endpoint<PublishSubscriptionActivatedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishSubscriptionActivatedEventEndpoint> _logger;

    public PublishSubscriptionActivatedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishSubscriptionActivatedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-subscription-activated-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a SubscriptionActivatedEvent to Kafka for dev testing. " +
                "Simulates what the Weather Alerts service would emit when subscription activation succeeds.";
            s.ExampleRequest = new PublishSubscriptionActivatedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                Tier = SubscriptionTier.Pro,
                DurationDays = 30,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(30)
            };
        });
    }

    public override async Task HandleAsync(PublishSubscriptionActivatedEventCommand req, CancellationToken ct)
    {
        var subscriptionActivatedEvent = new AlertSubscriptionActivatedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            Tier = req.Tier,
            DurationDays = req.DurationDays,
            ExpiresAtUtc = req.ExpiresAtUtc,
            ActivatedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishSubscriptionActivatedEventAsync(subscriptionActivatedEvent);

        _logger.LogInformation(
            "Published SubscriptionActivatedEvent - CorrelationId: {CorrelationId}, " +
            "UserId: {UserId}, PaymentTransactionId: {PaymentTransactionId}, Tier: {Tier}, DurationDays: {DurationDays}",
            req.CorrelationId, req.UserId, req.PaymentTransactionId, req.Tier, req.DurationDays);

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
