using FastEndpoints;
using Microsoft.Extensions.Options;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.Payments.PublishRequestPaymentCommand;

/// <summary>
/// Dev endpoint to publish <c>RequestPaymentCommand</c> for testing. Simulates what the Checkout
/// saga would emit on <c>payments.payment-commands</c> to initiate <c>PaymentProcessingSaga</c>.
/// Renamed from <c>PublishPaymentRequestedEventEndpoint</c> per ADR-0023.
/// </summary>
internal class PublishRequestPaymentCommandEndpoint : Endpoint<PublishRequestPaymentCommandRequest>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishRequestPaymentCommandEndpoint> _logger;

    public PublishRequestPaymentCommandEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishRequestPaymentCommandEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-request-payment-command");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a RequestPaymentCommand to Kafka for dev testing. " +
                "Simulates what the Checkout saga would emit to initiate the PaymentProcessingSaga.";
            s.ExampleRequest = new PublishRequestPaymentCommandRequest
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

    public override async Task HandleAsync(PublishRequestPaymentCommandRequest req, CancellationToken ct)
    {
        var requestPaymentCommand = new RequestPaymentCommand
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

        await _devEventsProducer.PublishRequestPaymentCommandAsync(requestPaymentCommand);

        _logger.LogInformation(
            "Published RequestPaymentCommand - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.PaymentCommands,
            Message = "Command published successfully"
        }, ct);
    }
}
