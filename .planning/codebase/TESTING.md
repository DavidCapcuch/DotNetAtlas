# Testing Patterns

**Analysis Date:** 2026-02-12

## Test Framework

**Runner:**
- xUnit v3 (xunit.v3 3.0.1)
- xunit.analyzers 1.24.0
- xunit.runner.visualstudio 3.1.4

**Assertion Library:**
- FluentResults.Extensions.FluentAssertions 2.2.1
- Fluent assertions for Result<T> types: `.Should().BeSuccess()`, `.Should().BeFailure()`

**Run Commands:**
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

## Test File Organization

**Location:**
- Unit tests: `test/DotNetAtlas.UnitTests/[Domain]/[Feature]/[ComponentName]Tests.cs`
- Integration tests: `test/DotNetAtlas.IntegrationTests/[Layer]/[Feature]/[ComponentName]Tests.cs`
- Functional tests: `test/DotNetAtlas.FunctionalTests/[Feature]/[ComponentName]Tests.cs`
- Architecture tests: `test/DotNetAtlas.ArchitectureTests/[Layer]/[ComponentName]Tests.cs`
- Saga unit tests: `saga/DotNetAtlas.Sagas.UnitTests/Sagas/[SagaName]Tests.cs`
- Saga integration tests: `saga/DotNetAtlas.Sagas.IntegrationTests/Sagas/[SagaName]Tests.cs`

**Naming:**
- Test classes: `[ComponentUnderTest]Tests`
- Test methods: `[MethodUnderTest]_When[Condition]_[ExpectedOutcome]`
  - Examples: `Create_WhenValidCity_ReturnsSuccess()`, `WhenSubscribing_SchedulesAlertJob()`

**Structure:**
```
test/
├── DotNetAtlas.UnitTests/
│   ├── Common/
│   │   └── ValueObjects/
│   │       ├── CityTests.cs
│   │       └── DateRangeTests.cs
│   └── WeatherAlerts/
│       └── DomainEventHandlers/
│           └── WeatherAlertBroadcastDomainEventHandlerTests.cs
├── DotNetAtlas.IntegrationTests/
│   ├── Common/
│   │   ├── BaseIntegrationTest.cs          # Base class with fixture setup
│   │   ├── IntegrationTestFixture.cs       # Shared test infrastructure
│   │   └── WaitHelper.cs
│   └── Application/
│       └── WeatherAlerts/
│           └── SubscribeForLocationAlertsCommandHandlerTests.cs
├── DotNetAtlas.FunctionalTests/
├── DotNetAtlas.ArchitectureTests/
│   ├── Application/
│   │   ├── CommandHandlerTests.cs
│   │   └── QueryHandlerTests.cs
└── DotNetAtlas.Test.Framework/
    ├── Database/
    │   └── SqlServerTestContainer.cs
    ├── Kafka/
    │   ├── KafkaTestContainer.cs
    │   └── KafkaTestConsumerRegistry.cs
    └── Redis/
        └── RedisTestContainer.cs
```

## Test Structure

**Suite Organization:**

All tests follow AAA (Arrange-Act-Assert) pattern with `using` scope for assertion groups:

```csharp
// Unit Test Example: test/DotNetAtlas.UnitTests/Common/ValueObjects/CityTests.cs
namespace DotNetAtlas.UnitTests.Common.ValueObjects;

public class CityTests
{
    [Theory]
    [InlineData("Prague")]
    [InlineData("New York")]
    public void Create_WhenValidCity_ReturnsSuccess(string cityName)
    {
        // Arrange & Act
        var cityResult = City.Create(cityName);

        // Assert
        using (new AssertionScope())
        {
            cityResult.Should().BeSuccess();
            cityResult.Value.Name.Should().Be(cityName);
        }
    }

    [Fact]
    public void Create_WhenEmpty_ReturnsValidationError(string? cityName)
    {
        // Arrange & Act
        var cityResult = City.Create(cityName);

        // Assert
        using (new AssertionScope())
        {
            cityResult.Should().BeFailure();
            var validationError = cityResult.Errors[0] as ValidationError;
            validationError.Should().NotBeNull();
            validationError!.ErrorCode.Should().Be("City.Invalid");
        }
    }
}
```

