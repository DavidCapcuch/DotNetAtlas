using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentFailedEvent;

/// <summary>
/// Dev endpoint to publish PaymentFailedEvent for testing.
/// </summary>
internal class PublishPaymentFailedEventEndpoint : Endpoint<PublishPaymentFailedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentFailedEventEndpoint> _logger;

    public PublishPaymentFailedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentFailedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-failed-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentFailedEvent to Kafka for dev testing. " +
                "Simulates what the Payment Saga would emit when payment processing fails terminally.";
            s.ExampleRequest = new PublishPaymentFailedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                ErrorCode = "PAYMENT_EXPIRED",
                ErrorMessage = "Payment authorization has expired."
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentFailedEventCommand req, CancellationToken ct)
    {
        var paymentFailedEvent = new PaymentFailedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            ErrorCode = req.ErrorCode,
            ErrorMessage = req.ErrorMessage,
            FailedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishPaymentFailedEventAsync(paymentFailedEvent);

        _logger.LogInformation(
            "Published PaymentFailedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
