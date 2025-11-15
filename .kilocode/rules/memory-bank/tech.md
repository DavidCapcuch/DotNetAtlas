# DotNetAtlas - Technology Stack & Implementation Guide

> **Technical Reference**: Comprehensive technology stack, architectural patterns, and implementation details for modern .NET development.

## Architecture Overview

DotNetAtlas implements **Clean Architecture** with **Domain-Driven Design** in an event-driven system. The architecture enforces strict layer dependency rules while maintaining high testability and observability.

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

## Core Framework & Runtime

- **.NET 10.0**
- **C# 12+** - Modern language features (records, pattern matching, async/await)
- **ASP.NET Core 10.0** - Web framework with minimal APIs and SignalR
- **GC Mode**: Server GC for multi-core optimization
- **Nullable Reference Types**: Enabled project-wide for null safety

## Data & Persistence Layer

### Entity Framework Core 10

**Core Configuration:**

- **Provider**: `Microsoft.EntityFrameworkCore.SqlServer`
- **Design-time**: `Microsoft.EntityFrameworkCore.Design` for migrations
- **Exception Handling**: `EntityFrameworkCore.Exceptions.SqlServer`
- **Auditing**: `IAuditableEntity` with `CreatedUtc/LastModifiedUtc`

### Database Migrations Strategy

**Development Workflow:**
```bash
# Local development - EF Core Migrations
dotnet ef migrations add MigrationName --project src/DotNetAtlas.Infrastructure

# Generate Flyway SQL for production
dotnet ef migrations script \
  --project src/DotNetAtlas.Infrastructure \
  --startup-project src/DotNetAtlas.Api \
  --context WeatherDbContext \
  --from MigrationA --to MigrationB \
  --idempotent \
  --output src/DotNetAtlas.Infrastructure/Persistence/Database/Flyway/VXXX__Description.sql
```

**Architecture Validation:**

- Architecture tests ensure Flyway script exists for each EF migration
- Production deployment uses Flyway SQL scripts (reviewable, DBA-friendly)
- Development uses EF Core migrations (fast iteration)

### Database Components

| Component | Purpose | Implementation |
|-----------|---------|----------------|
| **SQL Server 2022** | Primary database | Production-grade relational database |
| **Respawn** | Test database cleanup | Fast test isolation between runs |
| **Bogus** | Fake data generation | Realistic test data for development |

## Caching & Performance

### Redis & FusionCache Integration

**Multi-Level Caching Strategy:**

- **L1 Cache**: In-memory cache for nanosecond access
- **L2 Cache**: Redis distributed cache for multi-instance consistency
- **Backplane**: Redis pub/sub for cache invalidation across instances
- **Serialization**: MemoryPack for high-performance binary serialization
- **OpenTelemetry**: `FusionCache.OpenTelemetry` for distributed tracing

**Configuration:**
```csharp
services.AddFusionCache(options =>
{
    options.DefaultEntryOptions = new FusionCacheEntryOptions
    {
        Duration = TimeSpan.FromMinutes(15),
        IsCacheFailSafeEnabled = true,
        OpenTelemetryEnabled = true
    };
});
```

**Cache Key Patterns:**

- `weather-forecast:{city}:{countryCode}` - Forecast data
- `geocoding:{city}:{countryCode}` - Location coordinates

## Messaging & Event-Driven Architecture

### Apache Kafka Integration

**KafkaFlow:**
```csharp
// KafkaFlow provides high-level abstraction over native Kafka client
services.AddKafka(kafka => kafka
    .AddCluster(cluster => cluster
        .WithBrokers(kafkaOptions.Brokers)
        .WithSchemaRegistry(config => config.Url = kafkaOptions.SchemaRegistry.Url)
        .AddProducer<KafkaForecastEventsProducer>(producer =>
            producer
                .WithProducerConfig(producerOptions)
                .AddMiddlewares(m =>
                    m.AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer))
        ))
    .UseMicrosoftLog()
    .AddOpenTelemetryInstrumentation());
```

