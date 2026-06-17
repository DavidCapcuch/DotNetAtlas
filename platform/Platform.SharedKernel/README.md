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
public sealed record OrderCreatedDomainEvent(Guid OrderId, Guid BuyerId) : DomainEvent;

public sealed class Order : AggregateRoot<Guid>
{
    public Guid BuyerId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Created;

    private Order() { }  // for EF Core hydration - no events fired

    // Simplified for illustration: the real factory also takes the basket snapshot, addresses,
    // and payment method, and every factory/mutator threads an injected `DateTimeOffset utcNow`
    // (TimeProvider) - elided here. The order id is client-assigned at checkout (UUID v7).
    public static Order CreateFromBasket(Guid orderId, Guid buyerId)
    {
        var order = new Order
        {
            Id = orderId,
            BuyerId = buyerId,
            Status = OrderStatus.Created
        };
        order.AddDomainEvent(new OrderCreatedDomainEvent(order.Id, buyerId));
        return order;
    }

    public Result Cancel(string reason)
    {
        if (!Status.CanTransitionTo(OrderStatus.Cancelled))
            return Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name)); // not a bug, but business rule

        Status = OrderStatus.Cancelled;
        //...
        return Result.Ok();
    }

    public Result MarkShipped(string carrier, string trackingNumber)
    {
        if (!Status.CanTransitionTo(OrderStatus.Shipped))
            throw new DataIntegrityException("Order.InvalidStatusTransition",
                $"Cannot ship an order in status '{Status.Name}'."); // indicates a bug in system

        Status = OrderStatus.Shipped;
        //...
        return Result.Ok();
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
using FluentResults;
using Platform.SharedKernel.Base;
using Platform.SharedKernel.Errors;

// Immutable, equality-by-value, self-validating. The only construction path is the
// Create factory, which returns a Result so an invalid currency is a typed error, not a throw.
public sealed record Money : ValueObject
{
    public decimal Amount { get; private init; }
    public CurrencyCode Currency { get; private init; } = null!;  // ISO 4217 SmartEnum

    private Money() { }  // sole construction path is via Create

    public static Result<Money> Create(decimal amount, string currencyCode)
    {
        if (!CurrencyCode.TryFromName(currencyCode?.ToUpperInvariant(), out var currency))
            return Result.Fail<Money>(new ValidationError(
                nameof(Currency), $"Unknown ISO 4217 currency code '{currencyCode}'.", "Money.UnknownCurrencyCode"));

        return Result.Ok(new Money { Amount = amount, Currency = currency });
    }
}
```

### Domain Errors

```csharp
public static class OrderingErrors
{
    public static ConflictError CannotCancelInStatus(string status)
        => new ConflictError(
            entityName: "Order",
            message: $"Order in status '{status}' cannot be cancelled.",
            errorCode: "Order.CannotCancelInStatus");

    public static NotFoundError OrderNotFound(Guid orderId)
        => new NotFoundError(nameof(Order), orderId, "Order.NotFound");
}
```

## Configuration

### Register Domain Event Services

```csharp
// Auto-registration of event handlers, e.g. public class OrderConfirmedHandler : IDomainEventHandler<OrderConfirmedDomainEvent>
services.AddDomainEventHandlersFromAssembly(typeof(Program).Assembly);
services.AddDomainEventDispatcher();
```

### Implement Domain Event Handlers

Event handlers handle Domain Events raised from Aggregates/DomainEventDispatcher:

```csharp
public class OrderConfirmedHandler : IDomainEventHandler<OrderConfirmedDomainEvent>
{
    private readonly IEmailSender _emailSender;  // app-level dependency (illustrative)
    private readonly ILogger<OrderConfirmedHandler> _logger;

    public OrderConfirmedHandler(IEmailSender emailSender, ILogger<OrderConfirmedHandler> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task Handle(OrderConfirmedDomainEvent domainEvent, CancellationToken ct)
    {
        await _emailSender.SendOrderConfirmationAsync(domainEvent.OrderId, ct);
        _logger.LogInformation("Order confirmation email sent for Order {OrderId}", domainEvent.OrderId);
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
public async Task<Result> CancelOrderAsync(Guid orderId, string reason, CancellationToken ct)
{
    var order = await _repository.GetByIdAsync(orderId, ct);
    if (order is null)
        return Result.Fail(OrderingErrors.OrderNotFound(orderId));

    var result = order.Cancel(reason);  // Returns Result - caller handles business failures
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

- ✅ **Aggregates**: Raise events for single-aggregate operations (Order created, Order cancelled)
- ✅ **Domain services**: Raise events for cross-aggregate operations (Funds transferred between accounts)
- ❌ **Application services**: Should not raise domain events directly - delegate to aggregates or domain services

## Dependencies

- [FluentResults](https://github.com/altmann/FluentResults) - Result pattern implementation
- [Scrutor](https://github.com/khellang/Scrutor) - Assembly scanning for DI registration

## License

This project is licensed under the MIT License.
