# Platform.SharedKernel

A foundational library providing [Domain-Driven Design (DDD)](https://martinfowler.com/bliki/DomainDrivenDesign.html)
building blocks for .NET applications. This library includes base classes for entities, aggregates, value objects,
domain events, and standardized error handling patterns.

## Quick Start

### Installation

Add a project reference:

```xml

<ProjectReference Include="..\Platform.SharedKernel\Platform.SharedKernel.csproj"/>
```

## Features

- 🏗️ **DDD Building Blocks** - Base classes
  for [Entity](https://martinfowler.com/bliki/EvansClassification.html), [AggregateRoot](https://martinfowler.com/bliki/DDD_Aggregate.html),
  and [ValueObject](https://martinfowler.com/bliki/ValueObject.html)
- 📢 **Domain Events** - Infrastructure for raising and
  handling [domain events](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)
- ⚠️ **Standardized Errors** - Type-safe domain errors with [FluentResults](https://github.com/altmann/FluentResults)
  integration
- 🚨 **Critical Exceptions** - Exception hierarchy for system-level failures

### Entities and Aggregates

Minimal example:

```csharp
public sealed record SubscriberCreatedDomainEvent(Guid SubscriberId, Guid UserId) : DomainEvent;

public sealed class AlertSubscriber : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }
    public SubscriptionTier Tier { get; private set; }

    private AlertSubscriber() { }  // for EF Core hydration - no events fired

    public static AlertSubscriber CreateFree(Guid userId)
    {
        var subscriber = new AlertSubscriber
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Tier = SubscriptionTier.Free
        };
        subscriber.AddDomainEvent(new SubscriberCreatedDomainEvent(subscriber.Id, userId));
        return subscriber;
    }

    public Result Subscribe(Location location)
    {
        if (_subscriptions.Count >= Tier.MaxSubscriptions)
            return Result.Fail(AlertErrors.MaxSubscriptionsReached(Tier.MaxSubscriptions)); // not a bug, but business rule

        _subscriptions.Add(LocationSubscription.Create(location));
        return Result.Ok();
    }

    public void ActivatePaidSubscription(SubscriptionTier newSubscriptionTier, int durationDays)
    {
        if (newSubscriptionTier == SubscriptionTier.Free)
            throw new DataIntegrityException("Subscriber.InvalidTier",
                "Cannot activate paid subscription with Free tier."); // indicates a bug in system

        Tier = newSubscriptionTier;
        //...
    }
}
```

**Why factory methods?** EF Core hydrates entities via parameterless constructors. If domain events were raised in
constructors, they would fire during hydration - causing duplicate notifications.

**Result vs Exception:**

- **`Result`** - Expected business failures the caller should handle (validation, business rules)
- **`Exception`** - Bugs or impossible states indicating a programming error

### Value Objects

```csharp
using Platform.SharedKernel.Base;

public record Money(decimal Amount, string Currency) : ValueObject
{
    public static Money USD(decimal amount) => new(amount, "USD");
    public static Money EUR(decimal amount) => new(amount, "EUR");
}
```

### Domain Errors

```csharp
public static class AlertErrors
{
    public static ValidationError MaxSubscriptionsReached(int max)
        => new ValidationError(
            propertyName: "Subscriptions",
            errorMessage: $"User cannot have more than {max} active subscriptions.",
            errorCode: "Alert.MaxSubscriptionsReached");

    public static NotFoundError SubscriberNotFound(Guid userId)
        => new NotFoundError(nameof(AlertSubscriber), userId, "Subscriber.NotFound");
}
```

## Configuration

### Register Domain Event Services

```csharp
// Auto-registration of event handlers, e.g. public class SubscriberCreatedHandler : IDomainEventHandler<SubscriberCreatedEvent>
services.AddDomainEventHandlersFromAssembly(typeof(Program).Assembly);
services.AddDomainEventDispatcher();
```

### Implement Domain Event Handlers

Event handlers handle Domain Events raised from Aggregates/DomainEventDispatcher:

```csharp
public class SubscriberCreatedHandler : IDomainEventHandler<SubscriberCreatedEvent>
{
    private readonly ILogger<SubscriberCreatedHandler> _logger;

    public SubscriberCreatedHandler(ILogger<SubscriberCreatedHandler> logger)
    {
        _logger = logger;
    }

    public async Task Handle(SubscriberCreatedEvent domainEvent, CancellationToken ct)
    {
        // Send email...
        _logger.LogInformation("Welcome email sent for new subscriber {SubscriberId}", domainEvent.SubscriberId);
    }
}
```

## API Reference

### Base Classes

| Class                | Description                                                                          |
|----------------------|--------------------------------------------------------------------------------------|
| `Entity<TId>`        | Base class for entities with identity-based equality and `Timestamp` for concurrency |
| `AggregateRoot<TId>` | Entity with domain event support (`AddDomainEvent`, `PopDomainEvents`)             |
| `ValueObject`        | Abstract base record for immutable value objects                                     |
| `IAggregateRoot`     | Interface for aggregate roots (domain event methods)                                 |
| `IAuditableEntity`   | Interface for entities with `CreatedUtc` and `LastModifiedUtc` timestamps            |

### Domain Events

| Type                     | Description                                                                                      |
|--------------------------|--------------------------------------------------------------------------------------------------|
| `DomainEvent`            | Abstract base record with `OccurredOnUtc` timestamp (defaults to `DateTimeOffset.UtcNow`)        |
| `IDomainEventHandler<T>` | Handler interface with `Task Handle(T domainEvent, CancellationToken ct)`                        |
| `IDomainEventDispatcher` | Dispatcher interface with `Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct)` |

### Error Types

All errors extend [FluentResults `Error`](https://github.com/altmann/FluentResults) class:

| Error Type        | Constructor                          | Use Case                          |
|-------------------|--------------------------------------|-----------------------------------|
| `DomainError`     | `(message, errorCode)`               | Abstract base for domain errors   |
| `NotFoundError`   | `(entityName, id, errorCode)`        | Entity not found                  |
| `ValidationError` | `(propertyName, message, errorCode)` | Input validation failures         |
| `ConflictError`   | `(entityName, message, errorCode)`   | Concurrent modification conflicts |
| `ForbiddenError`  | `(entityName, id, errorCode)`        | Authorization failures            |

### FluentResults Extensions

| Method                              | Description                                                     |
|-------------------------------------|-----------------------------------------------------------------|
| `errors.ToErrorsSummary(separator)` | Joins errors as `"ErrorCode:Message"` strings                   |
| `errors.ToErrorDetails()`           | Returns `IList<(string ErrorCode, string ErrorMessage)>` tuples |

### Exceptions

| Exception                | Description                                                        |
|--------------------------|--------------------------------------------------------------------|
| `CriticalException`      | Abstract base for critical system errors with `ErrorCode` property |
| `DataIntegrityException` | Data consistency violations indicating bugs                        |

### Dependency Injection

| Method                                         | Description                                                  |
|------------------------------------------------|--------------------------------------------------------------|
| `AddDomainEventHandlersFromAssembly(assembly)` | Register all `IDomainEventHandler<T>` from assembly          |
| `AddDomainEventDispatcher()`                   | Register `DomainEventDispatcher` as `IDomainEventDispatcher` |

## Usage Patterns

### Result Pattern with Domain Errors

```csharp
public async Task<Result> SubscribeToLocationAsync(Guid userId, Location location, CancellationToken ct)
{
    var subscriber = await _repository.FindByUserIdAsync(userId, ct);
    if (subscriber is null)
        return Result.Fail(AlertErrors.SubscriberNotFound(userId));

    var result = subscriber.Subscribe(location);  // Returns Result - caller handles business failures
    if (result.IsFailed)
        return result;

    await _repository.SaveChangesAsync(ct);
    return Result.Ok();
}
```

### Dispatching Domain Events

**Default approach: Aggregates raise events**

The preferred pattern is for aggregates to raise domain events during business operations. Events are collected and
dispatched after the aggregate is persisted (typically in DbContext's `SaveChanges`):

```csharp
// In your DbContext or repository
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    // Collect events from all tracked aggregates before saving
    var aggregatesWithEvents = ChangeTracker.Entries<IAggregateRoot>()
        .Select(e => e.Entity)
        .Where(e => e.PopDomainEvents().Any())
        .ToList();

    var result = await base.SaveChangesAsync(ct);

    // Dispatch events after successful save
    foreach (var aggregate in aggregatesWithEvents)
    {
        foreach (var domainEvent in aggregate.PopDomainEvents())
        {
            await _dispatcher.DispatchAsync(domainEvent, ct);
        }
    }

    return result;
}
```

**When to use `IDomainEventDispatcher` directly**

Use `IDomainEventDispatcher`
in [domain services](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/net-core-microservice-domain-model#domain-services)
when an operation spans multiple aggregates and doesn't belong to any single aggregate:

```csharp
public record FundsTransferredDomainEvent(
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount) : DomainEvent;

/// <summary>
/// Domain service for operations spanning multiple Account aggregates.
/// The transfer operation doesn't belong to either Account - it's a cross-aggregate concern.
/// </summary>
public class FundsTransferService
{
    private readonly IAccountRepository _accounts;
    private readonly IDomainEventDispatcher _dispatcher;

    public FundsTransferService(IAccountRepository accounts, IDomainEventDispatcher dispatcher)
    {
        _accounts = accounts;
        _dispatcher = dispatcher;
    }

    public async Task<Result> TransferAsync(
        Guid fromAccountId,
        Guid toAccountId,
        decimal amount,
        CancellationToken ct)
    {
        var fromAccount = await _accounts.GetByIdAsync(fromAccountId, ct);
        var toAccount = await _accounts.GetByIdAsync(toAccountId, ct);

        // Each aggregate validates and performs its part
        var withdrawResult = fromAccount.Withdraw(amount);
        if (withdrawResult.IsFailed)
            return withdrawResult;

        toAccount.Deposit(amount);
        
        // Domain service raises the cross-aggregate event
        await _dispatcher.DispatchAsync(
            new FundsTransferredDomainEvent(fromAccountId, toAccountId, amount), ct);

        await _accounts.SaveChangesAsync(ct);


        return Result.Ok();
    }
}
```

**Guidance summary:**

- ✅ **Aggregates**: Raise events for single-aggregate operations (Subscriber created, subscription activated)
- ✅ **Domain services**: Raise events for cross-aggregate operations (Funds transferred between accounts)
- ❌ **Application services**: Should not raise domain events directly - delegate to aggregates or domain services

## Dependencies

- [FluentResults](https://github.com/altmann/FluentResults) - Result pattern implementation
- [Scrutor](https://github.com/khellang/Scrutor) - Assembly scanning for DI registration

## License

This project is licensed under the MIT License.