**Schema Registry Integration:**

- **Provider**: `Confluent.SchemaRegistry.Serdes.Avro`
- **Avro Schemas**: Located in `platform/DotNetAtlas.SchemaRegistry/Avro/`
- **Schema Evolution**: Backward and forward compatibility support
- **Auto-registration**: Development only, manual governance for production

### Event Publishing Strategies

**1. Fire-and-Forget (Non-Critical Events):**
```csharp
// Immediate publishing for non-critical events
// Used for: forecast requests, analytics events
_ = Task.Run(async () =>
{
    try
    {
        await _forecastEventsProducer.PublishForecastRequestedAsync(query);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to publish forecast request");
    }
}, ct);
```

**2. Outbox Pattern (Critical Events):**
```csharp
// Guaranteed delivery via transactional outbox
// Used for: feedback events requiring eventual consistency
public class FeedbackAggregate : AggregateRoot<FeedbackId>
{
    public async Task UpdateRatingAsync(Rating newRating)
    {
        var oldRating = Rating;
        Rating = newRating;

        // Raise domain event (automatically captured by EF interceptor)
        RaiseDomainEvent(new FeedbackChangedDomainEvent
        {
            FeedbackId = Id,
            OldRating = oldRating,
            NewRating = newRating,
            EventId = Guid.CreateVersion7(),
            OccurredOnUtc = DateTime.UtcNow
        });
    }
}
```

**Outbox Implementation Flow:**

1. **Domain Event Raised** → `AggregateRoot.RaiseDomainEvent()`
2. **EF Interceptor Captures** → `PopDomainEvents()` on SaveChanges
3. **OutboxMessage Created** → Same transaction as entity
4. **Worker Service Polls** → Reads from OutboxMessages table
5. **Event Published** → To Kafka with trace continuity
6. **Cleanup** → Delete successfully delivered messages

### Platform Components (Reusable Libraries)

**1. DotNetAtlas.Outbox.Core**

- Base outbox entities and interfaces
- Trace header serialization/deserialization

**2. DotNetAtlas.Outbox.EntityFrameworkCore**

- `IOutboxDbContext` abstraction
- `OutboxInterceptor` for event capture
- Avro mapping cache and batch processing

**3. DotNetAtlas.OutboxRelay.WorkerService**

- Standalone worker for publishing outbox messages
- OpenTelemetry integration for worker tracing
- Delivery failure tracking and monitoring

## Real-Time Communication

### SignalR with Redis Backplane

**SignalR Configuration:**
```csharp
services.AddSignalR().AddMessagePackProtocol()
    .AddRedisBackplane(options => options.Configuration = redisOptions);
```

**Custom Redis Group Management:**
The project implements custom Redis Lua scripts for atomic SignalR group operations:

```lua
-- Atomic add connection to group with member count tracking
local added = redis.call('SADD', 'connection:' .. connectionId .. ':groups', groupName)
if added == 1 then
    redis.call('INCR', 'group:' .. groupName .. ':count')
    return 1 -- Added to group
else
    return 0 -- Already in group
end
```

**Benefits:**

- Atomic operations without distributed locks
- Single network round-trip
- Automatic group cleanup when last member leaves

## API Development & Validation

### FastEndpoints Framework

**Why FastEndpoints over Minimal APIs:**

- Built-in validation and versioning
- Better organization for CQRS patterns
- Type-safe client generation via Kiota
- Automatic OpenAPI generation

**Endpoint Pattern:**
```csharp
public class SendFeedbackEndpoint : Endpoint<SendFeedbackCommand, Result<SendFeedbackResponse>>
{
    public override void Configure()
    {
        Post("/api/v1/weather/feedback");
        Group<WeatherGroup>();
        AllowAnonymous(); // Or require authentication
    }
    
    public override async Task HandleAsync(SendFeedbackCommand req, CancellationToken ct)
    {
        var result = await _mediator.Send(req, ct);
        return await SendResultAsync(result);
    }
}
```

