# DotNetAtlas - System Architecture

## Analysis Date: 2025-11-14

**Current Status**: Comprehensive analysis of entire codebase completed. This document reflects the actual, current implementation state.

## Overview

DotNetAtlas implements **Clean Architecture** with **Domain-Driven Design** in an event-driven system. The architecture enforces strict layer dependency rules while maintaining high testability and observability.

## Architectural Patterns

### 1. Clean Architecture (Onion Architecture)

```
┌─────────────────────────────────────────┐
│          Presentation Layer             │
│        (Api - FastEndpoints)            │
│  HTTP Controllers, SignalR Hubs         │
└────────────────┬────────────────────────┘
                 │
┌────────────────▼────────────────────────┐
│        Infrastructure Layer             │
│  EF Core, Kafka, Redis, Auth, HTTP      │
│  Implements Application Interfaces      │
└────────────────┬────────────────────────┘
                 │
┌────────────────▼────────────────────────┐
│         Application Layer               │
│  CQS Handlers, Interfaces, DTOs         │
│  Decorated with Validation/Tracing      │
└────────────────┬────────────────────────┘
                 │
┌────────────────▼────────────────────────┐
│           Domain Layer                  │
│  Entities, Value Objects, Events        │
│  Pure business logic, no dependencies   │
└─────────────────────────────────────────┘
```

**Dependency Rules** (enforced by NetArchTest):

- Domain -> No dependencies (only FluentResults)
- Application -> Depends on Domain only
- Infrastructure -> Depends on Application + Domain
- Api -> Depends on all layers

### 2. Domain-Driven Design

**Tactical Patterns Implemented:**

```
Aggregate Root (Feedback)
├── Raises Domain Events (FeedbackCreated, FeedbackChanged)
├── Contains Value Objects (FeedbackText, FeedbackRating)
├── Enforces Invariants (rating 1-5, text max length)
└── Provides Factory Methods (constructor validates)

Domain Event Flow:
1. Aggregate raises event → RaiseDomainEvent()
2. EF Interceptor captures → PopDomainEvents()
3. Outbox persists → Same transaction as entity
4. Worker publishes → Eventually to Kafka
```

**Strategic Patterns:**

- **Bounded Context**: Weather domain (Forecast + Feedback + Alerts)
- **Ubiquitous Language**: Terms match business domain
- **Anti-Corruption Layer**: External weather APIs abstracted

### 3. Command Query Separation (CQS)

Commands and queries are separated with dedicated handlers:

```
Commands → Modify state
Queries → Return data without side effects
```

### Handler Decorators

Handlers are automatically decorated with cross-cutting concerns:

1. **Validation Decorator**
   - FluentValidation integration
   - Validates commands before execution

2. **Tracing Decorator**
   - OpenTelemetry spans for each handler
   - Distributed tracing support

3. **Logging Decorator**
   - Structured logging with Serilog
   - Performance metrics

**Not CQRS** - Same data store for reads/writes, but operations separated:

```
Command Pipeline:
Request → FastEndpoint
  → Validation Decorator (FluentValidation)
    → Logging Decorator (Serilog)
      → Tracing Decorator (OpenTelemetry)
        → Command Handler (Business Logic)
          → Domain Aggregate
            → EF Core + Outbox Interceptor
              → Database Transaction

Query Pipeline:
Request → FastEndpoint
  → Validation Decorator
    → Logging Decorator
      → Tracing Decorator
        → Query Handler
          → Specification (Ardalis)
            → EF Core ReadOnly
              → FusionCache (L1 + L2)
```

### 4. Event-Driven Architecture

**Two Event Publishing Strategies:**

```
Fire-and-Forget (Non-Critical Events):
UserRequest → Handler → Domain Event
  → KafkaFlow Producer → Kafka ✓
  
Outbox Pattern (Critical Events):
UserRequest → Handler → Domain Event
  → Outbox Interceptor → Database + Outbox Table ✓
    → Worker Service (polling) → Kafka ✓
      → Consumer processes with guaranteed delivery
```

**Why Two Strategies?**

- **Fire-and-Forget**: Fast, simple, acceptable if occasional message loss OK
- **Outbox**: Slower, complex, but guarantees "at least once" delivery

