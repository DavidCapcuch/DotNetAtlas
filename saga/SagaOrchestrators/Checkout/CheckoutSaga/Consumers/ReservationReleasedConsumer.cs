using Inventory.Reservations;
using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="ReservationReleasedEvent"/> from
/// <c>inventory.reservations</c> and forwards it to the <see cref="CheckoutSagaOrchestrator"/>
/// as <see cref="ReservationReleasedSagaEvent"/> per docs/bc-design/checkout-saga.md § 8
/// row 9. Correlated by <c>OrderId</c> (Inventory's Avro lacks <c>CorrelationId</c>).
/// Drives transitions in <c>CompensatingStockReservations</c> -&gt; <c>Compensated</c> per
/// § 4 transition table; the saga discriminates on
/// <see cref="ReservationReleasedSagaEvent.ReleaseReason"/> to distinguish
/// compensation-driven releases from TTL expiry.
/// </summary>
public sealed class ReservationReleasedConsumer : IConsumer<ReservationReleasedEvent>
{
    private readonly ILogger<ReservationReleasedConsumer> _logger;

    public ReservationReleasedConsumer(ILogger<ReservationReleasedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReservationReleasedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, product {ProductId}, reservation {ReservationId}, reason {ReleaseReason}",
            nameof(ReservationReleasedConsumer), nameof(ReservationReleasedEvent),
            message.OrderId, message.ProductId, message.ReservationId, message.ReleaseReason);

        await context.Publish(new ReservationReleasedSagaEvent
        {
            OrderId = message.OrderId,
            ProductId = message.ProductId,
            ReservationId = message.ReservationId,
            ReleaseReason = message.ReleaseReason.ToString(),
            ReleasedAtUtc = message.ReleasedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
