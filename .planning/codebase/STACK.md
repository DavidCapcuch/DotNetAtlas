# Technology Stack

**Analysis Date:** 2026-02-12

## Languages

**Primary:**
- C# .NET 10.0.100 - Main application framework, API endpoints, background jobs, saga orchestration
- Avro - Event schema definitions in `platform/DotNetAtlas.SchemaRegistry.Contracts/Avro/`

**Build & Configuration:**
- XML (csproj, config files)
- JSON (appsettings configuration)
- YAML (docker-compose orchestration)

## Runtime

**Environment:**
- .NET 10.0 SDK
- Configured via `global.json` - enforces SDK version 10.0.100 with rollForward latestFeature

**Package Manager:**
- NuGet
- Lockfiles: `packages.lock.json` committed at repository root level and per-project level (reproducible builds)
- Centralized version management via `Directory.Packages.props` at multiple levels: root, `platform/`, `saga/`, `services/`, `test/`

## Frameworks

**Core Web:**
- FastEndpoints 7.0.1 - REST API endpoints (replaces controllers), defined per-endpoint class structure
- ASP.NET Core 10.0 - Web host and middleware

**API Documentation & Security:**
- FastEndpoints.Swagger 7.0.1 - OpenAPI/Swagger generation
- FastEndpoints.Security 7.0.1 - Authentication/authorization helpers
- NetEscapades.AspNetCore.SecurityHeaders 1.1.0 - Security header middleware

**ORM & Data:**
- Entity Framework Core 10.0.0 - SQL Server provider, migrations at `[Project]/Persistence/Database/Migrations/`
- EntityFrameworkCore.Exceptions.SqlServer 8.1.3 - Relational exception handling

**Real-Time Communication:**
- SignalR - WebSocket-based hubs defined in `src/DotNetAtlas.Api/SignalRHubs/`
- Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.0 - Distributed SignalR backplane via Redis
- Microsoft.AspNetCore.SignalR.Protocols.MessagePack 10.0.0 - Message protocol optimization
- TypedSignalR.Client.DevTools 1.2.4 - Client-side development tools

**Messaging & Event Processing:**
- Kafka via KafkaFlow 4.0.1 - Event streaming broker
- KafkaFlow.Serializer.SchemaRegistry.ConfluentAvro 4.0.1 - Avro serialization with schema registry
- Confluent Schema Registry 2.13.0 - Schema versioning and evolution
- MassTransit 8.5.7 - Saga orchestration and distributed transaction management (saga/ folder only)
  - MassTransit.Kafka 8.5.7 - Kafka transport for sagas
  - MassTransit.SqlTransport.SqlServer 8.5.7 - SQL transport for sagas

**Caching:**
- ZiggyCreatures.FusionCache 2.4.0 - Multi-level cache (L1 memory + L2 distributed)
- ZiggyCreatures.FusionCache.Backplane.StackExchangeRedis 2.4.0 - Cache coherence via Redis
- Microsoft.Extensions.Caching.StackExchangeRedis 10.0.0 - Distributed cache provider

**Background Jobs:**
- Hangfire 1.8.21 - Recurring and background job scheduling with SQL Server persistence
- Hangfire.SqlServer 1.8.21 - SQL Server job storage

**Validation & Domain:**
- FluentValidation 12.0.0 - Declarative validation with behaviors
- Ardalis.SmartEnum 8.1.0 - Type-safe enumerations
- Ardalis.SmartEnum.EFCore 8.1.0 - SmartEnum EF Core integration
- Ardalis.Specification.EntityFrameworkCore 9.3.1 - Specification pattern for queries

**Mapping:**
- Riok.Mapperly 4.2.1 - Source code generation for object mapping (replaces AutoMapper)

**Authentication & Authorization:**
- Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.0 - OpenID Connect protocol
- Microsoft.AspNetCore.Authentication.JwtBearer 10.0.0 - JWT bearer token validation
- Microsoft.AspNetCore.Authentication.Negotiate 10.0.0 - Windows/Kerberos authentication
- FusionAuth - Identity provider (configured at localhost:9011 in development, see `src/fusionauth/kickstart.json`)

**Testing:**
- xUnit v3 (3.0.1) - Test framework
- AwesomeAssertions 9.1.0 - Fluent assertion library
- Testcontainers 4.7.0 - Docker-based integration test infrastructure
  - Testcontainers.MsSql 4.7.0 - SQL Server containers
  - Testcontainers.Redis 4.7.0 - Redis containers
  - Testcontainers.Kafka 4.7.0 - Kafka containers
- Respawn 6.2.1 - Database cleanup between tests
- Microsoft.AspNetCore.Mvc.Testing 10.0.0 - Web application testing
- FastEndpoints.Testing 7.0.1 - FastEndpoints-specific test utilities
- NSubstitute 5.3.0 - Test mocking library

**Architecture Testing:**
- NetArchTest.eNhancedEdition 1.4.5 - Architecture rules validation

**Build & Dev Tools:**
- FastEndpoints.Generator 7.0.1 - Code generation for endpoints
- FastEndpoints.ClientGen.Kiota 7.0.1 - Client SDK generation
- Microsoft.EntityFrameworkCore.Design 10.0.0 - EF Core tools for migrations
- StyleCop.Analyzers 1.2.0-beta.556 - Code style enforcement

