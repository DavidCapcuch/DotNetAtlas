# DotNetAtlas - Glossary

> **Quick Reference**: Key terms, patterns, and concepts used throughout the project.

---

## Architectural Patterns

### Clean Architecture
A software design philosophy that separates concerns into layers with strict dependency rules flowing inward toward the domain. The domain layer has zero dependencies, while outer layers depend only on inner layers. This ensures business logic remains independent of frameworks, UI, databases, and external services.

**Layers in DotNetAtlas:**
- **Domain** (innermost) - Pure business logic
- **Application** - Use cases and interfaces
- **Infrastructure** - Implementation details (DB, APIs, messaging)
- **Api** (outermost) - Presentation layer

### Domain-Driven Design (DDD)
An approach to software development that centers design around the business domain and domain logic. Emphasizes collaboration between technical and domain experts using a shared "ubiquitous language."

**Tactical Patterns:**
- **Aggregate Root** - Cluster of domain objects treated as a single unit (e.g., `Feedback` entity)
- **Entity** - Object with a unique identity that persists over time
- **Value Object** - Immutable object defined by its attributes (e.g., `FeedbackText`, `FeedbackRating`)
- **Domain Event** - Something that happened in the domain that domain experts care about
- **Repository** - Pattern for data access (replaced by Specifications in this project)

**Strategic Patterns:**
- **Bounded Context** - Explicit boundary within which a domain model applies (Weather domain)
- **Ubiquitous Language** - Common vocabulary shared by developers and domain experts
- **Anti-Corruption Layer** - Isolates domain model from external systems (weather provider abstractions)

---

## Application Patterns

### Command Query Separation (CQS)
A principle that divides operations into two categories:
- **Commands** - Modify state, return void or Result (side effects)
- **Queries** - Return data without modifying state (no side effects)

**Not the same as CQRS** - CQS uses the same data store for reads and writes.

### Command Query Responsibility Segregation (CQRS)
An architectural pattern that separates read and write operations into different models and often different databases. DotNetAtlas uses **CQS, not CQRS** - operations are separated but use the same SQL Server database.

**Key Difference:**
- **CQS** = Separate operations, same data store
- **CQRS** = Separate operations AND separate data stores (read models vs write models)

### Specification Pattern
A pattern (from DDD) for encapsulating query logic into reusable, composable objects. In DotNetAtlas, Ardalis.Specification replaces the traditional Repository pattern.

**Example:** `WeatherFeedbackByIdSpec` encapsulates the logic to find feedback by ID.

---

## Messaging & Event Patterns

### Event-Driven Architecture
A software architecture pattern where system components communicate through events. Components produce events when something significant happens, and other components consume these events to react.

**Benefits:**
- Loose coupling between components
- Asynchronous processing
- Scalability
- Audit trail

**Trade-off:** Eventual consistency (time lag between event production and consumption)

### Outbox Pattern
A reliability pattern that ensures **guaranteed message delivery** to external systems (like Kafka) by persisting events in the database within the same transaction as business data changes.

**How it works in DotNetAtlas:**
1. Domain aggregate raises event
2. EF Interceptor captures event on SaveChanges
3. Event stored in Outbox table (same transaction as entity)
4. Worker service polls Outbox table
5. Events published to Kafka
6. Successfully delivered events deleted from Outbox

**Guarantees:** At-least-once delivery (events may be delivered multiple times)

### Fire-and-Forget
A simple event publishing strategy where events are sent immediately without transaction guarantees. Faster but risks message loss if the broker is unavailable.

**When to use:**
- Non-critical events (analytics, logging)
- Performance is critical
- Acceptable to occasionally lose messages

**Example in DotNetAtlas:** Forecast request events

---

## Resilience Patterns

### Circuit Breaker
A pattern that prevents cascading failures by "opening" after detecting too many failures, rejecting requests immediately instead of trying and failing. After a timeout, it "half-opens" to test if the service recovered.

**States:**
- **Closed** - Normal operation, requests pass through
- **Open** - Too many failures, reject requests immediately
- **Half-Open** - Testing if service recovered

### Hedging
A resilience strategy that races multiple identical requests in parallel after a timeout, using the first successful response. Reduces perceived latency when a service is slow.

**Example in DotNetAtlas:** `HedgingWeatherForecastService` races multiple weather providers

### Retry with Exponential Backoff
Automatically retries failed operations with increasing delays between attempts. Prevents overwhelming a struggling service while giving it time to recover.

**Formula:** delay = base_delay × 2^attempt_number (+ jitter)

