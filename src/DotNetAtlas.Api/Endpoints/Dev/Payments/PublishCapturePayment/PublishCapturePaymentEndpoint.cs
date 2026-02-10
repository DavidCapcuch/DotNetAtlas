using DotNetAtlas.Application.Common.Messaging;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.AvroExtensions;
using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishCapturePayment;

/// <summary>
/// Dev endpoint to publish a CapturePaymentCommand for testing.
/// Simulates what the Payment Saga would send to capture an authorized payment.
/// </summary>
internal class PublishCapturePaymentEndpoint : Endpoint<PublishCapturePaymentCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishCapturePaymentEndpoint> _logger;

    public PublishCapturePaymentEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishCapturePaymentEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-capture-payment-command");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a CapturePaymentCommand to Kafka for dev testing. " +
                "Simulates what the Payment Saga would send to the Payment Service to capture an authorized payment.";
            s.ExampleRequest = new PublishCapturePaymentCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                AuthorizationId = "auth_123456789",
                Amount = 9.99m
            };
        });
    }

    public override async Task HandleAsync(PublishCapturePaymentCommand req, CancellationToken ct)
    {
        var capturePaymentCommand = new CapturePaymentCommand
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            AuthorizationId = req.AuthorizationId,
            Amount = req.Amount.ToAvroDecimal(4),
            RequestedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishCapturePaymentCommandAsync(capturePaymentCommand);

        _logger.LogInformation(
            "Published CapturePaymentCommand - CorrelationId: {CorrelationId}, " +
            "UserId: {UserId}, AuthorizationId: {AuthorizationId}, Amount: {Amount}",
            req.CorrelationId, req.UserId, req.AuthorizationId, req.Amount);

        await Send.OkAsync(new
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            Topic = _topicsOptions.PaymentCommands,
            Message = "Command published successfully"
        }, ct);
    }
}
