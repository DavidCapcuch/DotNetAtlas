# Codebase Structure

**Analysis Date:** 2026-02-12

## Directory Layout

```
DotNetAtlas/
├── .planning/                  # GSD planning documents (you are reading this)
├── platform/                   # Shared libraries and infrastructure
│   ├── DotNetAtlas.CQS/                        # CQS interfaces and behavior decorators
│   ├── DotNetAtlas.SharedKernel/               # Base domain types (Entity, AggregateRoot, ValueObject, Result<T>)
│   ├── DotNetAtlas.ReliableMessaging.*/        # Transactional Outbox and Idempotent Inbox patterns
│   ├── DotNetAtlas.KafkaFlow.*/                # Kafka extensions (dead letter, inbox, producer headers)
│   ├── DotNetAtlas.OutboxRelay.WorkerService/  # Background worker for outbox relay to Kafka
│   ├── DotNetAtlas.SchemaRegistry.Contracts/   # Avro schemas and generated C# types
│   └── DotNetAtlas.ServiceDefaults/            # Serilog, OpenTelemetry, health checks
├── src/                        # Main application (weather domain)
│   ├── DotNetAtlas.Api/                        # FastEndpoints, SignalR hubs, middleware
│   ├── DotNetAtlas.Application/                # Command/query handlers, validators
│   ├── DotNetAtlas.Domain/                     # Aggregates, entities, value objects, domain events
│   └── DotNetAtlas.Infrastructure/             # EF Core DbContext, Kafka config, background jobs
├── services/                   # Microservices (each with own domain boundary)
│   ├── Order/                                  # Alert subscription ordering service
│   │   ├── Ordering.API/                       # FastEndpoints for order operations
│   │   ├── Ordering.Application/               # Order command/query handlers
│   │   ├── Ordering.Domain/                    # AlertSubscriptionOrder aggregate
│   │   └── Ordering.Infrastructure/            # OrderingDbContext, Kafka configuration
│   ├── Finance/Payments/                       # Payment processing service
│   │   ├── Program.cs                          # Entry point
│   │   ├── Payments.API/                       # Payment endpoints
│   │   ├── Payments.Application/               # Payment handlers
│   │   ├── Payments.Domain/                    # PaymentOrder aggregate
│   │   └── Payments.Infrastructure/            # PaymentDbContext
│   └── Notifications/Notifications/            # Email notification delivery service
├── saga/                       # Saga orchestration (distributed transactions)
│   └── DotNetAtlas.Sagas/                      # MassTransit state machines
│       ├── Orders/                             # Saga orchestrators for orders
│       │   ├── AlertSubscriptionPurchaseSaga/  # Purchase saga: payment → activation
│       │   └── AlertSubscriptionExtensionSaga/ # Extension saga: payment → extension
│       └── Common/                             # Shared saga infrastructure, persistence
├── test/                       # Test projects
│   ├── DotNetAtlas.UnitTests/
│   ├── DotNetAtlas.IntegrationTests/
│   ├── DotNetAtlas.FunctionalTests/
│   ├── DotNetAtlas.ArchitectureTests/
│   └── sagaTests/
├── docker-compose.yml          # Local infrastructure (SQL Server, Redis, Kafka, Jaeger, Seq)
├── DotNetAtlas.slnx            # Solution file (modern XML format)
├── Directory.Packages.props     # Centralized NuGet version management (root)
└── .editorconfig               # Code style rules (file-scoped namespaces, StyleCop, SonarAnalyzer)
```

## Directory Purposes

**platform/DotNetAtlas.CQS:**
- Purpose: Cross-cutting command/query infrastructure
- Contains: ICommand, IQuery, ICommandHandler, IQueryHandler interfaces; behavior decorators (validation, logging, metrics, tracing)
- Key files: `ICommand.cs`, `ICommandHandler.cs`, `IQuery.cs`, `IQueryHandler.cs`, `Behaviors/ValidationBehavior.cs`, `Behaviors/LoggingBehavior.cs`

**platform/DotNetAtlas.SharedKernel:**
- Purpose: Shared domain base types and utilities
- Contains: Entity<TId>, AggregateRoot<TId>, ValueObject, DomainEvent, Result<T>, Error, Exception types
- Used by: All domain projects

