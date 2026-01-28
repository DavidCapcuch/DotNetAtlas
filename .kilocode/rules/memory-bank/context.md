# DotNetAtlas - Current Context

## Current State (Updated: 2025-12-31)

### Project Status: **Production-Ready & Feature-Complete with Platform Libraries**

The project is in a **mature, production-ready state** demonstrating enterprise-grade .NET architecture with a comprehensive suite of reusable platform libraries. Recent significant additions include modular platform components for messaging (Inbox/Outbox patterns), CQS with observability, and a complete AlertSubscriber domain model.

### Major Features Implemented

1. **Weather Forecast System** ✅
   - Multiple provider support (WeatherAPI.com, OpenMeteo)
   - Geocoding service integration
   - Hedging strategy racing multiple providers
   - Multi-level caching (L1 memory + L2 Redis)
   - Resilience patterns (retry, circuit breaker, timeout)

2. **Weather Feedback System** ✅
   - DDD aggregate with domain events
   - Transactional outbox pattern
   - Complete trace continuity through async processing
   - Create and update feedback operations

3. **Alert Subscriber System (NEW)** ✅
   - Full subscription lifecycle (Free → Pro → Ultra tiers)
   - Location-based alert subscriptions with tier limits
   - Domain events: Created, Activated, Reactivated, Upgraded, Downgraded, Extended
   - Subscription expiry and downgrade handling
   - Kafka event consumption (SubscriptionPurchased, SubscriptionExtended)
   - Saga pattern compensation via failed event publishing

4. **Real-Time Weather Alerts (SignalR)** ✅
   - City-specific alert subscriptions
   - Custom Redis group management with Lua scripts
   - Automatic background job scheduling
   - Connection lifecycle management
   - Type-safe client/server contracts
   - Redis backplane for horizontal scaling

5. **Platform Libraries (NEW)** ✅
   - **DotNetAtlas.SharedKernel** - Base DDD types (AggregateRoot, Entity, ValueObject, DomainEvent)
   - **DotNetAtlas.CQS** - Complete CQS implementation with behaviors (Validation, Logging, Tracing, Metrics)
   - **DotNetAtlas.Messaging.Abstractions** - Standard message header keys
   - **DotNetAtlas.Inbox.Core** - Inbox entity for idempotent message processing
   - **DotNetAtlas.Outbox.Core** - Outbox entity with OpenTelemetry header support
   - **DotNetAtlas.ReliableMessaging.EFCore** - EF Core integration for Inbox/Outbox
   - **DotNetAtlas.KafkaFlow.DeadLetter** - Dead Letter Topic middleware for KafkaFlow
   - **DotNetAtlas.KafkaFlow.Inbox.EFCore** - Inbox middleware for idempotent Kafka consumption
   - **DotNetAtlas.KafkaFlow.ProducerHeaders** - Automatic message ID and origin headers

6. **Kafka Consumer Infrastructure (NEW)** ✅
   - Middleware pipeline: DeadLetter → Retry → Inbox → TypedHandler
   - Idempotent message processing via database-backed inbox
   - Dead Letter Topic for failed messages with error details
   - Saga compensation pattern with failed event publishing

7. **Outbox Pattern Implementation** ✅
   - Custom reusable library (Core + EntityFrameworkCore)
   - Standalone worker service for publishing
   - OpenTelemetry trace continuity
   - Avro serialization with Schema Registry
   - Grafana monitoring dashboard

8. **Admin & Management Features** ✅
   - Cache management endpoints (clear all, clear by tag)
   - Database seeding for development
   - Dev endpoints for publishing test events (SubscriptionPurchased, SubscriptionExtended)
   - Admin-only authorization policies

9. **Authentication & Authorization** ✅
   - FusionAuth OIDC integration
   - JWT Bearer authentication
   - Google OAuth federation
   - Cookie authentication
   - Policy-based authorization (DevOnly, etc.)
   - Login/Logout endpoints

10. **Comprehensive Testing** ✅
    - TestContainers for real infrastructure
    - Architecture validation (NetArchTest)
    - Unit, Integration, Functional test suites
    - Test tracing visible in Jaeger
    - Test container abstraction (ITestContainer)

11. **Complete Observability** ✅
    - OpenTelemetry instrumentation across all layers
    - CQS metrics (commands_total, queries_total, duration, errors, exceptions)
    - Distributed tracing (Jaeger)
    - Metrics collection (Prometheus + Grafana)
    - Structured logging (Serilog + Seq)
    - Pre-configured dashboards