### Validation Strategy

**FluentValidation Integration:**
```csharp
public class SendFeedbackCommandValidator : AbstractValidator<SendFeedbackCommand>
{
    public SendFeedbackCommandValidator()
    {
        RuleFor(sfr => sfr.Feedback)
            .SetValidator(new FeedbackTextValidator());
        RuleFor(sfr => sfr.Rating)
            .SetValidator(new FeedbackRatingValidator());
        RuleFor(sfr => sfr.UserId)
            .NotEmpty()
            .WithMessage("UserId cannot be empty.");
    }
}
```

**Decorator Chain (Scrutor):**
```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationHandlerBehavior<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingHandlerBehavior<,>));
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TracingHandlerBehavior<,>));

// Execution order: Validation → Logging → Tracing → Handler
```

## Authentication & Authorization

### FusionAuth Integration

**OIDC Configuration:**
```csharp
services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = configuration["FusionAuth:Authority"];
    options.Audience = "dotnetatlas-api";
    options.RequireHttpsMetadata = !Environment.IsDevelopment();
});
```

**Policy-Based Authorization:**
```csharp
public static class AuthPolicies
{
    public const string DevOnly = "DevOnly";
    public const string Admin = "Admin";
    public const string User = "User";
}

services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.Admin, policy => 
        policy.RequireRole("admin"));
    options.AddPolicy(AuthPolicies.DevOnly, policy => 
        policy.RequireEnvironment("Development"));
});
```

## Observability & Monitoring

### OpenTelemetry Instrumentation

**Automatic Instrumentation:**
```csharp
// Traces span from HTTP request to Kafka consumption
services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddRedisInstrumentation()
        .AddHangfireInstrumentation()
        .AddSource("DotNetAtlas")
    );
```

**Custom Instrumentation:**
```csharp
public class DotNetAtlasInstrumentation : ActivitySource
{
    public static readonly string Name = "DotNetAtlas";
    
    protected override void OnStartActivity(Activity activity, object? payload)
    {
        // Enrich spans with domain-specific information
        activity.SetTag("weather.request.city", payload?.City);
        activity.SetTag("weather.request.country", payload?.CountryCode);
    }
}
```

### Structured Logging (Serilog)

**Configuration:**
```csharp
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "DotNetAtlas.Api")
    .WriteTo.Console(outputTemplate: 
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.OpenTelemetry(options =>
    {
        options.Endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        options.IncludedData = IncludedData.SpanAttributes | IncludedData.ResourceAttributes;
    })
    .CreateLogger();
```

### Health Monitoring

**Comprehensive Health Checks:**
```csharp
services.AddHealthChecks()
    .AddDbContextCheck<WeatherDbContext>()
    .AddRedis(redisOptions.ConnectionString)
    .AddKafka(new KafkaOptions { BootstrapServers = "kafka:9092" })
    .AddHangfire(options => options.MaximumJobsFailed = 5)
    .AddOpenIdConnect(options =>
    {
        options.RequireHttpsMetadata = !Environment.IsDevelopment();
        options.Authority = configuration["FusionAuth:Authority"];
    });
```

## Testing Infrastructure

### TestContainers Integration

**Core Abstraction:**
```csharp
public interface ITestContainer
{
    Task StartAsync();
    Task StopAsync();
    string GetConnectionString();
    string GetEndpoint();
}
```

**Container Lifecycle Management:**
```csharp
public class ApiTestFixture : IAsyncLifetime
{
    private readonly List<ITestContainer> _containers = new();
    private WebApplicationFactory _factory = null!;
    
    public async Task InitializeAsync()
    {
        // Start containers in parallel
        _containers.AddRange(new ITestContainer[]
        {
            new SqlServerTestContainer(),
            new KafkaTestContainer(),
            new RedisTestContainer(),
            new SchemaRegistryTestContainer()
        });
        
        await Task.WhenAll(_containers.Select(c => c.StartAsync()));
        
        // Configure WebApplicationFactory
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Override infrastructure with test container endpoints
                    services.ReplaceSingleton(new WeatherDbContextOptions(
                        _containers.OfType<SqlServerTestContainer>().First().GetConnectionString()));
                });
            });
    }
}
```

