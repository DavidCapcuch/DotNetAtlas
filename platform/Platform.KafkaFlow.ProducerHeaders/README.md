# Platform.KafkaFlow.ProducerHeaders

KafkaFlow middleware that automatically adds `message.id` and `origin` headers to produced messages.

## The Problem

For idempotent message processing, every message needs a unique ID. Manually adding headers to every `ProduceAsync` call is:

- Error-prone (easy to forget)
- Repetitive (boilerplate in every producer)
- Inconsistent (different formats across services)

## The Solution

A middleware that automatically adds standard headers to all outgoing messages. Configure once, apply everywhere.

## Quick Start

```csharp
services.AddKafka(kafka => kafka
    .AddCluster(cluster => cluster
        .AddProducer("order-producer", producer => producer
            .DefaultTopic("orders.events")
            .AddMiddlewares(m => m
                .AddProducerHeaders("OrderService")))));  // <- adds headers automatically
```

## Headers Added

| Header | Value | Purpose                                                                          |
|--------|-------|----------------------------------------------------------------------------------|
| `message.id` | GUID v7 | Unique identifier for idempotent processing (only added if not present)          |
| `origin` | Configured value | Identifies producing service for debugging (optional, only added if not present) |

## Usage

```csharp
// Just produce - headers are added automatically
await _producer.ProduceAsync(order.Id.ToString(), new OrderCreatedEvent(order.Id));

// Resulting headers:
// message.id: 01945abc-def0-7123-4567-89abcdef0123
// origin: OrderService
```

## Related Packages

- [Platform.Messaging.Abstractions](../Platform.Messaging.Abstractions) - Header key constants
- [Platform.KafkaFlow.Inbox.EFCore](../Platform.KafkaFlow.Inbox.EFCore) - Reads `message.id` for deduplication
