# DotNetAtlas.ReliableMessaging.Inbox.EFCore

EF Core implementation of the [Idempotent Consumer pattern](https://microservices.io/patterns/communication-style/idempotent-consumer.html).

## The Problem

Messages can be delivered more than once due to:

- Consumer failures before offset commit
- Network timeouts and retries
- Kafka rebalancing

Without deduplication, you get duplicate orders, double charges, or repeated notifications.

## The Solution

Track processed message IDs in the database within the same transaction as your business logic. Before processing, check if the message was already handled. Skip duplicates, guaranteeing exactly-once processing semantics.

## Quick Start

### Installation

Add a project reference:

```xml
<ProjectReference Include="..\DotNetAtlas.ReliableMessaging.Inbox.EFCore\DotNetAtlas.ReliableMessaging.Inbox.EFCore.csproj" />
```

## Features

- 🗄️ **EF Core Integration** - Seamless integration with Entity Framework Core
- ⚙️ **Configurable Schema** - Customize table and schema names
- 🔒 **Startup Validation** - Validates DbContext configuration at startup
- 📊 **Inbox Message Entity configuration** - Complete EF core table config, including index for fast lookups

### Configure DbContext

**Important:** The DbContext must be configured with `UseExceptionProcessor()` from [EntityFramework.Exceptions](https://github.com/Giorgi/EntityFramework.Exceptions) to handle concurrent duplicate inserts gracefully.

```csharp
using DotNetAtlas.ReliableMessaging.Inbox.Core;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore;
using DotNetAtlas.ReliableMessaging.Inbox.EFCore.Common;

public class AppDbContext : DbContext, IInboxDbContext
{
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Use the ConfigureInbox extension method (recommended)
        modelBuilder.ConfigureInbox(schemaName: "app", tableName: "InboxMessages");

        // Or apply configuration directly
        // modelBuilder.ApplyConfiguration(
        //     new InboxMessageConfiguration("app", "InboxMessages"));
    }
}
```

### Register Services

```csharp
using DotNetAtlas.ReliableMessaging.Inbox.EFCore;

// Configure DbContext with UseExceptionProcessor (required!)
services.AddDbContextPool<AppDbContext>(options => options
    .UseSqlServer(connectionString)
    .UseExceptionProcessor());  // From EntityFramework.Exceptions

// Register inbox services
services.AddInbox<AppDbContext>();
```

**Note:** The `AddInbox<TContext>` method registers:

- `TimeProvider` as singleton
- `IInboxDbContext` as scoped (resolves to your DbContext)
- A hosted service that validates `UseExceptionProcessor()` configuration at startup

## API Reference

### Dependency Injection

| Method | Description |
|--------|-------------|
| `AddInbox<TContext>()` | Register inbox services for the specified DbContext |

### ModelBuilder Extension Methods

| Method | Description |
|--------|-------------|
| `ConfigureInbox(schemaName?, tableName)` | Configure inbox entity with schema/table names |

### Entity Configuration

| Parameter | Description |
|-----------|-------------|
| `schemaName` | Database schema name (default: model's default schema or "dbo") |
| `tableName` | Table name for inbox messages (default: "InboxMessages") |

## Usage with KafkaFlow

This package provides the infrastructure for inbox pattern. For automatic inbox integration with KafkaFlow consumers, use [DotNetAtlas.KafkaFlow.Inbox.EFCore](../DotNetAtlas.KafkaFlow.Inbox.EFCore):

```csharp
using DotNetAtlas.KafkaFlow.Inbox.EFCore;

services.AddKafka(kafka => kafka
    .AddCluster(cluster => cluster
        .AddConsumer(consumer => consumer
            .Topic("orders.events")
            .AddMiddlewares(middlewares => middlewares
                .RetryForever(...) // Add BEFORE inbox to handle transient faults (e.g. DB network problems)
                // Add inbox middleware for specific message types
                .AddInbox(typeof(OrderCreatedEvent), typeof(OrderUpdatedEvent))
                .AddTypedHandlers(...)))));
```

The KafkaFlow middleware wraps message processing and inbox recording in a single database transaction, ensuring atomicity - either both succeed or both roll back.

## Cleanup Strategy

Implement periodic cleanup of old inbox entries:

```csharp
public class InboxCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromDays(7);
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<IInboxDbContext>();

            var cutoff = DateTimeOffset.UtcNow - _retentionPeriod;
            await dbContext.InboxMessages
                .Where(x => x.ProcessedAtUtc < cutoff)
                .ExecuteDeleteAsync(stoppingToken);

            await Task.Delay(_cleanupInterval, stoppingToken);
        }
    }
}
```

## Related Packages

- [DotNetAtlas.ReliableMessaging.Inbox.Core](../DotNetAtlas.ReliableMessaging.Inbox.Core) - Core inbox abstractions
- [DotNetAtlas.KafkaFlow.Inbox.EFCore](../DotNetAtlas.KafkaFlow.Inbox.EFCore) - KafkaFlow middleware
- [DotNetAtlas.Messaging.Abstractions](../DotNetAtlas.Messaging.Abstractions) - Message header constants

## License

This project is licensed under the MIT License.
