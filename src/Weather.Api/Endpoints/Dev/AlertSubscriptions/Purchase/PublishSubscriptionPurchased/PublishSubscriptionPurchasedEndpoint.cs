using FastEndpoints;
using Microsoft.Extensions.Options;
using Weather.Alerts;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;
using WeatherSubscriptionTier = Weather.Alerts.SubscriptionTier;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishSubscriptionPurchased;

/// <summary>
/// Dev endpoint to publish an ActivateSubscriptionCommand for testing.
/// Simulates what the Purchase Saga would emit when requesting subscription activation.
/// </summary>
internal class PublishSubscriptionPurchasedEndpoint : Endpoint<PublishSubscriptionPurchasedCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishSubscriptionPurchasedEndpoint> _logger;

    public PublishSubscriptionPurchasedEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishSubscriptionPurchasedEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-subscription-purchased");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes an ActivateSubscriptionCommand to Kafka for dev testing. " +
                "Simulates what the Purchase Saga would emit.";
            s.ExampleRequest = new PublishSubscriptionPurchasedCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                SubscriptionTier = WeatherSubscriptionTier.Pro,
                DurationDays = 30
            };
        });
    }

    public override async Task HandleAsync(PublishSubscriptionPurchasedCommand req, CancellationToken ct)
    {
        var activateSubscriptionCommand = new ActivateAlertSubscriptionCommand
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            Tier = req.SubscriptionTier,
            DurationDays = req.DurationDays,
            RequestedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishActivateSubscriptionCommandAsync(activateSubscriptionCommand);

        _logger.LogInformation(
            "Published ActivateSubscriptionCommand - CorrelationId: {CorrelationId}, " +
            "UserId: {UserId}, PaymentTransactionId: {PaymentTransactionId}, Tier: {Tier}, DurationDays: {DurationDays}",
            req.CorrelationId, req.UserId, req.PaymentTransactionId, req.SubscriptionTier, req.DurationDays);

        await Send.OkAsync(new
        {
            CorrelationId = req.CorrelationId,
            PaymentTransactionId = req.PaymentTransactionId,
            Topic = _topicsOptions.WeatherAlertSubscriptionsCommands,
            Message = "Command published successfully"
        }, ct);
    }
}