**Integration Test Pattern:**

Integration tests inherit from `BaseIntegrationTest` and use `IntegrationTestFixture`:

```csharp
// test/DotNetAtlas.IntegrationTests/Application/WeatherAlerts/SubscribeForLocationAlertsCommandHandlerTests.cs
[Collection<SignalRTestCollection>]
public class SubscribeForLocationAlertsCommandHandlerTests : BaseIntegrationTest
{
    private readonly ICommandHandler<SubscribeForLocationAlertsCommand> _subscribeForLocationAlertsCommandHandler;
    private readonly IStorageConnection _jobStorageConnection;

    public SubscribeForLocationAlertsCommandHandlerTests(IntegrationTestFixture app)
        : base(app)
    {
        _subscribeForLocationAlertsCommandHandler =
            Scope.ServiceProvider.GetRequiredService<ICommandHandler<SubscribeForLocationAlertsCommand>>();
        _jobStorageConnection =
            Scope.ServiceProvider.GetRequiredService<IBackgroundJobClientV2>().Storage.GetConnection();
    }

    [Fact]
    public async Task WhenSubscribing_SchedulesAlertJob()
    {
        // Arrange
        var subscribeForLocationAlertsCommand = new SubscribeForLocationAlertsCommand
        {
            City = "Prague",
            CountryCode = CountryCode.CZ,
            ConnectionId = "conn-1"
        };

        // Act
        var subscribeForLocationAlertsResult = await _subscribeForLocationAlertsCommandHandler.HandleAsync(
            subscribeForLocationAlertsCommand,
            TestContext.Current.CancellationToken);

        var recurringJobCountAfterSubscribe = _jobStorageConnection.GetRecurringJobs().Count;

        // Assert
        using (new AssertionScope())
        {
            subscribeForLocationAlertsResult.Should().BeSuccess();
            recurringJobCountAfterSubscribe.Should().Be(1);
        }
    }
}
```

**Patterns:**
- Setup: Constructor dependency injection from fixture
- Teardown: `IAsyncLifetime.DisposeAsync()` inherited from `BaseIntegrationTest`
- Fixture reset: `_resetFixtureStateAsync()` called after each test (database, Redis, Hangfire cleaned)
- Test collections: Multiple test classes share fixtures via `[Collection<T>]` attribute
  - Allows parallel test execution across collections while running sequentially within a collection

## Mocking

**Framework:** NSubstitute v5.3.0

**Patterns:**

NSubstitute for dependency mocking:
```csharp
// test/DotNetAtlas.IntegrationTests/Common/IntegrationTestFixture.cs
services.AddSingleton(Substitute.For<IHealthCheckReportCollector>());
services.AddScoped(_ => Substitute.For<IWeatherAlertBroadcaster>());
```

**Fake Implementations (preferred over mocks for complex behavior):**

Test-scoped fake implementations capture behavior for verification:

```csharp
// test/DotNetAtlas.UnitTests/WeatherAlerts/DomainEventHandlers/WeatherAlertBroadcastDomainEventHandlerTests.cs
private sealed class FakeWeatherAlertBroadcaster : IWeatherAlertBroadcaster
{
    public List<(AlertGroup AlertGroup, WeatherAlert WeatherAlert)> SentAlerts { get; } = [];
    public List<(string ConnectionId, AlertGroup AlertGroup)> AddedConnections { get; } = [];
    public List<(string ConnectionId, AlertGroup AlertGroup)> RemovedConnections { get; } = [];

    public Task AddConnectionToGroupAsync(string connectionId, AlertGroup alertGroup, CancellationToken ct)
    {
        AddedConnections.Add((connectionId, alertGroup));
        return Task.CompletedTask;
    }

    public Task BroadcastToGroupAsync(AlertGroup alertGroup, WeatherAlert weatherAlert)
    {
        SentAlerts.Add((alertGroup, weatherAlert));
        return Task.CompletedTask;
    }
}
```

