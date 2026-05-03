using Inventory.Reservations;
using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="StockReservationFailedEvent"/> from
/// <c>inventory.reservations</c> and forwards it to the <see cref="CheckoutSagaOrchestrator"/>
/// as <see cref="StockReservationFailedSagaEvent"/> per docs/bc-design/checkout-saga.md § 8
/// row 7. Correlated by <c>OrderId</c> (M3 plan-file § C1 Path B). First arrival wins -
/// triggers transition to <c>CompensatingStockReservations</c>.
/// </summary>
public sealed class StockReservationFailedConsumer : IConsumer<StockReservationFailedEvent>
{
    private readonly ILogger<StockReservationFailedConsumer> _logger;

    public StockReservationFailedConsumer(ILogger<StockReservationFailedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StockReservationFailedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for order {OrderId}, product {ProductId}, requested {RequestedQuantity}, available {AvailableQuantity}",
            nameof(StockReservationFailedConsumer), nameof(StockReservationFailedEvent),
            message.OrderId, message.ProductId, message.RequestedQuantity, message.AvailableQuantity);

        await context.Publish(new StockReservationFailedSagaEvent
        {
            OrderId = message.OrderId,
            ProductId = message.ProductId,
            RequestedQuantity = message.RequestedQuantity,
            AvailableQuantity = message.AvailableQuantity,
            FailedAtUtc = message.FailedAtUtc.ToUtcDateTimeOffset()
        });
    }
}
