<div align="center">

# 🦄 A Gentle Introduction

</div>

| ⚡ TL;DR |
| -------- |
| DotNetAtlas is a reference architecture demonstrating Clean Architecture, DDD, CQS, and event-driven patterns in a real, runnable .NET application. It uses a simple weather domain to teach complex patterns without getting lost in business complexity. |

Most tutorials show you one pattern in isolation. Most books give you theory without runnable code. DotNetAtlas gives you **both**: production-ready patterns in a complete, working system you can explore, run, and learn from.

## 🎯 What Problem Does It Solve?

You've read about Clean Architecture. You understand DDD concepts. You know what an Outbox pattern is. But how do you actually put them all together?

DotNetAtlas answers that question with **working code**.

```
Clean Architecture + Domain-Driven Design + Event-Driven Architecture
                              ↓
              A Complete, Runnable Reference Implementation
                              ↓
                  Patterns You Can Study and Adapt
```

## 🏗️ The Architecture at a Glance

DotNetAtlas follows Clean Architecture with four main layers:

```
┌─────────────────────────────────────────────────────────────┐
│                         API Layer                           │
│     FastEndpoints • SignalR Hubs • Swagger • Middleware     │
└────────────────────────────┬────────────────────────────────┘
                             │ depends on
┌────────────────────────────▼────────────────────────────────┐
│                    Application Layer                        │
│        Commands • Queries • Handlers • Validators           │
└────────────────────────────┬────────────────────────────────┘
                             │ depends on
┌────────────────────────────▼────────────────────────────────┐
│                      Domain Layer                           │
│     Aggregates • Entities • Value Objects • Domain Events   │
└─────────────────────────────────────────────────────────────┘
                             ▲
                             │ implements interfaces from
┌────────────────────────────┴────────────────────────────────┐
│                   Infrastructure Layer                      │
│      EF Core • Kafka • Redis • External APIs • Auth         │
└─────────────────────────────────────────────────────────────┘
```

**The key rule**: Dependencies point inward. Domain has no external dependencies. Infrastructure implements interfaces defined by inner layers.

Read more: [**Clean Architecture**](../architecture/CleanArchitecture.md)

## 📦 The Domain: Weather

We use a simple weather domain as our demonstration vehicle:

- **Forecasts**: Get weather predictions (demonstrates caching, resilience)
- **Feedback**: Submit and modify user feedback (demonstrates DDD, outbox pattern)
- **Alerts**: Subscribe to weather alerts (demonstrates subscriptions, Kafka consumption)

The domain is intentionally simple. You're here to learn architecture patterns, not weather modeling.

## 🧱 Domain-Driven Design

DotNetAtlas implements DDD tactical patterns:

### Aggregates

The `Feedback` aggregate encapsulates feedback creation and modification:

```csharp
public sealed class Feedback : AggregateRoot<Guid>
{
    public static Result<Feedback> Create(FeedbackText text, FeedbackRating rating, Guid userId)
    {
        var feedback = new Feedback { /* ... */ };
        feedback.AddDomainEvent(new FeedbackCreatedDomainEvent { /* ... */ });
        return feedback;
    }
}
```

### Value Objects

Value objects ensure valid state and meaningful comparisons:

```csharp
public sealed class FeedbackRating : ValueObject
{
    public int Value { get; private set; }
    
    public static Result<FeedbackRating> Create(int value)
    {
        if (value < 1 || value > 5)
            return Result.Fail(FeedbackErrors.InvalidRating(value));
        return new FeedbackRating { Value = value };
    }
}
```

### Domain Events

State changes raise domain events, captured and published by infrastructure:

```csharp
// Raised when feedback is created
public class FeedbackCreatedDomainEvent : IDomainEvent
{
    public Guid FeedbackId { get; init; }
    public string Text { get; init; }
    public int Rating { get; init; }
}
```

Read more: [**Domain-Driven Design**](../architecture/DomainDrivenDesign.md)

## ⚡ Command Query Separation

Operations are split into commands (mutations) and queries (reads):

```csharp
// Command: Changes state
public record SendFeedbackCommand(string Text, int Rating) : ICommand;

// Query: Reads state  
public record GetFeedbackQuery(Guid Id) : IQuery<FeedbackResponse>;
```

Handlers process them through a decorator pipeline:

```
Request → Validation → Logging → Tracing → Metrics → Handler → Response
```

Read more: [**Command Query Separation**](../architecture/CQS.md)

## 📡 Event-Driven Architecture

DotNetAtlas shows two patterns for publishing events to Kafka:

### Fire-and-Forget
For non-critical events (forecast requests), publish directly:

```csharp
await _kafkaProducer.ProduceAsync(new ForecastRequestedEvent { City = city });
// If Kafka is down, the event is lost (acceptable for analytics)
```

### Transactional Outbox
For critical events (feedback), persist with the aggregate:

```csharp
// In the same DB transaction:
// 1. Save feedback aggregate
// 2. Save outbox message
// Worker service publishes to Kafka later
```

Read more: [**Event-Driven Architecture**](../architecture/EventDriven.md)

## 🔭 Observability

Every operation is traced, metered, and logged:

- **Traces**: OpenTelemetry spans from HTTP → Handler → Database → Kafka
- **Metrics**: Request counts, durations, error rates
- **Logs**: Structured logging with Serilog → Seq

Trace context propagates through the outbox, so you can follow a request from API to eventual Kafka consumption.

Read more: [**Observability**](../features/Observability.md)

## 🧩 Platform Libraries

Reusable components extracted from the main application:

| Library | Purpose |
|---------|---------|
| SharedKernel | DDD building blocks |
| CQS | Command/Query handlers and behaviors |
| Outbox.Core | Outbox message entity |
| Inbox.Core | Idempotent message processing |
| KafkaFlow.* | Kafka middleware pipeline |

These are designed to be copied and adapted, not installed as NuGet packages.

Read more: [**Platform Libraries**](../platform/SharedKernel.md)

## 🎯 What's Next?

Ready to dive deeper?

1. **[Step By Step](StepByStep.md)** - Follow a feedback submission through the entire system
2. **[Clean Architecture](../architecture/CleanArchitecture.md)** - Understand layer responsibilities
3. **[Outbox Pattern](../platform/OutboxPattern.md)** - Learn guaranteed message delivery

