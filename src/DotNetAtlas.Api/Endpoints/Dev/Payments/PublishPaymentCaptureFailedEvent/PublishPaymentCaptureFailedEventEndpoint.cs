using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentCaptureFailedEvent;

/// <summary>
/// Dev endpoint to publish PaymentCaptureFailedEvent for testing.
/// </summary>
internal class PublishPaymentCaptureFailedEventEndpoint : Endpoint<PublishPaymentCaptureFailedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentCaptureFailedEventEndpoint> _logger;

    public PublishPaymentCaptureFailedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentCaptureFailedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-capture-failed-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentCaptureFailedEvent to Kafka for dev testing. " +
                "Simulates what the Payment service would emit when payment capture fails.";
            s.ExampleRequest = new PublishPaymentCaptureFailedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                AuthorizationId = "auth_12345",
                ErrorCode = "INSUFFICIENT_FUNDS",
                ErrorMessage = "Insufficient funds for capture.",
                IsRetryable = false
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentCaptureFailedEventCommand req, CancellationToken ct)
    {
        var paymentCaptureFailedEvent = new PaymentCaptureFailedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            AuthorizationId = req.AuthorizationId,
            ErrorCode = req.ErrorCode,
            ErrorMessage = req.ErrorMessage,
            IsRetryable = req.IsRetryable,
            FailedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishPaymentCaptureFailedEventAsync(paymentCaptureFailedEvent);

        _logger.LogInformation(
            "Published PaymentCaptureFailedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
