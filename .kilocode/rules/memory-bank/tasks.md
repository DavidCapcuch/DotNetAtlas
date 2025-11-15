# Tasks

Use this file to capture repetitive tasks and their workflows.

## Template

- Task name and description
- Files to modify
- Step-by-step workflow
- Important considerations / gotchas
- Example of completed implementation
- Additional context discovered during execution

---

## Add new Avro schema

**Last performed:** 2025-11-14

**Files to modify:**

- `platform/DotNetAtlas.SchemaRegistry/*.avsc` (add new schema file)

**Steps:**

1. Create a new `.avsc` with proper namespace (e.g., `Weather.Contracts`) and doc.
2. Include `EventId` (uuid) and `OccurredOnUtc` (timestamp-millis) fields where appropriate.
3. Generate C# class:
   - Windows PowerShell: `./generate-avro.ps1 YourEventName.avsc`
4. Reference the generated `.cs` from the appropriate project.
5. If auto-registration is disabled, register the schema in Schema Registry via CI/CD.

**Important notes:**

- Prefer adding `aliases` for renamed fields rather than breaking changes.
- In production, avoid auto-registration; manage schemas centrally with governance.

**References:**

- `platform/DotNetAtlas.SchemaRegistry/README.md`

---

## Add new weather provider

**Last performed:** 2025-11-14

**Files to modify:**

- `src/DotNetAtlas.Application/WeatherForecast/Services/Abstractions/IWeatherForecastProvider.cs` - Add new interface method
- `src/DotNetAtlas.Infrastructure/HttpClients/WeatherProviders/[ProviderName]/[ProviderName]WeatherProvider.cs` - Implement provider
- `src/DotNetAtlas.Infrastructure/HttpClients/WeatherProviders/[ProviderName]/[ProviderName]Options.cs` - Configuration
- `src/DotNetAtlas.Infrastructure/HttpClientsDependencyInjection.cs` - Register provider in DI
- `src/DotNetAtlas.Application/WeatherForecast/Services/HedgingWeatherForecastService.cs` - Add to hedging strategy
- `test/DotNetAtlas.IntegrationTests/Infrastructure/HttpClients/[ProviderName]ProviderIntegrationTests.cs` - Integration tests

**Steps:**

1. Create new provider class implementing `IWeatherForecastProvider`
2. Add strongly-typed configuration options class
3. Register provider and configuration in HttpClientsDependencyInjection
4. Add provider to HedgingWeatherForecastService
5. Write integration tests with TestContainers
6. Update documentation

**Important notes:**

- Follow Polly resilience patterns (retry, circuit breaker, timeout)
- Include proper OpenTelemetry tracing
- Use the geocoding service abstraction
- Handle rate limiting and API key management
- Test with real API calls in integration tests

**Example:**

- WeatherApiComProvider.cs shows full implementation pattern
- OpenMeteoProvider.cs shows simpler implementation

---

## Add new Domain Aggregate

**Last performed:** 2025-11-14

**Files to modify:**

- `src/DotNetAtlas.Domain/Entities/[BoundedContext]/[AggregateName].cs` - Domain aggregate
- `src/DotNetAtlas.Domain/Entities/[BoundedContext]/ValueObjects/` - Value objects
- `src/DotNetAtlas.Domain/Entities/[BoundedContext]/Events/` - Domain events
- `src/DotNetAtlas.Domain/Entities/[BoundedContext]/Errors/` - Domain errors
- `src/DotNetAtlas.Application/[BoundedContext]/[Feature]/` - Command/Query handlers
- `src/DotNetAtlas.Infrastructure/Persistence/Database/EntityConfigurations/` - EF configuration
- `test/DotNetAtlas.UnitTests/[BoundedContext]/` - Unit tests
- `test/DotNetAtlas.IntegrationTests/Application/[BoundedContext]/` - Integration tests

**Steps:**

1. Create aggregate root inheriting from `AggregateRoot<TId>`
2. Add value objects for business rules validation
3. Define domain events raised by the aggregate
4. Create domain-specific error types
5. Add CQS handlers in Application layer
6. Configure EF entity mapping
7. Write unit tests for domain logic
8. Write integration tests for data persistence

**Important notes:**

- Use `Guid.CreateVersion7()` for time-ordered IDs
- Validate invariants in aggregate constructor
- Raise domain events via `RaiseDomainEvent()`
- Follow Result pattern for error handling
- Include OpenTelemetry tracing in handlers