### Timeout
Limits how long an operation can run before being cancelled. Prevents indefinite waiting for slow or hung operations.

**Types in DotNetAtlas:**
- **Per-attempt timeout** - For each individual retry
- **Total timeout** - For entire operation including all retries

---

## Caching Patterns

### L1/L2 Caching
Multi-level caching strategy for performance and consistency:

**L1 (Level 1) Cache:**
- In-memory cache within application process
- Fastest access (nanoseconds)
- Not shared across instances
- Lost on application restart

**L2 (Level 2) Cache:**
- Distributed cache (Redis)
- Shared across all application instances
- Survives application restarts
- Slower than L1 but still fast (milliseconds)

**FusionCache Strategy:**
1. Check L1 → hit? return
2. Check L2 → hit? store in L1, return
3. Miss → execute factory, store in L2 and L1, return

### Cache-Aside Pattern
Application code explicitly manages cache reads and writes. On cache miss, application retrieves data and populates cache.

**FusionCache implements this pattern automatically.**

### Backplane
A mechanism to synchronize cache invalidation across multiple application instances. When one instance invalidates a cache entry, all instances are notified via pub/sub.

**In DotNetAtlas:** Redis pub/sub notifies all instances to invalidate L1 caches

---

## Testing Patterns

### TestContainers
Library that provides lightweight, throwaway instances of databases, message brokers, and other services in Docker containers for testing.

**Benefits:**
- Test with real infrastructure (not mocks)
- Isolated test environments
- Automatic cleanup
- Catches integration issues mocks miss

**Used in DotNetAtlas:** SQL Server, Kafka, Schema Registry, Redis

### Arrange-Act-Assert (AAA)
Standard unit test structure:
- **Arrange** - Set up test data and dependencies
- **Act** - Execute the code being tested
- **Assert** - Verify the results

### Test Fixture
Shared setup and teardown logic for a group of tests. In xUnit, fixtures provide test context that's created once per test collection.

**In DotNetAtlas:** `ApiTestFixture` creates WebApplicationFactory + TestContainers

### Test Collection
Group of test classes that share a fixture instance. Tests in different collections run in parallel, but tests within a collection share the same fixture.

---

## Observability Concepts

### Distributed Tracing
Following a request's journey across multiple services by propagating a trace context (trace ID + span ID). All operations add spans to the same trace, creating a complete picture.

**W3C Traceparent:** Standard format for propagating trace context in HTTP headers

**In DotNetAtlas:** Traces flow from HTTP → Handler → Database → Outbox → Worker → Kafka

### Span
A single unit of work within a distributed trace. Each span has:
- Operation name
- Start/end time
- Parent span ID
- Attributes (tags)
- Events
- Status

### Baggage
Key-value pairs propagated with the trace context. Unlike span attributes (visible only in that span), baggage is visible to all operations in the trace.

**In DotNetAtlas:** `user.id` and `correlation.id` propagated as baggage

### Semantic Conventions
Standardized attribute names for common operations (e.g., `http.method`, `db.system`). Ensures consistent telemetry across different tools and languages.

### Instrumentation
Adding telemetry collection to code. Can be:
- **Manual** - Explicitly creating spans and metrics
- **Automatic** - Libraries instrument themselves (via OpenTelemetry)

---

## Development Patterns

### Decorator Pattern (via Scrutor)
Adding responsibilities to objects dynamically by wrapping them. In DotNetAtlas, command/query handlers are decorated with:
1. Validation (FluentValidation)
2. Logging (Serilog)
3. Tracing (OpenTelemetry)

**Scrutor** - Library for convention-based decorator registration in ASP.NET Core DI

### Result Pattern
Returning a `Result<T>` object that encapsulates either success with a value or failure with errors, instead of throwing exceptions.

**Benefits:**
- Explicit error handling
- Type-safe errors
- Better for expected failures (validation, not found)
- Avoids exception overhead

**When to use exceptions:** Truly exceptional cases (bugs, infrastructure failures)

