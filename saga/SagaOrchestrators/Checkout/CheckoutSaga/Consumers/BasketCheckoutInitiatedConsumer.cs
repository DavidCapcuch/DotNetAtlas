using System.Text.Json;
using Basket.Sessions;
using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;
using SagaOrchestrators.Checkout.CheckoutSaga.Snapshots;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="BasketCheckoutInitiatedEvent"/> from
/// <c>basket.sessions</c> and forwards it to the <see cref="CheckoutSagaOrchestrator"/> as the
/// initiator <see cref="BasketCheckoutInitiatedSagaEvent"/>. Maps Basket's pre-assigned
/// <c>OrderId</c> (ADR-0029) onto the saga's <c>CorrelationId</c> per
/// docs/bc-design/checkout-saga.md § 8 row 1. Address payloads (PII per ADR-0011) are
/// serialised to opaque JSON strings and never logged — the <c>Initially(...)</c> handler
/// persists them, terminal-state handlers null them out per the retention rule.
/// </summary>
public sealed class BasketCheckoutInitiatedConsumer : IConsumer<BasketCheckoutInitiatedEvent>
{
    private readonly ILogger<BasketCheckoutInitiatedConsumer> _logger;

    public BasketCheckoutInitiatedConsumer(ILogger<BasketCheckoutInitiatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BasketCheckoutInitiatedEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "{ConsumerType} received {EventType} for correlation {CorrelationId}, user {UserId}, item count {ItemCount}, total {TotalAmount} {Currency}",
            nameof(BasketCheckoutInitiatedConsumer), nameof(BasketCheckoutInitiatedEvent),
            message.OrderId, message.UserId, message.Items.Count,
            (decimal)message.TotalAmount, message.Currency);

        var basketSnapshotJson = JsonSerializer.Serialize(
            message.Items.Select(item => new BasketItemSnapshot(
                ProductId: item.ProductId,
                Sku: item.Sku,
                Name: item.Name,
                Quantity: item.Quantity,
                UnitPriceAmount: (decimal)item.UnitPriceAmount,
                UnitPriceCurrency: item.UnitPriceCurrency,
                LineTotal: (decimal)item.LineTotal)).ToArray());

        var sagaEvent = new BasketCheckoutInitiatedSagaEvent
        {
            CorrelationId = message.OrderId,
            UserId = message.UserId,
            BasketSnapshotJson = basketSnapshotJson,
            TotalAmount = (decimal)message.TotalAmount,
            Currency = message.Currency,
            PaymentMethodId = message.PaymentMethodId,
            ShippingAddressJson = JsonSerializer.Serialize(MapAddress(message.ShippingAddress)),
            BillingAddressJson = JsonSerializer.Serialize(MapAddress(message.BillingAddress)),
            InitiatedAtUtc = message.InitiatedAtUtc.ToUtcDateTimeOffset()
        };

        await context.Publish(sagaEvent);
    }

    private static AddressSnapshot MapAddress(CheckoutAddress address) =>
        new(
            Street1: address.Street1,
            Street2: address.Street2,
            City: address.City,
            State: address.State,
            PostalCode: address.PostalCode,
            CountryCode: address.CountryCode);
}
