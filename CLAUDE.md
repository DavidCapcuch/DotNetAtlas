# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Development Commands

```bash
# Build the entire solution
dotnet build -m

# Restore packages (uses lock files for reproducibility)
dotnet restore --locked-mode

# Run main API
dotnet run --project src/Weather.Api

# Run saga worker service
dotnet run --project saga/DotNetAtlas.Sagas

# Run order service
dotnet run --project services/Order/Ordering.API

# Start local infrastructure (SQL Server, Redis, Kafka, etc.)
docker compose --profile core up -d    # DB + Redis only
docker compose --profile full up -d    # All services (Jaeger, Seq, Kafka, etc.)
```

## Testing

```bash
# Run all tests
dotnet test

# Run all tests with coverage
dotnet test --collect:"XPlat Code Coverage" --settings test/coverlet.runsettings

# Run a single test by name
dotnet test --filter "FullyQualifiedName~ClassName.MethodName"

# Run a specific test project
dotnet test test/DotNetAtlas.UnitTests
dotnet test test/DotNetAtlas.IntegrationTests
dotnet test test/DotNetAtlas.FunctionalTests
dotnet test test/DotNetAtlas.ArchitectureTests
dotnet test saga/DotNetAtlas.Sagas.UnitTests
dotnet test saga/DotNetAtlas.Sagas.IntegrationTests

# Generate HTML coverage report (PowerShell)
.\test\test-coverage.ps1
```

- **Framework:** xUnit v3 with AwesomeAssertions (fluent assertions)
- **Integration tests** use TestContainers (Docker) and Respawn for DB cleanup
- **Functional tests** are organized into test collections that share fixtures (run sequentially within collection, parallel across collections)

## Formatting & Code Style

```bash
# Check formatting
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes

# Fix formatting
dotnet format whitespace --no-restore
dotnet format style --no-restore
```

CI enforces formatting and conventional commits on PRs. The `.editorconfig` defines comprehensive style rules including file-scoped namespaces (enforced), StyleCop analyzers, and SonarAnalyzer rules. `TreatWarningsAsErrors` is enabled globally.

## Architecture Overview

This is a distributed .NET 10 system using **Domain-Driven Design** with **Clean Architecture** layers.

### Project Layout

```
src/                    Main API (weather/feedback domain)
  Api/                  FastEndpoints REST API, SignalR hubs, Hangfire jobs
  Application/          CQS command/query handlers with behavior decorators
  Domain/               Aggregates, value objects, domain events
  Infrastructure/       EF Core, Kafka, Redis (FusionCache), Hangfire

services/               Microservices (each follows same Clean Architecture)
  Order/                Alert subscription ordering (API, Application, Domain, Infrastructure)
  Finance/Payments/     Payment processing
  Notifications/        Notification delivery

saga/                   MassTransit saga orchestration (separate worker service)
  DotNetAtlas.Sagas/    State machines for distributed transactions

platform/               Shared libraries consumed by all services
  SharedKernel/         DDD base types: Entity, AggregateRoot, ValueObject, Result<T>, Error
  CQS/                  ICommand/IQuery with behavior pipeline (validation, logging, metrics, tracing)
  ReliableMessaging.*/  Transactional Outbox and Inbox (idempotent consumer) patterns
  KafkaFlow.*/          Kafka extensions (dead letter, inbox, producer headers)
  OutboxRelay.*/        Background worker that relays outbox messages to Kafka
  SchemaRegistry.Contracts/ Avro schemas (.avsc) and generated C# types
  ServiceDefaults/      Serilog, health checks, security headers

test/                   Unit, integration, functional, and architecture tests
```

### Key Patterns

- **CQS (not CQRS):** Commands and queries use `ICommandHandler<TCommand, TResponse>` / `IQueryHandler<TQuery, TResponse>` injected directly via DI (no mediator/dispatcher). Behaviors (decorators) handle cross-cutting: validation (FluentValidation), logging, metrics, tracing.

- **Domain Events:** Aggregates raise domain events via `AddDomainEvent()`. Handlers publish to the transactional outbox for reliable Kafka delivery.

- **Transactional Outbox:** Business data and outbox messages are written in the same DB transaction. A separate OutboxRelay worker reads and publishes to Kafka (at-least-once delivery).

- **Idempotent Consumer (Inbox):** Services track processed MessageIds to achieve exactly-once semantics per service boundary.

- **Saga Orchestration:** MassTransit state machines manage distributed transactions (e.g., payment -> activation -> completion) with timeout schedules and compensation flows. State is persisted to SQL Server.

- **Event-Driven Messaging:** Kafka with Confluent Schema Registry. Messages use Avro serialization with schemas in `platform/DotNetAtlas.SchemaRegistry.Contracts/Avro/`.

### Conventions

- **API Endpoints:** FastEndpoints (not controllers). Each endpoint is a separate class.
- **Package Management:** Centralized via `Directory.Packages.props` at root, `services/`, `saga/`, `platform/`, and `test/` levels. Lock files (`packages.lock.json`) are committed.
- **Solution File:** `DotNetAtlas.slnx` (modern XML format).
- **EF Core Migrations:** Located under each infrastructure project's `Persistence/Database/Migrations/` directory.
- **Observability:** OpenTelemetry for tracing/metrics, Serilog for structured logging. Custom activities/meters per domain.
