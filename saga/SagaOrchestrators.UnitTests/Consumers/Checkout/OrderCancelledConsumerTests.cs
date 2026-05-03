using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Orders;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="OrderCancelledConsumer"/> field-by-field maps the Avro
/// <see cref="OrderCancelledEvent"/> onto the internal <see cref="OrderCancelledSagaEvent"/>.
/// </summary>
public class OrderCancelledConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderCancelledConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var correlationId = Guid.CreateVersion7();
            var orderId = Guid.CreateVersion7();
            var cancelledAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 13, 10, 0), DateTimeKind.Utc);

            var avro = new OrderCancelledEvent
            {
                OrderId = orderId,
                CorrelationId = correlationId,
                BuyerId = Guid.CreateVersion7(),
                CancelledAtUtc = cancelledAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<OrderCancelledSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<OrderCancelledSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(correlationId, published.CorrelationId);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal(new DateTimeOffset(cancelledAt, TimeSpan.Zero), published.CancelledAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
