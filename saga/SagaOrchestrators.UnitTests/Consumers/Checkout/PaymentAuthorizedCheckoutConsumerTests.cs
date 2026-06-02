using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Payments.Transactions;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="PaymentAuthorizedCheckoutConsumer"/> field-by-field maps the Avro
/// <see cref="PaymentAuthorizedEvent"/> onto the internal <see cref="PaymentAuthorizedCheckoutSagaEvent"/>
/// (ADR-0026: the Checkout saga reacts to Payments' authorization event to drive confirmation
/// before approving capture). Uses a mocked <see cref="ConsumeContext{T}"/> instead of
/// <see cref="MassTransit.Testing.ITestHarness"/> — same rationale as the other Checkout consumer
/// tests (Avro decimal does not round-trip through the in-memory harness serializer).
/// </summary>
public class PaymentAuthorizedCheckoutConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        var correlationId = Guid.CreateVersion7();
        var authorizationId = $"auth-{Guid.CreateVersion7()}";
        var authorizedAt = DateTime.SpecifyKind(new DateTime(2026, 6, 2, 9, 15, 0), DateTimeKind.Utc);

        var avro = new PaymentAuthorizedEvent
        {
            CorrelationId = correlationId,
            UserId = Guid.CreateVersion7(),
            AuthorizationId = authorizationId,
            Amount = new Avro.AvroDecimal(149.99m),
            Currency = "USD",
            AuthorizedAtUtc = authorizedAt,
            ExpiresAtUtc = authorizedAt.AddDays(7)
        };

        PaymentAuthorizedCheckoutSagaEvent? captured = null;
        var ctx = Substitute.For<ConsumeContext<PaymentAuthorizedEvent>>();
        ctx.Message.Returns(avro);
        ctx.Publish(Arg.Do<PaymentAuthorizedCheckoutSagaEvent>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var consumer = new PaymentAuthorizedCheckoutConsumer(NullLogger<PaymentAuthorizedCheckoutConsumer>.Instance);

        await consumer.Consume(ctx);

        Assert.NotNull(captured);
        Assert.Equal(correlationId, captured.CorrelationId);
        Assert.Equal(authorizationId, captured.AuthorizationId);
        Assert.Equal(new DateTimeOffset(authorizedAt, TimeSpan.Zero), captured.AuthorizedAtUtc);
    }
}
