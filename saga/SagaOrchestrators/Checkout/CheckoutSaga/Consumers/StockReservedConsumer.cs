using Inventory.Reservations;
using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="StockReservedEvent"/> from <c>inventory.reservations</c>
/// and forwards it to the <see cref="CheckoutSagaOrchestrator"/> as
/// <see cref="StockReservedSagaEvent"/> per docs/bc-design/checkout-saga.md § 8 row 6.
/// Correlated by <c>OrderId</c> — Inventory's Avro schemas don't carry
/// <c>CorrelationId</c>; the saga uses <c>OrderId</c> as the correlation key because the
/// state-machine sequence guarantees <c>OrderCreatedSagaEvent</c> precedes any Stock*
/// event.
/// </summary>
public sealed class StockReservedConsumer : IConsumer<StockReservedEvent>
{
    private readonly ILogger<StockReservedConsumer> _logger;

    public StockReservedConsumer(ILogger<StockReservedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockReservedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, product {ProductId}, reservation {ReservationId}, quantity {Quantity}",
            nameof(StockReservedConsumer), nameof(StockReservedEvent),
            message.OrderId, message.ProductId, message.ReservationId, message.Quantity);

        await context.Publish(new StockReservedSagaEvent
        {
            OrderId = message.OrderId,
            ProductId = message.ProductId,
            ReservationId = message.ReservationId,
            Quantity = message.Quantity,
            ReservedAtUtc = message.ReservedAtUtc.ToUtcDateTimeOffset(),
            ExpiresAtUtc = message.ExpiresAtUtc.ToUtcDateTimeOffset()
        });
    }
}