## Observability & Tracing

**Logging:**
- Serilog 4.2.0 - Structured logging framework
- Serilog.AspNetCore 9.0.0 - ASP.NET Core integration
- Serilog.Sinks.OpenTelemetry 4.2.0 - OTLP sink
- Serilog.Sinks.Seq 9.0.0 - Seq server integration (docker-compose: seq5341)
- Serilog.Expressions 5.0.0 - Template expressions
- Serilog.Exceptions 8.4.0 - Exception enrichment
- Serilog.Extensions.Hosting 9.0.0 - Hosting integration

**Distributed Tracing:**
- OpenTelemetry 1.12.0 - Observability API
- OpenTelemetry.Exporter.OpenTelemetryProtocol 1.12.0 - OTLP exporter
- OpenTelemetry.Instrumentation.AspNetCore 1.12.0 - HTTP instrumentation
- OpenTelemetry.Instrumentation.EntityFrameworkCore 1.12.0-beta.2 - EF Core instrumentation
- OpenTelemetry.Instrumentation.Http 1.12.0 - HTTP client instrumentation
- OpenTelemetry.Instrumentation.Runtime 1.12.0 - Runtime metrics
- OpenTelemetry.Instrumentation.Hangfire 1.12.0-beta.1 - Hangfire instrumentation
- OpenTelemetry.Resources.Container 1.12.0-beta.1 - Container resource detection
- AspNetCore.SignalR.OpenTelemetry 1.7.0 - SignalR instrumentation

**Metrics & Health Checks:**
- AspNetCore.HealthChecks.UI 9.0.0 - Health check dashboard
- AspNetCore.HealthChecks.UI.SqlServer.Storage 9.0.0 - Health check history storage
- AspNetCore.HealthChecks.Prometheus.Metrics 9.0.0 - Prometheus metrics endpoint
- AspNetCore.HealthChecks.Kafka 9.0.0 - Kafka health check
- AspNetCore.HealthChecks.Redis 9.0.0 - Redis health check
- AspNetCore.HealthChecks.Hangfire 9.0.0 - Hangfire job health check
- AspNetCore.HealthChecks.OpenIdConnectServer 9.0.0 - Auth provider health check
- AspNetCore.HealthChecks.Uris 9.0.0 - External HTTP endpoint health check

**Jaeger/Prometheus Stack (Optional, docker-compose profile: full):**
- Jaeger 1.74.0 - Distributed tracing (UI at localhost:16686)
- Prometheus 3.6.0 - Metrics collection (at localhost:9090)
- Grafana 12.2.0 - Metrics visualization (at localhost:3000)
- OpenTelemetry Collector - OTLP receiver/processor

## Configuration

**Environment:**
- appsettings.json (base) + appsettings.Local.json (development overrides) + appsettings.Testing.json (test overrides)
- `.env` file for docker-compose secrets (not committed; listed in `.gitignore`)
- Configuration sections: Serilog, OTEL_EXPORTER_OTLP_ENDPOINT, Authentication, ConnectionStrings, Kafka, Hangfire, etc.

**Build:**
- Global build props: `Directory.Build.props` at root and folder levels
- NuGet config: `NuGet.config`
- Editor config: `.editorconfig` (27 KB - comprehensive style rules including file-scoped namespaces, StyleCop, SonarAnalyzer)
- `TreatWarningsAsErrors` enabled globally

## Local Infrastructure (docker-compose.yaml)

**Core Profile (--profile core):**
- SQL Server 2022 (mssqldb:12345) - Primary data store
- Redis 7.4.6 (redis12346:6379) - Cache & SignalR backplane

**Full Profile (--profile full) - includes core + observability:**
- Kafka 7.5.9 (broker:9092, localhost:9094) - Event streaming
- Confluent Schema Registry 7.5.0 (schema-registry:8081) - Avro schema versioning
- Seq 2025.2 (seq5341:5341) - Structured log aggregation
- Jaeger 1.74.0 (jaeger16686ui4317grpc) - Distributed tracing UI
- Prometheus 3.6.0 (prometheus9090:9090) - Time-series metrics
- Grafana 12.2.0 (grafana3000:3000) - Metrics dashboards
- OpenTelemetry Collector - OTLP signal processing
- Redis Insight 2.70 (redis-insight:5540) - Redis visualization

**Development Services Not in Compose:**
- FusionAuth (localhost:9011) - Identity provider, bootstrapped via `src/fusionauth/kickstart.json`

## Platform Requirements

**Development:**
- .NET 10.0 SDK (specified in global.json)
- Docker & Docker Compose (for infrastructure)
- SQL Server (localhost:12345) - default password in docker-compose
- Redis (localhost:12346)
- Kafka cluster if using full profile

**Production:**
- .NET 10.0 runtime
- SQL Server database
- Redis cluster (for cache coherence)
- Kafka cluster for event streaming
- Jaeger/OTLP collector endpoint for tracing
- Health check UI (optional, for operational visibility)

---

*Stack analysis: 2026-02-12*
