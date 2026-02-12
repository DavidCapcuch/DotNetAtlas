# Architecture

**Analysis Date:** 2026-02-12

## Pattern Overview

**Overall:** Domain-Driven Design (DDD) with Clean Architecture layers in a distributed microservices system.

**Key Characteristics:**
- Layered architecture: API → Application → Domain → Infrastructure
- Command/Query Separation (CQS) with behavior pipeline (not CQRS - single datastore per service)
- Transactional Outbox pattern for reliable, at-least-once Kafka messaging
- Idempotent Consumer (Inbox) pattern for exactly-once semantics per service
- MassTransit saga orchestration for distributed transactions
- Event-driven architecture with Kafka for inter-service communication
- Each microservice owns its domain, data model, and infrastructure

## Layers

**Presentation (API):**
- Purpose: HTTP entry points, WebSocket hubs, request handling
- Location: `src/DotNetAtlas.Api`, `services/Order/Ordering.API`, `services/Finance/Payments`, `services/Notifications/Notifications`
- Contains: FastEndpoints route handlers, SignalR hubs, middleware, exception handling, CORS/security configuration
- Depends on: Application layer, platform services (CQS, observability)
- Used by: HTTP clients, browsers, WebSocket clients

**Application:**
- Purpose: CQS handlers (command/query business logic), validation, cross-cutting behavior
- Location: `src/DotNetAtlas.Application`, `services/Order/Ordering.Application`, `services/Finance/Payments.Application`
- Contains: Command handlers, query handlers, validator rules (FluentValidation), mappers, DTOs, domain event handlers
- Depends on: Domain layer, Infrastructure (data access), platform libraries (CQS, ReliableMessaging, KafkaFlow)
- Used by: API endpoints, saga orchestrators, Kafka consumers

**Domain:**
- Purpose: Business logic, entities, aggregates, value objects, domain events
- Location: `src/DotNetAtlas.Domain`, `services/Order/Ordering.Domain`, `services/Finance/Payments.Domain`
- Contains: Aggregate roots (AlertSubscriber, Feedback, AlertSubscriptionOrder, PaymentOrder), entities (Location, MonitoredLocation), value objects (SubscriptionTier, Money, Temperature, etc.), domain events, specifications (for queries), error definitions
- Depends on: SharedKernel (base types: Entity, AggregateRoot, ValueObject, Result<T>, DomainEvent)
- Used by: Application handlers, saga state machines

**Infrastructure:**
- Purpose: Data persistence, external integrations, background jobs, Kafka configuration
- Location: `src/DotNetAtlas.Infrastructure`, `services/Order/Ordering.Infrastructure`, `services/Finance/Payments.Infrastructure`
- Contains: DbContext implementations, EF Core mappings/migrations, Kafka consumer/producer configuration, Hangfire job definitions, repository implementations, database seeders
- Depends on: Domain layer (entities), platform libraries (ReliableMessaging, OutboxRelay, KafkaFlow), third-party (EF Core, KafkaFlow, Hangfire)
- Used by: Application handlers, API configuration

**Platform (Shared Libraries):**
- Purpose: Cross-cutting concerns shared by all services
- Location: `platform/`
- Key modules:
  - `DotNetAtlas.CQS`: ICommand, IQuery, ICommandHandler, IQueryHandler interfaces + behavior decorators (validation, logging, metrics, tracing)
  - `DotNetAtlas.SharedKernel`: Base types (Entity, AggregateRoot, ValueObject, Result<T>, DomainEvent, Error, IAuditableEntity)
  - `DotNetAtlas.ReliableMessaging.*`: Transactional Outbox (Core, EFCore) and Idempotent Inbox (Core, EFCore) implementations
  - `DotNetAtlas.KafkaFlow.*`: Kafka extensions (dead letter, inbox, producer headers)
  - `DotNetAtlas.OutboxRelay.*`: Background worker that reads outbox messages and publishes to Kafka
  - `DotNetAtlas.SchemaRegistry.Contracts`: Avro schemas (.avsc files) and generated C# message types
  - `DotNetAtlas.ServiceDefaults`: Serilog, OpenTelemetry, health checks, security headers

**Saga Orchestration:**
- Purpose: Distributed transaction management across services
- Location: `saga/DotNetAtlas.Sagas`
- Contains: MassTransit state machines, saga state entities, saga event consumers, saga activities (observability), schedules for timeouts
- Depends on: Domain events from other services, platform libraries (CQS, ReliableMessaging, KafkaFlow, SchemaRegistry.Contracts)
- Used by: Kafka consumers, state machine transitions trigger commands to other services

