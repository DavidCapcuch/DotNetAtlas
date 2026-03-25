using FastEndpoints;
using Microsoft.Extensions.Options;
using Order.AlertSubscriptions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Extension.PublishAlertSubscriptionExtensionInitiatedEvent;

/// <summary>
/// Dev endpoint to publish an AlertSubscriptionExtensionInitiatedEvent for testing.
/// Simulates what the Order service would emit when a user initiates an alert subscription extension.
/// </summary>
internal class PublishAlertSubscriptionExtensionInitiatedEventEndpoint
    : Endpoint<PublishAlertSubscriptionExtensionInitiatedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PublishAlertSubscriptionExtensionInitiatedEventEndpoint> _logger;

    public PublishAlertSubscriptionExtensionInitiatedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        TimeProvider timeProvider,
        ILogger<PublishAlertSubscriptionExtensionInitiatedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-alert-subscription-extension-initiated-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes an AlertSubscriptionExtensionInitiatedEvent to Kafka for dev testing. " +
                "Simulates what the Order service would emit when a user initiates an alert subscription extension.";
            s.ExampleRequest = new PublishAlertSubscriptionExtensionInitiatedEventCommand
            {
                AlertSubscriptionOrderId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentMethodId = Guid.CreateVersion7(),
                DurationDays = 30,
                Amount = 9.99m,
                Currency = "USD"
            };
        });
    }

    public override async Task HandleAsync(
        PublishAlertSubscriptionExtensionInitiatedEventCommand req,
        CancellationToken ct)
    {
        var alertSubscriptionExtensionInitiatedEvent = new AlertSubscriptionExtensionInitiatedEvent
        {
            AlertSubscriptionOrderId = req.AlertSubscriptionOrderId,
            UserId = req.UserId,
            PaymentMethodId = req.PaymentMethodId,
            DurationDays = req.DurationDays,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = req.Currency,
            InitiatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        await _devEventsProducer.PublishAlertSubscriptionExtensionInitiatedEventAsync(
            alertSubscriptionExtensionInitiatedEvent);

        _logger.LogInformation(
            "Published AlertSubscriptionExtensionInitiatedEvent - AlertSubscriptionOrderId: {AlertSubscriptionOrderId}, " +
            "UserId: {UserId}, DurationDays: {DurationDays}, Amount: {Amount} {Currency}",
            req.AlertSubscriptionOrderId, req.UserId, req.DurationDays, req.Amount, req.Currency);

        await Send.OkAsync(
            new
            {
                AlertSubscriptionOrderId = req.AlertSubscriptionOrderId,
                UserId = req.UserId,
                PaymentMethodId = req.PaymentMethodId,
                Topic = _topicsOptions.OrderAlertSubscriptions,
                Message = "Event published successfully"
            },
            ct);
    }
}