**platform/DotNetAtlas.ReliableMessaging.***:
- Purpose: Implement transactional outbox (at-least-once) and inbox (exactly-once) patterns
- Contains: OutboxMessage, InboxMessage entities; EF Core configurations; extension methods for registration
- Subprojects: Core (interfaces), EFCore (implementations, DbContext mixins)

**platform/DotNetAtlas.KafkaFlow.***:
- Purpose: Kafka-specific extensions and middleware
- Contains: Dead letter topic middleware, inbox middleware for idempotent consumers, producer header middleware for W3C trace context
- Subprojects: DeadLetter, Inbox.EFCore, ProducerHeaders

**platform/DotNetAtlas.OutboxRelay.WorkerService:**
- Purpose: Standalone background service that publishes outbox messages to Kafka
- Contains: Background job configuration, health checks, observability (logging, metrics, tracing)
- Runs independently as separate .NET worker service; polls all service databases for unpublished outbox messages

**platform/DotNetAtlas.SchemaRegistry.Contracts:**
- Purpose: Avro message schemas and generated C# types
- Contains: Directory structure mirrors domain topics: `Avro/Weather/Alerts/`, `Avro/Finance/Payments/`, `Avro/Order/AlertSubscriptions/`, `Avro/Notifications/`
- Files: Both `.avsc` schema files and auto-generated `.cs` C# types
- Used by: All services for Kafka message serialization/deserialization

**src/DotNetAtlas.Api:**
- Purpose: Main REST API entry point
- Contains: `Endpoints/` (FastEndpoints implementations), `SignalRHubs/` (WebSocket hubs), `Common/` (middleware, exception handling, CORS), `Pages/` (Razor pages)
- Key files: `Program.cs`, `Endpoints/Weather/GetForecastEndpoint.cs`, `SignalRHubs/WeatherAlerts/WeatherAlertHub.cs`

**src/DotNetAtlas.Application:**
- Purpose: Business logic and use case handlers
- Contains: `WeatherAlerts/` (command handlers, queries), `WeatherFeedback/`, `WeatherForecast/`, `Common/` (CQS behaviors, validators, observability)
- Structure: Each domain entity has its own folder with command/query subfolders
- Pattern: One handler per command/query, validators co-located with commands

**src/DotNetAtlas.Domain:**
- Purpose: Domain model and business rules
- Contains: `Alerts/` (AlertSubscriber aggregate, Location/MonitoredLocation entities), `Feedback/`, `Forecast/`, `Common/` (base types, errors, value objects)
- Key files: `Alerts/AlertSubscriber.cs` (main aggregate), `Alerts/Entities/Location.cs`, `Alerts/ValueObjects/SubscriptionTier.cs`, `Alerts/Specifications/` (query specs)

**src/DotNetAtlas.Infrastructure:**
- Purpose: Data persistence and external integrations
- Contains: `Persistence/Database/` (DbContext, EF Core mappings, migrations, seeders), `BackgroundJobs/` (Hangfire jobs), `Common/` (authorization, exception handlers)
- Key files: `Persistence/Database/WeatherDbContext.cs`, `Persistence/Database/Migrations/` (EF Core migrations)

**services/Order/Ordering.API:**
- Purpose: Order service REST API
- Contains: FastEndpoints for `PurchaseAlertSubscription`, `ExtendAlertSubscription`, `GetAlertSubscriptionOrderStatus`
- Key files: `AlertSubscriptionOrders/PurchaseAlertSubscription/PurchaseAlertSubscriptionEndpoint.cs`

**services/Order/Ordering.Application:**
- Purpose: Order service business logic
- Contains: Command/query handlers for order creation, status retrieval; domain event handlers
- Key files: `AlertSubscriptions/PurchaseAlertSubscription/PurchaseAlertSubscriptionCommandHandler.cs`

**services/Order/Ordering.Domain:**
- Purpose: Order service domain model
- Contains: `AlertSubscriptionOrders/AlertSubscriptionOrder` aggregate, `Errors/`, `Events/`, `ValueObjects/Money`
- Key files: `AlertSubscriptionOrders/AlertSubscriptionOrder.cs`, `AlertSubscriptionOrders/AlertSubscriptionOrderStatus.cs`

**saga/DotNetAtlas.Sagas:**
- Purpose: Distributed transaction orchestration
- Contains: `Orders/AlertSubscriptionPurchaseSaga/` (MassTransit state machine), `Orders/AlertSubscriptionExtensionSaga/`, `Common/` (database configuration, Kafka setup)
- Key files: `Orders/AlertSubscriptionPurchaseSaga/AlertSubscriptionPurchaseSagaOrchestrator.cs` (state machine definition)