### Options Pattern
ASP.NET Core pattern for strongly-typed configuration:
```csharp
services.Configure<MyOptions>(configuration.GetSection("MySection"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

**Always validate on start** to fail fast if configuration is invalid.

---

## Database Patterns

### Entity Framework Core Interceptor
Hook into EF Core's save pipeline to perform operations before/after database changes. 

**In DotNetAtlas:**
- `OutboxInterceptor` - Captures domain events
- `UpdateAuditableEntitiesInterceptor` - Sets audit timestamps

### Migration Strategies

**Dual Strategy in DotNetAtlas:**
- **Development** - EF Core Migrations (fast iteration)
- **Production** - Flyway SQL Scripts (reviewable, DBA-friendly)

**Critical:** Architecture test ensures Flyway script exists for every EF migration

### Flyway
Database migration tool that uses versioned SQL scripts (e.g., `V001__CreateFeedbackTable.sql`). Tracks applied migrations in database table.

---

## Authentication & Authorization

### OIDC (OpenID Connect)
Identity layer on top of OAuth 2.0 for authentication. Returns an ID token with user information.

### JWT (JSON Web Token)
Self-contained token format for securely transmitting information. Contains:
- Header (algorithm, type)
- Payload (claims)
- Signature

**Used for:** Stateless API authentication

### OAuth 2.0
Authorization framework for delegating access. Provides access tokens for calling APIs.

**Not for authentication** - Use OIDC for authentication

### Policy-Based Authorization
ASP.NET Core authorization based on custom policies instead of just roles.

**Example:** `DevOnly` policy requires Development environment

---

## Real-Time Communication

### SignalR
ASP.NET Core library for real-time web functionality. Abstracts WebSocket, Server-Sent Events, and Long Polling.

### Hub
SignalR server-side component that clients connect to. Defines methods clients can call and methods to invoke on clients.

### Backplane (SignalR)
Infrastructure to scale SignalR across multiple servers. Messages sent from one server are broadcast to clients connected to other servers.

**In DotNetAtlas:** Redis backplane with custom Lua scripts for group management

### MessagePack Protocol
Binary serialization protocol for SignalR. More compact and faster than JSON.

---

## Serialization

### Avro
Binary serialization framework with schema registry for versioning. Schemas define data structure separate from data.

**Benefits:**
- Compact binary format
- Schema evolution (backward/forward compatibility)
- Schema validation

**In DotNetAtlas:** Used for Kafka messages with Confluent Schema Registry

### MemoryPack
High-performance binary serialization for .NET. Used for caching.

---

## Background Processing

### Hangfire
Background job processing framework with:
- Persistent storage (SQL Server)
- Retry logic
- Scheduling (recurring jobs)
- Dashboard UI

### Worker Service
.NET template for long-running background services (Windows Services, Linux daemons, Docker containers).

**In DotNetAtlas:** Outbox Relay Worker Service

---

## Common Acronyms

| Acronym | Full Name | Description |
|---------|-----------|-------------|
| **API** | Application Programming Interface | Interface for software interaction |
| **AVRO** | Apache Avro | Data serialization system |
| **CQS** | Command Query Separation | Separate read/write operations |
| **CQRS** | Command Query Responsibility Segregation | Separate read/write models |
| **DDD** | Domain-Driven Design | Domain-centric design approach |
| **DI** | Dependency Injection | Inversion of control pattern |
| **DTO** | Data Transfer Object | Object for transferring data |
| **EF** | Entity Framework | ORM for .NET |
| **HTTP** | Hypertext Transfer Protocol | Web communication protocol |
| **HTTPS** | HTTP Secure | Encrypted HTTP |
| **JWT** | JSON Web Token | Token format for auth |
| **OIDC** | OpenID Connect | Authentication protocol |
| **ORM** | Object-Relational Mapping | Database abstraction |
| **OTEL** | OpenTelemetry | Observability framework |
| **PII** | Personally Identifiable Information | Sensitive user data |
| **REST** | Representational State Transfer | API architectural style |
| **SQL** | Structured Query Language | Database query language |
| **SSE** | Server-Sent Events | Real-time server push |
| **TLS** | Transport Layer Security | Encryption protocol |
| **UI** | User Interface | Visual interface |
| **URL** | Uniform Resource Locator | Web address |

---

## Quick References

### When to Use What

**Fire-and-Forget vs Outbox:**
- Use **Fire-and-Forget** for: Analytics, logging, non-critical events
- Use **Outbox** for: Business-critical events requiring guaranteed delivery

**CQS vs CQRS:**
- Use **CQS** for: Most applications (simpler, same data store)
- Use **CQRS** for: High-scale reads, complex reporting, different read/write characteristics

**Exceptions vs Result Pattern:**
- Use **Result Pattern** for: Expected failures (validation, not found, conflicts)
- Use **Exceptions** for: Bugs, infrastructure failures, truly exceptional cases

**TestContainers vs Mocks:**
- Use **TestContainers** for: Integration tests, verify actual behavior
- Use **Mocks** for: Unit tests, isolate component under test

---

**Last Updated:** 2025-11-14