**Example:**

- Feedback aggregate shows complete pattern implementation

---

## Add new SignalR hub method

**Last performed:** 2025-11-14

**Files to modify:**

- `src/DotNetAtlas.Application/WeatherAlerts/Contracts/` - Type-safe contracts
- `src/DotNetAtlas.Api/SignalRHubs/[HubName]/[HubName]Hub.cs` - Hub implementation
- `src/DotNetAtlas.Api/SignalRHubs/[HubName]/[HubName]Notifier.cs` - Notification implementation
- `test/DotNetAtlas.FunctionalTests/SignalR/` - SignalR tests

**Steps:**

1. Add method signature to client/server contracts
2. Implement method in SignalR hub
3. Add corresponding notifier implementation
4. Register notifier in dependency injection
5. Write functional tests
6. Update Swagger documentation processor if needed

**Important notes:**

- Use type-safe contracts for compile-time safety
- Handle connection lifecycle properly
- Include proper error handling and logging
- Consider Redis backplane for scale-out scenarios
- Add OpenTelemetry tracing for real-time operations

**Example:**

- WeatherAlertHub.cs shows complete pattern implementation

---

## Create new Background Job

**Last performed:** 2025-11-14

**Files to modify:**

- `src/DotNetAtlas.Infrastructure/BackgroundJobs/[JobName]/[JobName].cs` - Job implementation
- `src/DotNetAtlas.Infrastructure/BackgroundJobs/[JobName]/[JobName]Options.cs` - Configuration
- `src/DotNetAtlas.Infrastructure/BackgroundJobs/[JobName]/[JobName]Scheduler.cs` - Job scheduling
- `src/DotNetAtlas.Infrastructure/BackgroundJobsDependencyInjection.cs` - Register job
- `src/DotNetAtlas.Application/[Domain]/Abstractions/I[JobName]Scheduler.cs` - Scheduler interface
- `test/DotNetAtlas.IntegrationTests/Application/[Domain]/` - Job tests

**Steps:**

1. Create job class implementing `IBackgroundJob<T>`
2. Add configuration options for the job
3. Create scheduler to manage job lifecycle
4. Register job and scheduler in DI
5. Add integration tests
6. Configure Hangfire dashboard access

**Important notes:**

- Make jobs idempotent when possible
- Include proper error handling and retry logic
- Use OpenTelemetry tracing for job execution
- Consider job scheduling based on business requirements
- Monitor job execution with metrics

**Example:**

- FakeWeatherAlertBackgroundJob.cs shows complete pattern implementation

---

## Add new Database Migration

**Last performed:** 2025-11-14

**Files to modify:**

- `src/DotNetAtlas.Infrastructure/Persistence/Database/Migrations/[Timestamp]_[Name].cs` - EF Core migration
- `src/DotNetAtlas.Infrastructure/Persistence/Database/Flyway/[Name].sql` - Flyway SQL script
- `test/DotNetAtlas.ArchitectureTests/Migrations/DatabaseMigrationFilesTests.cs` - Test validation

**Steps:**

1. Create EF Core migration locally: `dotnet ef migrations add`
2. Generate corresponding Flyway SQL script with filename matching convention, for the command:
   - Infrastructure as migrations project
   - API as startup project
   - Always --idempotent
   - 1 EF Core migration = 1 Flyway script (always specify EF core miration FROM -> TO), never bundle multiple EF Core migrations together
   - example of compliant command: `dotnet ef migrations script --project src\DotNetAtlas.Infrastructure\DotNetAtlas.Infrastructure.csproj --startup-project src\DotNetAtlas.Api\DotNetAtlas.Api.csproj --context DotNetAtlas.Infrastructure.Persistence.Database.WeatherDbContext --configuration Debug 20251108150508_CreateFeedbackTable 20251112184015_CreateOutboxMessagesTable --output src\DotNetAtlas.Infrastructure\Persistence\Database\Migrations\Flyway\V002__CreateOutboxMessagesTable.sql --idempotent`

**Important notes:**

- Always create corresponding Flyway SQL script
- Follow Flyway naming convention: `V[version]__[description].sql`
- Architecture test validates Flyway script exists
- Test migrations before committing
- Consider data migration strategies for production

**Example:**

- 20251108150508_CreateFeedbackTable.cs shows migration pattern