## Key File Locations

**Entry Points:**
- `src/DotNetAtlas.Api/Program.cs` - Main REST API and WebSocket entry point
- `services/Order/Ordering.API/Program.cs` - Order service entry point
- `saga/DotNetAtlas.Sagas/Program.cs` - Saga orchestration worker entry point
- `platform/DotNetAtlas.OutboxRelay.WorkerService/Program.cs` - Outbox relay background worker entry point

**Configuration:**
- `Directory.Packages.props` - Root-level NuGet package version management (centralized versions)
- `services/Directory.Packages.props` - Services-level version overrides
- `DotNetAtlas.slnx` - Solution file (project references, build configuration)
- `.editorconfig` - Code style rules enforced by dotnet format

**Core Logic:**
- `src/DotNetAtlas.Domain/Alerts/AlertSubscriber.cs` - Main aggregate for weather alert subscriptions
- `services/Order/Ordering.Domain/AlertSubscriptionOrders/AlertSubscriptionOrder.cs` - Order aggregate
- `src/DotNetAtlas.Application/WeatherAlerts/PurchaseSubscription/PurchaseSubscriptionCommandHandler.cs` - Purchase subscription use case
- `saga/DotNetAtlas.Sagas/Orders/AlertSubscriptionPurchaseSaga/AlertSubscriptionPurchaseSagaOrchestrator.cs` - Saga state machine

**Testing:**
- `test/DotNetAtlas.UnitTests/` - Unit tests (fast, no external dependencies)
- `test/DotNetAtlas.IntegrationTests/` - Integration tests (use TestContainers + Respawn for DB cleanup)
- `test/DotNetAtlas.FunctionalTests/` - Functional tests (test collections with shared fixtures)
- `test/DotNetAtlas.ArchitectureTests/` - Architecture tests (verify layer dependencies, naming conventions)

## Naming Conventions

**Files:**
- Aggregates: `AggregateNameAggregate.cs` (e.g., `AlertSubscriber.cs`, `AlertSubscriptionOrder.cs`)
- Entities: `EntityName.cs` (e.g., `Location.cs`, `MonitoredLocation.cs`)
- Value Objects: `ValueObjectName.cs` (e.g., `SubscriptionTier.cs`, `Temperature.cs`)
- Commands: `*Command.cs` (e.g., `PurchaseSubscriptionCommand.cs`)
- Command Handlers: `*CommandHandler.cs` (e.g., `PurchaseSubscriptionCommandHandler.cs`)
- Command Validators: `*CommandValidator.cs` (e.g., `PurchaseSubscriptionCommandValidator.cs`)
- Queries: `*Query.cs` (e.g., `GetForecastQuery.cs`)
- Query Handlers: `*QueryHandler.cs` (e.g., `GetForecastQueryHandler.cs`)
- Endpoints: `*Endpoint.cs` (e.g., `GetForecastEndpoint.cs`)
- Domain Events: `*DomainEvent.cs` (e.g., `SubscriberActivatedDomainEvent.cs`)
- Errors: `*Errors.cs` (static class with error factory methods, e.g., `AlertSubscriberErrors.cs`)
- Specifications: `*Spec.cs` (e.g., `SubscriberByUserIdSpec.cs`)
- DbContext: `*DbContext.cs` (e.g., `WeatherDbContext.cs`, `OrderingDbContext.cs`)

**Directories:**
- Use PascalCase for namespace folders (e.g., `WeatherAlerts/`, `PurchaseSubscription/`)
- Organize by feature/aggregate: `DomainArea/FeatureName/` (e.g., `WeatherAlerts/PurchaseSubscription/`, `AlertSubscriptionOrders/`)
- Common shared code: `Common/`, `Base/`
- Configuration: `Config/`
- Value objects grouped: `ValueObjects/`
- Entities grouped: `Entities/`
- Specifications grouped: `Specifications/`
- Errors grouped: `Errors/`
- Events grouped: `Events/`

**Namespaces:**
- File-scoped namespaces enforced (`.editorconfig` enforces `csharp_style_namespace_declarations = file_scoped:silent`)
- Example: `namespace DotNetAtlas.Domain.Alerts.Specifications;` (single line, no braces)

## Where to Add New Code

