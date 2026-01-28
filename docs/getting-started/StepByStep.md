<div align="center">

# 👩‍🏫 Step By Step

</div>

| ⚡ TL;DR |
| -------- |
| This guide traces a feedback submission from HTTP request through all layers to Kafka publication, showing how Clean Architecture, DDD, CQS, and the Outbox pattern work together in practice. |

The best way to understand DotNetAtlas is to follow a request through the entire system. We'll trace what happens when a user submits feedback, watching the request flow through every layer, pattern, and infrastructure component.

## 🎯 The Scenario

A user wants to submit feedback about the weather forecast service. They send a POST request:

```http
POST /api/v1/weather/feedback
Authorization: Bearer eyJhbG...
Content-Type: application/json

{
  "text": "Excellent forecast accuracy!",
  "rating": 5
}
```

Let's follow this request through the system.

---

## 1️⃣ API Layer: FastEndpoints

The request first hits our `SendFeedbackEndpoint`:

```csharp
public class SendFeedbackEndpoint : Endpoint<SendFeedbackRequest, SendFeedbackResponse>
{
    public override void Configure()
    {
        Post("/api/v1/weather/feedback");
        Roles("user");  // Requires authenticated user
    }

    public override async Task HandleAsync(SendFeedbackRequest req, CancellationToken ct)
    {
        var command = new SendFeedbackCommand(req.Text, req.Rating);
        var result = await _commandHandler.HandleAsync(command, ct);
        
        await result.Match(
            success => SendOkAsync(new SendFeedbackResponse(success.Id)),
            failure => SendErrorsAsync(failure)
        );
    }
}
```

**What happens here:**
- JWT token is validated (user authenticated)
- Request is deserialized into `SendFeedbackRequest`
- User ID is extracted from claims
- Request is mapped to a `SendFeedbackCommand`

---

## 2️⃣ Application Layer: CQS Pipeline

The command enters our decorator pipeline. Each decorator wraps the next:

```
┌─────────────────────────────────────────────────────────────┐
│                  ValidationBehavior                         │
│  ┌───────────────────────────────────────────────────────┐  │
│  │                 LoggingBehavior                       │  │
│  │  ┌─────────────────────────────────────────────────┐  │  │
│  │  │               TracingBehavior                   │  │  │
│  │  │  ┌───────────────────────────────────────────┐  │  │  │
│  │  │  │            MetricsBehavior                │  │  │  │
│  │  │  │  ┌─────────────────────────────────────┐  │  │  │  │
│  │  │  │  │      SendFeedbackHandler           │  │  │  │  │
│  │  │  │  └─────────────────────────────────────┘  │  │  │  │
│  │  │  └───────────────────────────────────────────┘  │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

### ValidationBehavior

First, FluentValidation rules are checked:

```csharp
public class SendFeedbackCommandValidator : AbstractValidator<SendFeedbackCommand>
{
    public SendFeedbackCommandValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty()
            .MaximumLength(1000);
        
        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5);
    }
}
```

If validation fails, we return immediately with validation errors. No database call, no business logic executed.

### TracingBehavior

An OpenTelemetry span is created:

```csharp
using var activity = ActivitySource.StartActivity($"Handle {typeof(TCommand).Name}");
activity?.SetTag("command.type", typeof(TCommand).Name);
// ... execute next ...
activity?.SetTag("result.success", result.IsSuccess);
```

This span appears in Jaeger, linked to the parent HTTP request span.

### MetricsBehavior

Metrics are recorded:
- `cqs.commands.total` - Counter
- `cqs.commands.errors` - Counter
- `cqs.commands.duration` - Histogram

---

## 3️⃣ Application Layer: Command Handler

Now we reach the actual handler:

```csharp
public class SendFeedbackHandler : ICommandHandler<SendFeedbackCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(SendFeedbackCommand command, CancellationToken ct)
    {
        // Create value objects (validates business rules)
        var textResult = FeedbackText.Create(command.Text);
        var ratingResult = FeedbackRating.Create(command.Rating);
        
        if (textResult.IsFailed || ratingResult.IsFailed)
            return Result.Merge(textResult, ratingResult);
        
        // Create aggregate (raises domain event)
        var feedbackResult = Feedback.Create(
            textResult.Value, 
            ratingResult.Value, 
            _currentUser.Id);
        
        if (feedbackResult.IsFailed)
            return feedbackResult;
        
        // Persist
        await _dbContext.Feedback.AddAsync(feedbackResult.Value, ct);
        await _dbContext.SaveChangesAsync(ct);
        
        return feedbackResult.Value.Id;
    }
}
```

**Key points:**
- Value objects validate business rules at creation
- The Result pattern means no exceptions for expected failures
- Domain event is raised inside `Feedback.Create()`
- `SaveChangesAsync` triggers interceptors

---

## 4️⃣ Domain Layer: Aggregate and Events

Inside `Feedback.Create()`:

```csharp
public static Result<Feedback> Create(FeedbackText text, FeedbackRating rating, Guid userId)
{
    var feedback = new Feedback
    {
        Id = Guid.CreateVersion7(),
        FeedbackText = text,
        Rating = rating,
        CreatedByUser = userId
    };
    
    // Raise domain event
    feedback.AddDomainEvent(new FeedbackCreatedDomainEvent
    {
        FeedbackId = feedback.Id,
        UserId = userId,
        Text = text.Text,
        Rating = rating.Value,
        OccurredOnUtc = DateTimeOffset.UtcNow
    });
    
    return feedback;
}
```

The domain event is stored in the aggregate's internal collection, not yet published.

---

## 5️⃣ Infrastructure: EF Core Interceptors

When `SaveChangesAsync` is called, our interceptors activate:

### UpdateAuditableEntitiesInterceptor

Sets `CreatedUtc` and `LastModifiedUtc` on auditable entities:

```csharp
foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
{
    if (entry.State == EntityState.Added)
        entry.Entity.CreatedUtc = _timeProvider.GetUtcNow();

    entry.Entity.LastModifiedUtc = _timeProvider.GetUtcNow();
}
```

### DispatchDomainEventsInterceptor

This is where the **Outbox pattern** kicks in:

```csharp
public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(...)
{
    // 1. Collect domain events from all aggregates
    var aggregates = context.ChangeTracker
        .Entries<IAggregateRoot>()
        .Select(e => e.Entity)
        .ToList();

    var domainEvents = aggregates
        .SelectMany(a => a.PopDomainEvents())
        .ToList();

    // 2. Convert to outbox messages with OpenTelemetry context
    foreach (var domainEvent in domainEvents)
    {
        var outboxMessage = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            EventType = domainEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(domainEvent),
            TraceId = Activity.Current?.TraceId.ToString(),
            SpanId = Activity.Current?.SpanId.ToString(),
            CreatedUtc = DateTimeOffset.UtcNow
        };

        context.Set<OutboxMessage>().Add(outboxMessage);
    }

    // 3. SaveChanges commits both aggregate AND outbox in one transaction
    return await base.SavingChangesAsync(...);
}
```

**This is the magic**: The feedback and its outbox message are saved in the **same database transaction**. Either both are committed, or neither is. We have **guaranteed delivery**.

---

## 6️⃣ Database Transaction

A single SQL transaction executes:

```sql
BEGIN TRANSACTION

