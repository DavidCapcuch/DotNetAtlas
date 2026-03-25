using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentVoidedEvent;

/// <summary>
/// Dev endpoint to publish PaymentVoidedEvent for testing.
/// </summary>
internal class PublishPaymentVoidedEventEndpoint : Endpoint<PublishPaymentVoidedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentVoidedEventEndpoint> _logger;

    public PublishPaymentVoidedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentVoidedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-voided-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentVoidedEvent to Kafka for dev testing. " +
                "Simulates what the Payment service would emit when an authorized payment has been voided.";
            s.ExampleRequest = new PublishPaymentVoidedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                AuthorizationId = "auth_12345"
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentVoidedEventCommand req, CancellationToken ct)
    {
        var paymentVoidedEvent = new PaymentVoidedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            AuthorizationId = req.AuthorizationId,
            VoidedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishPaymentVoidedEventAsync(paymentVoidedEvent);

        _logger.LogInformation(
            "Published PaymentVoidedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
