# Architecture Tests — Rule Catalog

> Rules enforced by **NetArchTest** (existing pattern — see `test/Weather.ArchitectureTests/`) in each new service's `test/{Bc}.ArchitectureTests` project. Implementation agents author one test class per rule group. Every rule here maps to a concrete `Types.InAssembly(...).Should()....GetResult()` assertion; C# snippets below are illustrative pseudocode only.
>
> Enforcement: `dotnet test test/{Bc}.ArchitectureTests/` runs in CI as part of the standard test step; failures block merge (see [master design § 11.4](../eshop-master-design.md) and [§ 11.7](../eshop-master-design.md) — `dotnet build -m` / `dotnet restore --locked-mode` gate). Architecture-test rules are NOT bypassable — if a rule fires on a legitimate new pattern, the rule itself must be updated in a PR alongside the code change.

---

## 1. Common Rules (all four new BCs)

### 1.1 Layer Dependency Rules

The four-layer topology (`{Bc}.Domain` ← `{Bc}.Application` ← `{Bc}.Infrastructure` ← `{Bc}.Api`) is stated in [master design § Appendix B.2](../eshop-master-design.md). The architecture test enforces no upward or sideways leaks.

```csharp
// Pseudocode — implementation agent writes the real NetArchTest assertions

Types.InAssembly(DomainAssembly)
    .Should()
    .NotHaveDependencyOnAny(
        "Ordering.Application",
        "Ordering.Infrastructure",
        "Ordering.Api",
        "Microsoft.EntityFrameworkCore",
        "KafkaFlow",
        "FastEndpoints",
        "StackExchange.Redis")
    .GetResult();

Types.InAssembly(ApplicationAssembly)
    .Should()
    .NotHaveDependencyOnAny(
        "Ordering.Infrastructure",
        "Ordering.Api",
        "Microsoft.EntityFrameworkCore",   // EF Core lives in Infrastructure
        "KafkaFlow",                        // KafkaFlow lives in Infrastructure
        "StackExchange.Redis",              // Redis client lives in Infrastructure
        "FastEndpoints")                    // FastEndpoints lives in Api
    .GetResult();

Types.InAssembly(InfrastructureAssembly)
    .Should()
    .NotHaveDependencyOn("Ordering.Api")
    .GetResult();
```

Allowed references summary (copied from [master design § Appendix B.2](../eshop-master-design.md) for test-author convenience):

| Layer | May reference | Must NOT reference |
|-------|---------------|--------------------|
| `{Bc}.Domain` | `Platform.SharedKernel` only | Any `.Application` / `.Infrastructure` / `.Api`; EF Core; KafkaFlow; FastEndpoints; Redis |
| `{Bc}.Application` | `{Bc}.Domain`, `Platform.CQRS`, `Platform.CQS`, `Platform.ReliableMessaging.Outbox.*`, `Platform.SchemaRegistry.Contracts`, `FluentValidation`, `FluentResults` | Infrastructure / Api; EF Core; KafkaFlow; Redis |
| `{Bc}.Infrastructure` | `{Bc}.Application`, all `Platform.*`, `Microsoft.EntityFrameworkCore*`, `KafkaFlow.*`, `Npgsql`, `StackExchange.Redis` (Basket only) | `{Bc}.Api` |
| `{Bc}.Api` | `{Bc}.Application`, `{Bc}.Infrastructure`, `Platform.ServiceDefaults`, `FastEndpoints` | — |

### 1.2 Aggregate Discipline Rules

For every aggregate root in the BC:

- Inherits `AggregateRoot<TId>` from `Platform.SharedKernel`.
- Has a **private parameterless constructor** (EF Core / serializer materialization) — enforced via reflection.
- Has **at least one public static factory method** whose name starts with `Create` or `From` and returns `TAggregate` or `Result<TAggregate>`.
- Has **no public setters** on domain state — properties are either `init;` or `private set;`.
- Domain-event collection is encapsulated: only `protected/private` mutators like `RaiseDomainEvent(...)`.

