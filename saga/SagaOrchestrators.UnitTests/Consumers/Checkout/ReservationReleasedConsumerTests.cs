using Inventory.Reservations;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SagaOrchestrators.Checkout.CheckoutSaga.Consumers;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.UnitTests.Consumers.Checkout;

/// <summary>
/// Asserts <see cref="ReservationReleasedConsumer"/> field-by-field maps the Avro
/// <see cref="ReservationReleasedEvent"/> onto the internal
/// <see cref="ReservationReleasedSagaEvent"/>. The Avro <c>ReleaseReason</c> enum is
/// stringified via <c>.ToString()</c>; the saga discriminates on the value.
/// </summary>
public class ReservationReleasedConsumerTests
{
    [Fact]
    public async Task Consume_publishes_internal_saga_event_with_mapped_fields()
    {
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<ReservationReleasedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            var orderId = Guid.CreateVersion7();
            var productId = Guid.CreateVersion7();
            var reservationId = Guid.CreateVersion7();
            var releasedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 3, 12, 40, 0), DateTimeKind.Utc);

            var avro = new ReservationReleasedEvent
            {
                OrderId = orderId,
                ProductId = productId,
                ReservationId = reservationId,
                ReleaseReason = ReleaseReason.Compensation,
                ReleasedAtUtc = releasedAt
            };

            await harness.Bus.Publish(avro, TestContext.Current.CancellationToken);

            Assert.True(await harness.Published.Any<ReservationReleasedSagaEvent>(TestContext.Current.CancellationToken));
            var published = await harness.Published.GetSinglePublishedMessageAsync<ReservationReleasedSagaEvent>(TestContext.Current.CancellationToken);
            Assert.Equal(orderId, published.OrderId);
            Assert.Equal(productId, published.ProductId);
            Assert.Equal(reservationId, published.ReservationId);
            Assert.Equal("Compensation", published.ReleaseReason);
            Assert.Equal(new DateTimeOffset(releasedAt, TimeSpan.Zero), published.ReleasedAtUtc);
        }
        finally
        {
            await harness.Stop();
        }
    }
}