INSERT INTO Feedback (Id, FeedbackText, Rating, CreatedByUser, CreatedUtc, LastModifiedUtc)
VALUES (@p0, @p1, @p2, @p3, @p4, @p5)

INSERT INTO OutboxMessages (Id, EventType, Payload, TraceId, SpanId, CreatedUtc, ProcessedUtc)
VALUES (@p6, @p7, @p8, @p9, @p10, @p11, NULL)

COMMIT TRANSACTION
```

At this point:
- ✅ Feedback is persisted
- ✅ Outbox message is persisted
- ❌ Kafka doesn't know about it yet

The API returns `201 Created` to the user.

---

## 7️⃣ Outbox Relay Worker

A separate worker service polls the outbox table:

```csharp
public async Task ProcessOutboxMessagesAsync(CancellationToken ct)
{
    var messages = await _dbContext.OutboxMessages
        .Where(m => m.ProcessedUtc == null)
        .OrderBy(m => m.CreatedUtc)
        .Take(_options.BatchSize)
        .ToListAsync(ct);

    foreach (var message in messages)
    {
        // Restore OpenTelemetry context for trace continuity
        using var activity = RestoreTraceContext(message.TraceId, message.SpanId);

        // Publish to Kafka
        await _kafkaProducer.ProduceAsync(message.Topic, message.Payload);

        // Mark as processed
        message.ProcessedUtc = DateTimeOffset.UtcNow;
    }

    await _dbContext.SaveChangesAsync(ct);
}
```

**Key insight**: The `TraceId` and `SpanId` from the original HTTP request are restored. When you view the trace in Jaeger, you see a continuous flow from HTTP request to Kafka publication, even though they happened in different processes.

---

## 8️⃣ Kafka: Event Published

The event is now in Kafka with:
- Avro-serialized payload (Schema Registry)
- Message headers with trace context
- Message ID for deduplication

Any consumer can now process `FeedbackCreatedEvent`.

---

## 🔭 See It in Jaeger

After submitting feedback, open Jaeger at `http://localhost:16686`:

```
[HTTP POST /api/v1/weather/feedback]  ──────────────────────────────► 50ms
    └── [Handle SendFeedbackCommand]  ─────────────────────────────► 45ms
        ├── [Validation]  ─────────────────────────────────────────► 2ms
        ├── [Database SaveChanges]  ───────────────────────────────► 30ms
        └── [Outbox Write]  ───────────────────────────────────────► 10ms
                                    ... later ...
[OutboxRelay: Publish FeedbackCreated]  ───────────────────────────► 15ms
    └── [Kafka Produce]  ──────────────────────────────────────────► 12ms
```

The trace shows the complete journey, across process boundaries, linked by trace context.

---

## 🎯 What You've Learned

Following this request, you've seen:

1. **Clean Architecture** - Each layer has clear responsibilities
2. **CQS with Decorators** - Cross-cutting concerns without polluting handlers
3. **DDD Aggregates** - Domain events raised from business operations
4. **Outbox Pattern** - Guaranteed delivery via transactional outbox
5. **Distributed Tracing** - Context propagation across async boundaries

## 📖 Dive Deeper

- [**Clean Architecture**](../architecture/CleanArchitecture.md) - Layer details
- [**Outbox Pattern**](../platform/OutboxPattern.md) - Implementation deep dive
- [**Observability**](../features/Observability.md) - Trace context propagation

