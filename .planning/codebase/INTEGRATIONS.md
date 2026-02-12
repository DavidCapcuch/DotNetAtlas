# External Integrations

**Analysis Date:** 2026-02-12

## APIs & External Services

**Weather Data Providers:**
- Open-Meteo (https://api.open-meteo.com/)
  - SDK/Client: Custom HttpClient via `IWeatherForecastProvider`
  - Config: `WeatherProviders.OpenMeteo` in appsettings
  - Implementation: `src/DotNetAtlas.Infrastructure/WeatherProviders/OpenMeteoWeatherProvider.cs`
  - Includes geocoding API support via `IGeocodingProvider`

- Weather API (weatherapi.com) (https://api.weatherapi.com/)
  - SDK/Client: Custom HttpClient via `IWeatherForecastProvider`
  - API Key: `WeatherProviders.WeatherApiCom.ApiKey` in appsettings.json (hardcoded in source - not for production)
  - Implementation: `src/DotNetAtlas.Infrastructure/WeatherProviders/WeatherApiComProvider.cs`

**HTTP Resilience for External APIs:**
- Microsoft.Extensions.Http.Resilience 9.9.0
- Policies configured: Retries (3 attempts), circuit breaker, timeout (5s per attempt, 20s total)
- Config: `HttpResilience` section in appsettings
- All external weather provider clients use these resilience policies with hedging support

## Data Storage

**Databases:**

**Primary Data Store - SQL Server:**
- Type: Microsoft SQL Server 2022
- Connection: `ConnectionStrings.Weather` and `ConnectionStrings.Ordering` in appsettings
- Development: localhost:12345 (docker-compose: mssqldb:12345)
- Client: Entity Framework Core 10.0.0 with SQL Server provider
- Databases:
  - `Weather` - Main application domain (forecast data, feedback, subscriptions)
  - `Ordering` - Order service database for alert subscriptions
- Configuration: `EfCore` section controls query splitting, retry counts (6 max with 30s max delay), detailed errors
- Health Checks: SQL Server health check with 5s timeout

**Saga State Store - SQL Server:**
- MassTransit saga persistence: `saga/DotNetAtlas.Sagas/Common/Persistence/Database/SagaDbContext.cs`
- Databases: Implicit in saga service, separate DbContext from main services
- Stores saga state machines for distributed transactions (payment processing, subscription activation)

**Hangfire Job Storage - SQL Server:**
- Hangfire.SqlServer 1.8.21 persists background jobs to SQL Server
- Configuration: `Hangfire` section in appsettings
- Job queues: "critical" and "default"
- Used for recurring jobs (weather data generation, expired subscription cleanup)

**File Storage:**
- Local filesystem only - no external blob storage
- Avro schema files: `platform/DotNetAtlas.SchemaRegistry.Contracts/Avro/` (committed to repo)

**Caching:**

**Redis Distributed Cache:**
- Type: Redis 7.4.6
- Connection: `ConnectionStrings.Redis` in appsettings
- Development: localhost:12346 (docker-compose: redis12346:6379)
- Health Checks: Redis health check with 4s timeout
- Used by:
  - ZiggyCreatures.FusionCache - Multi-level cache backplane
  - SignalR backplane - Distributed session state
  - Distributed cache fallback
- Configuration: FusionCache settings include circuit breaker (2s), timeouts, jitter

**In-Memory Caching:**
- FusionCache L1 (in-process memory) + L2 (Redis) hybrid model
- Default cache config: 1 minute TTL, factory timeouts 100ms soft/1500ms hard
- Specific caches:
  - Forecast cache: 720 min (12 hours) with 120 min fail-safe
  - Circuit breaker: 2s failure window before falling back to fail-safe

## Authentication & Identity

**Auth Provider - FusionAuth:**
- Type: Self-hosted identity provider
- Authority: http://localhost:9011 (development)
- Implementation files:
  - `src/DotNetAtlas.Infrastructure/Common/AuthDependencyInjection.cs` - Auth setup
  - `src/DotNetAtlas.Infrastructure/Common/Authentication/AuthConfigSections.cs` - Config binding
  - `src/DotNetAtlas.Infrastructure/Common/Authentication/AuthPolicySchemes.cs` - Scheme definitions
  - Endpoints: `src/DotNetAtlas.Api/Endpoints/Auth/LoginEndpoint.cs`, `LogoutEndpoint.cs`

**Authentication Methods:**
- OpenID Connect (`Microsoft.AspNetCore.Authentication.OpenIdConnect`)
  - ClientId: `e9fdb985-9173-4e01-9d73-ac2d60d1dc8e`
  - ClientSecret: In appsettings (development hardcoded - MUST use secret vault in production)
  - PKCE enabled (UsePkce: true)
  - SaveTokens: true for refresh capability
  - Scopes configured via `AuthScopes.cs`

- JWT Bearer Token (`Microsoft.AspNetCore.Authentication.JwtBearer`)
  - Authority: http://localhost:9011
  - Token validation: Issuer (cme.com), Audience (e9fdb985-9173-4e01-9d73-ac2d60d1dc8e), signing keys
  - Health check: OpenIdConnectServer health check with 3s timeout

- Windows/Kerberos (`Microsoft.AspNetCore.Authentication.Negotiate`)
  - Supported for enterprise environments

**Authorization:**
- Policy-based authorization: `src/DotNetAtlas.Infrastructure/Common/Authorization/AuthPolicies.cs`
- Roles: Defined in `AuthPolicies.cs` (Admin, Dev, User implied from FusionAuth)
- Scopes: `AuthScopes.cs`
- Bootstrap data: `src/fusionauth/kickstart.json` - Seeded on FusionAuth startup with admin/dev/pleb users

**Cookie Authentication:**
- SlidingExpiration enabled
- LoginPath: /api/v1/auth/login
- LogoutPath: /api/v1/auth/logout

## Monitoring & Observability

**Error Tracking:**
- No external error tracking service (Sentry, etc.)
- Errors captured via Serilog and sent to OpenTelemetry

**Logs:**
- Structured logging via Serilog 4.2.0
- Sinks configured:
  - Console - Development
  - Seq (localhost:5341 in docker-compose full profile) - Log aggregation and search
  - OpenTelemetry - For OTLP-compatible backends
- Minimum level: Information (Microsoft/Polly/SignalR at Warning)
- Exceptions enriched via `Serilog.Exceptions`

**Distributed Tracing:**
- OpenTelemetry Protocol (OTLP) exporter
- Endpoint: Environment variable `OTEL_EXPORTER_OTLP_ENDPOINT` (default: http://localhost:4317)
- Backends:
  - Jaeger (localhost:16686 in full profile) - Trace storage and UI
  - Can be configured for any OTLP-compatible backend (Tempo, Datadog, etc.)
- Instrumentation:
  - AspNetCore (HTTP requests)
  - EntityFrameworkCore (database queries)
  - Http (outbound HTTP calls)
  - Runtime (GC, process metrics)
  - SignalR (WebSocket connections)
  - Hangfire (background job execution)

**Metrics:**
- Prometheus format via OpenTelemetry Exporter
- Scrape endpoint: /metrics (health checks UI exposes Prometheus metrics)
- Prometheus (localhost:9090 in full profile) - Time-series storage
- Grafana (localhost:3000 in full profile) - Metric visualization
- Custom meters per domain (defined in observability setup)
- FusionCache metrics included in traces/metrics

## CI/CD & Deployment

**Hosting:**
- Docker containerization via `Dockerfile` (multi-stage build)
- Docker Compose for local orchestration
- Deployment target: Not specified (AWS/Azure/Kubernetes-agnostic)

**CI Pipeline:**
- GitHub Actions (`.github/workflows/`)
- Key workflows:
  - `main-ci.yml` - Build, test, coverage on main branch
  - `pr-ci.yml` - PR validation (format, tests)
  - `pr-enforce-format.yml` - dotnet format validation
  - `pr-conventional-commit-validation.yml` - Commit message linting
  - `test-with-coverage.yml` - Coverage report generation
  - `sonar-cloud-analysis.yml` - Code quality analysis
  - `build-and-push-docker-image.yml` - Image build and registry push
  - `docker-image-build-push-sign.yml` - Signed image builds
  - `publish-coverage-site.yml` - Coverage report publication
  - `publish-test-coverage.yml` - Coverage metrics

**Build Configuration:**
- `dotnet build` supports multi-core compilation (-m flag)
- Lock files enforced: `dotnet restore --locked-mode`
- Solution format: `DotNetAtlas.slnx` (modern XML format)

## Environment Configuration

**Required env vars:**
- `MSSQL_SA_PASSWORD` - SQL Server admin password (docker-compose)
- `OTEL_EXPORTER_OTLP_ENDPOINT` - OpenTelemetry collector URL (default: http://localhost:4317)
- `.env` file exists but is git-ignored - contains database passwords and API keys

**Critical appsettings sections:**
- `Serilog` - Log level configuration
- `Authentication` (JwtBearer/OAuth/Oidc/Cookie) - Auth endpoints and credentials
- `ConnectionStrings` (Weather, Ordering, Redis) - Database and cache endpoints
- `Kafka` - Broker addresses, schema registry URL
- `Topics` - Kafka topic names
- `WeatherProviders` - External API endpoints and keys
- `HttpResilience` - Retry/circuit breaker policies
- `Hangfire` - Job queue configuration
- `HealthChecks` - Service timeout thresholds

**Secrets location:**
- Development: appsettings.json (hardcoded for local development only)
- Production: Should use Azure Key Vault / AWS Secrets Manager (not implemented - marked as TODO in code)
- `.env` file for docker-compose secrets (git-ignored)
- FusionAuth secrets: `src/fusionauth/kickstart.json` (marked with shouldBeInSecretVault comments)

## Webhooks & Callbacks

**Incoming:**
- Dev endpoints for testing: `src/DotNetAtlas.Api/Endpoints/Dev/AlertSubscriptions/` - Manual message publication for testing saga flows
- No external webhook listeners configured

**Outgoing:**
- Kafka event publishing via KafkaFlow to external topics
- Topics consumed by other services:
  - `weather.alert-subscriptions.commands` - Commands to order service
  - `weather.alert-subscriptions` - Events to sagas
  - `order.alert-subscriptions` - Events from order service back to weather
  - `notification.commands` - Events to notification service
  - `finance.payment-commands` - Events to finance service
  - `finance.payments` - Events from finance service
- Saga compensation flows (MassTransit) trigger distributed rollbacks via these topics

## Message Topics (Kafka)

**Weather Domain:**
- `weather.forecast.requests` - Forecast data generation requests
- `weather.feedbacks` - User feedback events
- `weather.alerts` - Weather alert events
- `weather.alert-subscriptions.commands` - Alert subscription commands to order service
- `weather.alert-subscriptions` - Alert subscription events

**Order Service:**
- `order.alert-subscriptions` - Alert subscription order events

**Cross-Domain:**
- `notification.commands` - Notification delivery commands
- `finance.payment-commands` - Payment processing commands
- `finance.payments` - Payment completion events

**Dead Letter:**
- `[topic-name].DotNetAtlas.DLT` - Dead letter topic suffix pattern
- KafkaFlow retry + dead letter handling via `DotNetAtlas.KafkaFlow.DeadLetter`

## Reliability Patterns

**Transactional Outbox:**
- Location: `platform/DotNetAtlas.ReliableMessaging.Outbox.EFCore/`
- Business data + outbox messages written in same DB transaction
- Separate `OutboxRelay.WorkerService` reads and publishes to Kafka (at-least-once)
- Enables exactly-once semantics across service boundaries

**Idempotent Consumer (Inbox):**
- Location: `platform/DotNetAtlas.ReliableMessaging.Inbox.EFCore/`
- Inbox table tracks processed MessageIds per service
- Guarantees exactly-once message processing
- Prevents duplicate handling of retried messages

**Saga Orchestration:**
- MassTransit state machines in `saga/DotNetAtlas.Sagas/`
- Sagas: AlertSubscriptionPurchaseSaga, AlertSubscriptionExtensionSaga, PaymentProcessingSaga
- State persisted to SQL Server
- Timeout schedules and compensation flows for distributed transaction rollback
- Schema: Avro contracts in `platform/DotNetAtlas.SchemaRegistry.Contracts/Avro/Order/`

---

*Integration audit: 2026-02-12*
