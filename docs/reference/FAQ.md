<div align="center">

# ❓ Frequently Asked Questions

</div>

Common questions about DotNetAtlas and its patterns.

## 🏗️ Architecture

### Why Clean Architecture instead of traditional N-tier?

Clean Architecture inverts the dependency direction. In N-tier, business logic depends on data access. In Clean Architecture, data access depends on business logic. This makes the domain:
- **Testable** without infrastructure
- **Portable** across different databases/frameworks
- **Focused** on business rules, not technical concerns

### Why separate Application and Domain layers?

The Domain layer contains pure business logic with no external dependencies. The Application layer orchestrates use cases and depends on abstractions (interfaces) for infrastructure. This separation:
- Keeps domain logic framework-agnostic
- Makes use cases explicit and testable
- Allows infrastructure to change without affecting business rules

### Is this overkill for small projects?

DotNetAtlas is a **reference architecture** demonstrating patterns at scale. For small projects:
- Start with fewer layers (combine Application + Domain)
- Skip patterns you don't need (outbox, inbox)
- Add complexity as requirements grow

The patterns are modular—use what makes sense for your context.

## 📦 Domain-Driven Design

### When should I use a Value Object vs a primitive?

Use a Value Object when:
- The value has validation rules (email format, rating range)
- The value has behavior (money arithmetic, date calculations)
- You want type safety (can't accidentally pass a `string` email where a `string` name is expected)

### Why are aggregates so small?

Small aggregates:
- Reduce contention (fewer conflicts in concurrent updates)
- Improve performance (less data to load/save)
- Simplify reasoning (smaller scope to understand)

If you need to coordinate multiple aggregates, use domain events or sagas.

### Should domain events be raised in the constructor?

Yes, in DotNetAtlas. The `Create` factory method raises the event because:
- The aggregate is fully constructed and valid
- The event represents "this thing was created"
- Events are collected and dispatched after `SaveChanges`

## 🔀 Command Query Separation

### Why not use MediatR?

DotNetAtlas uses a custom CQS implementation to:
- Demonstrate the pattern without magic
- Show how decorators work explicitly
- Avoid the "service locator" feel of MediatR

MediatR is excellent—this is just a teaching choice.

### Can a command return data?

Technically yes, and DotNetAtlas commands return `Result<T>`. Purists argue commands should return nothing, but returning the created ID or success/failure is pragmatic and widely accepted.

### How do I handle cross-cutting concerns?

Use decorators in the pipeline:
- `ValidationDecorator` - Validate before handling
- `LoggingDecorator` - Log entry/exit
- `TracingDecorator` - Add OpenTelemetry spans
- `TransactionDecorator` - Wrap in database transaction

## 📤 Messaging

### Why use the Outbox Pattern?

Without outbox, you risk:
- **Lost messages**: Database commits but Kafka publish fails
- **Duplicate messages**: Kafka publishes but database commit fails, then retry publishes again

The outbox ensures messages are published exactly when the database transaction commits.

### Why use the Inbox Pattern?

Kafka guarantees at-least-once delivery, meaning messages may be delivered multiple times. The inbox:
- Tracks processed message IDs
- Skips duplicates
- Ensures exactly-once processing semantics

### When should I use fire-and-forget vs outbox?

| Use Fire-and-Forget When | Use Outbox When |
|--------------------------|-----------------|
| Message loss is acceptable | Message delivery is critical |
| No database transaction involved | Message must be consistent with DB state |
| Performance is critical | Reliability is critical |

## 🧪 Testing

### Why TestContainers instead of in-memory databases?

In-memory databases (like EF Core's InMemory provider):
- Don't support all SQL features
- Behave differently than real databases
- Miss real integration issues

TestContainers run actual SQL Server, Redis, Kafka—catching real problems.

### How do I test domain events?

```csharp
var feedback = Feedback.Create(text, rating, userId).Value;
var events = feedback.PopDomainEvents();

events.Should().ContainSingle()
    .Which.Should().BeOfType<FeedbackCreatedDomainEvent>();
```

### Should I mock the database in unit tests?

For **handler unit tests**: Yes, mock the repository/DbContext to test handler logic in isolation.

For **integration tests**: No, use TestContainers with real databases.

## 🔧 Infrastructure

### Why Redis for both cache and SignalR backplane?

Redis is versatile:
- **Cache**: Fast key-value storage with TTL
- **SignalR backplane**: Pub/sub for message distribution
- **Distributed locks**: Coordination across instances

One infrastructure component, multiple uses.

### Why Hangfire instead of hosted services?

Hangfire provides:
- **Persistence**: Jobs survive restarts
- **Dashboard**: Visual monitoring
- **Scheduling**: Cron expressions
- **Retries**: Automatic with backoff

Hosted services are simpler but lack these features.

## 🚀 Production

### How do I deploy database migrations?

Options:
1. **CI/CD script**: Generate and apply SQL scripts
2. **Startup migration**: Apply on app start (dev only)
3. **Separate migration job**: Run before deployment

See [Database Migrations](../devops/Migrations.md) for details.

### How do I scale horizontally?

DotNetAtlas is designed for horizontal scaling:
- **Stateless API**: No session state
- **Redis cache**: Shared across instances
- **SignalR backplane**: Redis distributes messages
- **Kafka consumers**: Consumer groups share load

## 📖 Further Reading

- [**Glossary**](Glossary.md) - Key terms defined
- [**External Resources**](Resources.md) - Books and articles
- [**A Gentle Introduction**](../getting-started/AGentleIntroduction.md) - Concepts explained

