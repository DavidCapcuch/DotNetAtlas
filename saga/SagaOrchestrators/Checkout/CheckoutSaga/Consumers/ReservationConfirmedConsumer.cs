using Inventory.Reservations;
using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="ReservationConfirmedEvent"/> from
/// <c>inventory.reservations</c> and forwards it to the <see cref="CheckoutSagaOrchestrator"/>
/// as <see cref="ReservationConfirmedSagaEvent"/> per docs/bc-design/checkout-saga.md § 8
/// row 8 (in design doc) - informational tracking only, does not gate <c>Confirmed</c>.
/// Correlated by <c>OrderId</c> (Inventory's Avro lacks <c>CorrelationId</c>).
/// </summary>
public sealed class ReservationConfirmedConsumer : IConsumer<ReservationConfirmedEvent>
{
    private readonly ILogger<ReservationConfirmedConsumer> _logger;

    public ReservationConfirmedConsumer(ILogger<ReservationConfirmedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ReservationConfirmedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, product {ProductId}, reservation {ReservationId}",
            nameof(ReservationConfirmedConsumer), nameof(ReservationConfirmedEvent),
            message.OrderId, message.ProductId, message.ReservationId);

        await context.Publish(new ReservationConfirmedSagaEvent
        {
            OrderId = message.OrderId,
            ProductId = message.ProductId,
            ReservationId = message.ReservationId,
            ConfirmedAtUtc = message.ConfirmedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
