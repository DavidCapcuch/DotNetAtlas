# DotNetAtlas: Modern .NET Reference Solution

DotNetAtlas is a modern, pragmatic blueprint for building high‑quality, maintainable, observable, and testable .NET applications.

It provides:

- **Concrete, runnable examples** that developers can explore locally, instead of just book‑level theory.
- **End‑to‑end solution** that shows how Clean Architecture, DDD, CQS, and event‑driven patterns work together, with demonstrations of best practices for building distributed systems, including guaranteed message delivery, distributed tracing and building for horizontal scalability.
- A **weather‑related domain** (forecasts, feedback, alerts) as a simple but realistic demonstration vehicle.
- **Patterns and components** that can be adapted and reused in real projects as needed.


## Why This Project Exists

Most resources explain individual patterns or tools, but few show how to compose them into a cohesive, production‑ready system. This project provides that complete picture.

DotNetAtlas aims to:

- Bridge the gap between **theoretical patterns** and **practical implementation**.
- Provide a **reference architecture** that can scale from small services to larger systems.
- Serve as a **high‑quality learning resource** for .NET developers, tech leads, and architects.
- Offer a **shared vocabulary and structure** for teams adopting modern .NET practices.

## Problems It Solves

- **Lack of real-world examples** of Clean Architecture + DDD + event‑driven design working together in one coherent solution.
- **Integration complexity** across Kafka, Redis, SignalR, EF Core, and OpenTelemetry while preserving clean boundaries.
- **Testing against real infrastructure dependencies** simulating a real, production-like environment using TestContainers (SQL Server, Kafka, Redis, Schema Registry, etc.).
- **Production readiness**: health checks, observability, resiliency, and CI/CD pipelines.
- **End‑to‑end observability** spanning HTTP APIs, background jobs, messaging, and data stores.

## How It Works

The system demonstrates modern .NET architecture through a simple weather-focused domain:

- **HTTP API**: Weather forecasts, feedback, and admin utilities via FastEndpoints
- **Real-time Communication**: SignalR hub for weather alerts using Redis backplane and background jobs
- **Data Persistence**: EF Core with SQL Server, capturing domain events into Outbox table
- **Event Streaming**: Kafka integration for publishing events either directly (and with fire‑and‑forget) or Outbox pattern for guaranteed delivery
- **Performance**: Multi-level caching (FusionCache + Redis) with resilience policies for reliable responses even under failures
- **Observability**: Complete OpenTelemetry (traces, metrics, logs) instrumentation with visual dashboards

## Who Is This For?

### Software Developers

- **Real-world implementations** of Clean Architecture, DDD, CQS, and event-driven patterns
- **Patterns and components** that can be adapted and reused in real projects as needed
- **Comprehensive observability** showing distributed system behavior
- **Realistic testing strategies** using actual infrastructure components

### Software Architects

- **A reference architecture** demonstrating common enterprise integrations.
- **Examples of cross‑cutting concerns:** authentication, authorization, logging, resilience, configuration, and observability.
- Patterns for scalability, reliability, and operational readiness.
- **DevOps integration** examples with CI/CD and container orchestration

### Technical Teams

- A resource for understanding modern .NET development practices and establishing coding standards, patterns and project setup.
- **Pattern library** for solving common enterprise architecture challenges
- **Implementation examples** for complex integration scenarios

## Core Technology Stack

| Layer | Technology | Purpose |
|-------|------------|---------|
| **Framework** | .NET 10, C# | Core application platform |
| **Database** | SQL Server + EF Core | Primary data persistence |
| **Caching** | Redis + FusionCache | Distributed caching & SignalR backplane |
| **Integration Events** | Kafka + Avro | Event streaming and Schema Registry |
| **Real-time** | SignalR + MessagePack | WebSocket communication |
| **Auth** | FusionAuth (OIDC) | Authentication (Google federation support) |
| **Observability** | OpenTelemetry | Distributed tracing, metrics, logs |
| **Infrastructure** | Docker, Docker Compose | Containerized deployment |

### Key Libraries

#### Core libraries