## Recent Significant Changes

### New Platform Libraries (12 Total)

1. **DotNetAtlas.SharedKernel** - DDD building blocks extracted to reusable library
   - `AggregateRoot<TId>`, `Entity<TId>`, `ValueObject`, `IAuditableEntity`
   - `DomainEvent` base class
   - Error types: `DomainError`, `ValidationError`, `NotFoundError`, `ConflictError`, `ForbiddenError`
   - Exception types: `CriticalException`, `DataIntegrityException`
   - Shared value objects: `City`, `CountryCode`, `GeoCoordinates`

2. **DotNetAtlas.CQS** - Complete CQS implementation as platform library
   - `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>` interfaces
   - `ICommandHandler<TCommand>`, `ICommandHandler<TCommand, TResponse>`, `IQueryHandler<TQuery, TResponse>`
   - Behaviors: `ValidationBehavior`, `LoggingBehavior`, `TracingBehavior`, `MetricsBehavior`
   - `CqsInstrumentation` - Metrics for commands/queries (total, errors, exceptions, duration)
   - `CqsDependencyInjection` - Easy registration via `AddCqsHandlersFromAssembly()`

3. **DotNetAtlas.Messaging.Abstractions** - Standard message header keys
   - `MessageHeaderKeys.MessageId` - For idempotent processing
   - `MessageHeaderKeys.Origin` - Service identifier

4. **DotNetAtlas.KafkaFlow.ProducerHeaders** - Automatic header population
   - `ProducerHeadersMiddleware` - Adds message.id (GUID v7) and origin headers
   - `ProducerHeadersOptions` - Configuration for origin identifier

5. **DotNetAtlas.KafkaFlow.Inbox.EFCore** - Idempotent message consumption
   - `InboxMiddleware` - Deduplicates messages using database-backed inbox
   - `IInboxDbContext` - Abstraction for inbox entity management
   - Configurable per message type via `AddInbox(typeof(MessageType))`

6. **DotNetAtlas.KafkaFlow.DeadLetter** - Dead Letter Topic middleware
   - `DeadLetterMiddleware` - Routes failed messages to DLT
   - Captures exceptions AND FluentResults failures
   - Preserves original headers + adds DLT-specific headers (error, stack trace, original topic/partition/offset)

### New Domain Features

**AlertSubscriber Aggregate** - Full subscription management domain:

- Factory methods: `CreateFree()`, `CreateWithPaidSubscription()`
- Operations: `SubscribeToLocation()`, `UnsubscribeFromLocation()`, `ActivatePaidSubscription()`, `ExtendSubscription()`, `DowngradeToFree()`
- `SubscriptionTier` SmartEnum: Free (5 locations), Pro (25 locations), Ultra (100 locations)
- `Location` entity with `City` and `CountryCode` value objects
- Domain events: SubscriberCreated, SubscriberActivated, SubscriberReactivated, SubscriptionUpgraded, SubscriptionDowngraded, SubscriptionExtended, UserSubscribed, UserUnsubscribed

**Kafka Event Handlers** - Subscription event processing:

- `SubscriptionPurchasedEventKafkaHandler` - Processes purchase events
- `SubscriptionExtendedEventKafkaHandler` - Processes extension events
- Saga compensation via `SubscriptionActivationFailedEvent` publishing

### Kafka Consumer Pipeline Pattern

```text
DeadLetter → Retry (transient errors) → Inbox (idempotency) → TypedHandler
```

## Current Project Metrics

### Structure

- **17 Projects Total**:
  - 4 Core layers (Domain, Application, Infrastructure, Api)
  - 12 Platform projects in platform/ folder
  - 5 Test projects (Test.Framework, ArchitectureTests, UnitTests, IntegrationTests, FunctionalTests)

### Code Organization

- **3 Domain Subdomains**: Forecast, Feedback, Alerts
- **5+ Endpoint Groups**: Weather, Admin, Auth, Dev
- **Multiple Provider Implementations**: WeatherAPI.com, OpenMeteo
- **Custom Infrastructure**: Redis Lua scripts, Outbox interceptor, Test containers

### Infrastructure Services (docker-compose)

