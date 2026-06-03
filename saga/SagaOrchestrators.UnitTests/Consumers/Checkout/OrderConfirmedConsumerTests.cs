using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Orders;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="OrderConfirmedConsumer"/> field-by-field maps the Avro
/// <see cref="OrderConfirmedEvent"/> onto the internal <see cref="OrderConfirmedSagaEvent"/>.
/// </summary>
public class OrderConfirmedConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderConfirmedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var correlationId = Guid.CreateVersion7();
            var orderId = Guid.CreateVersion7();
            var confirmedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 13, 5, 0), DateTimeKind.Utc);

            var avro = new OrderConfirmedEvent
            {
                OrderId = orderId,
                BuyerId = Guid.CreateVersion7(),
                ConfirmedAtUtc = confirmedAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<OrderConfirmedSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<OrderConfirmedSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal(new DateTimeOffset(confirmedAt, TimeSpan.Zero), published.ConfirmedAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
