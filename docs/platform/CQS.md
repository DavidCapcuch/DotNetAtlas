<div align="center">

# ⚡ CQS Platform Library

</div>

| ⚡ TL;DR |
| -------- |
| The CQS library provides `ICommand`, `IQuery`, handler interfaces, and decorator behaviors for validation, logging, tracing, and metrics. Register handlers with Scrutor, then decorate them in order. |

The CQS platform library implements Command Query Separation with a decorator pipeline. It's designed to be copied and adapted, not installed as a NuGet package.

## 📦 What's Included

```
platform/DotNetAtlas.CQS/
├── Commands/
│   ├── ICommand.cs
│   ├── ICommandHandler.cs
│   └── Behaviors/
│       ├── CommandValidationBehavior.cs
│       ├── CommandLoggingBehavior.cs
│       ├── CommandTracingBehavior.cs
│       └── CommandMetricsBehavior.cs
├── Queries/
│   ├── IQuery.cs
│   ├── IQueryHandler.cs
│   └── Behaviors/
│       ├── QueryLoggingBehavior.cs
│       ├── QueryTracingBehavior.cs
│       └── QueryMetricsBehavior.cs
└── DependencyInjection/
    └── CQSServiceCollectionExtensions.cs
```

## 🔧 Core Interfaces

### Commands

```csharp
/// <summary>
/// Marker interface for commands that don't return a value.
/// </summary>
public interface ICommand { }

/// <summary>
/// Marker interface for commands that return a value.
/// </summary>
public interface ICommand<TResult> { }

/// <summary>
/// Handler for commands without return value.
/// </summary>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task<Result> HandleAsync(TCommand command, CancellationToken ct);
}

/// <summary>
/// Handler for commands with return value.
/// </summary>
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct);
}
```

### Queries

```csharp
/// <summary>
/// Marker interface for queries.
/// </summary>
public interface IQuery<TResult> { }

/// <summary>
/// Handler for queries.
/// </summary>
public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken ct);
}
```

## 🎭 Behaviors

### ValidationBehavior

Runs FluentValidation before the handler:

```csharp
public class CommandValidationBehavior<TCommand, TResult> 
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private readonly IValidator<TCommand>? _validator;
    private readonly ICommandHandler<TCommand, TResult> _next;
    
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        if (_validator is null)
            return await _next.HandleAsync(command, ct);
        
        var validationResult = await _validator.ValidateAsync(command, ct);
        
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new Error(e.ErrorMessage)
                    .WithMetadata("PropertyName", e.PropertyName))
                .ToList();
            
            return Result.Fail<TResult>(errors);
        }
        
        return await _next.HandleAsync(command, ct);
    }
}
```

### TracingBehavior

Creates OpenTelemetry spans:

```csharp
public class CommandTracingBehavior<TCommand, TResult> 
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private static readonly ActivitySource ActivitySource = new("DotNetAtlas.CQS");
    private readonly ICommandHandler<TCommand, TResult> _next;
    
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity(
            $"Command: {typeof(TCommand).Name}",
            ActivityKind.Internal);
        
        activity?.SetTag("cqs.type", "command");
        activity?.SetTag("cqs.command", typeof(TCommand).Name);
        
        try
        {
            var result = await _next.HandleAsync(command, ct);
            
            activity?.SetTag("cqs.success", result.IsSuccess);
            if (result.IsFailed)
            {
                activity?.SetTag("cqs.error", result.Errors.First().Message);
                activity?.SetStatus(ActivityStatusCode.Error);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.RecordException(ex);
            throw;
        }
    }
}
```

### MetricsBehavior

Records counters and histograms:

```csharp
public class CommandMetricsBehavior<TCommand, TResult> 
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private static readonly Meter Meter = new("DotNetAtlas.CQS");
    private static readonly Counter<long> CommandCounter = 
        Meter.CreateCounter<long>("cqs.commands.total");
    private static readonly Counter<long> ErrorCounter = 
        Meter.CreateCounter<long>("cqs.commands.errors");
    private static readonly Histogram<double> DurationHistogram = 
        Meter.CreateHistogram<double>("cqs.commands.duration", "ms");
    
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        var commandName = typeof(TCommand).Name;
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await _next.HandleAsync(command, ct);
            
            CommandCounter.Add(1, new KeyValuePair<string, object?>("command", commandName));
            
            if (result.IsFailed)
                ErrorCounter.Add(1, new KeyValuePair<string, object?>("command", commandName));
            
            return result;
        }
        finally
        {
            DurationHistogram.Record(
                stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("command", commandName));
        }
    }
}
```

## 🔌 Registration

Use the extension method to register everything:

```csharp
// In Program.cs or DI setup
services.AddCQS(typeof(SendFeedbackHandler).Assembly);
```

The extension method:

```csharp
public static IServiceCollection AddCQS(
    this IServiceCollection services, 
    params Assembly[] assemblies)
{
    // Register all handlers
    services.Scan(scan => scan
        .FromAssemblies(assemblies)
        .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<>)))
        .AsImplementedInterfaces()
        .WithScopedLifetime()
        .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)))
        .AsImplementedInterfaces()
        .WithScopedLifetime()
        .AddClasses(c => c.AssignableTo(typeof(IQueryHandler<,>)))
        .AsImplementedInterfaces()
        .WithScopedLifetime());
    
    // Register validators
    services.AddValidatorsFromAssemblies(assemblies);
    
    // Decorate with behaviors (order matters - outermost first)
    services.Decorate(typeof(ICommandHandler<>), typeof(CommandMetricsBehavior<>));
    services.Decorate(typeof(ICommandHandler<>), typeof(CommandTracingBehavior<>));
    services.Decorate(typeof(ICommandHandler<>), typeof(CommandLoggingBehavior<>));
    services.Decorate(typeof(ICommandHandler<>), typeof(CommandValidationBehavior<>));
    
    // Same for ICommandHandler<,> and IQueryHandler<,>
    // ...
    
    return services;
}
```

## 🎯 Usage in Endpoints

```csharp
public class SendFeedbackEndpoint : Endpoint<SendFeedbackRequest, SendFeedbackResponse>
{
    private readonly ICommandHandler<SendFeedbackCommand, Guid> _handler;
    
    public override async Task HandleAsync(SendFeedbackRequest req, CancellationToken ct)
    {
        var command = new SendFeedbackCommand(req.Text, req.Rating);
        var result = await _handler.HandleAsync(command, ct);
        
        await result.Match(
            success => SendOkAsync(new SendFeedbackResponse(success)),
            failure => SendErrorsAsync(failure.Errors)
        );
    }
}
```

## 📖 Further Reading

- [**CQS Architecture**](../architecture/CQS.md) - Conceptual overview
- [**Observability**](../features/Observability.md) - Tracing and metrics details

