# Design Decision: ITransactionalOutbox Interface Design

## Context

The `ITransactionalOutbox<TContext>` interface was created to replace the previous extension method approach that used `DbContext.GetInfrastructure()` to resolve services from EF Core's internal service provider - a practice that is considered an anti-pattern because it exposes internal implementation details and acts as a hidden service locator.

## Decision

The `ITransactionalOutbox<TContext>` interface exposes not only the core `AddOutboxMessage` method, but also:

- `SaveChangesAsync(CancellationToken)` - Delegates to the underlying DbContext
- `Database` - Exposes the `DatabaseFacade` for transaction management

## Rationale

### Why This Is Technically "Not Clean"

From a strict Interface Segregation Principle (ISP) perspective, a "transactional outbox" could be argued to only need `AddOutboxMessage`. Exposing `SaveChangesAsync` and `Database` makes the interface aware of broader DbContext concerns, blurring the single responsibility.

A purist design would have separate interfaces:
- `ITransactionalOutbox<T>` - Only `AddOutboxMessage`
- `IUnitOfWork` or similar - For `SaveChangesAsync` and transaction management

### Why We Made This Pragmatic Choice

1. **The Outbox Pattern Is Inherently Transaction-Driven**
   
   The entire point of the outbox pattern is to achieve atomicity between business operations and message publishing. The outbox message MUST be saved in the same transaction as the business data. Therefore, transaction management (`Database`) and persistence (`SaveChangesAsync`) are intrinsically linked to outbox operations.

2. **Common Usage Pattern**
   
   In practice, handlers that use the outbox follow a consistent pattern:
   ```csharp
   await _outbox.Database.EnsureTransactionAsync(async () =>
   {
       // Business logic...
       _outbox.AddOutboxMessage(key, event);
       await _outbox.SaveChangesAsync(ct);
   }, ct);
   ```
   
   Requiring multiple injections for this common pattern adds friction without meaningful benefit.

3. **Reduced Dependency Injection Noise**
   
   Without these conveniences, handlers would need to inject:
   - `ITransactionalOutbox<TContext>` for outbox operations
   - `IWeatherDbContext` (or similar) for `Database` and `SaveChangesAsync`
   
   This is redundant since both resolve to the same underlying DbContext instance.

4. **Cohesive API for Transactional Outbox Operations**
   
   The interface provides a cohesive API for the complete transactional outbox workflow:
   - Start/ensure transaction (`Database.EnsureTransactionAsync`)
   - Add outbox message (`AddOutboxMessage`)
   - Commit changes (`SaveChangesAsync`)

5. **Optional Usage**
   
   Handlers that don't need transactions or prefer explicit DbContext injection can still:
   - Use only `AddOutboxMessage` and ignore the other members
   - Inject both `IWeatherDbContext` and `ITransactionalOutbox<T>` if preferred

## Alternatives Considered

### 1. Pure Interface (Only AddOutboxMessage)

```csharp
public interface ITransactionalOutbox<TContext>
{
    void AddOutboxMessage(string? kafkaKey, ISpecificRecord event);
}
```

**Rejected because:** Forces handlers to inject multiple dependencies for the common transactional pattern.

### 2. Separate IUnitOfWork Interface

```csharp
public interface ITransactionalOutbox<TContext>
{
    void AddOutboxMessage(string? kafkaKey, ISpecificRecord event);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
    DatabaseFacade Database { get; }
}
```

**Rejected because:** Adds complexity without meaningful benefit. The outbox pattern is already coupled to the DbContext by design.

### 3. Extension Methods on ITransactionalOutbox

```csharp
public static class TransactionalOutboxExtensions
{
    public static DatabaseFacade GetDatabase<T>(this ITransactionalOutbox<T> outbox) where T : IOutboxDbContext
        => ((TransactionalOutbox<T>)outbox)._dbContext.Database;
}
```

**Rejected because:** Requires casting and breaks abstraction. Also, the interface should expose what's commonly needed.

## Consequences

### Positive

- Single injection point for transactional outbox operations
- Clean, cohesive API for the common pattern
- No hidden service locators or anti-patterns
- Explicit dependencies via constructor injection

### Negative

- Interface has broader scope than pure "transactional outbox" responsibility
- Might encourage misuse (using `Database` for non-outbox operations)

### Mitigations

- Clear documentation explaining the design rationale
- XML doc comments on interface members explaining intended usage
- Code examples showing the proper transactional pattern

## References

- [Transactional Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html)
- [Interface Segregation Principle](https://en.wikipedia.org/wiki/Interface_segregation_principle)
- [Pragmatic vs Dogmatic Software Design](https://www.thoughtworks.com/insights/blog/pragmatic-vs-dogmatic-software-design)