- 14+ containers running
- SQL Server, Redis, Kafka, Schema Registry, FusionAuth (+ PostgreSQL + OpenSearch)
- Observability: Jaeger, Prometheus, Grafana, OTel Collector, Seq
- Management UIs: AKHQ (Kafka), Redis Insight, Hangfire Dashboard

## Technology Versions

### Current Stack

- **.NET**: 10.0 (RTM)
- **C#**: 12+ with latest language features
- **EF Core**: 10.0
- **SQL Server**: 2022
- **Redis**: 7.4
- **Kafka**: Latest (KRaft mode)
- **KafkaFlow**: 4.0.1
- **FusionAuth**: Latest with full OIDC support
- **SignalR**: MessagePack protocol, Redis backplane

## What Works Out of the Box

- ✅ `docker-compose up` starts all 14+ services
- ✅ API accessible at `http://localhost:5000`
- ✅ Swagger interactive docs at `http://localhost:5000/swagger`
- ✅ SignalR test UI (Razor Page) - no external UI needed
- ✅ Real-time weather alerts fully functional
- ✅ Multiple weather providers with automatic failover
- ✅ Complete observability stack operational
- ✅ All tests run with `dotnet test` using real infrastructure
- ✅ Admin cache management endpoints
- ✅ Dev database seeding endpoint

## Documentation Status

### Completed ✅

1. **WeatherAlerts Feature Documentation** - Fully documented in architecture.md
   - Real-time alert system architecture
   - SignalR hub implementation details
   - Redis group management strategy with Lua scripts
   - Connection lifecycle management

2. **Geocoding Service Documentation** - Fully documented in architecture.md
   - Integration with weather providers
   - WeatherApiComGeocodingService implementation
   - Error handling and caching strategy

3. **Admin/Dev Endpoints** - Fully documented in architecture.md
   - Cache management capabilities (clear all, clear by tag)
   - Database seeding utilities
   - Security policies and authorization

4. **Provider Strategy Documentation** - Fully documented in architecture.md
   - Multiple weather provider support (WeatherAPI.com, OpenMeteo)
   - Hedging strategy implementation details
   - Provider failover logic and resilience

5. **Test Container Abstractions** - Fully documented in tech.md
   - ITestContainer interface purpose
   - Custom container implementations (SQL, Kafka, Redis, Schema Registry)
   - Benefits and usage patterns

6. **Glossary** - Created glossary.md
   - Comprehensive term definitions
   - Pattern explanations
   - Quick reference guides

### Remaining Tasks

1. **README.md Enhancement**
   - Add comprehensive project description
   - Include quick start guide
   - Feature showcase section
   - Architecture diagrams

## Next Steps (Prioritized)

### Immediate Actions

1. **Update All Memory Bank Files**
   - Document WeatherAlerts system completely
   - Add geocoding service details
   - Update architecture with actual structure
   - Document all endpoint groups

2. **Enhance README.md**
   - Add comprehensive project description
   - Include quick start guide
   - Feature showcase section
   - Architecture diagram

### Future Enhancements

1. **Add Mermaid Diagrams**
   - System architecture
   - SignalR real-time flow
   - Outbox pattern flow
   - Provider hedging strategy

2. **Create Tutorial Documentation**
   - How to add new weather provider
   - How to implement new aggregate
   - How to add SignalR hub method
   - How to create background job

3. **Performance Benchmarking**
   - Outbox worker throughput
   - Cache hit ratios
   - Provider response times

## Code Health

### Quality Metrics

- **StyleCop**: Enforced, zero violations
- **Warnings as Errors**: Enabled, zero warnings
- **Architecture Tests**: Pass, validates Clean Architecture
- **Test Coverage**: High coverage with real infrastructure
- **Code Organization**: Well-structured by feature

### Technical Debt

- **Minimal**: Project is well-maintained
- **TODO Comments**: Few, documented future improvements
- **Flyway Scripts**: Need generation from EF migrations
- **Documentation**: Primary gap is external docs (README)

## Maintenance Notes

### Regular Updates

- **NuGet Packages**: Renovate bot automated
- **.NET Version**: Update when .NET 10 RTM releases
- **Docker Images**: Keep base images current
- **Memory Bank**: Keep synchronized with code changes

## When Memory Bank Was Last Updated

**Last Comprehensive Analysis**: 2025-11-14
**Status**: Memory bank being updated with complete accurate information
**Findings**: Project is significantly more feature-rich than initially documented

This context file reflects the true current state after thorough code analysis.
