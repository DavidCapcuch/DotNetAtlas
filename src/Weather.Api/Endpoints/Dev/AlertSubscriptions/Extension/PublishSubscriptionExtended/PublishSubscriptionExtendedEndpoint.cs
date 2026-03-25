using FastEndpoints;
using Microsoft.Extensions.Options;
using Weather.Alerts;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.AlertSubscriptions.Extension.PublishSubscriptionExtended;

/// <summary>
/// Dev endpoint to publish an ExtendSubscriptionCommand for testing.
/// Simulates what the Extension Saga would emit when requesting subscription extension.
/// </summary>
internal class PublishSubscriptionExtendedEndpoint : Endpoint<PublishSubscriptionExtendedCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishSubscriptionExtendedEndpoint> _logger;

    public PublishSubscriptionExtendedEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishSubscriptionExtendedEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-subscription-extended");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes an ExtendSubscriptionCommand to Kafka for dev testing. " +
                "Simulates what the Extension Saga would emit.";
            s.ExampleRequest = new PublishSubscriptionExtendedCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                DurationExtendedDays = 30
            };
        });
    }

    public override async Task HandleAsync(PublishSubscriptionExtendedCommand req, CancellationToken ct)
    {
        var extendSubscriptionCommand = new ExtendAlertSubscriptionCommand
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            DurationDays = req.DurationExtendedDays,
            RequestedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishExtendSubscriptionCommandAsync(extendSubscriptionCommand);

        _logger.LogInformation(
            "Published ExtendSubscriptionCommand - CorrelationId: {CorrelationId}, " +
            "UserId: {UserId}, PaymentTransactionId: {PaymentTransactionId}, DurationDays: {DurationDays}",
            req.CorrelationId, req.UserId, req.PaymentTransactionId, req.DurationExtendedDays);

        await Send.OkAsync(new
        {
            CorrelationId = req.CorrelationId,
            PaymentTransactionId = req.PaymentTransactionId,
            Topic = _topicsOptions.WeatherAlertSubscriptionsCommands,
            Message = "Command published successfully"
        }, ct);
    }
}
