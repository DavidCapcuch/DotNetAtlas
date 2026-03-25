using Avro;
using Order.AlertSubscriptions;
using Ordering.Domain;
using Ordering.Domain.AlertSubscriptionOrders.Events;
using Platform.SchemaRegistry.Contracts.Avro.AvroExtensions;
using Riok.Mapperly.Abstractions;

namespace Ordering.Application.AlertSubscriptions.ExtendAlertSubscription;

/// <summary>
/// Mapper for converting <see cref="AlertSubscriptionExtensionOrderCreatedDomainEvent"/>
/// to the <see cref="AlertSubscriptionExtensionInitiatedEvent"/> Avro integration event.
/// </summary>
[Mapper]
public static partial class ExtensionOrderCreatedMapper
{
    [MapperRequiredMapping(RequiredMappingStrategy.Target)]
    [MapProperty(nameof(AlertSubscriptionExtensionOrderCreatedDomainEvent.AlertSubscriptionOrderId), nameof(AlertSubscriptionExtensionInitiatedEvent.AlertSubscriptionOrderId))]
    [MapProperty(nameof(AlertSubscriptionExtensionOrderCreatedDomainEvent.OccurredOnUtc), nameof(AlertSubscriptionExtensionInitiatedEvent.InitiatedAtUtc))]
    [MapProperty(nameof(AlertSubscriptionExtensionOrderCreatedDomainEvent.Price.Amount), nameof(AlertSubscriptionExtensionInitiatedEvent.Amount))]
    [MapProperty(nameof(AlertSubscriptionExtensionOrderCreatedDomainEvent.Price.Currency), nameof(AlertSubscriptionExtensionInitiatedEvent.Currency))]
    public static partial AlertSubscriptionExtensionInitiatedEvent ToExtensionInitiatedEvent(
        this AlertSubscriptionExtensionOrderCreatedDomainEvent source);

    [UserMapping]
    private static DateTime DateTimeOffsetToDateTime(DateTimeOffset t) => t.UtcDateTime;

    [UserMapping]
    private static AvroDecimal DecimalToAvroDecimal(decimal value) => value.ToAvroDecimal(4);

    [UserMapping]
    private static string CurrencyCodeToString(CurrencyCode code) => code.ToString().ToUpperInvariant();
}
