<div align="center">

# 🔀 Command Query Separation

</div>

| ⚡ TL;DR |
| -------- |
| CQS separates operations into Commands (change state, return nothing/ID) and Queries (read state, no side effects). DotNetAtlas implements this with a decorator pipeline that adds validation, logging, tracing, and metrics to every operation without polluting handler code. |

Command Query Separation (CQS) is a principle stating that every method should either be a **command** that performs an action, or a **query** that returns data, but not both.

## 🎯 Why CQS?

Without CQS, methods often do too much:

```csharp
// ❌ Mixed concerns - hard to reason about
public Feedback GetOrCreateFeedback(string text, int rating)
{
    var existing = _db.Feedback.FirstOrDefault(f => f.Text == text);
    if (existing != null)
        return existing;  // Query behavior
    
    var feedback = new Feedback(text, rating);
    _db.Feedback.Add(feedback);
    _db.SaveChanges();  // Command behavior
    return feedback;
}
```

With CQS, intent is clear:

```csharp
// ✅ Clear separation
public record GetFeedbackQuery(Guid Id) : IQuery<FeedbackResponse>;
public record SendFeedbackCommand(string Text, int Rating) : ICommand<Guid>;
```

## 📦 Commands and Queries

### Commands

Commands represent intent to change state. They're named as imperative verbs:

```csharp
public record SendFeedbackCommand(string Text, int Rating) : ICommand<Guid>;
public record ChangeFeedbackCommand(Guid Id, string Text, int Rating) : ICommand;
public record DeleteFeedbackCommand(Guid Id) : ICommand;
```

Commands can return:
- `ICommand` - Returns `Result` (success/failure only)
- `ICommand<T>` - Returns `Result<T>` (success with value, or failure)

### Queries

Queries represent intent to read state. They're named as questions:

```csharp
public record GetFeedbackQuery(Guid Id) : IQuery<FeedbackResponse>;
public record GetAllFeedbackQuery(int Page, int PageSize) : IQuery<PagedResult<FeedbackResponse>>;
public record GetFeedbackByUserQuery(Guid UserId) : IQuery<IReadOnlyList<FeedbackResponse>>;
```

Queries always return `Result<T>` - they can fail (not found, unauthorized, etc.).

## 🔧 Handlers

Each command/query has a dedicated handler:

```csharp
public class SendFeedbackHandler : ICommandHandler<SendFeedbackCommand, Guid>
{
    private readonly IWeatherDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    
    public async Task<Result<Guid>> HandleAsync(
        SendFeedbackCommand command, 
        CancellationToken ct)
    {
        // Create value objects
        var textResult = FeedbackText.Create(command.Text);
        var ratingResult = FeedbackRating.Create(command.Rating);
        
        if (textResult.IsFailed || ratingResult.IsFailed)
            return Result.Merge(textResult, ratingResult);
        
        // Create aggregate
        var feedbackResult = Feedback.Create(
            textResult.Value, 
            ratingResult.Value, 
            _currentUser.Id);
        
        if (feedbackResult.IsFailed)
            return feedbackResult;
        
        // Persist
        await _dbContext.Feedback.AddAsync(feedbackResult.Value, ct);
        await _dbContext.SaveChangesAsync(ct);
        
        return feedbackResult.Value.Id;
    }
}
```

Query handlers are similar but read-only:

```csharp
public class GetFeedbackHandler : IQueryHandler<GetFeedbackQuery, FeedbackResponse>
{
    public async Task<Result<FeedbackResponse>> HandleAsync(
        GetFeedbackQuery query, 
        CancellationToken ct)
    {
        var feedback = await _dbContext.Feedback
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == query.Id, ct);
        
        if (feedback is null)
            return Result.Fail(FeedbackErrors.NotFound(query.Id));
        
        return new FeedbackResponse(
            feedback.Id,
            feedback.FeedbackText.Text,
            feedback.Rating.Value);
    }
}
```

## 🎭 The Decorator Pipeline

The real power of CQS comes from the decorator pipeline. Cross-cutting concerns wrap handlers:

```
Request → Validation → Logging → Tracing → Metrics → Handler → Response
```

### ValidationBehavior

Runs FluentValidation before the handler:

```csharp
public class ValidationBehavior<TCommand, TResult> : ICommandHandler<TCommand, TResult>
{
    private readonly IValidator<TCommand> _validator;
    private readonly ICommandHandler<TCommand, TResult> _next;
    
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        var validationResult = await _validator.ValidateAsync(command, ct);
        
        if (!validationResult.IsValid)
            return Result.Fail(validationResult.ToErrors());
        
        return await _next.HandleAsync(command, ct);
    }
}
```

### LoggingBehavior

Logs command execution with structured data:

```csharp
public class LoggingBehavior<TCommand, TResult> : ICommandHandler<TCommand, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Handling {CommandType} {@Command}", 
            typeof(TCommand).Name, command);
        
        var result = await _next.HandleAsync(command, ct);
        
        _logger.LogInformation("Handled {CommandType} with result {IsSuccess}",
            typeof(TCommand).Name, result.IsSuccess);
        
        return result;
    }
}
```

### TracingBehavior

Creates OpenTelemetry spans:

```csharp
public class TracingBehavior<TCommand, TResult> : ICommandHandler<TCommand, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity(
            $"Handle {typeof(TCommand).Name}",
            ActivityKind.Internal);
        
        activity?.SetTag("cqs.command.type", typeof(TCommand).Name);
        
        var result = await _next.HandleAsync(command, ct);
        
        activity?.SetTag("cqs.result.success", result.IsSuccess);
        if (result.IsFailed)
            activity?.SetTag("cqs.result.error", result.Errors.First().Message);
        
        return result;
    }
}
```

### MetricsBehavior

Records metrics for monitoring:

```csharp
public class MetricsBehavior<TCommand, TResult> : ICommandHandler<TCommand, TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        
        var result = await _next.HandleAsync(command, ct);
        
        stopwatch.Stop();
        
        _commandCounter.Add(1, new("command.type", typeof(TCommand).Name));
        _commandDuration.Record(stopwatch.ElapsedMilliseconds, 
            new("command.type", typeof(TCommand).Name));
        
        if (result.IsFailed)
            _errorCounter.Add(1, new("command.type", typeof(TCommand).Name));
        
        return result;
    }
}
```

## 🔌 Registration

Decorators are registered in order using Scrutor:

```csharp
services.Scan(scan => scan
    .FromAssemblyOf<SendFeedbackHandler>()
    .AddClasses(c => c.AssignableTo(typeof(ICommandHandler<,>)))
    .AsImplementedInterfaces()
    .WithScopedLifetime());

services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationBehavior<,>));
services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingBehavior<,>));
services.Decorate(typeof(ICommandHandler<,>), typeof(TracingBehavior<,>));
services.Decorate(typeof(ICommandHandler<,>), typeof(MetricsBehavior<,>));
```

## 🎯 Benefits

| Benefit | Description |
|---------|-------------|
| **Single Responsibility** | Handlers focus on business logic only |
| **Testability** | Test handlers without cross-cutting concerns |
| **Consistency** | All operations get validation, logging, tracing |
| **Flexibility** | Add/remove behaviors without changing handlers |

## 📖 Further Reading

- [**CQS Platform Library**](../platform/CQS.md) - Implementation details
- [**Observability**](../features/Observability.md) - How tracing works
- [**Clean Architecture**](CleanArchitecture.md) - Where CQS fits

