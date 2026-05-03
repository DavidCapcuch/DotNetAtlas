using Inventory.Reservations;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="StockReservedConsumer"/> field-by-field maps the Avro
/// <see cref="StockReservedEvent"/> onto the internal <see cref="StockReservedSagaEvent"/>.
/// Behavioural saga transitions (M4) are out of scope.
/// </summary>
public class StockReservedConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<StockReservedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.CreateVersion7();
            var productId = Guid.CreateVersion7();
            var reservationId = Guid.CreateVersion7();
            var reservedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 12, 30, 0), DateTimeKind.Utc);
            var expiresAt = reservedAt.AddSeconds(900);

            var avro = new StockReservedEvent
            {
                OrderId = orderId,
                ProductId = productId,
                ReservationId = reservationId,
                Quantity = 4,
                ReservedAtUtc = reservedAt,
                ExpiresAtUtc = expiresAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<StockReservedSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<StockReservedSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal(productId, published.ProductId);
            Assert.Equal(reservationId, published.ReservationId);
            Assert.Equal(4, published.Quantity);
            Assert.Equal(new DateTimeOffset(reservedAt, TimeSpan.Zero), published.ReservedAtUtc);
            Assert.Equal(new DateTimeOffset(expiresAt, TimeSpan.Zero), published.ExpiresAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
