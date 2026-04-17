# Platform.ReliableMessaging.Outbox.EFCore

EF Core implementation of the [Transactional Outbox pattern](https://microservices.io/patterns/data/transactional-outbox.html).

## The Problem

When you need to save data AND publish a message, you face the dual-write problem:

- Publish first, DB fails -> message sent for uncommitted transaction
- Save first, publish fails -> message is lost forever

## The Solution

Store messages in the same database transaction as your business data. A background relay reads and publishes them to the message broker, guaranteeing at-least-once delivery.

## Quick Start

### Installation

Add a project reference:

```xml
<ProjectReference Include="..\Platform.ReliableMessaging.Outbox.EFCore\Platform.ReliableMessaging.Outbox.EFCore.csproj" />
```

## Features

- 🗄️ **EF Core Integration** - Seamless integration with Entity Framework Core
- 📦 **Avro Serialization** - Efficient binary serialization with Schema Registry
- 🏷️ **Auto Headers** - Automatic OpenTelemetry trace propagation and message metadata
- ⚙️ **Configurable Schema** - Customize table and schema names
- 💉 **Proper DI** - Constructor injection with `ITransactionalOutbox<TContext>`

### Configure DbContext

```csharp
using Platform.ReliableMessaging.Outbox.Core;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

public class AppDbContext : DbContext, IOutboxDbContext
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Use the ConfigureOutbox extension method (recommended)
        modelBuilder.ConfigureOutbox(schemaName: "app", tableName: "OutboxMessages");

        // Or apply configuration directly
        // modelBuilder.ApplyConfiguration(
        //     new OutboxMessageConfiguration("app", "OutboxMessages"));
    }
}
```

### Register Services

```csharp
using Platform.ReliableMessaging.Outbox.EFCore;

services.AddOutbox(outbox =>
{
    outbox.ConfigureMessageOrigin("MyService"); // optional
    outbox.ConfigureAvroSerializerConfig(config =>
    {
        // optional config
        // config.NormalizeSchemas = true;
    });
    // this is for internal avro serializer which uses CachedSchemaRegistryClient
    // with cached avro schemas for event serialization
    outbox.ConfigureSchemaRegistryConfig(config =>
    {
        config.Url = "http://localhost:8081";
    });
});
```

### Add Messages to Outbox

#### Option 1: Scoped DbContext (Recommended)

Inject `ITransactionalOutbox<TContext>` for request-scoped handlers. Both the writer and DbContext share the same instance within a scope.

```csharp
using Platform.ReliableMessaging.Outbox.EFCore;

public class OrderService
{
    private readonly IOrderDbContext _dbContext;
    private readonly ITransactionalOutbox<IOrderDbContext> _outbox;

    public OrderService(IOrderDbContext dbContext, ITransactionalOutbox<IOrderDbContext> outboxWriter)
    {
        _dbContext = dbContext;
        _outbox = outboxWriter;
    }

    public async Task CreateOrderAsync(CreateOrderCommand command, CancellationToken ct)
    {
        var order = new Order(command.CustomerId, command.Items);
        _dbContext.Orders.Add(order);

        // Add integration event to outbox with automatic Avro serialization
        // Headers (traceparent, message.id, origin) are auto-generated from Activity.Current
        _outbox.AddOutboxMessage(order.Id.ToString(), new OrderCreatedEvent(order.Id, order.Total));

        // Both saved atomically in same transaction
        await _outbox.SaveChangesAsync(ct);
    }
}
```

#### Option 2: DbContextFactory (Background Services)

Inject `IOutboxWriter` (non-generic) when using `IDbContextFactory<TContext>`:

```csharp
using Platform.ReliableMessaging.Outbox.EFCore;

public class OrderBackgroundService
{
    private readonly IDbContextFactory<OrderDbContext> _contextFactory;
    private readonly IOutboxWriter _outbox;

    public OrderBackgroundService(
        IDbContextFactory<OrderDbContext> contextFactory,
        IOutboxWriter outboxWriter)
    {
        _contextFactory = contextFactory;
        _outbox = outboxWriter;
    }

    public async Task ProcessPendingOrders(CancellationToken ct)
    {
        await using var dbContext = await _contextFactory.CreateDbContextAsync(ct);
        
        // ... process orders ...
        
        _outbox.AddOutboxMessage(dbContext, order.Id.ToString(), new OrderProcessedEvent(order.Id));
        await dbContext.SaveChangesAsync(ct);
    }
}
```

#### Option 3: Transactional Kafka Handlers

For Kafka message handlers that need explicit transaction control, use the `Database` property:

```csharp
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.ReliableMessaging.Outbox.EFCore.Common;

public class OrderEventKafkaHandler : IMessageHandler<OrderPlacedEvent>
{
    private readonly ITransactionalOutbox<IOrderDbContext> _outbox;

    public OrderEventKafkaHandler(ITransactionalOutbox<IOrderDbContext> outboxWriter)
    {
        _outbox = outboxWriter;
    }

    public async Task Handle(IMessageContext context, OrderPlacedEvent message)
    {
        var ct = context.ConsumerContext.WorkerStopped;

        // Wrap in transaction for atomicity
        await _outbox.Database.EnsureTransactionAsync(async () =>
        {
            // Process message and create response event...
            _outbox.AddOutboxMessage(message.OrderId.ToString(), responseEvent);
            await _outbox.SaveChangesAsync(ct);
        }, ct);
    }
}
```

> **Design Note:** The `ITransactionalOutbox<TContext>` interface exposes `Database` and `SaveChangesAsync` for convenience.
> While this is technically not "pure" interface segregation, it's a pragmatic choice because the outbox pattern
> is inherently transaction-driven. See [design decisions](../../docs/design-decisions/transactional-outbox-interface-design.md) for rationale.

**Note:** The `AddOutboxMessage` method automatically generates OpenTelemetry trace headers (`traceparent`, `tracestate`, `baggage`) from `Activity.Current`, plus `message.id` (GUID v7) and `origin` (from `ConfigureMessageOrigin`). Custom headers are not supported - use OpenTelemetry baggage for custom context propagation.

## API Reference

### Dependency Injection

| Method | Description |
| ------ | ----------- |
| `AddOutbox(configure)` | Register outbox services (serializer, schema registry, writers) |

### AddOutbox Configuration

| Method | Description |
| ------ | ----------- |
| `ConfigureMessageOrigin(origin)` | Set service origin identifier added to message headers |
| `ConfigureAvroSerializerConfig(config)` | Configure Avro serializer settings |
| `ConfigureSchemaRegistryConfig(config)` | Configure Schema Registry connection |

### Interfaces

| Interface | Lifetime | Description |
| --------- | -------- | ----------- |
| `IOutboxWriter` | Singleton | Core interface for DbContextFactory users |
| `ITransactionalOutbox<TContext>` | Scoped | Convenience interface for scoped handlers |

### IOutboxWriter Methods

| Method | Description |
| ------ | ----------- |
| `AddOutboxMessage(dbContext, kafkaKey, event)` | Add Avro message to specified DbContext's outbox |

### ITransactionalOutbox&lt;TContext&gt; Members

| Member | Description |
| ------ | ----------- |
| `AddOutboxMessage(kafkaKey, event)` | Add Avro message to injected DbContext's outbox |
| `SaveChangesAsync(ct)` | Save changes to the underlying DbContext |
| `Database` | DatabaseFacade for transaction management (e.g., `EnsureTransactionAsync`) |

### ModelBuilder Extension Methods

| Method | Description |
| ------ | ----------- |
| `ConfigureOutbox(schemaName?, tableName)` | Configure outbox entity with schema/table names |

### Entity Configuration

| Class | Description |
| ----- | ----------- |
| `OutboxMessageConfiguration(schemaName, tableName)` | EF Core entity configuration for OutboxMessage |

## How It Works

```text
┌─────────────────────────────────────────────────────────────┐
│                    Application Service                       │
│                                                              │
│  1. Create business entity                                   │
│  2. Call IOutboxWriter.AddOutboxMessage() - serializes Avro │
│  3. SaveChangesAsync() - atomic transaction                  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Outbox Relay Worker                       │
│                                                              │
│  1. Poll OutboxMessages table for new messages              │
│  2. Publish to Kafka with headers                           │
│  3. Delete successfully published messages                   │
└─────────────────────────────────────────────────────────────┘
```

## Related Packages

- [Platform.ReliableMessaging.Outbox.Core](../Platform.ReliableMessaging.Outbox.Core) - Core outbox abstractions
- [Platform.OutboxRelay.WorkerService](../Platform.OutboxRelay.WorkerService) - Background relay service
- [Platform.Messaging.Abstractions](../Platform.Messaging.Abstractions) - Message header constants

## License

This project is licensed under the MIT License.
