# Platform.KafkaFlow.DeadLetter

KafkaFlow middleware that routes messages with unhandled exceptions to a Dead Letter Topic (DLT).

## The Problem

Some messages will fail to process due to:

- Invalid message format or corrupted data
- Deserialization errors
- Infrastructure failures that exhaust retries

Without a dead letter strategy, these messages either block the queue or are lost.

## The Solution

This middleware catches unhandled exceptions and routes the message to a DLT topic (e.g., `orders.events.DLT`) with exception metadata. The original message offset is committed so processing continues.

## Quick Start

```csharp
services.AddKafka(kafka => kafka
    .AddCluster(cluster => cluster
        .AddConsumer(consumer => consumer
            .Topic("orders.events")
            .AddMiddlewares(middlewares => middlewares
                .AddDeadLetter(topicSuffix: ".DLT")  // <- catches exceptions, routes to DLT
                .AddTypedHandlers(h => h.AddHandler<OrderEventHandler>())))));
```

## Headers Added to DLT Messages

| Header | Description |
|--------|-------------|
| `DLT-Original-Topic` | Source topic name |
| `DLT-Original-Partition` | Source partition number |
| `DLT-Original-Offset` | Source message offset |
| `DLT-Exception-Type` | Exception type full name |
| `DLT-Exception-Message` | Exception message |
| `DLT-Exception-StackTrace` | Full stack trace |

Original message headers are also preserved.

## Related Packages

- [Platform.KafkaFlow.Inbox.EFCore](../Platform.KafkaFlow.Inbox.EFCore) - Idempotent message consumption
