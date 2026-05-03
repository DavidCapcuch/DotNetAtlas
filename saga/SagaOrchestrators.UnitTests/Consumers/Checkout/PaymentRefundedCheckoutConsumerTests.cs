using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Payments.Transactions;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="PaymentRefundedCheckoutConsumer"/> field-by-field maps the Avro
/// <see cref="PaymentRefundedEvent"/> onto the internal <see cref="PaymentRefundedSagaEvent"/>;
/// the saga record's <c>Amount</c> sources from Avro's <c>RefundedAmount</c>. Uses a mocked
/// <see cref="ConsumeContext{T}"/> because <see cref="Avro.AvroDecimal"/> does not round-trip
/// through the harness's in-memory message serializer.
/// </summary>
public class PaymentRefundedCheckoutConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        var correlationId = Guid.CreateVersion7();
        var paymentTransactionId = Guid.CreateVersion7();
        var refundedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 13, 35, 0), DateTimeKind.Utc);

        var avro = new PaymentRefundedEvent
        {
            CorrelationId = correlationId,
            UserId = Guid.CreateVersion7(),
            PaymentTransactionId = paymentTransactionId,
            RefundTransactionId = Guid.CreateVersion7(),
            RefundedAmount = new Avro.AvroDecimal(149.99m),
            Currency = "USD",
            RefundedAtUtc = refundedAt
        };

        PaymentRefundedSagaEvent? captured = null;
        var ctx = Substitute.For<ConsumeContext<PaymentRefundedEvent>>();
        ctx.Message.Returns(avro);
        ctx.Publish(Arg.Do<PaymentRefundedSagaEvent>(e => captured = e), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var consumer = new PaymentRefundedCheckoutConsumer(NullLogger<PaymentRefundedCheckoutConsumer>.Instance);

        await consumer.Consume(ctx);

        Assert.NotNull(captured);
        Assert.Equal(correlationId, captured.CorrelationId);
        Assert.Equal(paymentTransactionId, captured.PaymentTransactionId);
        Assert.Equal(149.99m, captured.Amount);
        Assert.Equal("USD", captured.Currency);
        Assert.Equal(new DateTimeOffset(refundedAt, TimeSpan.Zero), captured.RefundedAtUtc);
    }
}
