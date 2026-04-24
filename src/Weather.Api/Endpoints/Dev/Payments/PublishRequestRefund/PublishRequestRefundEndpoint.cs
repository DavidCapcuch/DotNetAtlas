using FastEndpoints;
using Microsoft.Extensions.Options;
using Payments.Transactions;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.Payments.PublishRequestRefund;

/// <summary>
/// Dev endpoint to publish a RequestRefundCommand for testing.
/// Simulates what the Payment Saga would send to request a refund.
/// </summary>
internal class PublishRequestRefundEndpoint : Endpoint<PublishRequestRefundCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishRequestRefundEndpoint> _logger;

    public PublishRequestRefundEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishRequestRefundEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-request-refund-command");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a RequestRefundCommand to Kafka for dev testing. " +
                "Simulates what the Payment Saga would send to the Payment service to request a refund.";
            s.ExampleRequest = new PublishRequestRefundCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentTransactionId = Guid.CreateVersion7(),
                Reason = "Customer requested refund"
            };
        });
    }

    public override async Task HandleAsync(PublishRequestRefundCommand req, CancellationToken ct)
    {
        var requestRefundCommand = new RequestRefundCommand
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentTransactionId = req.PaymentTransactionId,
            Reason = req.Reason,
            RequestedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishRequestRefundCommandAsync(requestRefundCommand);

        _logger.LogInformation(
            "Published RequestRefundCommand - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.PaymentCommands,
            Message = "Command published successfully"
        }, ct);
    }
}