```csharp
// Example — Ordering.Domain.Orders.Order must satisfy all four rules
Types.InAssembly(DomainAssembly)
    .That().Inherit(typeof(AggregateRoot<>))
    .Should()
    .MeetCustomRule(new HasPrivateParameterlessCtor())
    .And().MeetCustomRule(new HasPublicStaticFactoryMethod())
    .And().MeetCustomRule(new HasNoPublicSetters())
    .GetResult();
```

*Custom rule classes* are tiny `ICustomRule` implementations that scan `TypeInfo.GetConstructors(BindingFlags.NonPublic)` etc. NetArchTest's built-in selectors cover about 70% of these — the rest require custom rules.

Enumerate aggregates per BC:
- **Catalog:** `Product`, `Category`
- **Basket:** `Basket`
- **Ordering:** `Order`
- **Inventory:** `StockItem`

### 1.3 Domain-Event Discipline

**Internal domain events:**

- Inherit `DomainEvent` (from `Platform.SharedKernel`).
- Declared as `public sealed record`.
- Name ends in `DomainEvent` (e.g., `ProductPriceChangedDomainEvent`).
- Live in `{Bc}.Domain.{Aggregate}.Events` namespace.

```csharp
Types.InAssembly(DomainAssembly)
    .That().Inherit(typeof(DomainEvent))
    .Should()
    .BeSealed()
    .And().HaveNameEndingWith("DomainEvent")
    .And().ResideInNamespaceMatching(@"^\w+\.Domain\.\w+\.Events$")
    .GetResult();
```

**External summary events (Avro-generated):**

- Implement `ISpecificRecord` (Confluent Avro-generated).
- Have a corresponding `.avsc` file under `platform/Platform.SchemaRegistry.Contracts/Avro/{Domain}/{Aggregate}/`.
- Name ends in `Event` (not `DomainEvent`) and does NOT inherit `DomainEvent`.
- Are NEVER added to an aggregate's `_domainEvents` collection — external events are produced exclusively by Application-layer `IDomainEventHandler` adapters that translate internal → external + add to the transactional outbox.

```csharp
// The *external* event check lives in Platform.SchemaRegistry.Contracts.ArchitectureTests
Types.InAssembly(SchemaRegistryAssembly)
    .That().ImplementInterface(typeof(ISpecificRecord))
    .Should()
    .NotInherit(typeof(DomainEvent))
    .And().HaveNameEndingWith("Event")
    .GetResult();

// And in the BC: no aggregate raises an external event
Types.InAssembly(DomainAssembly)
    .Should()
    .MeetCustomRule(new NoAggregateRaisesIsSpecificRecord())
    .GetResult();
```

**Universal `*DomainEventHandler` suffix (U-D):**

Every concrete class implementing `IDomainEventHandler<T>` ends with `DomainEventHandler`. The role name precedes the suffix:

