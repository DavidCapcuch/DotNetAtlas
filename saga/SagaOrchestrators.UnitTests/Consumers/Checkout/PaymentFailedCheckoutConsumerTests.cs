using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Payments.Transactions;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="PaymentFailedCheckoutConsumer"/> field-by-field maps the Avro
/// <see cref="PaymentFailedEvent"/> onto the internal <see cref="PaymentFailedSagaEvent"/>.
/// </summary>
public class PaymentFailedCheckoutConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<PaymentFailedCheckoutConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var correlationId = Guid.CreateVersion7();
            var orderId = Guid.CreateVersion7();
            var failedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 13, 30, 0), DateTimeKind.Utc);

            var avro = new PaymentFailedEvent
            {
                OrderId = orderId,
                UserId = Guid.CreateVersion7(),
                ErrorCode = "PAYMENT_FAILED",
                ErrorMessage = "Card declined.",
                FailedAtUtc = failedAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<PaymentFailedSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<PaymentFailedSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal("PAYMENT_FAILED", published.ErrorCode);
            Assert.Equal("Card declined.", published.ErrorMessage);
            Assert.Equal(new DateTimeOffset(failedAt, TimeSpan.Zero), published.FailedAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