**Trace Continuity Flow:**
```
HTTP Request (traceparent: abc-123)
  → Activity.Current captured
    → Handler (span: GetForecast, parent: abc-123)
      → Outbox Interceptor (stores traceparent in headers)
        → Database commit
          → Worker reads outbox + headers
            → Restores Activity.Current
              → Kafka produce (span: OutboxPublish, parent: abc-123)
                → Consumer (extracts traceparent from Kafka headers)
                  → Complete end-to-end trace visible in Jaeger!
```

## Source Code Paths

### Core Layers

**src/DotNetAtlas.Domain/** - Pure domain logic

- Common/AggregateRoot.cs - Base class for aggregates with domain event support
- Common/Entity.cs - Base entity with ID, timestamp, and equality logic
- Common/ValueObject.cs - Marker interface using C# records
- Common/IAggregateRoot.cs - Marker interface
- Common/IAuditableEntity.cs - CreatedUtc/LastModifiedUtc tracking
- Common/Errors/ - Domain error types (DomainError, ValidationError, NotFoundError, etc.)
- Common/Events/IDomainEvent.cs - Domain event interface
- Entities/Weather/Feedback/
  - Feedback.cs - Aggregate root with Guid.CreateVersion7() for time-ordered IDs
  - Events/ - FeedbackCreatedDomainEvent, FeedbackChangedDomainEvent
  - ValueObjects/ - FeedbackText (max 500 chars), FeedbackRating (1-5 range)
  - Errors/ - FeedbackErrors, FeedbackRatingErrors with FluentResults integration
- Entities/Weather/Forecast/
  - CountryCode.cs - SmartEnum for countries
  - Errors/ForecastErrors.cs

**src/DotNetAtlas.Application/** - Use cases and interfaces

- Common/
  - ApplicationDependencyInjection.cs - Scrutor decorator registration
  - Cache/ICacheableItem.cs - Cache tagging interface
  - CQS/
    - ICommand.cs, IQuery.cs - Marker interfaces
    - ICommandHandler.cs, IQueryHandler.cs - Handler contracts
    - Behaviors/ - Validation, Logging, Tracing decorators
  - Data/IWeatherDbContext.cs - EF abstraction
  - Observability/
    - DiagnosticNames.cs - OpenTelemetry tag constants
    - IDotNetAtlasInstrumentation.cs - Custom instrumentation
  - Validators/CityValidator.cs - Shared validation
- WeatherAlerts/
  - Common/
    - GroupInfo.cs - SignalR group metadata
    - WeatherAlertGroupNames.cs - Group naming conventions
    - Abstractions/
      - IGroupManager.cs - Redis group operations
      - IWeatherAlertJobScheduler.cs - Background job scheduling
      - IWeatherAlertNotifier.cs - SignalR notification
    - Contracts/ - SignalR type-safe contracts
      - AlertSubscriptionDto.cs
      - IWeatherAlertClientContract.cs
      - IWeatherAlertHubContract.cs
      - WeatherAlert.cs, WeatherAlertMessage.cs
  - SubscribeForCityAlerts/ - Command + Handler + Validator
  - UnsubscribeFromCityAlerts/ - Command + Handler + Validator
  - SendWeatherAlert/ - Command + Handler + Validator
  - DisconnectCleanup/ - Connection cleanup on disconnect
- WeatherFeedback/
  - WeatherFeedbackMapper.cs - Mapperly mapper
  - SendFeedback/ - Command + Handler + Validator
  - ChangeFeedback/ - Command + Handler + Validator
  - GetFeedback/ - Query + Handler + Validator + Response
  - Common/
    - Specifications/ - Ardalis specifications for queries
    - Validation/ - Shared feedback validators
- WeatherForecast/
  - WeatherForecastMapper.cs - Mapperly mapper
  - GetForecasts/ - Query + Handler + Validator + Response
  - Common/
    - Abstractions/IForecastEventsProducer.cs - Kafka producer
    - Config/ - ForecastCacheOptions, WeatherHedgingOptions
  - Services/
    - CachedWeatherForecastService.cs - FusionCache wrapper
    - HedgingWeatherForecastService.cs - Race multiple providers
    - Abstractions/ - Provider interfaces
      - IGeocodingService.cs
      - IMainWeatherForecastProvider.cs
      - IWeatherForecastProvider.cs
      - IWeatherForecastService.cs
    - Models/GeoCoordinates.cs
    - Requests/ - ForecastRequest, GeocodingRequest

**src/DotNetAtlas.Infrastructure/** - External concerns

- BackgroundJobs/
  - FakeWeatherAlertBackgroundJob.cs - Generates fake alerts
  - FakeWeatherAlertJobScheduler.cs - Hangfire scheduling
  - Common/IBackgroundJob.cs
  - Config/FakeWeatherAlertJobOptions.cs
- Common/
  - ApplicationInfo.cs - Assembly metadata
  - *DependencyInjection.cs - Service registration for each area
    - AuthDependencyInjection.cs
    - BackgroundJobsDependencyInjection.cs
    - HealthChecksDependencyInjection.cs
    - HttpClientsDependencyInjection.cs
    - InfrastructureDependencyInjection.cs (main)
    - MessagingDependencyInjection.cs
    - ObservabilityDependencyInjection.cs
    - PersistenceDependencyInjection.cs
  - Authentication/
    - AuthConfigSections.cs - Config section names
    - AuthPolicySchemes.cs - Auth scheme constants
  - Authorization/
    - AuthPolicies.cs - Custom policies (DevOnly, etc.)
    - AuthScopes.cs - OAuth scopes
    - Roles.cs - User roles
  - Config/ - Strongly-typed configuration classes
  - Extensions/HostEnvironmentExtensions.cs
  - Observability/DotNetAtlasInstrumentation.cs
- HttpClients/WeatherProviders/
  - OpenMeteo/ (implementation not shown in files)
  - WeatherApiCom/
    - WeatherApiComProvider.cs - Full provider with geocoding
    - WeatherApiComGeocodingService.cs - Coordinates lookup
    - WeatherApiComOptions.cs - Configuration
    - WeatherApiForecastResponse.cs - DTO
- Messaging/
  - Kafka/
    - Config/ - KafkaOptions, SchemaRegistryOptions, etc.
    - DomainToAvroMappings/ - Event → Avro mappers
      - FeedbackEventsToAvroMapper.cs
      - ForecastEventsToAvroMapper.cs
    - WeatherForecastEvents/
      - KafkaForecastEventsProducer.cs - Fire-and-forget
      - KafkaForecastEventsProducerOptions.cs
  - SignalR/
    - RedisSignalRGroupManager.cs - Custom Redis Lua group tracking
- Persistence/Database/
  - WeatherDbContext.cs - Main DbContext
  - EntityConfigurations/FeedbackConfiguration.cs - Fluent API
  - Interceptors/
    - UpdateAuditableEntitiesInterceptor.cs - Auto audit fields
  - Migrations/ - EF Core migrations
    - 20251108150508_CreateFeedbackTable.cs
    - 20251112184015_CreateOutboxMessagesTable.cs
    - WeatherDbContextModelSnapshot.cs
    - Flyway/ - (Empty, needs SQL scripts)
  - Seed/
    - DatabaseSeedExtensions.cs - Seeding helpers
    - WeatherFeedbackFaker.cs - Bogus data generation

**src/DotNetAtlas.Api/** - Presentation layer

- Program.cs - Application startup
- Common/
  - ApiDependencyInjection.cs - API-specific services
  - FastEndpointsDependencyInjection.cs - FastEndpoints config
  - SwaggerDependencyInjection.cs - Swagger/OpenAPI setup
  - Config/ - CorsPolicyOptions, SwaggerConfigSections
  - Exceptions/GlobalExceptionHandler.cs - RFC 7807 Problem Details
  - Extensions/
    - MiddlewareExtensions.cs
    - ResultsExtensions.cs - Domain errors → HTTP status codes
  - Middlewares/
    - RequestContextEnrichmentMiddleware.cs - User ID propagation
  - Swagger/
    - AuthDescriptionOperationProcessor.cs - Auth docs
    - SignalRTypesDocumentProcessor.cs - SignalR in OpenAPI
- Endpoints/
  - EndpointGroupConstants.cs
  - Admin/
    - AdminGroup.cs
    - ClearAllCacheEndpoint.cs - FusionCache clear all
    - RemoveCacheByTagEndpoint.cs - Clear by tag
  - Auth/
    - AuthGroup.cs
    - LoginEndpoint.cs - OIDC login
    - LogoutEndpoint.cs - Session termination
  - Dev/
    - DevGroup.cs
    - SeedDatabaseEndpoint.cs - Bogus seeding
  - Weather/
    - WeatherGroup.cs
    - GetForecastEndpoint.cs - Forecast query
    - GetFeedbackByIdEndpoint.cs - Single feedback
    - SendFeedbackEndpoint.cs - Create feedback
    - ChangeFeedbackEndpoint.cs - Update feedback
- SignalRHubs/WeatherAlerts/
  - WeatherAlertHub.cs - Main hub with all methods
  - WeatherAlertNotifier.cs - IWeatherAlertNotifier implementation
- Pages/Index.cshtml - SignalR test UI (Razor Page)
- wwwroot/ - Static files (CSS, JS, Bootstrap, jQuery)

### Platform (Reusable Components)

**platform/DotNetAtlas.Outbox.Core/**

- OutboxMessage.cs - Base outbox entity
- OutboxMessageHeaderExtensions.cs - OTEL Header serialization/deserialization

**platform/DotNetAtlas.Outbox.EntityFrameworkCore/**

- Core/
  - DomainEventExtractionCache.cs - Domain Event extraction cache
  - AvroMappingCache.cs - Domain → Avro mappings cache
  - AvroSerializer.cs - Avro serialization
  - OutboxMessagesBatch.cs - Batch processing
- EntityFramework/
  - IOutboxDbContext.cs - DbSet<OutboxMessage>
  - OutboxInterceptor.cs - Captures events on SaveChanges
- EntityConfiguration/
  - OutboxMessageConfiguration.cs - EF configuration

**platform/DotNetAtlas.OutboxRelay.WorkerService/**

- Program.cs - Worker service startup
- Common/ - Service registration
- Observability/OutboxRelayInstrumentation.cs
- OutboxRelay/
  - OutboxDbContext.cs - Read-only context
  - OutboxMessageRelay.cs - Main polling logic
  - OutboxRelayWorker.cs - Background service
  - DeliveryFailureTracker.cs - Monitors Kafka delivery
  - Config/ - KafkaProducerOptions, OutboxRelayOptions

**platform/DotNetAtlas.OutboxRelay.Benchmark/** - Outbox relay performance benchmarks

**platform/DotNetAtlas.SchemaRegistry/**

- Avro/Weather/
  - Feedback/ - .avsc + generated .cs files
  - Forecast/ - .avsc + generated .cs files
- generate-avro.ps1 - PowerShell generator script
- README.md - Documentation

### Testing

**test/DotNetAtlas.Test.Framework/**
- Common/ITestContainer.cs - Container abstraction
- Database/SqlServerTestContainer.cs - SQL + Flyway
- Kafka/
  - KafkaTestContainer.cs - Kafka + Schema Registry
  - KafkaTestConsumerRegistry.cs - Message assertion
  - SchemaRegistryTestContainer.cs
  - WebHostBuilderExtensions.cs
- Redis/RedisTestContainer.cs - Redis with FLUSHDB
- Tracing/TestCaseTracer.cs - Wrap tests in spans

**test/DotNetAtlas.ArchitectureTests/**
- CleanArchitecture/CleanArchitectureLayerTests.cs - NetArchTest
- Migrations/DatabaseMigrationFilesTests.cs - Flyway validation
- TestFramework/TestContainersNamingTests.cs

**test/DotNetAtlas.UnitTests/**
- WeatherAlerts/Validators/ - FluentValidation tests
- WeatherFeedback/Specifications/ - Ardalis.Specification tests

**test/DotNetAtlas.IntegrationTests/**
- Application/
  - WeatherAlerts/ - All alert command handler tests
  - WeatherFeedback/ - Feedback handlers + outbox tests
  - WeatherForecast/ - Caching + hedging tests
- Infrastructure/
  - HttpClients/ - Provider integration tests
  - Kafka/ - Event publishing tests
  - SignalR/ - Redis group manager tests

**test/DotNetAtlas.FunctionalTests/**
- ApiEndpoints/ - End-to-end API tests
  - Dev/SeedDatabaseTests.cs
  - WeatherFeedback/ - All feedback endpoints
  - WeatherForecast/GetForecastsTests.cs
- SignalR/ - SignalR hub + background job tests
- Common/
  - ApiTestFixture.cs - WebApplicationFactory
  - Clients/ - Pre-configured test clients

## Key Architectural Decisions

### 1. FastEndpoints vs Minimal APIs vs Controllers

**Decision**: FastEndpoints
**Rationale**: Better organization, built-in validation/versioning, CQRS-friendly
**Trade-off**: Less common, team learning curve

### 2. Outbox Pattern Implementation

**Decision**: Custom implementation (not library like MassTransit)
**Rationale**: Learning exercise, lightweight, reusable
**Trade-off**: Need to maintain custom code

### 3. TestContainers vs Mocks

**Decision**: Real infrastructure with TestContainers  
**Rationale**: Tests validate actual behavior, catches integration issues
**Trade-off**: Slower test execution, Docker required

### 4. EF Core + Flyway Dual Strategy

**Decision**: EF migrations for dev, Flyway SQL for production
**Rationale**: Fast iteration in dev, reviewable SQL for production, DBA-friendly
**Trade-off**: Must generate Flyway from EF migrations

### 5. Result Pattern vs Exceptions

**Decision**: FluentResults for expected errors
**Rationale**: Explicit error handling, better tracing, avoids exception overhead
**Trade-off**: More verbose than throw/catch

### 6. Redis Lua Scripts for SignalR Group Management

**Decision**: Lua scripts vs multiple Redis commands
**Rationale**: Atomicity without distributed locks, single round-trip
**Trade-off**: Harder to debug, Lua knowledge required

## Critical Implementation Paths

### Path 1: HTTP Request → DB → Kafka (with Outbox)
```
1. Browser → POST /api/v1/weather/feedback
2. FastEndpoint receives request
3. RequestContextEnrichmentMiddleware adds user.id to baggage
4. Validation decorator validates SendFeedbackCommand
5. Logging decorator logs command
6. Tracing decorator creates Activity span
7. SendFeedbackCommandHandler invoked
8. Creates Feedback aggregate
9. Aggregate raises FeedbackCreatedDomainEvent
10. Handler calls dbContext.SaveChangesAsync()
11. OutboxInterceptor intercepts SaveChanges
12. Extracts domain events (PopDomainEvents)
13. Maps event → Avro schema
14. Captures Activity.Current headers
15. Creates OutboxMessage with payload + headers
16. Saves Feedback + OutboxMessage in same transaction
17. Worker polls OutboxMessages table
18. Deserializes OpenTelemetry headers
19. Restores Activity.Current
20. Produces to Kafka
21. Deletes successfully delivered messages
```

### Path 2: Cached Query with Resilience
```
1. Browser → GET /api/v1/weather/forecast?city=Prague
2. GetForecastQueryHandler invoked
3. CachedWeatherForecastService checks FusionCache
4. L1 (memory) miss → L2 (Redis) miss
5. Factory function invoked
6. HedgingWeatherForecastService races providers
7. Primary provider with timeout
8. If timeout: parallel hedged requests
9. Task.WhenEach - first success wins
10. HttpClient applies resilience (retry, circuit breaker)
11. Returns data
12. FusionCache stores L1 + L2
13. OpenTelemetry traces entire flow
```

### Path 3: SignalR Real-Time with Redis Group Management
```
1. User connects to /weatherAlertHub
2. Calls SubscribeForCityAlerts("Prague", "CZ")
3. SubscribeForCityAlertsCommandHandler invoked
4. Validates and geocodes city
5. RedisSignalRGroupManager.AddConnectionIdToGroup()
6. Lua script executes atomically:
   - SADD connection:groups
   - If added, INCR group:count
7. Returns memberCount = 1 (first subscriber)
8. FakeWeatherAlertJobScheduler.ScheduleJob()
9. Hangfire creates recurring job
10. Job generates fake alerts
11. Hub.Clients.Group("weather-alert-Prague-CZ").SendAlert()
12. Redis backplane propagates to all servers
13. User receives real-time alert via WebSocket
```
## Feature Deep Dives

### Weather Alerts System (SignalR Real-Time)

**Purpose**: City-specific weather alert subscriptions with automatic background job management

**Architecture**:
```
Client → SignalR Hub → Command Handler
  → Group Manager (Redis Lua) → Background Job Scheduler (Hangfire)
    → Worker generates alerts → SignalR notification → All subscribers
```

**Key Components**:

1. **SignalR Hub** ([`WeatherAlertHub.cs`](../../../src/DotNetAtlas.Api/SignalRHubs/WeatherAlerts/WeatherAlertHub.cs#L1))
   - Type-safe contracts via `IWeatherAlertHubContract` and `IWeatherAlertClientContract`
   - Methods: `SubscribeForCityAlerts`, `UnsubscribeFromCityAlerts`
   - JWT authentication required
   - Connection lifecycle managed via `OnDisconnectedAsync`

2. **Redis Group Management** ([`RedisSignalRGroupManager.cs`](../../../src/DotNetAtlas.Infrastructure/Messaging/SignalR/RedisSignalRGroupManager.cs#L1))
   - Custom implementation using Redis Lua scripts
   - Atomic operations: add/remove connection, track member counts
   - Group naming: `weather-alert-{City}-{CountryCode}` (e.g., "weather-alert-Prague-CZ")
   - Data structures:
     - `connection:{connectionId}:groups` - SET of groups per connection
     - `group:{groupName}:count` - Member count per group
   
3. **Background Job Scheduling** ([`FakeWeatherAlertJobScheduler.cs`](../../../src/DotNetAtlas.Infrastructure/BackgroundJobs/FakeWeatherAlertBackgroundJob.cs#L1))
   - Hangfire recurring jobs per subscribed city
   - Job ID format: `weather-alert-job-{City}-{CountryCode}`
   - Automatic cleanup when last subscriber leaves
   - Configurable alert generation interval

4. **Alert Notification Flow**:
   ```
   Background Job → IWeatherAlertNotifier → Hub.Clients.Group()
     → Redis Backplane (pub/sub) → All connected servers
       → WebSocket → Connected clients in group
   ```

**Connection Lifecycle**:
1. Client connects → JWT validated
2. Client calls `SubscribeForCityAlerts("Prague", "CZ")`
3. City validation + geocoding check
4. Redis adds connection to group atomically
5. If first subscriber → Hangfire job created
6. Client receives real-time alerts
7. On disconnect → Cleanup command invoked
8. Redis removes connection from all groups
9. If last subscriber → Hangfire job deleted

**Horizontal Scaling**:
- Redis backplane ensures alerts reach clients on any server
- Group membership tracked in Redis (shared state)
- No server affinity required

### Geocoding Service Integration

**Purpose**: Convert city names to geographic coordinates for weather providers

**Interface**: [`IGeocodingService`](../../../src/DotNetAtlas.Application/WeatherForecast/Services/Abstractions/IGeocodingService.cs#L1)
```csharp
Task<Result<GeoCoordinates>> GetCoordinatesAsync(
    GeocodingRequest request, 
    CancellationToken cancellationToken);
```

**Implementation**: [`WeatherApiComGeocodingService`](../../../src/DotNetAtlas.Infrastructure/HttpClients/WeatherProviders/WeatherApiCom/WeatherApiComGeocodingService.cs#L1)

**How It Works**:
1. Accepts city name + optional country code
2. Calls WeatherAPI.com geocoding endpoint
3. Returns `GeoCoordinates` (latitude, longitude)
4. Cached for performance (city names rarely change coordinates)
5. Used by weather providers that require coordinates

**Error Handling**:
- City not found → Returns `ForecastErrors.CityNotFound`
- API failure → Resilience policies apply (retry, circuit breaker)
- Invalid input → Validation errors via FluentValidation

**Integration Points**:
- Used by `HedgingWeatherForecastService` before provider calls
- Used by `SubscribeForCityAlertsCommandHandler` for validation
- Abstracted via interface for provider independence

### Admin & Dev Endpoints

**Purpose**: Operational management and development utilities

#### Admin Endpoints (Require Admin Policy)

**1. Clear All Cache** ([`ClearAllCacheEndpoint.cs`](../../../src/DotNetAtlas.Api/Endpoints/Admin/ClearAllCacheEndpoint.cs#L1))
- **Route**: `DELETE /api/v1/admin/cache`
- **Action**: Clears both L1 (memory) and L2 (Redis) caches
- **Use Case**: Force cache refresh after data changes
- **Implementation**: `FusionCache.Clear()`

**2. Remove Cache by Tag** ([`RemoveCacheByTagEndpoint.cs`](../../../src/DotNetAtlas.Api/Endpoints/Admin/RemoveCacheByTagEndpoint.cs#L1))
- **Route**: `DELETE /api/v1/admin/cache/{tag}`
- **Action**: Invalidates all cache entries with specified tag
- **Use Case**: Selective cache invalidation (e.g., all forecast caches)
- **Tags Used**: `weather-forecast`, `weather-feedback`
- **Implementation**: `FusionCache.RemoveByTag(tag)`

#### Dev Endpoints (Require Development Environment)

**1. Seed Database** ([`SeedDatabaseEndpoint.cs`](../../../src/DotNetAtlas.Api/Endpoints/Dev/SeedDatabaseEndpoint.cs#L1))
- **Route**: `POST /api/v1/dev/seed`
- **Action**: Generates fake weather feedback data using Bogus
- **Policy**: DevOnly (only in Development environment)
- **Configuration**: Configurable number of records to generate
- **Use Case**: Local development, testing, demos
- **Implementation**: Uses `WeatherFeedbackFaker` to generate realistic data

**Security**:
- Admin endpoints require authenticated user with Admin role
- Dev endpoints blocked in non-Development environments
- Authorization enforced via FastEndpoints policies

### Weather Provider Strategy

**Purpose**: Resilient, high-performance weather data retrieval with automatic failover

**Architecture**: Multi-provider hedging with geocoding abstraction

#### Provider Abstraction

**Interfaces**:
1. [`IWeatherForecastProvider`](../../../src/DotNetAtlas.Application/WeatherForecast/Services/Abstractions/IWeatherForecastProvider.cs#L1) - Base provider contract
2. [`IMainWeatherForecastProvider`](../../../src/DotNetAtlas.Application/WeatherForecast/Services/Abstractions/IMainWeatherForecastProvider.cs#L1) - Primary provider marker
3. [`IGeocodingService`](../../../src/DotNetAtlas.Application/WeatherForecast/Services/Abstractions/IGeocodingService.cs#L1) - Coordinate resolution

**Current Providers**:
1. **WeatherAPI.com** (Primary)
   - Full-featured with built-in geocoding
   - Implementation: [`WeatherApiComProvider`](../../../src/DotNetAtlas.Infrastructure/HttpClients/WeatherProviders/WeatherApiCom/WeatherApiComProvider.cs#L1)
   - Geocoding: [`WeatherApiComGeocodingService`](../../../src/DotNetAtlas.Infrastructure/HttpClients/WeatherProviders/WeatherApiCom/WeatherApiComGeocodingService.cs#L1)

2. **OpenMeteo** (Fallback)
   - Free, no API key required
   - Requires geocoding service for coordinates
   - Implementation: `OpenMeteoProvider` (in Infrastructure layer)

#### Hedging Strategy

**Implementation**: [`HedgingWeatherForecastService`](../../../src/DotNetAtlas.Application/WeatherForecast/Services/HedgingWeatherForecastService.cs#L1)

**How Hedging Works**:
```
1. Request comes in for city "Prague"
2. Geocode city → coordinates (50.0755, 14.4378)
3. Call primary provider (WeatherAPI.com) with timeout
4. If primary times out or fails:
   - Launch parallel "hedged" requests to backup providers
   - Use Task.WhenAny - first successful response wins
   - Cancel remaining tasks
5. Return fastest successful result
```

**Configuration** ([`WeatherHedgingOptions`](../../../src/DotNetAtlas.Application/WeatherForecast/Common/Config/WeatherHedgingOptions.cs#L1)):
- Primary provider timeout threshold
- Enabled/disabled hedging
- List of hedged providers

**Benefits**:
- Reduces perceived latency when primary provider is slow
- Automatic failover if primary provider fails
- Transparent to caller (abstracted by service layer)

**Resilience Applied at Each Provider**:
- **Retry**: 3 attempts with exponential backoff
- **Circuit Breaker**: Opens after 50% failure rate
- **Timeout**: Per-attempt (5s) + total (15s)
- **Tracing**: Each provider call creates OpenTelemetry span

#### Caching Layer

**Implementation**: [`CachedWeatherForecastService`](../../../src/DotNetAtlas.Application/WeatherForecast/Services/CachedWeatherForecastService.cs#L1)

**Cache Strategy**:
```
Request → Check L1 (memory) → Check L2 (Redis) → Factory (HedgingService)
  → Store in L2 → Store in L1 → Return
```

**Configuration** ([`ForecastCacheOptions`](../../../src/DotNetAtlas.Application/WeatherForecast/Common/Config/ForecastCacheOptions.cs#L1)):
- Cache duration (default: 15 minutes)
- Cache key format: `weather-forecast:{city}:{countryCode}`
- Cache tags: `weather-forecast` (for bulk invalidation)

**Fail-Safe Behavior**:
- If all providers fail → Serve stale cached data
- If cache is empty → Return error to caller
- Soft timeout: Try to refresh in background
- Hard timeout: Serve stale immediately

## Observability Architecture

### Trace Propagation

- **W3C Traceparent**: Flows through HTTP, DB, Kafka, SignalR
- **Baggage**: user.id + correlation.id in all operations
- **Semantic Conventions**: OpenTelemetry standard attributes

### Metrics Collection

- ASP.NET Core: Request rate, duration, errors
- EF Core: Query count, duration
- Redis: Command rate, latency
- Kafka: Produce/consume rate, lag
- Custom: Cache hit ratio, outbox lag

### Log Aggregation

- Serilog → Seq (structured queries)
- Serilog → OpenTelemetry → Collector → Jaeger
- Console with tree-style formatting for local dev

## Security Architecture

### Authentication

- **JWT Bearer**: API authentication
- **OpenID Connect**: User login via FusionAuth
- **Google OAuth**: Federated identity
- **Cookie**: Session management

### Authorization

- **Policy-Based**: Custom policies (DevOnly, etc.)
- **Scope-Based**: OAuth scopes for API access
- **Role-Based**: User roles in claims

### Data Protection

- **PII**: No sensitive data in logs/traces
- **Connection Strings**: Injected via environment variables
- **Secrets**: Never committed to repository
- **HTTPS**: Required in production

## Component Integration Map

```
┌─────────────┐      ┌──────────────┐      ┌──────────────┐
│   Browser   │─────▶│  FastEndpoint│─────▶│ CQS Handler  │
│  (Swagger)  │◀─────│   (API)      │◀─────│(Application) │
└─────────────┘      └──────────────┘      └────────┬─────┘
                            │                       │
                            │                       ▼
┌─────────────┐      ┌──────────────┐      ┌──────────────┐
│   Jaeger    │◀─────│  OTel Coll.  │◀─────│  Aggregate   │
│  (Traces)   │      │              │      │   (Domain)   │
└─────────────┘      └──────────────┘      └────────┬─────┘
                            ▲                       │
                            │                       ▼
┌─────────────┐      ┌──────────────┐      ┌──────────────┐
│   Grafana   │◀─────│  Prometheus  │      │   EF Core    │
│ (Metrics)   │      │              │      │+Outbox Inter.│
└─────────────┘      └──────────────┘      └────────┬─────┘
                                                    │
┌─────────────┐      ┌──────────────┐               ▼
│     Seq     │◀─────│   Serilog    │      ┌──────────────┐
│   (Logs)    │      │              │      │  SQL Server  │
└─────────────┘      └──────────────┘      │  +Outbox Tbl │
                                           └───────┬──────┘
┌─────────────┐      ┌──────────────┐              │
│ SignalR Hub │◀────▶│    Redis     │              ▼
│(Real-time)  │      │+Backplane+Lua│      ┌──────────────┐
└─────────────┘      └──────────────┘      │Outbox Worker │
       ▲                    ▲              │   Service    │
       │                    │              └───────┬──────┘
┌──────┴──────┐      ┌──────┴──────┐               │
│  Hangfire   │      │FusionCache  │               ▼
│(Background) │      │  L1 + L2    │      ┌──────────────┐
└─────────────┘      └─────────────┘      │    Kafka     │
                                           │+SchemaReg    │
                                           └──────────────┘
```

### Deployment Architecture

#### Version Management

- **GitVersion**: Semantic versioning
- **Environment-Specific**: Configuration per environment
- **Secrets**: Azure Key Vault integration for production

#### Container Strategy

- **Multi-Stage Dockerfile**: Optimized production images
- **Security**: Non-root user, minimal attack surface
- **Performance**: ReadyToRun compilation, tiered PGO
- **Observability**: OpenTelemetry auto-instrumentation

#### Production Considerations

- **Health Monitoring**: Comprehensive health checks
- **Scaling**: Horizontal scaling with Redis backplane
- **Monitoring**: Full observability stack integration
- **Security**: Authentication, authorization, and input validation

## Evolution Path

The architecture supports evolution to:

1. **CQRS**: Separate read models (add projections)
2. **Microservices**: Split by bounded context
3. **Event Sourcing**: Store events instead of state
4. **Sagas**: Distributed transaction coordination
5. **Service Mesh**: Istio/Linkerd for advanced routing

Current design makes these transitions possible without complete rewrite.
