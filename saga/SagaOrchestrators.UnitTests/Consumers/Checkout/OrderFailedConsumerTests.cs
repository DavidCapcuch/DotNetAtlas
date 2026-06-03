using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Orders;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="OrderFailedConsumer"/> field-by-field maps the Avro
/// <see cref="OrderFailedEvent"/> onto the internal <see cref="OrderFailedSagaEvent"/>.
/// </summary>
public class OrderFailedConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderFailedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var correlationId = Guid.CreateVersion7();
            var orderId = Guid.CreateVersion7();
            var failedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 13, 15, 0), DateTimeKind.Utc);

            var avro = new OrderFailedEvent
            {
                OrderId = orderId,
                BuyerId = Guid.CreateVersion7(),
                ErrorCode = "ORDER_VALIDATION_FAILED",
                ErrorMessage = "Buyer is suspended.",
                AtStatus = OrderStatusAtTransition.Created,
                FailedAtUtc = failedAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<OrderFailedSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<OrderFailedSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal("ORDER_VALIDATION_FAILED", published.ErrorCode);
            Assert.Equal("Buyer is suspended.", published.ErrorMessage);
            Assert.Equal(new DateTimeOffset(failedAt, TimeSpan.Zero), published.FailedAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
