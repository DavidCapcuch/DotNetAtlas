# Coding Conventions

**Analysis Date:** 2026-02-12

## Naming Patterns

**Files:**
- C# source files: `PascalCase.cs` (e.g., `AlertSubscriber.cs`, `ExtendSubscriptionCommandHandler.cs`)
- Test files: `[ComponentName]Tests.cs` (e.g., `CityTests.cs`, `AlertSubscriptionExtensionSagaOrchestratorTests.cs`)
- EF Core migrations: `[timestamp]_[DescriptiveName].cs` located in `Persistence/Database/Migrations/`

**Functions/Methods:**
- PascalCase for all public, internal, and private methods
- Sealed handler classes use `HandleAsync` or `Handle` for command/query/event processing
- Test methods: `[MethodUnderTest]_When[Condition]_[ExpectedOutcome]` pattern
  - Example: `WhenSubscribing_SchedulesAlertJob()`, `Create_WhenValidCity_ReturnsSuccess()`

**Variables:**
- camelCase for local variables, parameters, and field declarations
- Avoid `this.` qualification (enforced via EditorConfig as error)

**Types:**
- PascalCase for classes, structs, interfaces, enums
- Interface naming: `I` prefix (e.g., `ICommandHandler<T>`, `IWeatherForecastService`)
- Type parameters: `T` prefix (e.g., `TCommand`)

**Constants/Fields:**
- Public/internal fields: PascalCase
- Private fields: `_camelCase` with leading underscore
- Constants (public): PascalCase
- Constants (private): PascalCase (no underscore)
- Static readonly fields: PascalCase

## Code Style

**Formatting:**
- dotnet format (enforced in CI)
- Indentation: 4 spaces
- Line endings: LF
- Charset: UTF-8
- File must end with single newline

**Linting:**
- StyleCop.Analyzers v1.2.0-beta.556
- SonarAnalyzer.CSharp rules
- Microsoft Code Analyzers (CA* rules)
- TreatWarningsAsErrors enabled globally
- EditorConfig enforces 100+ rules as errors

**Key EditorConfig Rules Enforced as Errors:**
- File-scoped namespaces: `csharp_style_namespace_declarations = file_scoped`
  - All namespaces use `namespace X;` format (not `namespace X { }`)
  - See `src/DotNetAtlas.Api/Endpoints/Auth/LoginEndpoint.cs` line 5 as example
- No qualification of `this`, `Me` on fields/properties/methods/events
- Predefined types for locals/parameters (use `int` not `Int32`)
- Pattern matching over `is`/`as` casts
- Null coalescing and propagation operators
- Auto-properties preferred
- Braces required on single-line statements
- Using directives outside namespace

**Expression-Bodied Members:**
- Properties: encouraged (`=> value;`)
- Accessors: encouraged
- Methods: discouraged (multi-line implementation preferred)
- Constructors: discouraged (full block preferred)
- Lambdas: encouraged

## Import Organization

**Order (enforced):**
1. System imports (`using System;`, `using System.Collections.Generic;`, etc.)
2. Third-party imports (alphabetical)
3. Project imports (alphabetical)

**Path Aliases:**
Not used; full namespaces only. Example from `src/DotNetAtlas.Application/WeatherAlerts/ExtendSubscription/ExtendSubscriptionCommandHandler.cs`:
```csharp
using Ardalis.Specification.EntityFrameworkCore;
using DotNetAtlas.Application.Common.Data;
using DotNetAtlas.CQS;
using DotNetAtlas.Domain.Alerts.Errors;
using DotNetAtlas.Domain.Alerts.Specifications;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
```

## Error Handling

**Pattern:** FluentResults library

All business logic returns `Result<T>` or `Result` to represent success/failure:
```csharp
// From src/DotNetAtlas.Domain/Alerts/AlertSubscriber.cs
public static AlertSubscriber CreateFree(Guid userId) { ... }

// From src/DotNetAtlas.Application/.../ExtendSubscriptionCommandHandler.cs
public async Task<Result> HandleAsync(ExtendSubscriptionCommand command, CancellationToken ct)
{
    var alertSubscriber = await _weatherDbContext.AlertSubscribers
        .WithSpecification(new SubscriberByUserIdSpec(command.UserId))
        .FirstOrDefaultAsync(ct);

    if (alertSubscriber is null)
    {
        _logger.LogWarning("Subscriber not found for UserId {UserId}", command.UserId);
        return Result.Fail(AlertSubscriberErrors.SubscriberNotFound(command.UserId));
    }

    alertSubscriber.ExtendSubscription(...);
    await _weatherDbContext.SaveChangesAsync(ct);
    return Result.Ok();
}
```

**Error Classes:**
- Located in domain aggregate folders: `src/DotNetAtlas.Domain/Alerts/Errors/AlertSubscriberErrors.cs`
- Static factory methods that return `IError` instances
- Example: `AlertSubscriberErrors.SubscriberNotFound(userId)`

