using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentCompletedEvent;

/// <summary>
/// Dev endpoint to publish PaymentCompletedEvent for testing.
/// </summary>
internal class PublishPaymentCompletedEventEndpoint : Endpoint<PublishPaymentCompletedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentCompletedEventEndpoint> _logger;

    public PublishPaymentCompletedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentCompletedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-completed-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentCompletedEvent to Kafka for dev testing. " +
                "Simulates what the Payment Saga would emit when payment processing completes successfully.";
            s.ExampleRequest = new PublishPaymentCompletedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                Amount = 1000,
                Currency = "CZK"
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentCompletedEventCommand req, CancellationToken ct)
    {
        var paymentCompletedEvent = new PaymentCompletedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = "CZK",
            CompletedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishPaymentCompletedEventAsync(paymentCompletedEvent);

        _logger.LogInformation(
            "Published PaymentCompletedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
