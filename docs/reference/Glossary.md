<div align="center">

# 📚 Glossary

</div>

Key terms and definitions used throughout DotNetAtlas documentation.

## Architecture

| Term | Definition |
|------|------------|
| **Clean Architecture** | Software design philosophy that separates concerns into layers with dependencies pointing inward toward the domain |
| **Domain-Driven Design (DDD)** | Approach to software development that focuses on modeling the business domain |
| **Bounded Context** | A boundary within which a particular domain model is defined and applicable |
| **Hexagonal Architecture** | Also called Ports and Adapters; the domain is at the center with adapters connecting to external systems |

## Domain-Driven Design

| Term | Definition |
|------|------------|
| **Aggregate** | A cluster of domain objects treated as a single unit for data changes |
| **Aggregate Root** | The entry point to an aggregate; all external references go through it |
| **Entity** | An object defined by its identity rather than its attributes |
| **Value Object** | An immutable object defined by its attributes, with no identity |
| **Domain Event** | A record of something significant that happened in the domain |
| **Repository** | An abstraction for accessing aggregates from persistence |
| **Domain Service** | Stateless operations that don't naturally belong to an entity or value object |
| **Ubiquitous Language** | A shared vocabulary between developers and domain experts |

## Command Query Separation

| Term | Definition |
|------|------------|
| **Command** | An operation that changes state but returns no data |
| **Query** | An operation that returns data but doesn't change state |
| **Handler** | A class that processes a specific command or query |
| **Decorator** | A wrapper that adds behavior (logging, validation, etc.) to handlers |
| **Pipeline** | The chain of decorators that process commands/queries |

## Messaging

| Term | Definition |
|------|------------|
| **Event-Driven Architecture** | A pattern where components communicate through events |
| **Message Broker** | Infrastructure that routes messages between producers and consumers |
| **Producer** | A component that publishes messages to a broker |
| **Consumer** | A component that receives and processes messages from a broker |
| **Topic** | A named channel for publishing messages (Kafka) |
| **Consumer Group** | A set of consumers that share message processing load |
| **Dead Letter Topic (DLT)** | A topic for messages that failed processing |

## Reliability Patterns

| Term | Definition |
|------|------------|
| **Outbox Pattern** | Store messages in the database, then relay to the broker for guaranteed delivery |
| **Inbox Pattern** | Track processed message IDs to ensure idempotent consumption |
| **Idempotency** | The property where an operation produces the same result regardless of how many times it's executed |
| **At-Least-Once Delivery** | Messages are guaranteed to be delivered but may be delivered multiple times |
| **Exactly-Once Semantics** | Each message is processed exactly once (achieved via inbox pattern) |

## Resilience

| Term | Definition |
|------|------------|
| **Retry** | Automatically re-attempt failed operations |
| **Circuit Breaker** | Stop calling a failing service temporarily to allow recovery |
| **Timeout** | Fail fast when operations take too long |
| **Hedging** | Send parallel requests and use the first successful response |
| **Backoff** | Increasing delay between retry attempts |
| **Jitter** | Random variation in retry delays to prevent thundering herd |

## Caching

| Term | Definition |
|------|------------|
| **Cache-Aside** | Application manages cache reads/writes explicitly |
| **Write-Through** | Writes go to cache and database simultaneously |
| **Cache Stampede** | Many requests hit the database when cache expires |
| **Stale-While-Revalidate** | Serve stale data while refreshing in background |
| **Distributed Cache** | Cache shared across multiple application instances |

## Observability

| Term | Definition |
|------|------------|
| **Trace** | A record of a request's path through the system |
| **Span** | A single operation within a trace |
| **Metric** | A numeric measurement of system behavior |
| **Log** | A timestamped record of an event |
| **Correlation ID** | An identifier that links related operations across services |
| **OpenTelemetry** | A standard for collecting traces, metrics, and logs |

## Testing

| Term | Definition |
|------|------------|
| **Unit Test** | Tests a single component in isolation |
| **Integration Test** | Tests multiple components working together |
| **Architecture Test** | Verifies code structure follows architectural rules |
| **TestContainers** | Library for running real infrastructure in tests via Docker |
| **Test Fixture** | Shared setup/teardown for a group of tests |

## Infrastructure

| Term | Definition |
|------|------------|
| **Docker** | Container platform for packaging and running applications |
| **Docker Compose** | Tool for defining multi-container applications |
| **Kubernetes** | Container orchestration platform |
| **CI/CD** | Continuous Integration and Continuous Deployment |
| **Migration** | A versioned change to database schema |

## .NET Specific

| Term | Definition |
|------|------------|
| **Minimal API** | Lightweight approach to building HTTP APIs in ASP.NET Core |
| **FastEndpoints** | Library for building APIs with endpoint classes |
| **EF Core** | Entity Framework Core, an ORM for .NET |
| **Polly** | .NET resilience and transient-fault-handling library |
| **FusionCache** | Multi-level caching library for .NET |
| **Serilog** | Structured logging library for .NET |

## 📖 Further Reading

- [**A Gentle Introduction**](../getting-started/AGentleIntroduction.md) - Concepts in context
- [**External Resources**](Resources.md) - Books and articles for deeper learning

