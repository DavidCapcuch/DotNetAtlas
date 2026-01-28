<div align="center">

# 🔄 Kafka Middleware

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas uses KafkaFlow for Kafka integration with a middleware pipeline: Dead Letter Topic handling, Inbox deduplication, OpenTelemetry tracing, and Avro serialization. Producers add message headers for trace context and deduplication. |

KafkaFlow provides a middleware-based approach to Kafka consumers and producers. DotNetAtlas extends it with custom middleware for observability, reliability, and the inbox pattern.

## 🏗️ Consumer Pipeline

```
┌─────────────────────────────────────────────────────────────┐
│                    Kafka Message                             │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│              DeadLetterTopicMiddleware                       │
│  Catches exceptions, publishes to DLT, continues            │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                   InboxMiddleware                            │
│  Deduplicates messages by ID                                 │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                  TracingMiddleware                           │
│  Restores trace context from headers                         │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                  AvroDeserializer                            │
│  Deserializes Avro payload using Schema Registry            │
└────────────────────────────┬────────────────────────────────┘
                             │
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                   Message Handler                            │
│  Your business logic                                         │
└─────────────────────────────────────────────────────────────┘
```

## 📦 Middleware Components

### DeadLetterTopicMiddleware

Catches exceptions and publishes failed messages to a dead letter topic:

```csharp
public class DeadLetterTopicMiddleware : IMessageMiddleware
{
    private readonly IMessageProducer _dltProducer;
    private readonly ILogger<DeadLetterTopicMiddleware> _logger;
    
    public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Message processing failed, sending to DLT. Topic: {Topic}, Partition: {Partition}, Offset: {Offset}",
                context.ConsumerContext.Topic,
                context.ConsumerContext.Partition,
                context.ConsumerContext.Offset);
            
            // Publish to dead letter topic with error metadata
            var dltMessage = new DeadLetterMessage
            {
                OriginalTopic = context.ConsumerContext.Topic,
                OriginalPartition = context.ConsumerContext.Partition,
                OriginalOffset = context.ConsumerContext.Offset,
                OriginalKey = context.Message.Key?.ToString(),
                OriginalPayload = context.Message.Value,
                ErrorMessage = ex.Message,
                ErrorStackTrace = ex.StackTrace,
                FailedAtUtc = DateTimeOffset.UtcNow
            };
            
            await _dltProducer.ProduceAsync(
                $"{context.ConsumerContext.Topic}.DLT",
                dltMessage);
            
            // Don't rethrow - message is handled via DLT
        }
    }
}
```

### TracingMiddleware

Restores OpenTelemetry context from message headers:

```csharp
public class TracingMiddleware : IMessageMiddleware
{
    private static readonly ActivitySource ActivitySource = new("DotNetAtlas.Kafka");
    
    public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        var traceId = context.Headers.GetString("trace-id");
        var spanId = context.Headers.GetString("span-id");
        
        ActivityContext? parentContext = null;
        if (!string.IsNullOrEmpty(traceId))
        {
            parentContext = new ActivityContext(
                ActivityTraceId.CreateFromString(traceId),
                ActivitySpanId.CreateFromString(spanId ?? ""),
                ActivityTraceFlags.Recorded);
        }
        
        using var activity = ActivitySource.StartActivity(
            $"Consume {context.ConsumerContext.Topic}",
            ActivityKind.Consumer,
            parentContext ?? default);
        
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination", context.ConsumerContext.Topic);
        activity?.SetTag("messaging.kafka.partition", context.ConsumerContext.Partition);
        activity?.SetTag("messaging.kafka.offset", context.ConsumerContext.Offset);
        
        try
        {
            await next(context);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }
}
```

### ProducerHeadersMiddleware

Adds trace context and message ID to outgoing messages:

```csharp
public class ProducerHeadersMiddleware : IMessageMiddleware
{
    public Task Invoke(IMessageContext context, MiddlewareDelegate next)
    {
        // Add trace context
        if (Activity.Current is not null)
        {
            context.Headers.SetString("trace-id", Activity.Current.TraceId.ToString());
            context.Headers.SetString("span-id", Activity.Current.SpanId.ToString());
        }
        
        // Add message ID for deduplication (if not already set)
        if (!context.Headers.Any(h => h.Key == "message-id"))
        {
            context.Headers.SetString("message-id", Guid.CreateVersion7().ToString());
        }
        
        return next(context);
    }
}
```

## 🔧 Registration

### Consumer Registration

```csharp
services.AddKafka(kafka => kafka
    .UseMicrosoftLog()
    .AddCluster(cluster => cluster
        .WithBrokers(_options.BootstrapServers)
        .WithSchemaRegistry(config => config.Url = _options.SchemaRegistryUrl)
        .AddConsumer(consumer => consumer
            .Topic("subscription-events")
            .WithGroupId("dotnetatlas-api")
            .WithBufferSize(100)
            .WithWorkersCount(3)
            .WithAutoOffsetReset(AutoOffsetReset.Earliest)
            .AddMiddlewares(middlewares => middlewares
                .Add<DeadLetterTopicMiddleware>()
                .Add<InboxMiddleware>()
                .Add<TracingMiddleware>()
                .AddSchemaRegistryAvroDeserializer()
                .AddTypedHandlers(handlers => handlers
                    .WithHandlerLifetime(InstanceLifetime.Scoped)
                    .AddHandler<SubscriptionPurchasedEventHandler>()
                    .AddHandler<SubscriptionExtendedEventHandler>())))));
```

### Producer Registration

```csharp
services.AddKafka(kafka => kafka
    .AddCluster(cluster => cluster
        .WithBrokers(_options.BootstrapServers)
        .WithSchemaRegistry(config => config.Url = _options.SchemaRegistryUrl)
        .AddProducer<ForecastRequestedEvent>(producer => producer
            .DefaultTopic("forecast-events")
            .AddMiddlewares(middlewares => middlewares
                .Add<ProducerHeadersMiddleware>()
                .AddSchemaRegistryAvroSerializer()))));
```

## 📊 Avro Serialization

DotNetAtlas uses Avro with Schema Registry for message serialization:

```csharp
// Event class with Avro attributes
[AvroSchema]
public class ForecastRequestedEvent
{
    [AvroField("city")]
    public string City { get; set; } = string.Empty;
    
    [AvroField("country_code")]
    public string CountryCode { get; set; } = string.Empty;
    
    [AvroField("requested_at_utc")]
    public DateTimeOffset RequestedAtUtc { get; set; }
}
```

Schema Registry ensures:
- Schema evolution compatibility
- Automatic schema registration
- Cross-language compatibility

## 🎯 Configuration

```json
{
  "Kafka": {
    "BootstrapServers": ["localhost:9092"],
    "SchemaRegistryUrl": "http://localhost:8081"
  },
  "Topics": {
    "ForecastEvents": "forecast-events",
    "SubscriptionEvents": "subscription-events",
    "FeedbackEvents": "feedback-events"
  }
}
```

## 📖 Further Reading

- [**Inbox Pattern**](InboxPattern.md) - Deduplication details
- [**Outbox Pattern**](OutboxPattern.md) - Producer side
- [**Observability**](../features/Observability.md) - Tracing across Kafka

