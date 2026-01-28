<div align="center">

# 📦 Domain-Driven Design

</div>

| ⚡ TL;DR |
| -------- |
| DDD tactical patterns help model complex business logic. DotNetAtlas uses Aggregates (consistency boundaries), Entities (identity), Value Objects (immutable values), and Domain Events (state change notifications). The SharedKernel library provides base classes for all of these. |

Domain-Driven Design (DDD) is both a philosophy and a set of patterns for tackling complex business domains. DotNetAtlas focuses on the **tactical patterns** - the building blocks you use to model your domain.

## 🎯 Why DDD Patterns?

Without intentional modeling, business logic scatters across the codebase:

```csharp
// ❌ Anemic domain model - logic everywhere except the domain
public class FeedbackService
{
    public void UpdateFeedback(Feedback feedback, string newText, int newRating)
    {
        if (newRating < 1 || newRating > 5)
            throw new ArgumentException("Invalid rating");
        
        if (string.IsNullOrEmpty(newText))
            throw new ArgumentException("Text required");
        
        feedback.Text = newText;
        feedback.Rating = newRating;
        feedback.ModifiedAt = DateTime.UtcNow;
        
        _eventPublisher.Publish(new FeedbackChangedEvent(...));
    }
}
```

With DDD patterns, the domain protects itself:

```csharp
// ✅ Rich domain model - logic in the domain
public class Feedback : AggregateRoot<Guid>
{
    public Result ChangeFeedback(FeedbackText newText, FeedbackRating newRating)
    {
        FeedbackText = newText;
        Rating = newRating;
        
        AddDomainEvent(new FeedbackChangedDomainEvent(...));
        return Result.Ok();
    }
}
```

## 🧱 Building Blocks

### Aggregates

An **Aggregate** is a cluster of domain objects treated as a single unit for data changes. The **Aggregate Root** is the entry point - all modifications go through it.

```csharp
public sealed class Feedback : AggregateRoot<Guid>
{
    // Private setters - state changes only through methods
    public FeedbackText FeedbackText { get; private set; }
    public FeedbackRating Rating { get; private set; }
    public Guid CreatedByUser { get; private set; }
    
    // Factory method - ensures valid creation
    public static Result<Feedback> Create(
        FeedbackText text, 
        FeedbackRating rating, 
        Guid userId)
    {
        var feedback = new Feedback
        {
            Id = Guid.CreateVersion7(),
            FeedbackText = text,
            Rating = rating,
            CreatedByUser = userId
        };
        
        feedback.AddDomainEvent(new FeedbackCreatedDomainEvent
        {
            FeedbackId = feedback.Id,
            Text = text.Text,
            Rating = rating.Value,
            UserId = userId
        });
        
        return feedback;
    }
    
    // Behavior method - encapsulates business logic
    public Result ChangeFeedback(FeedbackText newText, FeedbackRating newRating)
    {
        FeedbackText = newText;
        Rating = newRating;
        
        AddDomainEvent(new FeedbackChangedDomainEvent
        {
            FeedbackId = Id,
            NewText = newText.Text,
            NewRating = newRating.Value
        });
        
        return Result.Ok();
    }
}
```

**Key principles:**
- Private setters prevent external state modification
- Factory methods ensure valid object creation
- Behavior methods encapsulate business rules
- Domain events are raised from within the aggregate

### Entities

**Entities** have identity that persists over time. Two entities with the same data but different IDs are different entities.

```csharp
public sealed class AlertSubscription : Entity<Guid>
{
    public AlertType AlertType { get; private set; }
    public bool IsActive { get; private set; }
    
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
```

Entities are typically owned by an Aggregate Root:

```csharp
public sealed class AlertSubscriber : AggregateRoot<Guid>
{
    private readonly List<AlertSubscription> _subscriptions = [];
    public IReadOnlyCollection<AlertSubscription> Subscriptions => _subscriptions.AsReadOnly();
    
    public Result AddSubscription(AlertType alertType)
    {
        if (_subscriptions.Any(s => s.AlertType == alertType))
            return Result.Fail(AlertErrors.AlreadySubscribed(alertType));
        
        _subscriptions.Add(new AlertSubscription(alertType));
        return Result.Ok();
    }
}
```

### Value Objects

**Value Objects** are immutable and compared by value, not identity. Two value objects with the same data are equal.

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
    
    // ValueObject base class handles equality
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
```

**Benefits of Value Objects:**
- Validation at creation - invalid values can't exist
- Self-documenting - `FeedbackRating` is clearer than `int`
- Encapsulated behavior - can add methods like `IsExcellent()`

```csharp
public sealed class FeedbackText : ValueObject
{
    public string Text { get; private set; }
    
    public static Result<FeedbackText> Create(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Result.Fail(FeedbackErrors.TextRequired);
        
        if (text.Length > 1000)
            return Result.Fail(FeedbackErrors.TextTooLong(text.Length));
        
        return new FeedbackText { Text = text.Trim() };
    }
    
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Text;
    }
}
```

### Domain Events

**Domain Events** capture something that happened in the domain. They're raised by aggregates and handled by infrastructure.

```csharp
public sealed class FeedbackCreatedDomainEvent : IDomainEvent
{
    public Guid FeedbackId { get; init; }
    public string Text { get; init; }
    public int Rating { get; init; }
    public Guid UserId { get; init; }
    public DateTimeOffset OccurredOnUtc { get; init; }
}
```

Events are raised inside the aggregate:

```csharp
// Inside Feedback.Create()
feedback.AddDomainEvent(new FeedbackCreatedDomainEvent { ... });
```

And collected by infrastructure during `SaveChanges`:

```csharp
// In DispatchDomainEventsInterceptor
var events = aggregates.SelectMany(a => a.PopDomainEvents());
// Convert to outbox messages...
```

Read more: [**Outbox Pattern**](../platform/OutboxPattern.md)

## 🏗️ SharedKernel Base Classes

The `DotNetAtlas.SharedKernel` library provides base classes:

```csharp
// AggregateRoot - has ID and domain events
public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];
    
    protected void AddDomainEvent(IDomainEvent domainEvent) 
        => _domainEvents.Add(domainEvent);
    
    public IReadOnlyCollection<IDomainEvent> PopDomainEvents()
    {
        var events = _domainEvents.ToList();
        _domainEvents.Clear();
        return events;
    }
}

// Entity - has ID and equality by ID
public abstract class Entity<TId> : IEntity<TId>
{
    public TId Id { get; protected set; }
    
    public override bool Equals(object? obj) 
        => obj is Entity<TId> other && Id.Equals(other.Id);
}

// ValueObject - equality by components
public abstract class ValueObject
{
    protected abstract IEnumerable<object> GetEqualityComponents();
    
    public override bool Equals(object? obj)
        => obj is ValueObject other && 
           GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
}
```

Read more: [**SharedKernel**](../platform/SharedKernel.md)

## 📖 Further Reading

- [**Clean Architecture**](CleanArchitecture.md) - How DDD fits in the architecture
- [**Event-Driven Architecture**](EventDriven.md) - What happens to domain events
- [**SharedKernel**](../platform/SharedKernel.md) - Base class implementations

