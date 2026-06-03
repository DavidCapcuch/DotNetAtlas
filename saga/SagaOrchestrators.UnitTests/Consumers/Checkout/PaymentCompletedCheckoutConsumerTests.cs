using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Payments.Transactions;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="PaymentCompletedCheckoutConsumer"/> field-by-field maps the Avro
/// <see cref="PaymentCompletedEvent"/> onto the internal <see cref="PaymentCompletedSagaEvent"/>.
/// Uses a mocked <see cref="ConsumeContext{T}"/> instead of <see cref="MassTransit.Testing.ITestHarness"/>
/// because <see cref="Avro.AvroDecimal"/>'s <c>UnscaledValue</c> (a <c>BigInteger</c> with no
/// setter) does not round-trip through the harness's in-memory message serializer - direct
/// invocation keeps the test focused on the consumer's mapping contract.
/// </summary>
public class PaymentCompletedCheckoutConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        var correlationId = Guid.CreateVersion7();
        var orderId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var completedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 13, 25, 0), DateTimeKind.Utc);

        var avro = new PaymentCompletedEvent
        {
            CorrelationId = correlationId,
            OrderId = orderId,
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = paymentTransactionId,
            Amount = new Avro.AvroDecimal(149.99m),
            Currency = "USD",
            CompletedAtUtc = completedAt
        };

        PaymentCompletedSagaEvent? captured = null;
        var ctx = Substitute.For<ConsumeContext<PaymentCompletedEvent>>();
        ctx.Message.Returns(avro);
        ctx.Publish(Arg.Do<PaymentCompletedSagaEvent>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var consumer = new PaymentCompletedCheckoutConsumer(NullLogger<PaymentCompletedCheckoutConsumer>.Instance);

        await consumer.Consume(ctx);

        Assert.NotNull(captured);
        Assert.Equal(orderId, captured.OrderId);
        Assert.Equal(paymentTransactionId, captured.PaymentTransactionId);
        Assert.Equal(149.99m, captured.Amount);
        Assert.Equal("USD", captured.Currency);
        Assert.Equal(new DateTimeOffset(completedAt, TimeSpan.Zero), captured.CompletedAtUtc);
    }
}
