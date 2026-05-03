using Inventory.Reservations;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="StockReservationFailedConsumer"/> field-by-field maps the Avro
/// <see cref="StockReservationFailedEvent"/> onto the internal
/// <see cref="StockReservationFailedSagaEvent"/>. Path B record shape: <c>OrderId</c> is the
/// correlation key; <c>ReservationId</c> / <c>ErrorCode</c> / <c>ErrorMessage</c> are absent
/// because the underlying Avro lacks them.
/// </summary>
public class StockReservationFailedConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<StockReservationFailedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.CreateVersion7();
            var productId = Guid.CreateVersion7();
            var failedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 12, 31, 0), DateTimeKind.Utc);

            var avro = new StockReservationFailedEvent
            {
                OrderId = orderId,
                ProductId = productId,
                RequestedQuantity = 5,
                AvailableQuantity = 2,
                FailedAtUtc = failedAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<StockReservationFailedSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<StockReservationFailedSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal(productId, published.ProductId);
            Assert.Equal(5, published.RequestedQuantity);
            Assert.Equal(2, published.AvailableQuantity);
            Assert.Equal(new DateTimeOffset(failedAt, TimeSpan.Zero), published.FailedAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