### Test Project Organization

```
test/
├── DotNetAtlas.Test.Framework/         # ITestContainer abstractions
│   ├── Common/ITestContainer.cs
│   ├── Database/SqlServerTestContainer.cs
│   ├── Kafka/KafkaTestContainer.cs
│   └── Redis/RedisTestContainer.cs
├── DotNetAtlas.ArchitectureTests/     # Clean Architecture validation
├── DotNetAtlas.UnitTests/            # Fast unit tests
├── DotNetAtlas.IntegrationTests/     # Real infrastructure tests
└── DotNetAtlas.FunctionalTests/      # End-to-end API tests
```

**Parallel Execution Strategy:**

- Each test collection gets independent fixtures with containers
- Tests within collection share fixture (parallel safe)
- Collections run in parallel without interference
- GitHub Actions caches container images for faster CI

### Testing Patterns

**1. Domain Logic Testing (Unit Tests):**
```csharp
public class FeedbackTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateFeedback()
    {
        // Arrange
        var text = new FeedbackText("Great weather!");
        var rating = new FeedbackRating(5);
        
        // Act
        var feedback = new Feedback(FeedbackId.CreateVersion7(), text, rating);
        
        // Assert
        Assert.Equal(text, feedback.Text);
        Assert.Equal(rating, feedback.Rating);
    }
}
```

**2. Infrastructure Integration (Integration Tests):**
```csharp
public class WeatherApiComProviderTests : IClassFixture<ApiTestFixture>
{
    private readonly WeatherApiComProvider _provider;
    
    public WeatherApiComProviderTests(ApiTestFixture fixture)
    {
        _provider = new WeatherApiComProvider(
            new HttpClient(),
            Options.Create(new WeatherApiComOptions { ApiKey = "test-key" }));
    }
    
    [Fact]
    public async Task GetForecastAsync_WithValidCity_ShouldReturnForecast()
    {
        // Act
        var result = await _provider.GetForecastAsync(
            new ForecastRequest("Prague", "CZ"), CancellationToken.None);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }
}
```

**3. API Testing (Functional Tests):**
```csharp
public class SendFeedbackTests : BaseApiTest
{
    [Fact]
    public async Task SendFeedback_WithValidData_ShouldCreateFeedback()
    {
        // Arrange
        var command = new SendFeedbackCommand
        {
            Text = "Excellent weather service!",
            Rating = 5
        };
        
        // Act
        var response = await Client.SendFeedbackAsync(command);
        
        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Value.FeedbackId.Should().NotBeEmpty();
    }
}
```

## Build & CI/CD

### Multi-Stage Dockerfile

```dockerfile
# Stage 1: Base runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0.0-noble-chiseled-extra AS base
WORKDIR /app
EXPOSE 8080
USER app

# Stage 2: Build SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files and restore with layer caching
COPY ["Directory.Packages.props", "."]
COPY ["src/DotNetAtlas.Api/DotNetAtlas.Api.csproj", "src/DotNetAtlas.Api/"]
RUN dotnet restore "src/DotNetAtlas.Api/DotNetAtlas.Api.csproj"

# Copy source and build
COPY . .
WORKDIR "/src/."
RUN dotnet build "src/DotNetAtlas.Api/DotNetAtlas.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Stage 3: Publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "src/DotNetAtlas.Api/DotNetAtlas.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:PublishReadyToRun=true

# Stage 4: Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DotNetAtlas.Api.dll"]
```

### Centralized Package Management

