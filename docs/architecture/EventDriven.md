<div align="center">

# 📡 Event-Driven Architecture

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas demonstrates two event publishing patterns: **Fire-and-Forget** (direct Kafka publish, acceptable loss) and **Transactional Outbox** (guaranteed delivery via database). Choose based on whether you can afford to lose events. |

Event-driven architecture decouples producers from consumers. Instead of direct calls, services communicate through events. DotNetAtlas shows both patterns you'll encounter in production systems.

## 🎯 The Problem

You want to publish an event to Kafka when something happens in your domain. Sounds simple, but there's a fundamental problem:

```csharp
// ❌ This is broken
public async Task HandleAsync(SendFeedbackCommand command, CancellationToken ct)
{
    var feedback = Feedback.Create(...);
    await _dbContext.SaveChangesAsync(ct);  // 1. Save to DB
    await _kafka.ProduceAsync(event);        // 2. Publish to Kafka
}
```

**What if Kafka is down after the database commit?**
- Feedback is saved ✅
- Event is lost ❌
- Consumers never know about the feedback

**What if the process crashes between steps 1 and 2?**
- Same problem - data inconsistency

## 🔥 Pattern 1: Fire-and-Forget

For events where occasional loss is acceptable (analytics, metrics, non-critical notifications):

```csharp
public class ForecastEventsKafkaProducer : IForecastEventsProducer
{
    private readonly IMessageProducer<ForecastRequestedEvent> _producer;
    
    public async Task PublishForecastRequestedAsync(
        string city, 
        string countryCode, 
        CancellationToken ct)
    {
        var @event = new ForecastRequestedEvent
        {
            City = city,
            CountryCode = countryCode,
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        
        // Direct publish - if Kafka is down, event is lost
        await _producer.ProduceAsync(@event, ct);
    }
}
```

**When to use:**
- Analytics events (page views, feature usage)
- Metrics and telemetry
- Non-critical notifications
- Events that can be reconstructed from source data

**Trade-offs:**
- ✅ Simple implementation
- ✅ Low latency
- ❌ Events can be lost
- ❌ No guaranteed delivery

## 📦 Pattern 2: Transactional Outbox

For events that **must** be delivered (order placed, payment processed, feedback created):

```
┌─────────────────────────────────────────────────────────────┐
│                    Same DB Transaction                       │
│  ┌─────────────────────┐    ┌─────────────────────────────┐ │
│  │   Save Feedback     │    │   Save Outbox Message       │ │
│  │   to Feedback table │    │   to OutboxMessages table   │ │
│  └─────────────────────┘    └─────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                              │
                              │ Later (async)
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Outbox Relay Worker                       │
│  1. Poll OutboxMessages where ProcessedUtc IS NULL          │
│  2. Publish to Kafka                                         │
│  3. Mark as processed                                        │
└─────────────────────────────────────────────────────────────┘
```

### How It Works

**Step 1: Domain event raised in aggregate**

```csharp
public static Result<Feedback> Create(FeedbackText text, FeedbackRating rating, Guid userId)
{
    var feedback = new Feedback { /* ... */ };
    
    feedback.AddDomainEvent(new FeedbackCreatedDomainEvent
    {
        FeedbackId = feedback.Id,
        Text = text.Text,
        Rating = rating.Value
    });
    
    return feedback;
}
```

**Step 2: Interceptor converts to outbox message**

```csharp
public class DispatchDomainEventsInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(...)
    {
        var domainEvents = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .SelectMany(e => e.Entity.PopDomainEvents())
            .ToList();
        
        foreach (var domainEvent in domainEvents)
        {
            var outboxMessage = new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                EventType = domainEvent.GetType().AssemblyQualifiedName,
                Payload = JsonSerializer.Serialize(domainEvent),
                TraceId = Activity.Current?.TraceId.ToString(),
                SpanId = Activity.Current?.SpanId.ToString(),
                CreatedUtc = DateTimeOffset.UtcNow
            };
            
            context.Set<OutboxMessage>().Add(outboxMessage);
        }
        
        return await base.SavingChangesAsync(...);
    }
}
```

**Step 3: Worker publishes to Kafka**

```csharp
public class OutboxMessageRelay
{
    public async Task ProcessAsync(CancellationToken ct)
    {
        var messages = await _dbContext.OutboxMessages
            .Where(m => m.ProcessedUtc == null)
            .OrderBy(m => m.CreatedUtc)
            .Take(_options.BatchSize)
            .ToListAsync(ct);
        
        foreach (var message in messages)
        {
            // Restore trace context for observability
            using var activity = RestoreTraceContext(message);
            
            await _kafkaProducer.ProduceAsync(
                message.Topic, 
                message.Key, 
                message.Payload);
            
            message.ProcessedUtc = DateTimeOffset.UtcNow;
        }
        
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

**When to use:**
- Business-critical events (orders, payments, registrations)
- Events that trigger downstream workflows
- Events required for data consistency across services
- Audit events that must not be lost

**Trade-offs:**
- ✅ Guaranteed delivery (at-least-once)
- ✅ Atomic with business operation
- ✅ Trace context preserved
- ❌ Higher latency (async publication)
- ❌ More complex implementation
- ❌ Requires polling/worker service

Read more: [**Outbox Pattern**](../platform/OutboxPattern.md)

## 📥 Consumer Side: Inbox Pattern

The outbox guarantees **at-least-once** delivery. This means consumers might receive duplicates. The **Inbox pattern** handles this:

```csharp
public class InboxMiddleware : IMessageMiddleware
{
    public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        var messageId = context.Headers.GetString("message-id");
        
        // Check if already processed
        if (await _inbox.ExistsAsync(messageId))
        {
            _logger.LogInformation("Duplicate message {MessageId}, skipping", messageId);
            return;
        }
        
        // Process message
        await next(context);
        
        // Mark as processed
        await _inbox.MarkProcessedAsync(messageId);
    }
}
```

Read more: [**Inbox Pattern**](../platform/InboxPattern.md)

## 🔭 Observability Across Boundaries

The outbox preserves OpenTelemetry trace context:

```csharp
// When saving to outbox
var outboxMessage = new OutboxMessage
{
    TraceId = Activity.Current?.TraceId.ToString(),
    SpanId = Activity.Current?.SpanId.ToString(),
    // ...
};

// When publishing from worker
using var activity = ActivitySource.StartActivity(
    "OutboxRelay.Publish",
    ActivityKind.Producer,
    new ActivityContext(
        ActivityTraceId.CreateFromString(message.TraceId),
        ActivitySpanId.CreateFromString(message.SpanId),
        ActivityTraceFlags.Recorded));
```

In Jaeger, you see a continuous trace from HTTP request → Database → Outbox → Kafka → Consumer.

## 🎯 Choosing the Right Pattern

| Scenario | Pattern | Reason |
|----------|---------|--------|
| Analytics event | Fire-and-Forget | Loss acceptable |
| Feature flag check | Fire-and-Forget | Can retry |
| Order placed | Outbox | Must not lose |
| Payment processed | Outbox | Financial data |
| User feedback | Outbox | Business data |
| Health check ping | Fire-and-Forget | Ephemeral |

## 📖 Further Reading

- [**Outbox Pattern**](../platform/OutboxPattern.md) - Implementation details
- [**Inbox Pattern**](../platform/InboxPattern.md) - Idempotent consumption
- [**Kafka Middleware**](../platform/KafkaMiddleware.md) - KafkaFlow pipeline

