using System.Text.Json;
using Basket.Sessions;
using MassTransit;
using SagaOrchestrators.Checkout.CheckoutSaga.InternalSagaEvents;

namespace SagaOrchestrators.Checkout.CheckoutSaga.Consumers;

/// <summary>
/// Consumer that receives <see cref="BasketCheckoutInitiatedEvent"/> from
/// <c>basket.sessions</c> and forwards it to the <see cref="CheckoutSagaOrchestrator"/> as the
/// initiator <see cref="BasketCheckoutInitiatedSagaEvent"/>. Maps Basket's
/// <c>BasketCorrelationId</c> onto the saga's <c>CorrelationId</c> per
/// docs/bc-design/checkout-saga.md § 8 row 1. Address payloads (PII per ADR-0011) are
/// serialised to opaque JSON strings and never logged - the M4 <c>Initially(...)</c> handler
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
            message.BasketCorrelationId, message.UserId, message.Items.Count,
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
            CorrelationId = message.BasketCorrelationId,
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

    /// <summary>
    /// Snapshot DTO for a basket line item - the JSON shape persisted into
    /// <c>CheckoutSagaState.BasketSnapshotJson</c>. M3 owns the writer side; M4 owns the reader
    /// side and may move this record into a shared location once the reader contract solidifies.
    /// </summary>
    internal sealed record BasketItemSnapshot(
        Guid ProductId,
        string Sku,
        string Name,
        int Quantity,
        decimal UnitPriceAmount,
        string UnitPriceCurrency,
        decimal LineTotal);

    /// <summary>
    /// Snapshot DTO for an address - the JSON shape persisted into
    /// <c>CheckoutSagaState.{Shipping,Billing}AddressJson</c>. PII per ADR-0011: nulled out on
    /// terminal saga states.
    /// </summary>
    internal sealed record AddressSnapshot(
        string Street1,
        string? Street2,
        string City,
        string? State,
        string PostalCode,
        string CountryCode);
}
