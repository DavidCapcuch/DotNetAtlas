using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.Extensions;
using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentRefundedEvent;

/// <summary>
/// Dev endpoint to publish PaymentRefundedEvent for testing.
/// </summary>
internal class PublishPaymentRefundedEventEndpoint : Endpoint<PublishPaymentRefundedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentRefundedEventEndpoint> _logger;

    public PublishPaymentRefundedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentRefundedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-refunded-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentRefundedEvent to Kafka for dev testing. " +
                "Simulates what the Payment service would emit when a captured payment has been refunded.";
            s.ExampleRequest = new PublishPaymentRefundedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                RefundId = "refund_12345",
                RefundTransactionId = Guid.CreateVersion7(),
                RefundedAmount = 99.99m,
                Currency = "USD"
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentRefundedEventCommand req, CancellationToken ct)
    {
        var paymentRefundedEvent = new PaymentRefundedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            RefundTransactionId = req.RefundTransactionId,
            RefundedAmount = req.RefundedAmount.ToAvroDecimal(4),
            Currency = req.Currency,
            RefundedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishPaymentRefundedEventAsync(paymentRefundedEvent);

        _logger.LogInformation(
            "Published PaymentRefundedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