**Assertion on mock calls:**
```csharp
// test/DotNetAtlas.IntegrationTests/Application/WeatherForecast/CachedWeatherForecastFacadeTests.cs
var decoratedMock = Substitute.For<IWeatherForecastService>();
decoratedMock.GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>())
    .Returns(expectedForecast);

// After action
await decoratedMock.Received(1).GetForecastAsync(Arg.Any<ForecastCriteria>(), Arg.Any<CancellationToken>());
```

**What to Mock:**
- External services (HTTP clients, APIs)
- Event broadcasters for unit tests
- Storage access (use real database for integration tests via TestContainers)

**What NOT to Mock:**
- Aggregates and domain entities (test real behavior)
- Database context (use TestContainers with real SQL Server)
- Command/Query handlers (use integration tests with real dependencies)
- EF Core specifications (test with real queries)

## Fixtures and Factories

**Test Data Creation:**

Domain-driven approach using factory methods:

```csharp
// From test helper
private static WeatherAlertIssuedDomainEvent CreateDomainEvent(
    string city = "Prague",
    CountryCode? countryCode = null,
    string message = "High temperature alert: 40°C")
{
    return new WeatherAlertIssuedDomainEvent
    {
        MonitoredLocationId = Guid.CreateVersion7(),
        City = City.Create(city).Value,
        CountryCode = countryCode ?? CountryCode.CZ,
        WeatherAlert = WeatherAlert.Create(AlertType.HighTemperature, AlertSeverity.Warning, message).Value,
        TriggeringReading = WeatherReading.Create(
            Temperature.FromCelsius(40).Value,
            Humidity.FromPercent(50).Value,
            WindSpeed.FromKilometersPerHour(15).Value,
            UtcNow),
        IssuedAtUtc = UtcNow
    };
}
```

**Location:**
- Shared test fixtures: `test/DotNetAtlas.Test.Framework/`
- Integration test base: `test/DotNetAtlas.IntegrationTests/Common/BaseIntegrationTest.cs`
- Test containers: `test/DotNetAtlas.Test.Framework/Database/`, `Kafka/`, `Redis/`

**Test Infrastructure:**

`IntegrationTestFixture` provides:
- SQL Server TestContainer with Respawn for database cleanup
- Redis TestContainer
- Kafka TestContainer with Schema Registry
- ASP.NET TestHost for API endpoints
- Health check stubs
- Serilog routing to XUnit output
- Fixture reset between tests (database, cache, job queue)

```csharp
// test/DotNetAtlas.IntegrationTests/Common/IntegrationTestFixture.cs
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly SqlServerTestContainer _dbContainer = new(...);
    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    public async Task ResetFixtureStateAsync()
    {
        await Task.WhenAll(
            _dbContainer.CleanDataAsync(),
            _redisContainer.CleanDataAsync(),
            CleanHangfireJobsAsync()
        );
    }
}
```

## Coverage

**Requirements:** Not enforced via metrics, but `coverlet.runsettings` defines exclusions

**View Coverage:**
```bash
dotnet test --collect:"XPlat Code Coverage" --settings test/coverlet.runsettings
```

**Excluded from Coverage:**

File patterns (`.planning/codebase/test/coverlet.runsettings` lines 9-36):
- Generated code: `*Generated*`, `*.g.cs`, `*.Designer.cs`, `*AssemblyInfo.cs`
- EF migrations: `**/Migrations/**`
- Infrastructure: `**/DotNetAtlas.Api/Common/Swagger/**`, `**/DotNetAtlas.Api/Common/Exceptions/**`
- UI: `**/DotNetAtlas.Api/Pages/Index*`
- Base types: `**/DotNetAtlas.Domain/Common/Entity.cs`

## Test Types

**Unit Tests:**
- Scope: Single class/function in isolation
- Location: `test/DotNetAtlas.UnitTests/`
- Dependencies: Minimal (mocked or faked)
- Speed: Fast (< 100ms each)
- Example: `test/DotNetAtlas.UnitTests/Common/ValueObjects/CityTests.cs` - tests value object validation without database
- Uses: Fake implementations for event broadcasting, NSubstitute for logging

