using Platform.SharedKernel.Exceptions;
using AppAddressInput = Ordering.Application.Orders.CreateOrder.AddressInput;
using AppCancelOrderCommand = Ordering.Application.Orders.CancelOrder.CancelOrderCommand;
using AppConfirmOrderCommand = Ordering.Application.Orders.ConfirmOrder.ConfirmOrderCommand;
using AppCreateOrderCommand = Ordering.Application.Orders.CreateOrder.CreateOrderCommand;
using AppCreateOrderItemInput = Ordering.Application.Orders.CreateOrder.CreateOrderItemInput;
using AppMarkOrderFailedCommand = Ordering.Application.Orders.MarkOrderFailed.MarkOrderFailedCommand;
using AvroCancelOrderCommand = Ordering.Orders.CancelOrderCommand;
using AvroConfirmOrderCommand = Ordering.Orders.ConfirmOrderCommand;
using AvroCreateOrderCommand = Ordering.Orders.CreateOrderCommand;
using AvroCreateOrderItem = Ordering.Orders.CreateOrderItem;
using AvroMarkOrderFailedCommand = Ordering.Orders.MarkOrderFailedCommand;
using AvroOrderAddress = Ordering.Orders.OrderAddress;

namespace Ordering.Infrastructure.Messaging.Kafka.SagaCommands;

/// <summary>
/// Translates saga-issued Avro commands on <c>ordering.order-commands</c>
/// to the application-layer command DTOs. Pure functions, no DI, no
/// side-effects — simple mapping is clearer than a Mapperly config here
/// because the shape differences (AvroDecimal, DateTime↔DateTimeOffset,
/// per-item currency collapsing to single top-level currency) are all
/// explicit.
/// </summary>
internal static class SagaCommandMappers
{
    /// <summary>
    /// Maps <see cref="AvroCreateOrderCommand"/> to the application layer
    /// <see cref="AppCreateOrderCommand"/>. Currency is taken from the first
    /// item — the Avro schema requires uniform currency across items
    /// (events-catalog.md § 5.5.1). A mismatch across items is a bug-class
    /// signal from the saga and surfaces as <see cref="DataIntegrityException"/>
    /// so the message flows to DLT instead of silently collapsing onto
    /// <c>Items[0]</c>'s currency.
    /// </summary>
    // ADR-0008 — CorrelationId is passed in explicitly from the Kafka header rather than read
    // from the Avro payload field; the header is the authoritative source.
    internal static AppCreateOrderCommand ToAppCommand(this AvroCreateOrderCommand avro, Guid correlationId)
    {
        var (items, currency) = MapItemsAndResolveCurrency(avro.Items);

        return new AppCreateOrderCommand
        {
            CorrelationId = correlationId,
            BuyerId = avro.BuyerId,
            PaymentMethodId = avro.PaymentMethodId,
            Currency = currency,
            Items = items,
            ShippingAddress = ToAddressInput(avro.ShippingAddress),
            BillingAddress = ToAddressInput(avro.BillingAddress),
            // Avro deserialisers produce DateTimeKind.Utc; SpecifyKind is a no-op
            // there and a defensive guard against future Kind=Unspecified drift.
            RequestedAtUtc = new DateTimeOffset(
                DateTime.SpecifyKind(avro.RequestedAtUtc, DateTimeKind.Utc),
                TimeSpan.Zero),
        };
    }

    private static (AppCreateOrderItemInput[] Items, string Currency) MapItemsAndResolveCurrency(
        IList<AvroCreateOrderItem>? avroItems)
    {
        if (avroItems is null || avroItems.Count == 0)
        {
            return (Array.Empty<AppCreateOrderItemInput>(), string.Empty);
        }

        var currency = avroItems[0].UnitPriceCurrency;
        var items = new AppCreateOrderItemInput[avroItems.Count];
        items[0] = ToItemInput(avroItems[0]);

        for (var i = 1; i < avroItems.Count; i++)
        {
            var avroItem = avroItems[i];
            if (!string.Equals(avroItem.UnitPriceCurrency, currency, StringComparison.Ordinal))
            {
                throw new DataIntegrityException(
                    "Ordering.MultipleCurrencies",
                    $"CreateOrderCommand items must share a single UnitPriceCurrency; saw '{currency}' and '{avroItem.UnitPriceCurrency}'.");
            }

            items[i] = ToItemInput(avroItem);
        }

        return (items, currency);
    }

    internal static AppConfirmOrderCommand ToAppCommand(this AvroConfirmOrderCommand avro) =>
        new() { OrderId = avro.OrderId };

    /// <summary>
    /// Maps <see cref="AvroCancelOrderCommand"/> to the application layer
    /// <see cref="AppCancelOrderCommand"/>. Saga-originated cancellations
    /// run with <c>IsAdmin=true</c> and <c>BuyerId=Guid.Empty</c>: the saga
    /// is a privileged caller whose correlation-id match is its
    /// authorisation (the buyer-ownership check only applies to the HTTP
    /// surface per <c>ordering.md § 9.2</c>).
    /// </summary>
    internal static AppCancelOrderCommand ToAppCommand(this AvroCancelOrderCommand avro) =>
        new()
        {
            OrderId = avro.OrderId,
            Reason = avro.Reason,
            BuyerId = Guid.Empty,
            IsAdmin = true,
        };

    internal static AppMarkOrderFailedCommand ToAppCommand(this AvroMarkOrderFailedCommand avro) =>
        new()
        {
            OrderId = avro.OrderId,
            ErrorCode = avro.ErrorCode,
            ErrorMessage = avro.ErrorMessage,
        };

    private static AppCreateOrderItemInput ToItemInput(AvroCreateOrderItem item) =>
        new(
            item.ProductId,
            item.Sku,
            item.Name,
            item.Quantity,
            (decimal)item.UnitPriceAmount);

    private static AppAddressInput ToAddressInput(AvroOrderAddress address) =>
        new(
            address.Street1,
            address.Street2,
            address.City,
            address.State,
            address.PostalCode,
            address.CountryCode);
}
