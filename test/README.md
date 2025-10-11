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

Integration and Functional tests that have infrastructure dependencies (Database, Kafka, Redis, etc.) are separated into collections, each with a dedicated Fixture hosting required dependencies.

**Within a collection, tests run sequentially; across collections, tests run in parallel. This ensures:**

- Fixture state is reset between tests than run within a collection (e.g., using
  [Respawn](https://github.com/jbogard/Respawn) to clean database tables)
- Safe parallel execution across collections without interference with each other

For example, in [Functional Tests](DotNetAtlas.FunctionalTests), there are three collections
(each with its own Fixture): `FeedbackTestCollection`, `ForecastTestCollection`,
`SignalRTestCollection`, which run in parallel to each other.