**New Feature (e.g., new subscription tier):**
- Primary code: Add methods to aggregate `src/DotNetAtlas.Domain/Alerts/AlertSubscriber.cs`
- Domain events: New event class in `src/DotNetAtlas.Domain/Alerts/Events/`
- Application logic: New command/handler in `src/DotNetAtlas.Application/WeatherAlerts/NewFeatureName/`
- Endpoint: New endpoint in `src/DotNetAtlas.Api/Endpoints/Weather/`
- Tests: Co-locate with feature in `test/DotNetAtlas.UnitTests/` and `test/DotNetAtlas.IntegrationTests/`

**New Microservice:**
- Directory structure: `services/DomainName/Service.API/`, `services/DomainName/Service.Application/`, `services/DomainName/Service.Domain/`, `services/DomainName/Service.Infrastructure/`
- Database: Own DbContext in Infrastructure, own set of migrations
- Messaging: Register Kafka consumers in Infrastructure configuration
- Schemas: Add Avro schemas in `platform/DotNetAtlas.SchemaRegistry.Contracts/Avro/DomainName/`

**New API Endpoint:**
- Create endpoint class in `src/DotNetAtlas.Api/Endpoints/FeatureName/` inheriting from `Endpoint<TRequest, TResponse>`
- Endpoint injects `ICommandHandler<TCommand, TResponse>` or `IQueryHandler<TQuery, TResponse>` from DI
- Call handler in `HandleAsync()` method
- Use `result.MatchAsync()` pattern to map to HTTP responses

**New Command/Query:**
- Command class: Inherit from `ICommand` or `ICommand<TResponse>`, place in `src/DotNetAtlas.Application/FeatureName/CommandName/`
- Handler: Implement `ICommandHandler<TCommand>` or `ICommandHandler<TCommand, TResponse>`, in `CommandName/CommandNameCommandHandler.cs`
- Validator: Implement `IValidator<TCommand>` from FluentValidation, in `CommandName/CommandNameCommandValidator.cs`
- Query and query handler follow same pattern

**New Domain Event Handler (Outbox Publisher):**
- Class implementing `IDomainEventHandler<TEvent>` interface
- Uses `IOutboxRepository` to write message to outbox table
- Registered in DI during application startup
- Example: `src/DotNetAtlas.Application/WeatherAlerts/Common/SubscriberActivatedOutboxPublisherDomainEventHandler.cs`

**Utilities and Helpers:**
- Shared helpers: `src/DotNetAtlas.Domain/Common/` or `src/DotNetAtlas.Application/Common/`
- Extension methods: In `Extensions.cs` files (e.g., `src/DotNetAtlas.Api/Common/Extensions/`)
- Validators: In `Common/Validators/` if reused across multiple handlers

## Special Directories

**Migrations:**
- Purpose: EF Core database schema changes
- Generated: Yes (via `dotnet ef migrations add MigrationName`)
- Committed: Yes (SQL scripts generated from migrations used in production)
- Locations: `src/DotNetAtlas.Infrastructure/Persistence/Database/Migrations/`, `services/Order/Ordering.Infrastructure/Persistence/Database/Migrations/`, `saga/DotNetAtlas.Sagas/Common/Persistence/Database/Migrations/`
- SQL Scripts: Also stored alongside migrations for manual application in production

**Avro Schemas:**
- Purpose: Message contracts for Kafka (enables polyglot consumers, schema versioning)
- Generated: `.cs` files auto-generated from `.avsc` schema files
- Committed: Both `.avsc` (source) and `.cs` (generated)
- Location: `platform/DotNetAtlas.SchemaRegistry.Contracts/Avro/`
- Tool: Apache Avro C# code generator (part of build process)

**Domain Errors:**
- Purpose: Strongly-typed domain error definitions
- Pattern: Static error factory methods in `*Errors.cs` classes
- Example: `src/DotNetAtlas.Domain/Alerts/Errors/AlertSubscriberErrors.cs` with methods like `MaxSubscriptionsReached(int limit)`
- Used by: Aggregate methods returning Result.Fail()

**Health Checks:**
- Purpose: Service readiness checks for Kubernetes/container orchestration
- Location: Configured in `AddPresentation()` or infrastructure extension methods
- Used by: Kubernetes probes (liveness, readiness)

**bin/ and obj/**
- Purpose: Build output (compiled assemblies, temporary files)
- Generated: Yes (during `dotnet build`)
- Committed: No (git-ignored)
