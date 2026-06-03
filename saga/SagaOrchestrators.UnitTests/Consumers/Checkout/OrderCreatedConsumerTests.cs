using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Orders;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="OrderCreatedConsumer"/> field-by-field maps the Avro
/// <see cref="OrderCreatedEvent"/> onto the internal <see cref="OrderCreatedSagaEvent"/>,
/// including the rename of Avro <c>CreatedAtUtc</c> to saga record <c>OrderCreatedAtUtc</c>.
/// </summary>
public class OrderCreatedConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<OrderCreatedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var correlationId = Guid.CreateVersion7();
            var orderId = Guid.CreateVersion7();
            var createdAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 12, 0, 0), DateTimeKind.Utc);

            var avro = new OrderCreatedEvent
            {
                OrderId = orderId,
                BuyerId = Guid.CreateVersion7(),
                Items = [],
                TotalAmount = new Avro.AvroDecimal(100m),
                Currency = "USD",
                PaymentMethodId = Guid.CreateVersion7(),
                CreatedAtUtc = createdAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<OrderCreatedSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<OrderCreatedSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal(new DateTimeOffset(createdAt, TimeSpan.Zero), published.OrderCreatedAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