## Data Flow

**Synchronous (REST API):**

1. Request arrives at FastEndpoint (`src/DotNetAtlas.Api/Endpoints/Weather/GetForecastEndpoint.cs`)
2. Endpoint injects `IQueryHandler<GetForecastQuery, GetForecastResponse>` from DI container
3. Handler is wrapped by behavior decorators (validation, logging, tracing)
4. Handler queries domain models via IWeatherDbContext or other repositories
5. Response mapped and returned to HTTP client

**Event-Driven (Domain Events → Kafka):**

1. Aggregate raises domain event via `AddDomainEvent()` (e.g., `SubscriberActivatedDomainEvent`)
2. Domain event handler publishes to transactional outbox in same DB transaction as aggregate update
3. OutboxRelay worker (`platform/DotNetAtlas.OutboxRelay.WorkerService`) polls outbox periodically
4. Reads unpublished messages and publishes to Kafka (at-least-once delivery)
5. Message marked as published in outbox table

**Kafka Consumers (Inbox Pattern):**

1. Service receives Kafka message with CorrelationId header (from schema registry Avro type)
2. Consumer checks inbox table for MessageId - if exists, message already processed (idempotent)
3. If new, consumer executes command/query handler and stores result
4. Stores message in inbox table in same transaction as handler execution
5. Outbox handler publishes resulting domain events back to outbox
6. Cycle repeats: new events → outbox relay → Kafka → consumers

**Saga Orchestration (Distributed Transaction):**

1. Initiating event received (e.g., `AlertSubscriptionPurchaseInitiatedEvent` via Kafka consumer)
2. MassTransit state machine instantiated with saga state persisted to SQL Server
3. State machine publishes command to next service (e.g., PaymentRequestedEvent to Finance.Payments)
4. Subsequent events (PaymentCompletedEvent, AlertSubscriptionActivatedEvent) update saga state
5. On success: state machine finalizes with ActivationCompleted state
6. On failure with compensation flag: publishes RequestRefundCommand back to Finance service
7. All transitions logged with observability activities for tracing

**State Management:**

- Domain state: Managed in aggregate roots (AlertSubscriber, AlertSubscriptionOrder), persisted to database
- Application state: Transient (command/query handlers are stateless)
- Saga state: Persisted in SagaDbContext (`saga/DotNetAtlas.Sagas/Common/Persistence/Database/SagaDbContext.cs`)
- Distributed state: Coordinated via Kafka messages with CorrelationId for causality tracking
- Inbox/Outbox state: Tracked in InboxMessage and OutboxMessage tables for at-least-once/exactly-once guarantees

## Key Abstractions

**Aggregate Roots:**
- Purpose: Encapsulate business logic, raise domain events, maintain invariants
- Examples: `src/DotNetAtlas.Domain/Alerts/AlertSubscriber.cs`, `services/Order/Ordering.Domain/AlertSubscriptionOrders/AlertSubscriptionOrder.cs`
- Pattern: Factory methods for creation, private setters for encapsulation, Result<T> return for domain operations that can fail

**Value Objects:**
- Purpose: Immutable, semantic types representing business concepts
- Examples: `SubscriptionTier`, `Money`, `Temperature`, `AlertThresholds`, `WeatherAlert`
- Pattern: Implement equality by value, validated in constructor, used in aggregates

**Domain Events:**
- Purpose: Signal state changes within aggregate boundary for async processing
- Examples: `SubscriberActivatedDomainEvent`, `AlertSubscriptionOrderCreatedDomainEvent`
- Pattern: Raised via `AddDomainEvent()` in aggregate, published to outbox by handler, consumed by other services

**Specifications (DDD Pattern):**
- Purpose: Encapsulate query logic with strongly-typed filters
- Examples: `src/DotNetAtlas.Domain/Alerts/Specifications/SubscriberByUserIdSpec.cs`
- Pattern: Inherit from Ardalis.Specification.Specification<T>, used with EF Core via `WithSpecification()` extension

**Command/Query Handlers:**
- Purpose: Implement use cases, orchestrate domain operations, produce side effects
- Examples: `src/DotNetAtlas.Application/WeatherAlerts/PurchaseSubscription/PurchaseSubscriptionCommandHandler.cs`
- Pattern: Injected into FastEndpoints, wrapped by behaviors (validation, logging, tracing), return Result<T>

