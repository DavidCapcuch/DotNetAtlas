using DotNetAtlas.Application.Common.Messaging.Config;
using DotNetAtlas.Infrastructure.Messaging.Kafka.Dev;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.Extensions;
using FastEndpoints;
using Finance.Payments;
using Microsoft.Extensions.Options;

namespace DotNetAtlas.Api.Endpoints.Dev.Payments.PublishPaymentAuthorizedEvent;

/// <summary>
/// Dev endpoint to publish PaymentAuthorizedEvent for testing.
/// </summary>
internal class PublishPaymentAuthorizedEventEndpoint : Endpoint<PublishPaymentAuthorizedEventCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishPaymentAuthorizedEventEndpoint> _logger;

    public PublishPaymentAuthorizedEventEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishPaymentAuthorizedEventEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-payment-authorized-event");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes a PaymentAuthorizedEvent to Kafka for dev testing. " +
                "Simulates what the Payment service would emit when payment authorization succeeds.";
            s.ExampleRequest = new PublishPaymentAuthorizedEventCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                AuthorizationId = "auth_12345",
                Amount = 99.99m,
                Currency = "USD"
            };
        });
    }

    public override async Task HandleAsync(PublishPaymentAuthorizedEventCommand req, CancellationToken ct)
    {
        var paymentAuthorizedEvent = new PaymentAuthorizedEvent
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            AuthorizationId = req.AuthorizationId,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = req.Currency,
            AuthorizedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        await _devEventsProducer.PublishPaymentAuthorizedEventAsync(paymentAuthorizedEvent);

        _logger.LogInformation(
            "Published PaymentAuthorizedEvent - CorrelationId: {CorrelationId}, UserId: {UserId}",
            req.CorrelationId, req.UserId);

        await Send.OkAsync(new
        {
            req.CorrelationId,
            Topic = _topicsOptions.Payments,
            Message = "Event published successfully"
        }, ct);
    }
}