**Directory.Packages.props:**
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageVersionsFilePath>Directory.Packages.props</CentralPackageVersionsFilePath>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" />
    <PackageVersion Include="StackExchange.Redis" Version="2.8.16" />
    <PackageVersion Include="ApacheKafka" Version="3.7.0" />
  </ItemGroup>
</Project>
```

### GitHub Actions Workflows

**Main CI Pipeline (`main-ci.yml`):**
```yaml
name: Main CI
on: [push, pull_request]

jobs:
  build-test:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        test-project: [UnitTests, IntegrationTests, FunctionalTests]
    
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.100
      
      - name: Restore dependencies
        run: dotnet restore
      
      - name: Build
        run: dotnet build --no-restore
      
      - name: Test
        run: dotnet test --no-build --verbosity normal
```

## Development Tools & Utilities

### Code Generation & Mapping

| Tool | Purpose | Integration |
|------|---------|-------------|
| **Mapperly** | Compile-time object mapping | `dotnet add package Riok.Mapperly` |
| **Kiota** | API client generation | FastEndpoints integration |
| **Bogus** | Fake data generation | Test seeding and development |

### Performance Optimization

**Runtime Optimizations:**

- **ReadyToRun (R2R)**: Ahead-of-time compilation for faster startup
- **Tiered PGO**: Profile-guided optimization for better performance
- **Server GC**: Multi-core garbage collection optimization
- **Optimization Level**: Release builds with IL trimming

**Monitoring Performance:**
```csharp
// BenchmarkDotNet for performance testing
[SimpleJob(RunStrategy.ColdStart, iterationCount: 10)]
public class OutboxRelayBenchmark
{
    [Benchmark]
    public async Task ProcessMessages()
    {
        await _outboxRelay.ProcessBatchAsync();
    }
}
```

## Docker Compose Infrastructure

### Local Development Stack

**Services Overview (14+ containers):**

| Service | Purpose | Port | Dependencies |
|---------|---------|------|--------------|
| **SQL Server 2022** | Primary database | 1433 | - |
| **Redis 7.4** | Cache & SignalR backplane | 6379 | - |
| **Kafka** | Event streaming | 9092 | - |
| **Schema Registry** | Avro schema management | 8081 | Kafka |
| **FusionAuth** | Identity provider | 9011 | PostgreSQL |
| **Jaeger** | Distributed tracing UI | 16686 | - |
| **Prometheus** | Metrics collection | 9090 | - |
| **Grafana** | Dashboard visualization | 3000 | Prometheus |
| **OTel Collector** | Telemetry pipeline | 4317, 4318 | - |
| **Seq** | Log aggregation | 5341 | - |
| **AKHQ** | Kafka UI | 8080 | Kafka, Schema Registry |
| **Redis Insight** | Redis UI | 8001 | Redis |
| **Outbox Worker** | Background message publishing | - | SQL Server, Kafka |

**Health Checks:**
All services include proper wait strategies and health monitoring to ensure dependencies are ready before dependent services start.

### Container Security

- **Non-root User**: Application runs as `app` user in containers
- **Chiseled Images**: Minimal attack surface (~100MB base images)
- **Network Isolation**: Docker networks for service isolation
- **Secrets Management**: Environment variables for sensitive configuration

## Key Libraries & Patterns

### Core Libraries

| Category | Library | Purpose | Why This Choice |
|----------|---------|---------|-----------------|
| **Error Handling** | FluentResults | Result pattern | Explicit error handling vs exceptions |
| **Validation** | FluentValidation | Input validation | Type-safe, composable rules |
| **Queries** | Ardalis.Specification | DDD query pattern | Replaces traditional repositories |
| **Mapping** | Mapperly | Object mapping | Compile-time generation, catches errors |
| **Decorators** | Scrutor | DI registration | Clean separation of concerns |
| **Resilience** | Polly | HTTP resilience | Retry, circuit breaker, timeout |
| **Background Jobs** | Hangfire | Job scheduling | Rich UI, persistent storage |
| **Health Checks** | AspNetCore.HealthChecks | Monitoring | Pre-built checks for all dependencies |

### Design Patterns Implemented

**1. Command Query Separation (CQS):**
```csharp
// Commands modify state
public interface ICommandHandler<TCommand> 
    where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken ct);
}

