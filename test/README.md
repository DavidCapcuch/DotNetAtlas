# Testing Guide

## Quick Start: Code Coverage

### Automated Script (Recommended)

**Run from the test directory:**
```bash
.\test-coverage.ps1
```
The [test-coverage.ps1](test-coverage.ps1) script automates the entire coverage workflow:
- Runs all tests with coverage collection
- Generates a coverage HTML report in `test/coveragereport/`
- Opens the coverage report in your browser
- Cleans up intermediate test results

### Script Options

```powershell
# Default behavior - cleans test results before and after
.\test-coverage.ps1

# Keep intermediate test results (don't clean before running)
.\test-coverage.ps1 -CleanTestResults:$false

# Use different report format (see ReportGenerator docs for options)
.\test-coverage.ps1 -ReportTypes "Html;HtmlSummary;Badges"
```

### Manual: Step-by-Step

If you need to run coverage manually (e.g., for CI/CD customization), here are the individual steps that the script automates:

```bash
# 1. Install ReportGenerator tool (one-time)
dotnet tool install -g dotnet-reportgenerator-globaltool

# 2. Run tests with coverage (from repo root)
dotnet test --collect:"XPlat Code Coverage" --settings test/coverlet.runsettings

# 3. Generate unified HTML report
reportgenerator `
  -reports:"**/coverage.cobertura.xml" `
  -targetdir:"test/coveragereport" `
  -reporttypes:"Html_Dark"

# 4. Open the report
start test/coveragereport/index.html  # Windows
open test/coveragereport/index.html   # macOS
xdg-open test/coveragereport/index.html  # Linux
```

## Test Project Structure

All test projects share common configuration through:

- **[test/Directory.Build.props](Directory.Build.props)** - Shared MSBuild properties and configuration
- **[test/Directory.Packages.props](Directory.Packages.props)** - Centralized package management specifically for tests
- **[test/xunit.runner.json](xunit.runner.json)** - Shared xUnit configuration
- **[test/coverlet.runsettings](coverlet.runsettings)** - Shared code coverage settings for Coverlet

This avoids duplication across test projects and ensures consistency.

## Configuration Files

### [test/Directory.Build.props](Directory.Build.props)

Inherits from the root [Directory.Build.props](../Directory.Build.props) and adds test-specific configuration:

- Common usings (Xunit, AwesomeAssertions)
- Common package references (Xunit, AwesomeAssertions, Coverlet, analyzers)
- Common test settings
- Shared file references (`xunit.runner.json`, `coverlet.runsettings`)

### [test/Directory.Packages.props](Directory.Packages.props)

Defines test-specific packages, completely separate from the main `Directory.Packages.props` to prevent pollution as the dependencies are completely different for test projects.

### [test/xunit.runner.json](xunit.runner.json)

Shared xUnit runner configuration:

- Parallel test execution settings
- Max parallel threads

### [test/coverlet.runsettings](coverlet.runsettings)

Shared code coverage configuration for Coverlet:

- **Output Format:** Cobertura XML (`coverage.cobertura.xml`)
- **Collector:** Coverlet XPlat Code Coverage (`--collect:"XPlat Code Coverage"`)
- Defines test coverage exclusions:
    - Excluded by File Path (`ExcludeByFile`)
    - `**/test/**` - All test project files
    - **Auto-generated and build artifacts:**
        - `**/*Designer.cs` - Designer-generated files
        - `**/*.g.cs`, `**/*.g.i.cs` - Generated code files
        - `**/obj/**`, `**/bin/**` - Build output directories
        - `**/Migrations/**` - Database migrations
        - Exclusion by Attributes `[ExcludeFromCodeCoverage]`, `[GeneratedCode]`, `[CompilerGenerated]`
      eg from Mapperly, MessagePack, FastEndpoints, TypedSignalR..

## Test Collections

Integration tests that have infrastructure dependencies (Database, Kafka, Redis, etc.) share a single collection per assembly, backed by a single Fixture that hosts the required dependencies.

**Within a collection, tests run sequentially. This ensures:**

- Fixture state is reset between tests (e.g., using
  [Respawn](https://github.com/jbogard/Respawn) to clean database tables, Redis `FLUSHALL`, Hangfire job cleanup)
- A single set of test containers is alive per assembly — bounding peak Docker RAM and avoiding OOM-driven flakiness

Each assembly has exactly one collection:

- `Catalog.IntegrationTests` → `IntegrationTestCollection` (backed by `IntegrationTestFixture`)
- `SagaOrchestrators.IntegrationTests` → `SagaTestCollection` (backed by `SagaIntegrationTestFixture`)

## Choosing the Right Test Level (Harness Rules)

Each test must use the harness appropriate to its level. **Never hand-reassemble a partial
composition root** — a `new ServiceCollection()` that calls a *subset* of the app's real wiring
(e.g. just `AddApplication()`), sets a few config keys, and `BuildServiceProvider()`. That is a
drift-prone half-replica of `Program`: it silently diverges the moment a binding moves to a layer
the test forgot to call (a real bug this rule was introduced to kill — an Application-only test
that never bound `TopicsOptions`, so the handler emitted a `null` Kafka topic at runtime).

| Level | Harness | Rule |
| --- | --- | --- |
| `*.UnitTests` | None | Construct the SUT directly with explicit deps/stubs (NSubstitute, fakes). No DI container, no infrastructure. |
| `*.IntegrationTests` | Shared `AppFixture<Program>` fixture | Ride the per-assembly fixture (real composition root on Testcontainers). Drive the slice's public entrance — an HTTP request or the Kafka message — through the fixture; resolve the SUT directly (the fixture's *real* `DbContext` + a stubbed port) only for behaviour with no outer entrance. Swap **only** external seams via `ConfigureTestServices(... services.Replace(...) ...)` — repositories, HTTP adapters, `IOutboxWriter`→`FakeOutboxWriter`, `TimeProvider`. |

If a "unit" test needs real wiring/infra, it is an integration test — move it onto the fixture.
If an "integration" test only exercises in-process logic with stubs and a directly-constructed SUT
(no Testcontainers, no real composition root), it is a unit test — move it down a level.

**The only legitimate `new ServiceCollection()` / `BuildServiceProvider()` in tests:**

1. A `Common/` fixture overriding `AppFixture<Program>.ConfigureTestServices` — the canonical good pattern.
2. A `*.UnitTests` test whose SUT **is** a DI-registration extension (e.g.
   `Platform.ServiceDefaults.UnitTests` asserting `AddIdempotency()` registers the right services) —
   here the container is the system under test, not a stand-in for `Program`.
3. MassTransit `AddMassTransitTestHarness(...)` in saga/consumer unit tests — the framework's
   prescribed in-memory harness; no app composition-root extension is called.