- `*ProjectionDomainEventHandler` — read-model projection writes (Catalog, Inventory).
- `*OutboxPublisherDomainEventHandler` — external-event emissions via the transactional outbox (every BC).
- `*LifecycleDomainEventHandler` — hybrid handlers that combine projection + outbox emit in one class (Inventory's `ReservationLifecycleDomainEventHandler`).
- Future roles follow the same shape: name the role, then the contract.

Classes implementing a **different** contract keep a contract-matching suffix — for example, Catalog's `StockLevelChangedEventProjectionHandler` implements the custom Application port `IStockLevelChangedEventProjector` (Kafka-delivered, inbox-deduped projection write) and keeps the plain `*ProjectionHandler` suffix. KafkaFlow `IMessageHandler<T>` adapters use `*KafkaHandler`. The rule is *contract-named suffix when the role suffix would be ambiguous about contract*.

Enforced per-BC in each `{Bc}.ArchitectureTests/Application/DomainEventHandlerTests.cs` (or `BoundedContext/DomainEventHandlerTests.cs` for Inventory) with the same one-line rule:

```csharp
Types.InAssembly(ApplicationAssembly)
    .That().ImplementInterface(typeof(IDomainEventHandler<>))
    .Should().HaveNameEndingWith("DomainEventHandler")
    .GetResult();
```

### 1.4 Command / Query Discipline

- Every command handler:
  - Class name ends in `CommandHandler`.
  - Implements `ICommandHandler<TCommand>` or `ICommandHandler<TCommand, TResponse>` (from `Platform.CQRS`).
  - Lives under `{Bc}.Application.{UseCase}` (e.g., `Catalog.Application.Products.CreateProduct`).
  - Returns `Task<Result>` or `Task<Result<T>>` — never `Task<void>` nor a raw domain type.
- Every query handler:
  - Class name ends in `QueryHandler`.
  - Implements `IQueryHandler<TQuery, TResponse>`.
- Every command/query with body parameters has a paired `AbstractValidator<TRequest>` in the same namespace (FluentValidation). Parameterless queries may omit.

```csharp
Types.InAssembly(ApplicationAssembly)
    .That().ImplementInterface(typeof(ICommandHandler<>))
    .Or().ImplementInterface(typeof(ICommandHandler<,>))
    .Should().HaveNameEndingWith("CommandHandler")
    .GetResult();

Types.InAssembly(ApplicationAssembly)
    .That().ImplementInterface(typeof(IQueryHandler<,>))
    .Should().HaveNameEndingWith("QueryHandler")
    .GetResult();

Types.InAssembly(ApplicationAssembly)
    .That().HaveNameEndingWith("CommandHandler")
    .Or().HaveNameEndingWith("QueryHandler")
    .Should().MeetCustomRule(new ReturnsResultOrResultOfT())
    .GetResult();
```

### 1.5 Result-Pattern Enforcement

The [error-taxonomy.md](error-taxonomy.md) § 2 classification (user / business-expected / bug / infrastructure) is architecturally enforceable:

- **Handler path:** command/query handlers and aggregate methods whose state transitions CAN fail due to user action return `Result` / `Result<T>` — **no raw throws** of `ArgumentException`, `InvalidOperationException`, `ArgumentNullException`.
- **Bug path:** aggregate invariant violations throw `DataIntegrityException` (from `Platform.SharedKernel.Exceptions`) — **not** `InvalidOperationException`, not `Exception`, not a custom exception without inheriting `CriticalException`.
- **Consumer handler path:** saga-command consumer handlers ([use-cases.md § 3.3](use-cases.md), [§ 4.3](use-cases.md)) handling a `Result.Fail(userError)` **must not rethrow as `InvalidOperationException`** for user-actionable errors; instead they emit a business outcome event (per [kafka-dlt-strategy.md § 2](kafka-dlt-strategy.md) "Exceptions to the throw → DLT rule").

```csharp
// No raw ArgumentException / InvalidOperationException in handlers
Types.InAssembly(ApplicationAssembly)
    .That().HaveNameEndingWith("CommandHandler").Or().HaveNameEndingWith("QueryHandler")
    .Should()
    .MeetCustomRule(new DoesNotThrow(typeof(ArgumentException), typeof(InvalidOperationException)))
    .GetResult();

// Aggregates only throw DataIntegrityException, never raw
Types.InAssembly(DomainAssembly)
    .That().Inherit(typeof(AggregateRoot<>))
    .Should()
    .MeetCustomRule(new OnlyThrows(typeof(DataIntegrityException)))
    .GetResult();
```

### 1.6 Cross-BC Reference Rules

No direct type reference from one BC's `Domain` (or `Application`) to another BC's `Domain` (or `Application`). Cross-BC integration happens only via:

- Avro external events consumed through the inbox (`Platform.SchemaRegistry.Contracts` → BC's Infrastructure Kafka consumer → internal command via `ISender`).
- HTTP calls through an ACL adapter living in `{Bc}.Infrastructure.<AdapterArea>` (e.g., Basket's `ProductCatalogHttpAdapter` — see [basket.md § ACL](basket.md)).

```csharp
// Catalog.Domain must not reference Basket / Ordering / Inventory domains
Types.InAssembly(CatalogDomainAssembly)
    .Should()
    .NotHaveDependencyOnAny(
        "Basket.Domain", "Basket.Application",
        "Ordering.Domain", "Ordering.Application",
        "Inventory.Domain", "Inventory.Application")
    .GetResult();
// Mirror for Basket, Ordering, Inventory — 4 tests total
```

---

## 2. Per-BC Specific Rules

These complement § 1 with rules that encode chapter-specific invariants the reviewer would otherwise have to re-check manually.

### 2.1 Catalog

- **`Product.CategoryId` only** — `Product` aggregate references `Category` solely by `CategoryId` (a strongly-typed ID value object), never by `Category` type. Prevents accidental navigation-property-induced joins.
- **`ProductSearchViewRow` location and two projection-writer shapes** — the projection row type lives in `Catalog.Application.Common.ReadModels.ProductSearchViewRow`. Projection writes happen in classes co-located with their feature folder under `Catalog.Application.{Products,Categories}.<UseCase>.`, in **two shapes**:
  - **(a) In-process domain-event projections** — 7 per-event sealed `*ProjectionDomainEventHandler` classes, each implementing `IDomainEventHandler<T>` (e.g., `ProductCreatedProjectionDomainEventHandler`, `CategoryReparentedProjectionDomainEventHandler`). Run inside the command's UoW; row and aggregate commit in the same `SaveChangesAsync`.
  - **(b) Kafka-delivered, inbox-deduped projection** — one sealed `StockLevelChangedEventProjectionHandler` implementing the custom Application port `IStockLevelChangedEventProjector`. Driven by Inventory's `StockLevelChangedEvent` Avro event consumed by `StockLevelChangedEventKafkaHandler` in Infrastructure; inbox-dedup middleware (`Platform.KafkaFlow.Inbox.EFCore`) sits in front of the KafkaFlow pipeline for exactly-once delivery. Keeps the plain `*ProjectionHandler` suffix because it does NOT implement `IDomainEventHandler<T>` — its contract is the custom port, so the suffix matches the contract per § 1.3's U-D rule.

  Both shapes are sealed; both live under `Catalog.Application.{Aggregate}.{UseCase}`; both write through the same `CatalogDbContext`. This two-shape closure is the deliberate read-side design — a new shape (notification handler, etc.) would require widening Catalog's design, not just adding code.
- **No projection writes outside the handlers** — the `DbSet<ProductSearchViewRow>` is only assigned/updated from classes whose name ends with `ProjectionDomainEventHandler` or `ProjectionHandler`, plus `CategoryPathService` (the shared helper used by category-rename/reparent rebuilds — see [catalog.md](catalog.md)). Custom rule scans method bodies for writes.

```csharp
// Example pseudocode
Types.InAssembly(CatalogDomainAssembly)
    .That().Inherit(typeof(AggregateRoot<>)).And().HaveName("Product")
    .Should().MeetCustomRule(new OnlyReferencesIdNot(typeof(Category)))
    .GetResult();
```

### 2.2 Basket

- **Basket DbContext carries no `DbSet<Basket>`** — the Basket aggregate lives in Redis. The SQL-side `BasketDbContext` only holds `OutboxMessage` / `InboxMessage` sets (per [basket.md § Redis-backed aggregate + SQL side-car](basket.md)).
- **`ProductCatalogHttpAdapter` is the only cross-BC surface** — only `Basket.Infrastructure.Catalog.ProductCatalogHttpAdapter` references Catalog HTTP DTOs. All other Basket code — Domain and Application — references `ProductSnapshot` (internal VO).
- **`IBasketRepository` uses Redis** — the Infrastructure implementation of `IBasketRepository` references `StackExchange.Redis` types; no EF Core references are permitted in that class.

```csharp
Types.InAssembly(BasketInfraAssembly)
    .That().HaveName("BasketDbContext")
    .Should().MeetCustomRule(new DoesNotContainDbSetOf(typeof(Basket)))
    .GetResult();

Types.InAssembly(BasketInfraAssembly)
    .That().HaveNameMatching(@".*CatalogDto.*|.*CatalogResponse.*|.*GetProductResponse.*")
    .Should().OnlyBeReferencedBy("Basket.Infrastructure.Catalog.ProductCatalogHttpAdapter")
    .GetResult();
```

### 2.3 Ordering

- **`Order.Items` backing field is private** — exposed as `IReadOnlyCollection<OrderItem>`; the private backing field is `List<OrderItem>` with `private set;` or `init;`.
- **`OrderStatus` changes only via `CanTransitionTo`** — direct `Status = newStatus` assignment outside the FSM guard is forbidden. Every transition goes through a named method (`MarkStockReserved`, `MarkPaymentCompleted`, `Confirm`, `Ship`, `Deliver`, `Cancel`, `Fail`) that calls `Throw.If(!Status.CanTransitionTo(target))` internally (see [ordering.md § State transitions](ordering.md)).
- **`ShippingAddress` / `BillingAddress` are immutable VOs** — declared as `sealed record` with `init;`-only properties.
- **Saga-command consumers location** — `Ordering.Infrastructure.Messaging.Kafka.SagaCommands` is the only namespace that hosts classes implementing KafkaFlow `IMessageHandler<T>` for command topics; each dispatches via `ISender.Send(...)` per [use-cases.md § 3.3](use-cases.md).

```csharp
Types.InAssembly(OrderingDomainAssembly)
    .That().HaveName("Order")
    .Should().MeetCustomRule(new PropertyIsReadOnlyCollection("Items", typeof(OrderItem)))
    .GetResult();

Types.InAssembly(OrderingInfraAssembly)
    .That().ImplementInterface(typeof(IMessageHandler<>))
    .Should().ResideInNamespace("Ordering.Infrastructure.Messaging.Kafka.SagaCommands")
    .GetResult();
```

### 2.4 Inventory

- **`StockItem` is NOT directly persisted** — `StockItem` has no EF Core mapping; the repository rehydrates it from events. Any `DbSet<StockItem>` is forbidden.
- **`stock_events` is append-only** — `InventoryDbContext.StockEvents.Update(...)` / `.Remove(...)` must not be called from any repository method. Only `.Add(...)` is permitted.
- **Projection-handler location** — Inventory uses **multiplexed handlers** (one class implements `IDomainEventHandler<T>` for several stock-event types), so handlers live in `Inventory.Application.StockItems` — *not* in a separate `Inventory.Application.Projections` folder. There are two: `CurrentStockLevelsProjectionDomainEventHandler` (writes `current_stock_levels` + emits `StockLevelChangedEvent` via outbox) and `ReservationLifecycleDomainEventHandler` (writes `reservation_audit` + emits the three `inventory.reservations` events). Both follow § 1.3's universal `*DomainEventHandler` suffix; the role names (`Projection`, `Lifecycle`) reflect their primary role. They upsert into `current_stock_levels` / `reservation_audit` within the same DbContext transaction as the event append (`SaveChangesAsync` commits both). No projection runs on a separate DbContext or outside the event-handler chain.

```csharp
Types.InAssembly(InventoryInfraAssembly)
    .Should().MeetCustomRule(new NoDbSetOf(typeof(StockItem)))
    .GetResult();

Types.InAssembly(InventoryInfraAssembly)
    .That().AreClasses()
    .Should().MeetCustomRule(new DoesNotCall("StockEvents.Update", "StockEvents.Remove"))
    .GetResult();

Types.InAssembly(InventoryAppAssembly)
    .That().ImplementInterface(typeof(IDomainEventHandler<>))
    .Should().ResideInNamespaceMatching(@"^Inventory\.Application\.StockItems(\.\w+)?$")
    .GetResult();
```

### 2.5 Invoicing

The shipped Invoicing test project (`test/Invoicing.ArchitectureTests/`) enforces **30 facts** (the original 29 + the `BlobsNamespace_ShouldNotCall_StaticUtcNow` rule added by closeout commit `196501b`). The per-BC rules below complement § 1 with invariants that are specific to the Invoicing chapter — PDF determinism, blob containment, PII allowlisting, and the strict `TimeProvider` posture inherited from ADR-0015.

- **`Invoicing.Infrastructure.Pdf.*` is the only QuestPDF caller** — `PdfGenerationContainmentTests.PdfGenerator_ShouldOnlyBeIn_PdfNamespace` asserts no type outside the `Invoicing.Infrastructure.Pdf.*` regex references `QuestPDF.*`. Prevents PDF rendering from leaking into Domain / Application / API.
- **`Invoicing.Infrastructure.Blobs.*` is the only `Azure.Storage.Blobs` caller** — `BlobStorageContainmentTests.AzureStorage_ShouldOnlyBeIn_BlobsNamespace` asserts the SDK is contained. The blob path is also the only namespace allowed to mint SAS URLs.
- **PII allowlist** — `OtelTagAllowlistTests` asserts that the only span-attribute keys Invoicing emits are on the ADR-0011 allowlist (or `DataIntegrityException`-tagged `error.*` keys). Buyer email / name / address must never appear in a span tag.
- **`NoStaticUtcNowInDomain`** — `NoStaticUtcNowInDomainTests` asserts `Invoicing.Domain.**` does not call `DateTime[Offset].UtcNow`. Inherits the universal § 1 contract; restated here as a per-BC pin.
- **Blobs-namespace UtcNow ban** — `BlobStorageContainmentTests.BlobsNamespace_ShouldNotCall_StaticUtcNow` extends the no-static-UtcNow rule to `Invoicing.Infrastructure.Blobs.*` (added in closeout commit `196501b` to plug the AzureBlobStore SAS-expiry hole — see H4 in the Invoicing closeout).
- **Clean-Architecture layer rules** — 6 facts in `CleanArchitectureLayerTests` mirror the universal § 1.1 contract at the Invoicing-assembly level (Domain ⟂ Application/Infrastructure/API; Application ⟂ Infrastructure/API; Infrastructure ⟂ API).
- **Aggregate discipline** — 4 facts in `AggregateRootTests` cover the universal § 1.2 contract for `Invoice` and `CreditNote` (private parameterless ctor, public static factory, no public setters, encapsulated domain-event collection).
- **Domain-event discipline** — 3 facts in `DomainEventTests` cover the universal § 1.3 internal-event contract (sealed, naming suffix, namespace).
- **Command/Query discipline** — 4 facts in `CommandHandlerTests` / `QueryHandlerTests` enforce the § 1.4 naming + return-type contract per BC.
- **No cross-BC reference** — 2 facts in `NoCrossBcReferenceTests` forbid imports from `{Basket,Catalog,Inventory,Ordering,Payments}.{Domain,Application}` in `Invoicing.{Domain,Application}` (cross-BC integration only via Avro contracts under `Platform.SchemaRegistry.Contracts`).

```csharp
// Example — PDF containment (literal selector inside the BC test project)
Types.InAssembly(InvoicingInfraAssembly)
    .That().HaveDependencyOnAny("QuestPDF")
    .Should()
    .ResideInNamespaceMatching(@"^Invoicing\.Infrastructure\.Pdf(\..*)?$")
    .GetResult();

// Example — Blobs namespace no-static-UtcNow (commit 196501b)
Types.InAssembly(InvoicingInfraAssembly)
    .That().ResideInNamespaceMatching(@"^Invoicing\.Infrastructure\.Blobs(\..*)?$")
    .Should().MeetCustomRule(new DoesNotCallStaticUtcNowRule())
    .GetResult();
```

---

## 3. Test Project Scaffolding

Each BC's architecture-test project follows the shape already established by `test/Weather.ArchitectureTests/`. Recommended test-class pattern:

```csharp
// File: test/Ordering.ArchitectureTests/ArchitectureRules.cs
public sealed class ArchitectureRules
{
    private static readonly Assembly DomainAssembly = typeof(Ordering.Domain.IAssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Ordering.Application.IAssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Ordering.Infrastructure.IAssemblyMarker).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Ordering.Api.IAssemblyMarker).Assembly;

    [Fact]
    public void Domain_should_not_depend_on_infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should().NotHaveDependencyOn("Ordering.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should().NotHaveDependencyOn("Ordering.Infrastructure")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(
            string.Join(", ", result.FailingTypes?.Select(t => t.Name) ?? []));
    }

    [Fact]
    public void All_command_handlers_end_with_CommandHandler() { /* § 1.4 */ }

    [Fact]
    public void All_query_handlers_end_with_QueryHandler() { /* § 1.4 */ }

    [Fact]
    public void Aggregates_have_private_parameterless_ctor() { /* § 1.2 */ }

    [Fact]
    public void Internal_domain_events_follow_naming_and_namespace() { /* § 1.3 */ }

    [Fact]
    public void Order_Items_is_readonly_collection() { /* § 2.3 */ }

    // ... one test per rule in § 1 and § 2.{BC}
}
```

**Marker interfaces** (`IAssemblyMarker`) per project are a common convention to avoid `typeof(SomePublicType).Assembly` brittleness — each BC should add a trivial `public interface IAssemblyMarker {}` in the root namespace of each of the four projects.

---

## 4. Rule Enforcement Checklist

Implementation agents tick these off as they author the architecture-tests project for their BC:

- [ ] **§ 1.1 Layer rules** — one test per layer pair (Domain→Infra forbidden, Domain→Api forbidden, Application→Infra forbidden, Application→Api forbidden) — 4 tests
- [ ] **§ 1.2 Aggregate discipline** — one test per aggregate (Catalog 2, Basket 1, Ordering 1, Inventory 1); each test checks ctor + factory + no-public-setters
- [ ] **§ 1.3 Event naming** — one test for internal-event convention, one for external-event convention, one for "aggregates don't raise external events" — 3 tests
- [ ] **§ 1.4 Handler naming** — 3 tests (`*CommandHandler`, `*QueryHandler`, handlers return `Result`/`Result<T>`)
- [ ] **§ 1.5 Result-pattern** — 2 tests (handlers don't throw `ArgumentException`/`InvalidOperationException`; aggregates only throw `DataIntegrityException`)
- [ ] **§ 1.6 Cross-BC** — one test per BC asserting no reference to other BCs' `Domain` or `Application` namespaces — 4 tests
- [ ] **§ 2.1 Catalog specific** — 3 tests
- [ ] **§ 2.2 Basket specific** — 3 tests
- [ ] **§ 2.3 Ordering specific** — 4 tests
- [ ] **§ 2.4 Inventory specific** — 3 tests

**Target test count per BC:** approximately 18-22 tests across common + BC-specific rules.

---

## 5. Running in CI

- **Command:** `dotnet test test/{Bc}.ArchitectureTests/` — each BC has its own project; the solution-wide `dotnet test` picks them up automatically.
- **Build gate:** architecture tests run in the same CI stage as unit tests. Failure blocks merge to `main`.
- **Local workflow:**
  ```bash
  dotnet build -m
  dotnet test test/Catalog.ArchitectureTests/
  dotnet test test/Basket.ArchitectureTests/
  dotnet test test/Ordering.ArchitectureTests/
  dotnet test test/Inventory.ArchitectureTests/
  ```
- **When a rule fires on legitimate new code:** update the rule in the same PR as the code change — do NOT `[Skip]` the test. The rule IS the design; if the design evolves, the rule must evolve with it (PR review attention).

---

## 6. Cross-References

- [master design § 11.4 Testing Layers](../eshop-master-design.md) — architecture tests are the third of four layers
- [master design § Appendix B.2](../eshop-master-design.md) — authoritative layer-reference table that § 1.1 mirrors
- [error-taxonomy.md § 2](error-taxonomy.md) — the categorization `§ 1.5` enforces
- [kafka-dlt-strategy.md § 2](kafka-dlt-strategy.md) — the consumer-handler anti-rethrow rule referenced by § 1.5
- [catalog.md](catalog.md) / [basket.md](basket.md) / [ordering.md](ordering.md) / [inventory.md](inventory.md) — the BC chapters whose invariants § 2.{BC} encodes
- `test/Weather.ArchitectureTests/` — existing precedent for NetArchTest usage in this repo
- [`platform/Platform.SharedKernel/Exceptions/DataIntegrityException.cs`](../../platform/Platform.SharedKernel/Exceptions/DataIntegrityException.cs) — the exception type checked by § 1.5
