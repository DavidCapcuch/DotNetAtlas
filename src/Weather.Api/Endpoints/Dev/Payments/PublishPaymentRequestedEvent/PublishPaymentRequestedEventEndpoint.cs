using FastEndpoints;
using Microsoft.Extensions.Options;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.Payments.PublishPaymentRequestedEvent;

/// <summary>
/// Dev endpoint to publish PaymentRequestedEvent for testing.
/// </summary>
internal class PublishPaymentRequestedEventEndpoint : Endpoint<PublishPaymentRequestedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentRequestedEventEndpoint> _logger;

    public PublishPaymentRequestedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentRequestedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-requested-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentRequestedEvent to Kafka for dev testing. " +
                "Simulates what the Order service would emit to trigger the Payment Saga.";
            s.ExampleRequest = new PublishPaymentRequestedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                OrderId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentMethodId = "pm_dev_4242",
                Amount = 99.99m,
                Currency = "USD",
                IdempotencyKey = Guid.NewGuid().ToString()
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentRequestedEventCommand req, CancellationToken ct)
    {
        var paymentRequestedEvent = new PaymentRequestedEvent
        {
            CorrelationId = req.CorrelationId,
            OrderId = req.OrderId,
            UserId = req.UserId,
            PaymentMethodId = req.PaymentMethodId,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = req.Currency,
            IdempotencyKey = req.IdempotencyKey,
            RequestedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishPaymentRequestedEventAsync(paymentRequestedEvent);

        _logger.LogInformation(
            "Published PaymentRequestedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