// Queries return data
public interface IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken ct);
}
```

**2. Domain-Driven Design (DDD):**
```csharp
// Aggregate Root with domain events
public class FeedbackAggregate : AggregateRoot<FeedbackId>
{
    private readonly List<IDomainEvent> _events = new();
    
    public void UpdateRating(Rating newRating)
    {
        if (Rating == newRating) return;
        
        var oldRating = Rating;
        Rating = newRating;
        
        // Raise domain event
        RaiseDomainEvent(new FeedbackChangedDomainEvent
        {
            FeedbackId = Id,
            OldRating = oldRating,
            NewRating = newRating
        });
    }
}

// Value object with business rules
public class FeedbackRating : ValueObject
{
    public int Value { get; }
    
    private FeedbackRating(int value)
    {
        if (value < 1 || value > 5)
            throw new ArgumentOutOfRangeException(nameof(value), 
                "Rating must be between 1 and 5");
        Value = value;
    }
    
    public static FeedbackRating Create(int value) => new(value);
}
```

## Configuration Management

### Environment-Specific Configuration

**appsettings.json (Base):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "${ConnectionStrings__DefaultConnection}",
    "Redis": "${ConnectionStrings__Redis}"
  },
  "WeatherProviders": {
    "WeatherApiCom": {
      "ApiKey": "${WeatherProviders__WeatherApiCom__ApiKey}",
      "BaseUrl": "https://api.weatherapi.com/v1"
    }
  }
}
```

**appsettings.Development.json:**
```json
{
  "WeatherProviders": {
    "WeatherApiCom": {
      "ApiKey": "dev-key"
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

### Strongly-Typed Options Pattern

```csharp
public class WeatherApiComOptions
{
    public const string SectionName = "WeatherProviders:WeatherApiCom";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string BaseUrl { get; set; }

    [Required(AllowEmptyStrings = false)]
    public required string ApiKey { get; set; }
}

// Registration with validation
services.AddOptionsWithValidateOnStart<WeatherApiComOptions>()
    .BindConfiguration(WeatherApiComOptions.Section)
    .ValidateDataAnnotations();

```

## Production Considerations

### Deployment Architecture

**Scalability Patterns:**

- **Horizontal Scaling**: Redis backplane for SignalR, shared cache
- **Event-Driven**: Async processing via Kafka for loose coupling
- **Circuit Breakers**: Prevent cascading failures in external dependencies
- **Caching**: Multi-level caching reduces database load

**Monitoring & Alerting:**

- **Application Metrics**: Response times, error rates, throughput
- **Infrastructure Metrics**: CPU, memory, disk, network usage
- **Business Metrics**: Cache hit ratios, message lag, user engagement
- **Alerting**: Prometheus alerts → Grafana → Notification channels

**Security Measures:**

- **Authentication**: JWT Bearer with OIDC/OAuth2
- **Authorization**: Policy-based with role/claim support
- **Input Validation**: FluentValidation for all inputs
- **HTTPS**: Required in production environments
- **Container Security**: Non-root users, minimal images

## Technology Evolution Path

The architecture supports evolution to:

1. **Microservices**: Split by bounded context
2. **Event Sourcing**: Store events instead of state
3. **CQRS**: Separate read/write models with projections
4. **Service Mesh**: Istio/Linkerd for advanced routing
5. **Cloud Native**: Kubernetes, container orchestration

**Migration Readiness:**

- Clean Architecture boundaries enable service extraction
- Event-driven patterns support async decomposition
- Comprehensive observability enables monitoring at scale
- Platform components (Outbox) support cross-service communication

---

**Last Updated**: 2025-11-15  
**Version**: 2.0  
**Status**: Comprehensive Technical Reference