**Integration Tests:**
- Scope: Full handler with real dependencies (database, Kafka, Redis)
- Location: `test/DotNetAtlas.IntegrationTests/`
- Infrastructure: TestContainers (SQL Server, Redis, Kafka)
- Base class: `BaseIntegrationTest` with `IntegrationTestFixture`
- Speed: Slower (1-10 seconds per test)
- Example: `test/DotNetAtlas.IntegrationTests/Application/WeatherAlerts/SubscribeForLocationAlertsCommandHandlerTests.cs`
  - Tests command handler with real database, Hangfire, fixture cleanup
- Collections: Tests grouped by `[Collection<T>]` for fixture sharing and sequential execution within collection

**Functional Tests:**
- Scope: End-to-end HTTP API testing
- Location: `test/DotNetAtlas.FunctionalTests/`
- Base class: `ApiTestFixture` (extends FastEndpoints testing)
- Speed: Slower (2-15 seconds per test)
- Focus: API endpoint contract, response codes, routing

**Architecture Tests:**
- Scope: Code structure and naming conventions enforcement
- Location: `test/DotNetAtlas.ArchitectureTests/`
- Framework: NetArchTest.Rules
- Speed: Fast (< 1 second)
- Example: `test/DotNetAtlas.ArchitectureTests/Application/CommandHandlerTests.cs`
  ```csharp
  [Fact]
  public void CommandHandlers_Should_HaveNameEndingWith_CommandHandler()
  {
      var result = Types.InAssembly(ApplicationAssembly)
          .That()
          .ImplementInterface(typeof(ICommandHandler<>))
          .Should()
          .HaveNameEndingWith("CommandHandler")
          .GetResult();
      result.FailingTypes.Should().BeEmpty(...);
  }
  ```
- Enforces: Handler naming conventions, sealed classes, interface implementations

**Saga Tests:**
- Unit tests: `saga/DotNetAtlas.Sagas.UnitTests/` - State machine transitions using MassTransit.Testing
- Integration tests: `saga/DotNetAtlas.Sagas.IntegrationTests/` - Full saga with database and messaging

## Common Patterns

**Async Testing:**

All async test methods return `async Task`:
```csharp
[Fact]
public async Task WhenSubscribing_SchedulesAlertJob()
{
    // Arrange
    var command = new SubscribeForLocationAlertsCommand { ... };

    // Act
    var result = await _subscribeForLocationAlertsCommandHandler.HandleAsync(
        command,
        TestContext.Current.CancellationToken);

    // Assert
    result.Should().BeSuccess();
}
```

**Cancellation Token:**
- Use `TestContext.Current.CancellationToken` in integration tests
- Use `CancellationToken.None` in unit tests with mocks

**Error Testing:**

Test both success and failure paths with FluentResults:
```csharp
[Fact]
public void Create_WhenEmpty_ReturnsValidationError(string? cityName)
{
    // Arrange & Act
    var cityResult = City.Create(cityName);

    // Assert
    using (new AssertionScope())
    {
        cityResult.Should().BeFailure();
        var validationError = cityResult.Errors[0] as ValidationError;
        validationError.Should().NotBeNull();
        validationError!.ErrorCode.Should().Be("City.Invalid");
    }
}
```

**Data-Driven Tests:**

Use `[Theory]` with `[InlineData]` for multiple input sets:
```csharp
[Theory]
[InlineData("Prague")]
[InlineData("New York")]
[InlineData("AB")]
public void Create_WhenValidCity_ReturnsSuccess(string cityName)
{
    var cityResult = City.Create(cityName);
    cityResult.Should().BeSuccess();
}
```

**Assertion Scope:**

Use `using (new AssertionScope())` to capture multiple assertions before failure:
```csharp
using (new AssertionScope())
{
    subscribeForLocationAlertsResult.Should().BeSuccess();
    recurringJobCountAfterSubscribe.Should().Be(1);
}
```

**Wait/Polling Helper:**

Integration tests may use `WaitHelper` for eventual consistency:
- See `test/DotNetAtlas.IntegrationTests/Common/WaitHelper.cs`

**Observability in Tests:**

Test failures automatically logged to Jaeger:
- `BaseIntegrationTest` uses `TestCaseTracer` to emit OpenTelemetry traces
- Failed tests recorded with exception details
- Local Jaeger link logged to test output for inspection

---

*Testing analysis: 2026-02-12*
