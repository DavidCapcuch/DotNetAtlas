<div align="center">

# 🔭 Observability

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas implements the three pillars of observability: **Traces** (OpenTelemetry → Jaeger), **Metrics** (OpenTelemetry → Prometheus/Grafana), and **Logs** (Serilog → Seq). Trace context propagates through HTTP, Kafka, and the outbox for end-to-end visibility. |

Observability lets you understand what's happening inside your system by examining its outputs. DotNetAtlas demonstrates production-grade observability with distributed tracing, metrics, and structured logging.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    DotNetAtlas API                           │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐                      │
│  │ Traces  │  │ Metrics │  │  Logs   │                      │
│  └────┬────┘  └────┬────┘  └────┬────┘                      │
└───────┼────────────┼────────────┼───────────────────────────┘
        │            │            │
        ▼            ▼            ▼
┌───────────┐  ┌───────────┐  ┌───────────┐
│  Jaeger   │  │ Prometheus│  │    Seq    │
│  (traces) │  │ (metrics) │  │  (logs)   │
└───────────┘  └─────┬─────┘  └───────────┘
                     │
                     ▼
               ┌───────────┐
               │  Grafana  │
               │(dashboards)│
               └───────────┘
```

## 📊 Distributed Tracing

### Configuration

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("DotNetAtlas.Api"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("DotNetAtlas.CQS")
        .AddSource("DotNetAtlas.Kafka")
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }));
```

### Custom Spans

```csharp
public class SendFeedbackHandler : ICommandHandler<SendFeedbackCommand, Guid>
{
    private static readonly ActivitySource ActivitySource = new("DotNetAtlas.CQS");
    
    public async Task<Result<Guid>> HandleAsync(SendFeedbackCommand command, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("SendFeedback");
        activity?.SetTag("feedback.rating", command.Rating);
        
        // ... handler logic ...
        
        activity?.SetTag("feedback.id", feedbackId);
        return feedbackId;
    }
}
```

### Trace Context Propagation

Trace context flows through:

1. **HTTP Headers** - W3C Trace Context (`traceparent`, `tracestate`)
2. **Kafka Headers** - Custom headers (`trace-id`, `span-id`)
3. **Outbox Messages** - Stored in database columns

```csharp
// Outbox stores trace context
var outboxMessage = new OutboxMessage
{
    TraceId = Activity.Current?.TraceId.ToString(),
    SpanId = Activity.Current?.SpanId.ToString(),
    // ...
};

// Worker restores context
var parentContext = new ActivityContext(
    ActivityTraceId.CreateFromString(message.TraceId),
    ActivitySpanId.CreateFromString(message.SpanId),
    ActivityTraceFlags.Recorded);

using var activity = ActivitySource.StartActivity(
    "OutboxRelay.Publish",
    ActivityKind.Producer,
    parentContext);
```

## 📈 Metrics

### Configuration

```csharp
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("DotNetAtlas.CQS")
        .AddMeter("DotNetAtlas.Kafka")
        .AddPrometheusExporter());

app.MapPrometheusScrapingEndpoint();
```

### Custom Metrics

```csharp
public class CommandMetricsBehavior<TCommand, TResult>
{
    private static readonly Meter Meter = new("DotNetAtlas.CQS");
    
    private static readonly Counter<long> CommandCounter = 
        Meter.CreateCounter<long>(
            "cqs_commands_total",
            description: "Total number of commands executed");
    
    private static readonly Histogram<double> CommandDuration = 
        Meter.CreateHistogram<double>(
            "cqs_commands_duration_ms",
            unit: "ms",
            description: "Command execution duration");
    
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var commandName = typeof(TCommand).Name;
        
        try
        {
            var result = await _next.HandleAsync(command, ct);
            
            CommandCounter.Add(1, 
                new("command", commandName),
                new("success", result.IsSuccess));
            
            return result;
        }
        finally
        {
            CommandDuration.Record(
                stopwatch.ElapsedMilliseconds,
                new("command", commandName));
        }
    }
}
```

### Key Metrics

| Metric | Type | Description |
|--------|------|-------------|
| `http_server_request_duration_seconds` | Histogram | HTTP request duration |
| `cqs_commands_total` | Counter | Commands executed |
| `cqs_commands_duration_ms` | Histogram | Command duration |
| `kafka_consumer_messages_total` | Counter | Messages consumed |
| `outbox_messages_published_total` | Counter | Outbox messages published |

## 📝 Structured Logging

### Configuration

```csharp
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "DotNetAtlas.Api")
    .Enrich.WithSpanId()
    .Enrich.WithTraceId()
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq("http://localhost:5341")
    .CreateLogger();
```

### Structured Log Messages

```csharp
_logger.LogInformation(
    "Processing feedback {FeedbackId} with rating {Rating} from user {UserId}",
    feedbackId,
    rating,
    userId);
```

Output in Seq:
```json
{
  "Timestamp": "2024-01-15T10:30:00.000Z",
  "Level": "Information",
  "MessageTemplate": "Processing feedback {FeedbackId} with rating {Rating} from user {UserId}",
  "Properties": {
    "FeedbackId": "550e8400-e29b-41d4-a716-446655440000",
    "Rating": 5,
    "UserId": "user-123",
    "TraceId": "abc123",
    "SpanId": "def456",
    "Application": "DotNetAtlas.Api"
  }
}
```

### Log Enrichment

Request context is automatically added:

```csharp
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent);
        diagnosticContext.Set("UserId", httpContext.User.FindFirst("sub")?.Value);
    };
});
```

## 🖥️ Observability UIs

| Tool | URL | Purpose |
|------|-----|---------|
| Jaeger | http://localhost:16686 | Distributed traces |
| Grafana | http://localhost:3000 | Metrics dashboards |
| Seq | http://localhost:5341 | Log search and analysis |

## 📖 Further Reading

- [**Step By Step**](../getting-started/StepByStep.md) - See traces in action
- [**CQS**](../architecture/CQS.md) - Tracing in the decorator pipeline
- [**Outbox Pattern**](../platform/OutboxPattern.md) - Trace context preservation

