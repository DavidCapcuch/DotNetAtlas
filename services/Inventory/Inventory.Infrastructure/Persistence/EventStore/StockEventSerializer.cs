using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Domain.StockItems.Events;
using Platform.SharedKernel.Base.DomainEvents;
using Platform.SharedKernel.Exceptions;

namespace Inventory.Infrastructure.Persistence.EventStore;

/// <summary>
/// Maps the six internal ES events to/from a <c>(EventType, Payload)</c> pair
/// for storage in <c>inventory.stock_events</c>. The <c>EventType</c> column
/// drives rehydration dispatch via an explicit registry — safer than
/// <c>Type.GetType(typeName)</c>, which silently returns <c>null</c> on rename.
/// </summary>
internal static class StockEventSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Enums are serialized by NAME, not numeric value, because the
        // payload is durable ES state: reordering or removing an enum member
        // must never silently change the meaning of historical rows on
        // replay. E.g. ReleaseReason.Expiry persists as "Expiry", not "1".
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Dictionary<string, Type> EventTypeRegistry =
        new(StringComparer.Ordinal)
        {
            [nameof(StockItemInitializedDomainEvent)] = typeof(StockItemInitializedDomainEvent),
            [nameof(StockReceivedDomainEvent)] = typeof(StockReceivedDomainEvent),
            [nameof(StockReservedDomainEvent)] = typeof(StockReservedDomainEvent),
            [nameof(ReservationConfirmedDomainEvent)] = typeof(ReservationConfirmedDomainEvent),
            [nameof(ReservationReleasedDomainEvent)] = typeof(ReservationReleasedDomainEvent),
            [nameof(StockAdjustedDomainEvent)] = typeof(StockAdjustedDomainEvent),
        };

    /// <summary>
    /// Serializes a domain event into its <c>(EventType, Payload)</c> pair.
    /// The event type name is the CLR type name (e.g. <c>"StockReservedDomainEvent"</c>);
    /// the payload is a JSON document stored in the <c>jsonb</c> column.
    /// </summary>
    internal static (string EventType, string Payload) Serialize(DomainEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = @event.GetType().Name;

        if (!EventTypeRegistry.ContainsKey(eventType))
        {
            throw new DataIntegrityException(
                "Inventory.UnknownEventType",
                $"Cannot serialize unregistered event type '{@event.GetType().FullName}'. "
                + "Add it to StockEventSerializer.EventTypeRegistry.");
        }

        var payload = JsonSerializer.Serialize(@event, @event.GetType(), JsonOptions);
        return (eventType, payload);
    }

    /// <summary>
    /// Rehydrates a domain event from a stored row. Unknown <paramref name="eventType"/>
    /// names or malformed payloads throw — they indicate either a missing registry
    /// entry after a rename or a corrupted stream; either way the write-side is
    /// inconsistent and must fail loudly.
    /// </summary>
    internal static DomainEvent Deserialize(string eventType, string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        if (!EventTypeRegistry.TryGetValue(eventType, out var clrType))
        {
            throw new DataIntegrityException(
                "Inventory.UnknownEventType",
                $"Cannot deserialize event type '{eventType}' — not in StockEventSerializer.EventTypeRegistry.");
        }

        if (JsonSerializer.Deserialize(payload, clrType, JsonOptions) is not DomainEvent @event)
        {
            throw new DataIntegrityException(
                "Inventory.EventDeserializationFailed",
                $"Payload for '{eventType}' deserialized to null. Stream is corrupted.");
        }

        return @event;
    }
}
