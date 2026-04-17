# Platform.ReliableMessaging.Outbox.Core

Core entity and header utilities for the [Transactional Outbox pattern](https://microservices.io/patterns/data/transactional-outbox.html).

## The Problem

When you need to save data AND publish a message, you face the dual-write problem:

- Publish first, DB fails -> message sent for uncommitted transaction
- Save first, publish fails -> message is lost forever

## The Solution

Store messages in the same database transaction as your business data. A background relay reads and publishes them to the message broker, guaranteeing at-least-once delivery.

## Contents

- `OutboxMessage` - Entity representing a message queued for reliable delivery
- `OutboxMessageHeaderExtensions` - Utilities for OpenTelemetry header serialization

## OutboxMessage

```csharp
public class OutboxMessage
{
    public long Id { get; set; }                    // Auto-increment PK for ordering
    public string TopicName { get; set; }           // Topic name to route to
    public string? KafkaKey { get; set; }           // Kafka key for partition routing (typically aggregate ID)
    public required byte[] AvroPayload { get; set; } // Serialized message
    public required string Type { get; set; }       // Avro type name
    public string? Headers { get; set; }            // JSON-serialized W3C trace context
    public required DateTimeOffset CreatedUtc { get; set; }
}
```

## Header Extensions

Build headers from current `Activity` for distributed tracing:

```csharp
Dictionary<string, string>? headers = OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity(Activity.Current);
message.Headers = OutboxMessageHeaderExtensions.SerializeHeaders(headers);
```

Deserialize headers when processing:

```csharp
Dictionary<string, string>? headers = message.DeserializeHeaders();
```

Headers use W3C Trace Context format (`traceparent`, `tracestate`, `baggage`).

## Related Packages

- [Platform.ReliableMessaging.Outbox.EFCore](../Platform.ReliableMessaging.Outbox.EFCore) - EF Core integration and `AddOutboxMessage` extension
