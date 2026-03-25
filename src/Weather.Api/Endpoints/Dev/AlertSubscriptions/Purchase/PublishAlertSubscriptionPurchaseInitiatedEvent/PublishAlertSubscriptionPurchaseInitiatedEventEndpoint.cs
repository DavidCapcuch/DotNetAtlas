using FastEndpoints;
using Microsoft.Extensions.Options;
using Order.AlertSubscriptions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Purchase.PublishAlertSubscriptionPurchaseInitiatedEvent;

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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PublishAlertSubscriptionPurchaseInitiatedEventEndpoint> _logger;

    public PublishAlertSubscriptionPurchaseInitiatedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        TimeProvider timeProvider,
        ILogger<PublishAlertSubscriptionPurchaseInitiatedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _timeProvider = timeProvider;
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
                AlertSubscriptionOrderId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentMethodId = Guid.CreateVersion7(),
                Tier = SubscriptionTier.Pro,
                DurationDays = 30,
                Amount = 9.99m,
                Currency = "USD"
            };
        });
    }

    public override async Task HandleAsync(PublishAlertSubscriptionPurchaseInitiatedEventCommand req,
        CancellationToken ct)
    {
        var alertSubscriptionPurchaseInitiatedEvent = new AlertSubscriptionPurchaseInitiatedEvent
        {
            AlertSubscriptionOrderId = req.AlertSubscriptionOrderId,
            UserId = req.UserId,
            PaymentMethodId = req.PaymentMethodId,
            Tier = req.Tier,
            DurationDays = req.DurationDays,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = req.Currency,
            InitiatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        await _devEventsProducer.PublishAlertSubscriptionPurchaseInitiatedEventAsync(
            alertSubscriptionPurchaseInitiatedEvent);

        _logger.LogInformation(
            "Published AlertSubscriptionPurchaseInitiatedEvent - AlertSubscriptionOrderId: {AlertSubscriptionOrderId}, " +
            "UserId: {UserId}, Tier: {Tier}, Amount: {Amount} {Currency}",
            req.AlertSubscriptionOrderId, req.UserId, req.Tier, req.Amount, req.Currency);

        await Send.OkAsync(new
        {
            AlertSubscriptionOrderId = req.AlertSubscriptionOrderId,
            UserId = req.UserId,
            PaymentMethodId = req.PaymentMethodId,
            Topic = _topicsOptions.OrderAlertSubscriptions,
            Message = "Event published successfully"
        }, ct);
    }
}
