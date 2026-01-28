<div align="center">

# 📥 Inbox Pattern

</div>

| ⚡ TL;DR |
| -------- |
| The Inbox pattern ensures idempotent message processing by tracking which messages have been handled. Before processing, check if the message ID exists in the inbox table. If yes, skip. If no, process and record. This handles duplicates from at-least-once delivery. |

The Outbox pattern provides **at-least-once** delivery, which means consumers may receive the same message multiple times. The Inbox pattern handles this by making message processing idempotent.

## 🎯 The Problem

With at-least-once delivery, duplicates happen:

```csharp
// ❌ Without inbox - processes duplicates
public async Task HandleAsync(FeedbackCreatedEvent @event)
{
    // If this message is delivered twice, we create duplicate records
    await _dbContext.FeedbackAnalytics.AddAsync(new FeedbackAnalytics
    {
        FeedbackId = @event.FeedbackId,
        ProcessedAt = DateTimeOffset.UtcNow
    });
    await _dbContext.SaveChangesAsync();
}
```

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Kafka Consumer                            │
│  Message arrives with ID in header                           │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                   InboxMiddleware                            │
│  1. Extract message ID from headers                          │
│  2. Check if ID exists in InboxMessages table                │
│  3. If exists → skip (already processed)                     │
│  4. If not → continue to handler                             │
└────────────────────────────┬────────────────────────────────┘
                             │ (only if new)
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                   Message Handler                            │
│  Process the message (business logic)                        │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                   InboxMiddleware                            │
│  Record message ID in InboxMessages table                    │
└─────────────────────────────────────────────────────────────┘
```

## 📦 Components

### InboxMessage Entity

```csharp
public class InboxMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string ConsumerGroup { get; set; } = string.Empty;
    public DateTimeOffset ProcessedUtc { get; set; }
}
```

The composite key is `(MessageId, ConsumerGroup)` because different consumer groups process messages independently.

### InboxMiddleware

KafkaFlow middleware that checks/records message processing:

```csharp
public class InboxMiddleware : IMessageMiddleware
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InboxMiddleware> _logger;
    
    public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        // 1. Extract message ID from headers
        var messageId = context.Headers.GetString("message-id");
        if (string.IsNullOrEmpty(messageId))
        {
            // No message ID - can't deduplicate, process anyway
            _logger.LogWarning("Message without ID, cannot deduplicate");
            await next(context);
            return;
        }
        
        var consumerGroup = context.ConsumerContext.ConsumerName;
        
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InboxDbContext>();
        
        // 2. Check if already processed
        var exists = await dbContext.InboxMessages
            .AnyAsync(m => m.MessageId == messageId && m.ConsumerGroup == consumerGroup);
        
        if (exists)
        {
            _logger.LogInformation(
                "Duplicate message {MessageId} for {ConsumerGroup}, skipping",
                messageId, consumerGroup);
            return;
        }
        
        // 3. Process the message
        await next(context);
        
        // 4. Record as processed
        dbContext.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            ConsumerGroup = consumerGroup,
            ProcessedUtc = DateTimeOffset.UtcNow
        });
        
        await dbContext.SaveChangesAsync();
    }
}
```

### Producer: Adding Message ID

The producer must add a unique message ID header:

```csharp
public class OutboxMessageRelay
{
    public async Task PublishAsync(OutboxMessage message, CancellationToken ct)
    {
        var headers = new Headers
        {
            { "message-id", Encoding.UTF8.GetBytes(message.Id.ToString()) },
            { "trace-id", Encoding.UTF8.GetBytes(message.TraceId ?? "") },
            { "span-id", Encoding.UTF8.GetBytes(message.SpanId ?? "") }
        };
        
        await _producer.ProduceAsync(
            message.Topic!,
            message.Key,
            message.Payload,
            headers,
            ct);
    }
}
```

## 🔧 Registration

Add the middleware to your KafkaFlow consumer pipeline:

```csharp
services.AddKafka(kafka => kafka
    .AddCluster(cluster => cluster
        .WithBrokers(new[] { "localhost:9092" })
        .AddConsumer(consumer => consumer
            .Topic("feedback-events")
            .WithGroupId("feedback-analytics")
            .WithBufferSize(100)
            .WithWorkersCount(3)
            .AddMiddlewares(middlewares => middlewares
                .Add<InboxMiddleware>()  // First - deduplication
                .Add<TracingMiddleware>()
                .AddDeserializer<AvroDeserializer>()
                .AddTypedHandlers(handlers => handlers
                    .AddHandler<FeedbackCreatedEventHandler>())))));
```

## ⚠️ Important Considerations

### Transaction Boundaries

The inbox check and record happen in separate transactions from the handler. This means:

1. **Check** - Read from inbox table
2. **Process** - Handler executes (may have its own transaction)
3. **Record** - Write to inbox table

If the process crashes between steps 2 and 3, the message will be reprocessed. Your handlers should be **idempotent** even with the inbox pattern.

### Inbox Table Cleanup

The inbox table grows over time. Implement cleanup for old records:

```csharp
public class InboxCleanupJob
{
    public async Task CleanupAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
        
        await _dbContext.InboxMessages
            .Where(m => m.ProcessedUtc < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
```

### Performance

For high-throughput scenarios, consider:

- **Caching**: Cache recent message IDs in Redis
- **Batching**: Check multiple IDs in one query
- **Partitioning**: Partition inbox table by date

## 🎯 When to Use

| Scenario | Use Inbox? |
|----------|------------|
| Creating records | Yes - prevents duplicates |
| Sending emails | Yes - prevents duplicate sends |
| Updating counters | Maybe - if not naturally idempotent |
| Logging/analytics | No - duplicates often acceptable |
| Idempotent operations | No - already safe |

## 📖 Further Reading

- [**Outbox Pattern**](OutboxPattern.md) - The producer side
- [**Kafka Middleware**](KafkaMiddleware.md) - Full middleware pipeline
- [**Event-Driven Architecture**](../architecture/EventDriven.md) - Overall patterns