**Exceptions vs Results:**
- Business validations: return `Result.Fail(error)`
- Data integrity violations: throw exceptions (caught by DeadLetterMiddleware)
- See comment in `ExtendSubscriptionCommandHandler.cs` lines 42-44 regarding `DataIntegrityException`

## Logging

**Framework:** Serilog with structured logging

**Patterns:**
- Dependency injection: `ILogger<T>` injected in constructor
- Log levels:
  - `LogInformation`: State changes, domain event outcomes
  - `LogWarning`: Recoverable errors (subscriber not found, validation failures)
  - `LogError`: Unrecoverable system errors
  - `LogDebug`: Detailed state information (in integration tests only)

**Example from handlers:**
```csharp
_logger.LogWarning(
    "Subscriber not found for UserId {UserId}, cannot extend subscription",
    command.UserId);

_logger.LogInformation(
    "Extended subscription for subscriber {SubscriberId} (UserId: {UserId}) by {DurationDays} days",
    alertSubscriber.Id, command.UserId, command.DurationExtendedDays);
```

**Integration Tests:**
- Serilog output redirected to XUnit test output via `IInjectableTestOutputSink`
- See `test/DotNetAtlas.IntegrationTests/Common/IntegrationTestFixture.cs` lines 87-96

## Comments

**When to Comment:**
- Complex business logic requiring explanation
- Non-obvious algorithm rationale
- Constraints or assumptions about data/state
- See `src/DotNetAtlas.Domain/Alerts/AlertSubscriber.cs` for extensive examples

**JSDoc/TSDoc Format (XML):**
- `<summary>`: Brief description (1-2 sentences)
- `<remarks>`: Implementation details, related events, caveats
- `<param>`: Parameter descriptions
- `<returns>`: Return value description
- `<list type="bullet">` with `<item>`: Enumerated lists
- `<see cref="...">` for cross-references

**Example:**
```csharp
/// <summary>
/// Creates a new subscriber with the Free tier.
/// </summary>
/// <param name="userId">The user identifier.</param>
/// <returns>A new free tier subscriber.</returns>
/// <remarks>
/// Possible raised events:
/// <list type="bullet">
/// <item><see cref="SubscriberCreatedDomainEvent"/>: Always raised when a new subscriber is created.</item>
/// </list>
/// </remarks>
public static AlertSubscriber CreateFree(Guid userId)
```

**StyleCop Configuration:**
- SA1600 (missing XML documentation) disabled globally
- SA1633 (file headers) disabled
- Specific rules disabled for readability (see `.editorconfig` lines 377-475)

## Function Design

**Size:** Aim for single responsibility; methods typically 10-30 lines

**Parameters:**
- Name parameters descriptively (`userId`, `correlationId`, `durationExtendedDays`)
- Always include `CancellationToken ct` as final parameter for async operations
- Pass by reference not used; immutability preferred

**Return Values:**
- Commands return `Task<Result>` or `Task<Result<T>>`
- Queries return `Task<Result<T>>`
- Void returns used only in Event-driven patterns (domain event handlers)
- Async operations always return `Task` or `ValueTask`

**Handler Signatures:**
```csharp
public sealed class ExtendSubscriptionCommandHandler : ICommandHandler<ExtendSubscriptionCommand>
{
    public async Task<Result> HandleAsync(ExtendSubscriptionCommand command, CancellationToken ct)
    {
        // implementation
    }
}
```

## Module Design

**Exports:**
- Public classes represent the public API of each layer
- Internal classes hide implementation details
- Sealed classes prevent unintended inheritance (enforce in architecture tests)

**Barrel Files:**
- Not used; explicit imports required
- Forces developers to think about API surface
- Example: Import directly from `DotNetAtlas.Application.WeatherAlerts.ExtendSubscription` not a barrel

**Namespace Convention:**
- Match folder structure exactly (enforced by EditorConfig `dotnet_style_namespace_match_folder`)
- Example: `src/DotNetAtlas.Application/WeatherAlerts/ExtendSubscription/` → `namespace DotNetAtlas.Application.WeatherAlerts.ExtendSubscription;`

**Specification Pattern:**
- Query filters use `Ardalis.Specification` (e.g., `SubscriberByUserIdSpec`)
- Located in domain or application aggregate folders
- Decouples queries from repository implementation

**CQS Pattern (not CQRS):**
- Commands: `ICommandHandler<TCommand>` or `ICommandHandler<TCommand, TResponse>`
- Queries: `IQueryHandler<TQuery, TResponse>`
- Handlers injected directly, not via mediator
- Behaviors (cross-cutting concerns) applied via decorator pattern
  - Validation (FluentValidation)
  - Logging
  - Metrics
  - Tracing

---

*Convention analysis: 2026-02-12*
