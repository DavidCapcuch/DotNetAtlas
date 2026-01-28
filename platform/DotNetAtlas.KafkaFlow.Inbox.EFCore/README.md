# DotNetAtlas.KafkaFlow.Inbox.EFCore

KafkaFlow middleware for idempotent message processing using the [Inbox pattern](https://microservices.io/patterns/communication-style/idempotent-consumer.html).

## The Problem

Messages can be delivered more than once due to:

- Consumer failures before offset commit
- Network timeouts and retries
- Kafka rebalancing

Without deduplication, you get duplicate orders, double charges, or repeated notifications.

## The Solution

Track processed message IDs in the database within the same transaction as your business logic. Before processing, check if the message was already handled. Skip duplicates, guaranteeing exactly-once processing semantics.

## Quick Start

### 1. Configure DbContext

```csharp
public class AppDbContext : DbContext, IInboxDbContext
{
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureInbox(schemaName: "app", tableName: "InboxMessages");
    }
}
```

### 2. Register Services

```csharp
services.AddDbContextPool<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseExceptionProcessor());  // Required for concurrent duplicate handling

services.AddInbox<AppDbContext>();
```

### 3. Add Middleware

```csharp
.AddMiddlewares(middlewares => middlewares
    .AddInbox(typeof(OrderCreatedEvent))  // <- skips duplicates
    .AddTypedHandlers(h => h.AddHandler<OrderEventHandler>()))
```

## How It Works

```
┌─────────────────────────────────────────────────────────────┐
│                    Kafka Message Received                    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    InboxMiddleware                           │
│                                                              │
│  1. Extract message.id from headers                         │
│  2. Check if message.id exists in InboxMessages table       │
│  3. If exists → Skip (already processed)                    │
│  4. If not exists → Continue to next middleware             │
│  5. After successful processing → Add to InboxMessages      │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Your Message Handler                      │
│                                                              │
│  Process the message (only runs once per message.id)        │
└─────────────────────────────────────────────────────────────┘
```

## Requirements

- Messages must have a `message.id` header (use [ProducerHeaders](../DotNetAtlas.KafkaFlow.ProducerHeaders) or OutboxRelay)
- DbContext must use `UseExceptionProcessor()` for concurrent duplicate handling

## Related Packages

- [DotNetAtlas.Messaging.Abstractions](../DotNetAtlas.Messaging.Abstractions) - Header key constants
- [DotNetAtlas.KafkaFlow.ProducerHeaders](../DotNetAtlas.KafkaFlow.ProducerHeaders) - Adds `message.id` header to produced messages
