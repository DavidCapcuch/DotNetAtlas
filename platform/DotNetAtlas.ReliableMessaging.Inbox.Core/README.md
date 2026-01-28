# DotNetAtlas.ReliableMessaging.Inbox.Core

Core entity for the [Idempotent Consumer pattern](https://microservices.io/patterns/communication-style/idempotent-consumer.html).

## The Problem

Messages can be delivered more than once due to:

- Consumer failures before offset commit
- Network timeouts and retries
- Kafka rebalancing

Without deduplication, you get duplicate orders, double charges, or repeated notifications.

## The Solution

Track processed message IDs in the database within the same transaction as your business logic. Before processing, check if the message was already handled. Skip duplicates, guaranteeing exactly-once processing semantics.

## Contents

- `InboxMessage` - Entity representing a processed message for deduplication

## InboxMessage

```csharp
public class InboxMessage
{
    public required Guid MessageId { get; set; }           // PK - unique message identifier
    public required DateTimeOffset ProcessedAtUtc { get; set; }  // When processed
}
```

Each service maintains its own inbox table. The `MessageId` is sufficient for deduplication within a service boundary.

## Related Packages

- [DotNetAtlas.ReliableMessaging.Inbox.EFCore](../DotNetAtlas.ReliableMessaging.Inbox.EFCore) - EF Core integration
