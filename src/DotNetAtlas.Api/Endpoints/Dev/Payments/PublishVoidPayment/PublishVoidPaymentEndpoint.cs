using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishVoidPayment;

/// <summary>
/// Dev endpoint to publish a VoidPaymentCommand for testing.
/// Simulates what the Payment Saga would send to void an authorized payment.
/// </summary>
internal class PublishVoidPaymentEndpoint : Endpoint<PublishVoidPaymentCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishVoidPaymentEndpoint> _logger;

    public PublishVoidPaymentEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishVoidPaymentEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-void-payment-command");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a VoidPaymentCommand to Kafka for dev testing. " +
                "Simulates what the Payment Saga would send to the Payment service to void an authorized payment.";
            s.ExampleRequest = new PublishVoidPaymentCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                AuthorizationId = "auth_12345",
                Reason = "Customer cancelled order"
            };
        });
    }

    public override async Task HandleAsync(PublishVoidPaymentCommand req, CancellationToken ct)
    {
        var voidPaymentCommand = new VoidPaymentCommand
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            AuthorizationId = req.AuthorizationId,
            Reason = req.Reason,
            RequestedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishVoidPaymentCommandAsync(voidPaymentCommand);

        _logger.LogInformation(
            "Published VoidPaymentCommand - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.PaymentCommands,
            Message = "Command published successfully"
        }, ct);
    }
}
