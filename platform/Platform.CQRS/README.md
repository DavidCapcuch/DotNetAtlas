# Platform.CQRS

A lightweight Command Query Responsibility Separation (CQRS) library for .NET applications. Provides a clean architecture pattern for separating read and write operations with built-in support for cross-cutting concerns through decorator-based behaviors.

## The Problem

Applying cross-cutting concerns (validation, logging, metrics, tracing) to commands and queries leads to:

- Repetitive boilerplate in every handler
- Inconsistent implementation across the codebase
- Tight coupling between business logic and infrastructure

## The Solution

Handlers implement a simple interface. Cross-cutting concerns are added as decorators that wrap handlers automatically. Inject handlers directly - no dispatchers needed.

## Quick Start

### 1. Define Command/Query

```csharp
public record CreateOrderCommand(Guid CustomerId) : ICommand;
public record GetOrderQuery(Guid Id) : IQuery<OrderDto>;
```

### 2. Implement Handler

```csharp
public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
{
    public async Task<Result> HandleAsync(CreateOrderCommand command, CancellationToken ct)
    {
        // Your business logic
        return Result.Ok();
    }
}
```

### 3. Register Services

```csharp
// Register handlers first (order matters - behaviors decorate existing registrations)
services.AddCqrsHandlersFromAssembly(typeof(Program).Assembly);

// Then add behaviors (decorators)
services.AddCqrsValidationBehavior();
services.AddCqrsLoggingBehavior();
services.AddCqrsMetricsBehavior();
services.AddCqrsTracingBehavior();
```

### 4. OpenTelemetry Setup (if using Metrics or Tracing Behavior)

```csharp
using Platform.CQRS.Observability;

services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(CqrsInstrumentation.MeterName))
    .WithTracing(tracing => tracing
        .AddSource(CqrsInstrumentation.ActivitySourceName));
```

### 5. Inject and Use

```csharp
public class OrderService
{
    private readonly ICommandHandler<CreateOrderCommand> _handler;

    public OrderService(ICommandHandler<CreateOrderCommand> handler)
    {
        _handler = handler;
    }

    public Task<Result> CreateAsync(Guid customerId, CancellationToken ct)
        => _handler.HandleAsync(new CreateOrderCommand(customerId), ct);
}
```

## Behaviors

| Behavior | What It Does |
|----------|--------------|
| `AddCqrsValidationBehavior()` | Runs FluentValidation before handler, returns `Result.Fail` on validation errors |
| `AddCqrsLoggingBehavior()` | Structured logging of processing, completion, errors, exceptions |
| `AddCqrsMetricsBehavior()` | OpenTelemetry metrics (counters, histograms) - meter: `Platform.CQRS` |
| `AddCqrsTracingBehavior()` | OpenTelemetry activity spans with trace context propagation |

### Metrics Behavior detail

- Use meter name `Platform.CqrsInstrumentation.MeterName`.

**Command Metrics:**

| Metric | Type | Tags | Description |
|--------|------|------|-------------|
| `commands_total` | Counter | `command_name`, `status` | Total commands executed |
| `command_duration_ms` | Histogram | `command_name`, `status` | Execution duration in ms |
| `command_errors_total` | Counter | `command_name`, `error_type`, `error_code` | Domain errors by type |
| `command_exceptions_total` | Counter | `command_name`, `exception_type`, `is_critical` | Exceptions by type |

**Query Metrics:**

| Metric | Type | Tags | Description |
|--------|------|------|-------------|
| `queries_total` | Counter | `query_name`, `status` | Total queries executed |
| `query_duration_ms` | Histogram | `query_name`, `status` | Execution duration in ms |
| `query_errors_total` | Counter | `query_name`, `error_type`, `error_code` | Domain errors by type |
| `query_exceptions_total` | Counter | `query_name`, `exception_type`, `is_critical` | Exceptions by type |

**Status values:** `success`, `failed`, `exception`

### Tracing Behavior Detail

- Use activity source name `CqrsInstrumentation.ActivitySourceName`.
- Creates individual spans for each command/query execution with trace context propagation.

**Span Tags (on error):**

| Tag | Description |
|-----|-------------|
| `domain.error` | `true` if domain errors occurred |
| `domain.error.count` | Number of domain errors |
| `error.type` | Error type name |
| `error.code` | Error code |
| `error.message` | Error message |
| `exception.critical` | `true` if exception is `CriticalException` |
| `exception.code` | Error code from `CriticalException` |

**Events:**

| Event | When |
|-------|------|
| `DomainError` | Added for each domain error in failed Result |
| `Error` | Added when exception is thrown |

## Interfaces

| Interface | Description |
|-----------|-------------|
| `ICommand` | Command with no return value |
| `ICommand<TResponse>` | Command with return value |
| `ICommandHandler<TCommand>` | Returns `Task<Result>` |
| `ICommandHandler<TCommand, TResponse>` | Returns `Task<Result<TResponse>>` |
| `IQuery<TResponse>` | Query with return type |
| `IQueryHandler<TQuery, TResponse>` | Returns `Task<Result<TResponse>>` |

## Related Packages

- [Platform.SharedKernel](../Platform.SharedKernel) - Error types for Result pattern
