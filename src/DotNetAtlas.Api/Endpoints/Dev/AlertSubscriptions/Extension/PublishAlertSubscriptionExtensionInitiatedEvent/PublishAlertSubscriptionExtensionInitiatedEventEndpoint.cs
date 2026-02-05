using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.Extensions;
using FastEndpoints;
using Microsoft.Extensions.Options;
using Order.AlertSubscriptions;

namespace DotNetAtlas.Api.Endpoints.Dev.AlertSubscriptions.Extension.PublishAlertSubscriptionExtensionInitiatedEvent;

/// <summary>
/// Dev endpoint to publish an AlertSubscriptionExtensionInitiatedEvent for testing.
/// Simulates what the Order service would emit when a user initiates an alert subscription extension.
/// </summary>
internal class PublishAlertSubscriptionExtensionInitiatedEventEndpoint
    : Endpoint<PublishAlertSubscriptionExtensionInitiatedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishAlertSubscriptionExtensionInitiatedEventEndpoint> _logger;

    public PublishAlertSubscriptionExtensionInitiatedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishAlertSubscriptionExtensionInitiatedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
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
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentMethodId = Guid.CreateVersion7(),
                DurationDays = 30,
                Amount = 9.99m,
                Currency = "USD",
                IdempotencyKey = Guid.CreateVersion7().ToString()
            };
        });
    }

    public override async Task HandleAsync(
        PublishAlertSubscriptionExtensionInitiatedEventCommand req,
        CancellationToken ct)
    {
        var alertSubscriptionExtensionInitiatedEvent = new AlertSubscriptionExtensionInitiatedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentMethodId = req.PaymentMethodId,
            DurationDays = req.DurationDays,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = req.Currency,
            IdempotencyKey = req.IdempotencyKey,
            InitiatedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishAlertSubscriptionExtensionInitiatedEventAsync(
            alertSubscriptionExtensionInitiatedEvent);

        _logger.LogInformation(
            "Published AlertSubscriptionExtensionInitiatedEvent - CorrelationId: {CorrelationId}, " +
            "UserId: {UserId}, DurationDays: {DurationDays}, Amount: {Amount} {Currency}",
            req.CorrelationId, req.UserId, req.DurationDays, req.Amount, req.Currency);

        await Send.OkAsync(
            new
            {
                CorrelationId = req.CorrelationId,
                UserId = req.UserId,
                PaymentMethodId = req.PaymentMethodId,
                Topic = _topicsOptions.OrderAlertSubscriptions,
                Message = "Event published successfully"
            },
            ct);
    }
}
