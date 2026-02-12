using Avro;
using DotNetAtlas.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Order.AlertSubscriptions;
using Ordering.Domain;
using Ordering.Domain.AlertSubscriptionOrders;
using Ordering.Domain.AlertSubscriptionOrders.Events;
using Riok.Mapperly.Abstractions;

namespace Ordering.Application.AlertSubscriptions.PurchaseAlertSubscription;

/// <summary>
/// Mapper for converting <see cref="AlertSubscriptionPurchaseOrderCreatedDomainEvent"/>
/// to the <see cref="AlertSubscriptionPurchaseInitiatedEvent"/> Avro integration event.
/// </summary>
[Mapper]
public static partial class PurchaseOrderCreatedMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(AlertSubscriptionPurchaseOrderCreatedDomainEvent.AlertSubscriptionOrderId), nameof(AlertSubscriptionPurchaseInitiatedEvent.AlertSubscriptionOrderId))]
    [MapProperty(nameof(AlertSubscriptionPurchaseOrderCreatedDomainEvent.OccurredOnUtc), nameof(AlertSubscriptionPurchaseInitiatedEvent.InitiatedAtUtc))]
    [MapProperty(nameof(AlertSubscriptionPurchaseOrderCreatedDomainEvent.Price.Amount), nameof(AlertSubscriptionPurchaseInitiatedEvent.Amount))]
    [MapProperty(nameof(AlertSubscriptionPurchaseOrderCreatedDomainEvent.Price.Currency), nameof(AlertSubscriptionPurchaseInitiatedEvent.Currency))]
    public static partial AlertSubscriptionPurchaseInitiatedEvent ToPurchaseInitiatedEvent(
        this AlertSubscriptionPurchaseOrderCreatedDomainEvent source);

    [UserMapping]
    private static DateTime DateTimeOffsetToDateTime(DateTimeOffset t) => t.UtcDateTime;

    [UserMapping]
    private static AvroDecimal DecimalToAvroDecimal(decimal value) => value.ToAvroDecimal(4);

    [UserMapping]
    private static string CurrencyCodeToString(CurrencyCode code) => code.ToString().ToUpperInvariant();

    [UserMapping]
    private static SubscriptionTier MapTier(AlertSubscriptionTier tier) => tier switch
    {
        AlertSubscriptionTier.Pro => SubscriptionTier.Pro,
        AlertSubscriptionTier.Ultra => SubscriptionTier.Ultra,
        _ => throw new InvalidOperationException(
            $"Cannot map tier '{tier}' to Avro SubscriptionTier.")
    };
}
