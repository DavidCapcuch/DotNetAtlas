<div align="center">

# 🧱 SharedKernel

</div>

| ⚡ TL;DR |
| -------- |
| SharedKernel provides DDD building blocks: `AggregateRoot<TId>`, `Entity<TId>`, `ValueObject`, and `IDomainEvent`. These base classes handle identity, equality, and domain event collection so your domain code focuses on business logic. |

The SharedKernel library contains foundational types used across all domain models. It's intentionally minimal - just the building blocks you need for DDD tactical patterns.

## 📦 What's Included

```
platform/DotNetAtlas.SharedKernel/
├── AggregateRoot.cs      # Base for aggregate roots
├── Entity.cs             # Base for entities
├── ValueObject.cs        # Base for value objects
├── IAggregateRoot.cs     # Interface for aggregate detection
├── IEntity.cs            # Interface for entity detection
├── IDomainEvent.cs       # Marker for domain events
└── IAuditableEntity.cs   # Audit timestamp interface
```

## 🏗️ AggregateRoot

The `AggregateRoot<TId>` class is the base for all aggregate roots. It extends `Entity<TId>` and adds domain event management:

```csharp
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];
    
    /// <summary>
    /// Raises a domain event to be dispatched when the aggregate is persisted.
    /// </summary>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }
    
    /// <summary>
    /// Pops all domain events from the aggregate. Called by infrastructure
    /// during SaveChanges to collect events for the outbox.
    /// </summary>
    public IReadOnlyCollection<IDomainEvent> PopDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}
```

### Usage

```csharp
public sealed class Feedback : AggregateRoot<Guid>
{
    public FeedbackText FeedbackText { get; private set; }
    public FeedbackRating Rating { get; private set; }
    
    public static Result<Feedback> Create(FeedbackText text, FeedbackRating rating, Guid userId)
    {
        var feedback = new Feedback
        {
            Id = Guid.CreateVersion7(),
            FeedbackText = text,
            Rating = rating
        };
        
        // Domain event will be collected during SaveChanges
        feedback.AddDomainEvent(new FeedbackCreatedDomainEvent
        {
            FeedbackId = feedback.Id,
            Text = text.Text,
            Rating = rating.Value
        });
        
        return feedback;
    }
}
```

## 🆔 Entity

The `Entity<TId>` class provides identity-based equality:

```csharp
public abstract class Entity<TId> : IEntity<TId>
{
    public TId Id { get; protected set; } = default!;
    
    public override bool Equals(object? obj)
    {
        if (obj is not Entity<TId> other)
            return false;
        
        if (ReferenceEquals(this, other))
            return true;
        
        if (GetType() != other.GetType())
            return false;
        
        if (Id is null || other.Id is null)
            return false;
        
        return Id.Equals(other.Id);
    }
    
    public override int GetHashCode()
    {
        return Id?.GetHashCode() ?? 0;
    }
    
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }
    
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right)
    {
        return !(left == right);
    }
}
```

### Usage

```csharp
public sealed class AlertSubscription : Entity<Guid>
{
    public AlertType AlertType { get; private set; }
    public bool IsActive { get; private set; }
    
    internal AlertSubscription(Guid id, AlertType alertType)
    {
        Id = id;
        AlertType = alertType;
        IsActive = true;
    }
}
```

## 💎 ValueObject

The `ValueObject` class provides value-based equality:

```csharp
public abstract class ValueObject
{
    /// <summary>
    /// Override to provide the components used for equality comparison.
    /// </summary>
    protected abstract IEnumerable<object> GetEqualityComponents();
    
    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
            return false;
        
        var other = (ValueObject)obj;
        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }
    
    public override int GetHashCode()
    {
        return GetEqualityComponents()
            .Select(x => x?.GetHashCode() ?? 0)
            .Aggregate((x, y) => x ^ y);
    }
    
    public static bool operator ==(ValueObject? left, ValueObject? right)
    {
        if (left is null && right is null)
            return true;
        if (left is null || right is null)
            return false;
        return left.Equals(right);
    }
    
    public static bool operator !=(ValueObject? left, ValueObject? right)
    {
        return !(left == right);
    }
}
```

### Usage

```csharp
public sealed class FeedbackRating : ValueObject
{
    public int Value { get; private set; }
    
    private FeedbackRating() { }
    
    public static Result<FeedbackRating> Create(int value)
    {
        if (value < 1 || value > 5)
            return Result.Fail(FeedbackErrors.InvalidRating(value));
        
        return new FeedbackRating { Value = value };
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

## 📢 IDomainEvent

A marker interface for domain events:

```csharp
public interface IDomainEvent
{
    DateTimeOffset OccurredOnUtc { get; }
}
```

### Usage

```csharp
public sealed class FeedbackCreatedDomainEvent : IDomainEvent
{
    public Guid FeedbackId { get; init; }
    public string Text { get; init; } = string.Empty;
    public int Rating { get; init; }
    public Guid UserId { get; init; }
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

## 📝 IAuditableEntity

Interface for entities that track creation and modification times:

```csharp
public interface IAuditableEntity
{
    DateTimeOffset CreatedUtc { get; set; }
    DateTimeOffset LastModifiedUtc { get; set; }
}
```

Used by `UpdateAuditableEntitiesInterceptor` to automatically set timestamps.

## 🎯 Design Decisions

### Why `protected set` on Entity.Id?

Allows derived classes to set the ID during construction while preventing external modification.

### Why `PopDomainEvents()` instead of `GetDomainEvents()`?

The "pop" semantic ensures events are only processed once. After calling `PopDomainEvents()`, the collection is cleared.

### Why no `IRepository<T>` interface?

DotNetAtlas uses EF Core's `DbSet<T>` directly. Generic repository patterns often add abstraction without value. If you need repositories, add them to your domain.

## 📖 Further Reading

- [**Domain-Driven Design**](../architecture/DomainDrivenDesign.md) - Using these building blocks
- [**Outbox Pattern**](OutboxPattern.md) - How domain events become outbox messages

