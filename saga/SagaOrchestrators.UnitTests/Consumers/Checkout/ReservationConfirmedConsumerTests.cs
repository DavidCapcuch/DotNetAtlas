using Inventory.Reservations;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="ReservationConfirmedConsumer"/> field-by-field maps the Avro
/// <see cref="ReservationConfirmedEvent"/> onto the internal
/// <see cref="ReservationConfirmedSagaEvent"/>.
/// </summary>
public class ReservationConfirmedConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<ReservationConfirmedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.CreateVersion7();
            var productId = Guid.CreateVersion7();
            var reservationId = Guid.CreateVersion7();
            var confirmedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 12, 35, 0), DateTimeKind.Utc);

            var avro = new ReservationConfirmedEvent
            {
                OrderId = orderId,
                ProductId = productId,
                ReservationId = reservationId,
                ConfirmedAtUtc = confirmedAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<ReservationConfirmedSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<ReservationConfirmedSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal(productId, published.ProductId);
            Assert.Equal(reservationId, published.ReservationId);
            Assert.Equal(new DateTimeOffset(confirmedAt, TimeSpan.Zero), published.ConfirmedAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