**Behavior Pipeline (Decorator Pattern):**
- Purpose: Cross-cutting concerns applied to all CQS operations
- Location: `platform/DotNetAtlas.CQS/Behaviors/`
- Behaviors: ValidationBehavior, LoggingBehavior, TracingBehavior, MetricsBehavior
- Pattern: Registered in DI as ordered decorators, each wraps the next handler

## Entry Points

**Main API:**
- Location: `src/DotNetAtlas.Api/Program.cs`
- Triggers: HTTP requests on port 5000+ (development)
- Responsibilities: Request routing via FastEndpoints, SignalR WebSocket connections, Hangfire background jobs, database initialization

**Order Service API:**
- Location: `services/Order/Ordering.API/Program.cs`
- Triggers: HTTP requests on port 5100+ (development), Kafka purchase order events
- Responsibilities: Alert subscription order creation/retrieval, command-driven order processing

**Saga Worker Service:**
- Location: `saga/DotNetAtlas.Sagas/Program.cs`
- Triggers: MassTransit saga events from Kafka
- Responsibilities: Saga state machine orchestration, timeout scheduling, compensation flow

**OutboxRelay Worker:**
- Location: `platform/DotNetAtlas.OutboxRelay.WorkerService/`
- Triggers: Timed background job (configurable interval)
- Responsibilities: Poll outbox tables, publish unpublished messages to Kafka, mark as published

**Kafka Consumers:**
- Triggers: Kafka topic messages
- Responsibilities: Idempotent message processing via inbox pattern, command/event handling, domain event publishing to outbox
- Examples: AlertSubscriptionPurchaseInitiatedConsumer, WeatherAlertIssuedConsumer

## Error Handling

**Strategy:** Result<T> monadic approach (FluentResults library) for domain/application errors; exceptions for infrastructure/unexpected failures.

**Patterns:**

- **Domain Errors:** Return Result.Fail() from aggregate methods or handlers. Converted to HTTP 400/422 by endpoint response handling
- **Validation Errors:** FluentValidation validators return ValidationError[] wrapped in Result.Fail(), caught by ValidationBehavior before reaching handler
- **Infrastructure Errors:** DbContext.SaveChangesAsync() can throw DbUpdateException - unhandled, becomes 500 via GlobalExceptionHandler
- **DataIntegrityException:** Custom exception thrown by aggregates for unintended system bugs (tier transitions, duration validation). Caught by DeadLetterMiddleware in Kafka consumers, sent to Dead Letter Topic
- **Global Exception Handler:** `src/DotNetAtlas.Api/Common/Exceptions/GlobalExceptionHandler.cs` - maps exceptions to HTTP problem details with status codes:
  - ApplicationException → 400 Bad Request
  - TimeoutException → 408 Request Timeout
  - All others → 500 Internal Server Error

**Kafka Consumer Error Handling:**
- Failed message processing sends to Dead Letter Topic (via KafkaFlow.DeadLetter middleware)
- DeadLetterMiddleware intercepts DataIntegrityException and infrastructure errors
- Failed messages can be manually retried or analyzed

## Cross-Cutting Concerns

**Logging:**
- Framework: Serilog with structured logging
- Scope-aware via LogContext.PushProperty() (e.g., LogContext.PushProperty("UserId", request.UserId))
- Configured in Program.cs via AddPlatformSerilog()
- Output: Console (development), can be piped to Seq/Datadog (production)

**Validation:**
- Framework: FluentValidation for command/query validators
- Location: One validator per command/query (e.g., `PurchaseAlertSubscriptionCommandValidator`)
- Applied via ValidationBehavior decorator before handler execution
- Returns ValidationError[] with property name and error message

**Authentication:**
- Framework: Bearer token (JWT) via IHttpAuthenticationService
- Location: `src/DotNetAtlas.Infrastructure/Common/Authorization/`
- Policy-based authorization (DevOnly policy for admin endpoints)
- Enforced at endpoint configuration level (RequireAuthorization)

**Tracing:**
- Framework: OpenTelemetry with W3C Trace Context propagation
- Activities: Created per CQS operation, tagged with observable properties (UserId, CorrelationId, etc.)
- Exporters: Jaeger (dev), Datadog (production)
- Saga activities: Custom Activity implementations for saga state transitions

**Metrics:**
- Framework: OpenTelemetry Meter with System.Diagnostics.Metrics
- Metrics: Command/query execution time, Kafka publish/consume counts
- Exporters: Prometheus (scraped by observability stack)

**Caching:**
- Framework: FusionCache (Redis-backed in production, memory in development)
- Usage: OutputCache for HTTP GET endpoints, custom cache keys for domain lookups
- Invalidation: Manual via cache tags on command handlers that mutate state
