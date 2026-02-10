using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.AvroExtensions;
using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentCapturedEvent;

/// <summary>
/// Dev endpoint to publish PaymentCapturedEvent for testing.
/// </summary>
internal class PublishPaymentCapturedEventEndpoint : Endpoint<PublishPaymentCapturedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentCapturedEventEndpoint> _logger;

    public PublishPaymentCapturedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentCapturedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-captured-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentCapturedEvent to Kafka for dev testing. " +
                "Simulates what the Payment service would emit when payment capture succeeds.";
            s.ExampleRequest = new PublishPaymentCapturedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                CaptureId = "capture_12345",
                AuthorizationId = "auth_12345",
                Amount = 99.99m,
                Currency = "USD"
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentCapturedEventCommand req, CancellationToken ct)
    {
        var paymentCapturedEvent = new PaymentCapturedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = Guid.CreateVersion7(),
            AuthorizationId = req.AuthorizationId,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = req.Currency,
            CapturedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishPaymentCapturedEventAsync(paymentCapturedEvent);

        _logger.LogInformation(
            "Published PaymentCapturedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
