using FastEndpoints;
using Microsoft.Extensions.Options;
using Payments.Transactions;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Weather.Application.Common.Messaging;
using Weather.Infrastructure.Messaging.Kafka.Dev;

namespace Weather.Api.Endpoints.Dev.Payments.PublishAuthorizePayment;

/// <summary>
/// Dev endpoint to publish an AuthorizePaymentCommand for testing.
/// Simulates what the Payment Saga would send to request payment authorization from the Payment Service.
/// </summary>
internal class PublishAuthorizePaymentEndpoint : Endpoint<PublishAuthorizePaymentCommand>
{
    private readonly DevEventsKafkaProducer _devEventsProducer;
    private readonly TopicsOptions _topicsOptions;
    private readonly ILogger<PublishAuthorizePaymentEndpoint> _logger;

    public PublishAuthorizePaymentEndpoint(
        DevEventsKafkaProducer devEventsProducer,
        IOptions<TopicsOptions> topicsOptions,
        ILogger<PublishAuthorizePaymentEndpoint> logger)
    {
        _devEventsProducer = devEventsProducer;
        _topicsOptions = topicsOptions.Value;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("publish-authorize-payment-command");
        Version(1);
        Group<DevGroup>();
        Summary(s =>
        {
            s.Description =
                "Publishes an AuthorizePaymentCommand to Kafka for dev testing. " +
                "Simulates what the Payment Saga would send to request payment authorization from the Payment Service.";
            s.ExampleRequest = new PublishAuthorizePaymentCommand
            {
                CorrelationId = Guid.CreateVersion7(),
                UserId = Guid.Parse("00000000-0000-0000-0000-111111111111"), // dev@dotnetatlas.com
                PaymentMethodId = Guid.CreateVersion7(),
                Amount = 9.99m,
                Currency = "USD",
                IdempotencyKey = Guid.CreateVersion7().ToString()
            };
        });
    }

    public override async Task HandleAsync(PublishAuthorizePaymentCommand req, CancellationToken ct)
    {
        var authorizePaymentCommand = new AuthorizePaymentCommand
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            PaymentMethodId = req.PaymentMethodId,
            Amount = req.Amount.ToAvroDecimal(4),
            Currency = req.Currency,
            IdempotencyKey = req.IdempotencyKey,
            RequestedAtUtc = DateTime.UtcNow
        };

        await _devEventsProducer.PublishAuthorizePaymentCommandAsync(authorizePaymentCommand);

        _logger.LogInformation(
            "Published AuthorizePaymentCommand - CorrelationId: {CorrelationId}," +
            " UserId: {UserId}, Amount: {Amount} {Currency}",
            req.CorrelationId, req.UserId, req.Amount, req.Currency);

        await Send.OkAsync(new
        {
            CorrelationId = req.CorrelationId,
            UserId = req.UserId,
            Topic = _topicsOptions.PaymentCommands,
            Message = "Command published successfully"
        }, ct);
    }
}
