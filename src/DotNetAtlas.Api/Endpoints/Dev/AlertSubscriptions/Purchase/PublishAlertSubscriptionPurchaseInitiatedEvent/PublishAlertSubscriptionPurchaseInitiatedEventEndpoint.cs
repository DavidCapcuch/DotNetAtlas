using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.AvroExtensions;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Order.AlertSubscriptions;

namespace DotNetAtlas.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishAlertSubscriptionPurchaseInitiatedEvent;

/// <summary>
/// Dev endpoint to publish an AlertSubscriptionPurchaseInitiatedEvent for testing.
/// Simulates what the Order service would emit when a user initiates a new alert subscription purchase.
/// </summary>
internal class
    PublishAlertSubscriptionPurchaseInitiatedEventEndpoint : Endpoint<
    PublishAlertSubscriptionPurchaseInitiatedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishAlertSubscriptionPurchaseInitiatedEventEndpoint> _logger;

    public PublishAlertSubscriptionPurchaseInitiatedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishAlertSubscriptionPurchaseInitiatedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-alert-subscription-purchase-initiated-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes an AlertSubscriptionPurchaseInitiatedEvent to Kafka for dev testing. " +
                "Simulates what the Order service would emit when a user initiates a new alert subscription purchase.";
            s.ExampleRequest = new PublishAlertSubscriptionPurchaseInitiatedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentMethodId = Guid.CreateVersion7(),
                Tier = SubscriptionTier.Pro,
                DurationDays = 30,
                Amount = 9.99m,
                Currency = "USD",
                IdempotencyKey = Guid.CreateVersion7().ToString()
            };
        });
    }

    public override async Task HandleAsync(PublishAlertSubscriptionPurchaseInitiatedEventCommand req,
        CancellationToken ct)
    {
        var alertSubscriptionPurchaseInitiatedEvent = new AlertSubscriptionPurchaseInitiatedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentMethodId = req.PaymentMethodId,
            Tier = req.Tier,
            DurationDays = req.DurationDays,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = req.Currency,
            IdempotencyKey = req.IdempotencyKey,
            InitiatedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishAlertSubscriptionPurchaseInitiatedEventAsync(
            alertSubscriptionPurchaseInitiatedEvent);

        _logger.LogInformation(
            "Published AlertSubscriptionPurchaseInitiatedEvent - CorrelationId: {CorrelationId}, " +
            "UserId: {UserId}, Tier: {Tier}, Amount: {Amount} {Currency}",
            req.CorrelationId, req.UserId, req.Tier, req.Amount, req.Currency);

        await Send.OkAsync(new
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentMethodId = req.PaymentMethodId,
            Topic = _topicsOptions.OrderAlertSubscriptions,
            Message = "Event published successfully"
        }, ct);
    }
}
