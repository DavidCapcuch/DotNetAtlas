<div align="center">

# 📤 Outbox Pattern

</div>

| ⚡ TL;DR |
| -------- |
| The Outbox pattern guarantees event delivery by saving events to a database table in the same transaction as business data. A separate worker polls the table and publishes to Kafka. This ensures atomicity: either both the data and event are saved, or neither is. |

The Outbox pattern solves the dual-write problem: how do you reliably update a database AND publish an event when either operation could fail?

## 🎯 The Problem

```csharp
// ❌ Dual-write problem
await _dbContext.SaveChangesAsync();  // Success
await _kafka.ProduceAsync(event);      // Fails - event lost!
```

If Kafka fails after the database commit, you have inconsistent state. The Outbox pattern eliminates this by making event publication part of the database transaction.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Service                       │
│  1. Create/modify aggregate                                  │
│  2. Aggregate raises domain event                            │
│  3. SaveChangesAsync()                                       │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│              DispatchDomainEventsInterceptor                 │
│  1. Collect domain events from aggregates                    │
│  2. Convert to OutboxMessage entities                        │
│  3. Add to DbContext                                         │
│  4. Single transaction commits both                          │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                      SQL Server                              │
│  ┌─────────────────┐    ┌─────────────────────────────────┐ │
│  │   Feedback      │    │      OutboxMessages             │ │
│  │   (business)    │    │      (events to publish)        │ │
│  └─────────────────┘    └─────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                             │
                             │ Polling (every N seconds)
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                  OutboxRelay Worker Service                  │
│  1. Query unprocessed messages                               │
│  2. Restore trace context                                    │
│  3. Publish to Kafka                                         │
│  4. Mark as processed                                        │
└─────────────────────────────────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                         Kafka                                │
│  Events available for consumers                              │
└─────────────────────────────────────────────────────────────┘
```

## 📦 Components

### OutboxMessage Entity

```csharp
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public string? Key { get; set; }
    
    // OpenTelemetry context for trace continuity
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ProcessedUtc { get; set; }
    
    // Retry tracking
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
}
```

### DispatchDomainEventsInterceptor

This EF Core interceptor captures domain events during `SaveChanges`:

```csharp
public class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken ct)
    {
        var context = eventData.Context;
        if (context is null) return result;
        
        // 1. Find all aggregates with domain events
        var aggregates = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Select(e => e.Entity)
            .Where(a => a.DomainEvents.Any())
            .ToList();
        
        // 2. Collect and clear domain events
        var domainEvents = aggregates
            .SelectMany(a => a.PopDomainEvents())
            .ToList();
        
        // 3. Convert to outbox messages
        foreach (var domainEvent in domainEvents)
        {
            var outboxMessage = new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                EventType = domainEvent.GetType().AssemblyQualifiedName!,
                Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                Topic = GetTopicForEvent(domainEvent),
                TraceId = Activity.Current?.TraceId.ToString(),
                SpanId = Activity.Current?.SpanId.ToString(),
                CreatedUtc = DateTimeOffset.UtcNow
            };
            
            context.Set<OutboxMessage>().Add(outboxMessage);
        }
        
        // 4. Continue with SaveChanges (single transaction)
        return await base.SavingChangesAsync(eventData, result, ct);
    }
}
```

### OutboxMessageRelay

The worker service that publishes messages to Kafka:

```csharp
public class OutboxMessageRelay
{
    private readonly OutboxDbContext _dbContext;
    private readonly IKafkaProducer _kafkaProducer;
    private readonly OutboxRelayOptions _options;
    
    public async Task ProcessBatchAsync(CancellationToken ct)
    {
        // 1. Get unprocessed messages
        var messages = await _dbContext.OutboxMessages
            .Where(m => m.ProcessedUtc == null)
            .Where(m => m.RetryCount < _options.MaxRetries)
            .OrderBy(m => m.CreatedUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);
        
        foreach (var message in messages)
        {
            try
            {
                // 2. Restore trace context
                using var activity = RestoreTraceContext(message);
                
                // 3. Publish to Kafka
                await _kafkaProducer.ProduceAsync(
                    message.Topic!,
                    message.Key,
                    message.Payload,
                    ct);
                
                // 4. Mark as processed
                message.ProcessedUtc = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.LastError = ex.Message;
            }
        }
        
        await _dbContext.SaveChangesAsync(ct);
    }
    
    private Activity? RestoreTraceContext(OutboxMessage message)
    {
        if (string.IsNullOrEmpty(message.TraceId))
            return null;
        
        var parentContext = new ActivityContext(
            ActivityTraceId.CreateFromString(message.TraceId),
            ActivitySpanId.CreateFromString(message.SpanId ?? ""),
            ActivityTraceFlags.Recorded);
        
        return ActivitySource.StartActivity(
            "OutboxRelay.Publish",
            ActivityKind.Producer,
            parentContext);
    }
}
```

### OutboxRelayWorker

The hosted service that runs the relay:

```csharp
public class OutboxRelayWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var relay = scope.ServiceProvider.GetRequiredService<OutboxMessageRelay>();
                
                await relay.ProcessBatchAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }
            
            await Task.Delay(_options.PollingInterval, ct);
        }
    }
}
```

## 🔭 Trace Continuity

The outbox preserves OpenTelemetry context, so traces span the async boundary:

```
[HTTP Request]  ────────────────────────────────────────► 50ms
    └── [SaveChanges]  ─────────────────────────────────► 30ms
        └── [Outbox Write]  ────────────────────────────► 5ms
                                    ... later ...
[OutboxRelay.Publish]  ─────────────────────────────────► 15ms
    └── [Kafka Produce]  ───────────────────────────────► 10ms
```

In Jaeger, these appear as a single trace with a gap representing the async delay.

## ⚙️ Configuration

```json
{
  "OutboxRelay": {
    "PollingIntervalSeconds": 5,
    "BatchSize": 100,
    "MaxRetries": 3
  }
}
```

## 🎯 Guarantees

| Guarantee | Description |
|-----------|-------------|
| **At-least-once delivery** | Messages are published at least once (may duplicate on retry) |
| **Ordering** | Messages are processed in `CreatedUtc` order |
| **Atomicity** | Business data and outbox message commit together |
| **Durability** | Messages survive process crashes (in database) |

## 📖 Further Reading

- [**Event-Driven Architecture**](../architecture/EventDriven.md) - When to use outbox vs fire-and-forget
- [**Inbox Pattern**](InboxPattern.md) - Handling duplicates on the consumer side
- [**Step By Step**](../getting-started/StepByStep.md) - See outbox in action