- **API Framework**: [FastEndpoints](https://fast-endpoints.com/) - High-performance API development with built-in validation and versioning
- **Error Handling**: [FluentResults](https://github.com/altmann/FluentResults) - Result pattern for explicit error handling, leaving exceptions for truly exceptional cases
- **Validation**: [FluentValidation](https://github.com/FluentValidation/FluentValidation) - Strongly-typed validation rules for business logic
- **Specifications**: [Ardalis.Specification](https://github.com/ardalis/specification) - DDD specification pattern for complex queries (replaces repositories)

#### Data & Persistence

- **ORM**: [Entity Framework Core](https://learn.microsoft.com/ef/core/) - Data access and persistence
- **Caching**: [FusionCache](https://github.com/ZiggyCreatures/FusionCache) - Distributed caching, used with [MemoryPack](https://github.com/Cysharp/MemoryPack) serialization

#### Integration & Messaging

- **Kafka Client**: [KafkaFlow](https://github.com/Farfetch/kafka-flow)
- **Schema Registry**: [Confluent.SchemaRegistry](https://github.com/confluentinc/schema-registry) with [Apache.Avro](https://avro.apache.org/) serialization for Kafka messages
- **Real-time:** SignalR with [MessagePack](https://learn.microsoft.com/aspnet/core/signalr/messagepackhubprotocol) and [Redis Backplane](https://learn.microsoft.com/aspnet/core/signalr/redis-backplane) for client coordination in distributed environment

#### Observability & Operations

- **Observability**: [OpenTelemetry](https://opentelemetry.io/) - Standardized telemetry collection (traces, metrics, logs)
- **Logging**: [Serilog](https://serilog.net/) - Structured logging with rich formatting and sinks
- **Health Checks**: [AspNetCore.HealthChecks](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks) - health monitoring for infrastructure and external dependencies

### Background Jobs

- [Hangfire](https://www.hangfire.io/) - Reliable background job processing with dashboard and scheduling

### Project Structure

The solution contains 14 projects:

```
src/                          # Core application layers (DDD + Clean Architecture)
├── DotNetAtlas.Domain       # Pure business logic, zero infrastructure deps
├── DotNetAtlas.Application  # Use cases with CQS handlers and interfaces  
├── DotNetAtlas.Infrastructure # External concerns (DB, Kafka, Redis, etc.)
└── DotNetAtlas.Api          # FastEndpoints, SignalR hubs, Swagger

platform/                     # Reusable platform components
├── DotNetAtlas.Outbox.Core           # Base for Event-driven outbox pattern
├── DotNetAtlas.Outbox.EntityFrameworkCore  # Reusable EF Core outbox integration
├── DotNetAtlas.OutboxRelay.WorkerService   # Outbox message publishing worker
├── DotNetAtlas.OutboxRelay.Benchmark      # Performance testing for OutboxRelay
└── DotNetAtlas.SchemaRegistry             # Avro schema management

test/                         # Comprehensive testing strategy  
├── DotNetAtlas.Test.Framework      # Shared test utilities and containers
├── DotNetAtlas.ArchitectureTests   # Architecture validation (NetArchTest)
├── DotNetAtlas.UnitTests          # Fast, isolated unit tests
├── DotNetAtlas.IntegrationTests   # Real infrastructure integration tests
└── DotNetAtlas.FunctionalTests    # End-to-end API tests
```

## Core Architecture, Design Patterns & Methodologies

### 1. Clean Architecture

Isolates core business logic from infrastructure and external frameworks, ensuring that the business logic focuses solely on the application's rules and behavior.

**Why:** Keeps the core logic flexible, maintainable, and easily testable, unaffected by changes in external systems like databases, APIs, or UI frameworks.

**Dependency Flow** (enforced by architecture tests):

- **Domain** -> No dependencies on outer layers (only FluentResults)
- **Application** -> Depends only on Domain (interfaces for infrastructure)
- **Infrastructure** -> Implements Application interfaces + depends on Domain
- **Api** -> Depends on all layers

### 2. Domain-Driven Design

An approach that organizes the system around the core business domain by modeling it with aggregates, entities, value objects, and domain events. It emphasizes how business rules, behaviors, and invariants are represented in the system, ensuring they align with the real-world domain.

**Why:** Ensures the system reflects the business domain's language, rules, and behavior, keeping the business logic explicit and coherent. It creates a shared understanding of the domain between technical and non-technical teams.

- **Aggregates**: `AggregateRoot<TId>`, acts as the boundary for invariants and domain consistency, raise events via `RaiseDomainEvent()`, `PopDomainEvents()` before SaveChanges
- **Value Objects**: Immutable, equality by value (e.g., **FeedbackText**, **FeedbackRating**), Validate themselves on creation and prevent invalid states
- **Domain Events**: `IDomainEvent`, raised within aggregates, captured by EF interceptor and guaranteed to be published to Kafka by worker service
- **Entities**: Base `Entity<TId>`, identified by a stable ID over time, typically a Guid (e.g., Guid.CreateVersion7() for time-ordered IDs).

### 3. Event-Driven Architecture

Service communication through events

**Why:** Promotes asynchronous, loosely coupled communication across services through event streams (in this case, Kafka) which allows for resilient and scalable systems. Eventual consistency (time between event propagation from Service A to Service B) is a trade-off of this architecture.

**Shows two Event Publishing Strategies**:

1. **Fire-and-Forget**: Non-critical events published immediately (forecast request events)
2. **Outbox Pattern**: Critical events persisted transactionally, published by worker (feedback events), guarantees **at least once** delivery.

### 4. Command Query Separation (CQS) with Decorator Chain (Scrutor)

Division of operations into commands (that change state) and queries (that only read state).

**Why:** Splitting reads and writes communicates exactly what it does, decorators cleanly add cross cutting concerns without polluting business logic and each handler is isolated and straightforward to test. Can diverge into CQRS later if needed.

- **Interfaces**:
  - `ICommandHandler<T>` - Execute use cases that modify state
  - `IQueryHandler<T, TResponse>` - Return data without side effects
- **Decorator Order**: Validation -> Logging -> Tracing -> Handler execution
- **Result Type**: `Result<T>` from FluentResults for success or expected errors

## Development Best Practices

### Code Quality and Build Configuration

- **StyleCop** - Enforced code style across all projects
- **.editorconfig** - Custom coding rules and style
- **Warnings as Errors** - No warnings in builds
- **Directory.Packages.props** for centralized NuGet package versions (separate for test projects)
- **Directory.Build.props** for shared MSBuild properties (separate for test projects)
- **Package Lock Files** - Reproducible builds with caching
- **Whitespace/style format checks in CI** and auto-correction
- **Architecture Tests**: NetArchTest validates Clean Architecture rules
- **Conventional Commits** PRs must conform to Conventional Commits (types: feat, fix, docs, test, ci, refactor, perf, chore, revert).

### Error Handling

- **Result Pattern**: FluentResults for expected errors (validation, not found, conflicts...)
- **Exceptions**: ONLY for unexpected failures (infrastructure, bugs)
- **Domain Errors**: Tagged in OpenTelemetry spans with error code and details
- **HTTP Mapping**: ResultExtensions auto-converts domain errors to HTTP status codes

### Configuration

- **IOptions pattern**: Strongly typed configs, bound from reloadable configuration, always validated with DataAnnotations (or custom Validations), always registered with ValidateOnStart

## Implementation Details

### API Pipeline

- **Middleware**: [`RequestContextEnrichmentMiddleware`](../../../src/DotNetAtlas.Api/Common/Middlewares/RequestContextEnrichmentMiddleware.cs#L13) propagates user id and correlation id to OpenTelemetry baggage

### Database & Persistence

- **Migrations**: EF Core migrations for local env, Flyway SQL scripts for production (MUST generate Flyway from EF migrations)
- **Outbox Interceptor**: Hooks into SaveChangesAsync, extracts domain events, extracts OpenTelemetry data for propagation in Outbox Messages
- **Architecture Test**: Validates that a Flyway script exists for every EF migration
- **Auditing**: IAuditableEntity with auto-populated CreatedUtc/LastModifiedUtc

### Exception Handling

- **Global Handler**: UseExceptionHandler() produces RFC 7807 Problem Details
- **Activity Status**: Failed results mark OpenTelemetry span as error with details

### OpenTelemetry Correlation Flow Example

```
HTTP Request
  → FastEndpoint Handler
  → CQS CommandHandler (traced)
  → Domain Aggregate (events raised)
  → EF Interceptor (events captured)
  → Outbox Worker Service (context restored)
  → Kafka Producer (new span linked)
  → Kafka Consumer (context extracted)
```

### Resilience Patterns

**HTTP Client Resilience**:

- **Retry Policy**: Exponential backoff with jitter
- **Circuit Breaker**: Sampling-based failure detection
- **Timeout Strategy**: Per-attempt + total request timeouts
- **Hedging**: Race multiple providers on timeout/failure

**Caching Strategy (automatically handled by Fusion Cache)**:

- **L1**: In-memory cache for fastest access
- **L2**: Redis distributed cache for multi-instance consistency
- **Backplane**: Redis pub/sub for cache invalidation
- **Resilience**: Fail-safe with stale data, soft/hard timeouts, circuit breaker

### Testing Infrastructure

**Real Infrastructure Testing**:

- **TestContainers**: SQL Server, Kafka, SchemaRegistry, Redis with automatic setup and clean up between tests
- **Architecture Tests**: NetArchTest validates Clean Architecture rules, naming conventions and validates that all EF Core migrations have corresponding Flyway scripts
- **Coverage**: Automated collection and GitHub Pages publishing
- **Parallel Testing**: Tests are organized into separate Test Collections, each Test Collection gets its own fixture with infrastructure setup. These collections run in parallel and avoid data pollution because each one has its own fixture with its own infrastructure.

#### Test Utilities

- **Fixtures**: Core of testing, WebApplicationFactory + TestContainers
- **HttpClientRegistry**: Pre-configured clients (authenticated admin, authenticated user, anonymous)
- **SignalRClientFactory**: Pre-configured SignalR test clients with JWT
- **TestCaseTracer**: Wraps test methods in OpenTelemetry spans -> visible in Jaeger UI with test method name

## Development Workflows

- **Local**: EF Core migrations, docker-compose for infrastructure, on-demand Bogus seeding
- **Testing**: PowerShell script for coverage collection and reporting, TestContainers for real infrastructure
- **Production**: Flyway scripts for DB migrations (generated from EF Core migrations). CI pipeline for docker image creation from Dockerfile, ENV variables from secure vault for secret values overrides (connection strings etc.)

### Docker Compose ([`docker-compose.yaml`](../../../docker-compose.yaml#L1))

#### Infrastructure stack for local development

- **Data**: SQL Server 2022 with auto-migration, Redis 7.4, Redis Insight for Redis UI
- **Messaging**: Kafka (KRaft mode), Schema Registry, AKHQ UI
- **Auth**: FusionAuth (with PostgreSQL + OpenSearch)
- **Application**: Outbox Relay Worker (built from Dockerfile)
- **Health Checks**: All services with proper wait strategies
- **Volumes**: Persistent storage for databases, Kafka, logs

##### Docker Compose Container URLs

- **Jaeger (Tracing):** http://localhost:16686
- **Grafana (Metrics):** http://localhost:3000 (admin/admin)
- **Seq (Logs):** http://localhost:5341
- **AKHQ (Kafka):** http://localhost:8080
- **Redis Insight:** http://localhost:8001

##### App URLs

- **API:** http://localhost:5000
- **Swagger:** http://localhost:5000/swagger
- **Health Checks:** http://localhost:5000/health
- **SignalR Test UI:** http://localhost:5000/signalr-ui
- **Hangfire Dashboard:** http://localhost:5000/hangfire-dashboard

### Dockerfile ([`Dockerfile`](../../../Dockerfile#L1))

- **Multi-Stage Build**:
  1. `base`: mcr.microsoft.com/dotnet/aspnet:10.0.0-noble-chiseled-extra (minimal runtime)
  2. `build`: SDK image with BuildKit cache mounts for NuGet packages
  3. `publish`: Optimized release build with ReadyToRun compilation
  4. `final`: Chiseled image (no shell, minimal attack surface, <100MB)
- **Layer Caching**: NuGet restore cached separately from source compilation
- **Performance**: GC Server mode, Tiered PGO enabled
- **Security**: Non-root user, chiseled images

## DevOps & Infrastructure

### CI/CD Pipeline

#### Composite Workflows ([`main-ci.yml`](../../../.github/workflows/main-ci.yml#L1))

- **Version Determination**: GitVersion semantic versioning
- **Build**: .NET build with version injection
- **Test with Coverage**: Full test suite with real TestContainers
- **Publish Coverage**: Combined coverage reports to GitHub Pages
- **SonarQube Analysis**: Code quality and security scanning
- **Docker Build**: Multi-arch images with attestation and signing

#### Individual Workflows

- **PR Validation** ([`pr-ci.yml`](../../../.github/workflows/pr-ci.yml#L1)):
  - Link validation in Markdown documentation
  - Conventional commit enforcement (semantic versioning)
  - Code formatting validation with dotnet-format
  - StyleCop analyzer violations fail build
- **Test Coverage** ([`test-with-coverage.yml`](../../../.github/workflows/test-with-coverage.yml#L1)):
  - Runs all test projects (Unit, Integration, Functional, Architecture)
  - **TestContainer Image Caching**: Docker images cached in GitHub Actions for faster execution
  - Collects Cobertura coverage from all projects
- **Coverage Publishing** ([`publish-test-coverage.yml`](../../../.github/workflows/publish-test-coverage.yml#L1)):
  - ReportGenerator combines coverage from all test projects
  - Generates HTML report, Cobertura XML, OpenCover XML
  - Publishes summary to GitHub Actions summary page
- **Docker Build** ([`docker-image-build-push-sign.yml`](../../../.github/workflows/docker-image-build-push-sign.yml#L1)):
  - Multi-stage builds with BuildKit layer caching
  - GitVersion tags (SemVer, AssemblySemVer, InformationalVersion)
  - GHCR (GitHub Container Registry) push
  - Image attestation and signing
- **Main Branch Tagging** ([`main-release-tag.yml`](../../../.github/workflows/main-release-tag.yml#L1)):
  - Automatic release tag creation on main branch push
  - GitVersion-based semantic versioning
