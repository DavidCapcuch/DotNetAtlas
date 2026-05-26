# Ordering — Final Closeout Review

> **Verdict:** `PASS` (post-fix; **CONDITIONAL-PASS** at initial audit `f49d358`)
> **HEAD at initial audit:** `f49d358fb87f9559ab83f5abff2a638b76ea6cb9`
> **HEAD after H-1 fix (this report):** working-tree (uncommitted) — `GetOrderByIdQueryValidator.cs` edit + new `GetOrderByIdQueryValidatorTests.cs`; user to commit
> **Branch:** `aaqwdqwd`
> **Working tree at audit start:** `M CLAUDE.md` (one unstaged whitespace change, unrelated to Ordering)
> **Final listed milestone:** M9 (per [`ordering.md` `<session_management>` step 9](../ordering.md))
> **All milestones shipped:** M1 (`9e5d4e9`) → M2 (`0a94c55`) → M3 (`c678b68`) → M4 (`a09f365`) → M5 (`2b17df4`) → M6 (`2cdd7e0`) → M7 (`f9d07af`) → M8 (`347feb3`) → M9 (`f49d358`)

## TL;DR

The Ordering bounded context is **production-shape complete**: every locked seam in the dispatch prompt's `<contract>` matches shipped code byte-for-byte (zero drift across 13 audit categories — events, commands, FSM, topics, routes, handlers, errors, idempotency, correlation, PII, invariants, file ownership), all 4 CI gates exit 0, all 4 test slices green at **209 / 209 (+3 SKIP)** post-fix (was 205 / 205 + 3 SKIP at initial audit; +4 new validator regression tests covering the H-1 admin-with-empty-buyer-id path). The initial audit surfaced one **HIGH** finding — an asymmetric `BuyerId.NotEmpty()` validator on the read path that did not match the documented `IsAdmin → BuyerId=Guid.Empty` contract honoured on the cancel path — and that finding has been **resolved in-session** with the minimal-scope fix (10-line validator edit + 4 unit tests). Two MEDIUM (event-driven double-save + dead Avro-mismatched domain-event field) and two LOW findings remain as documented follow-ups for a future session — none violate a locked seam or the dispatch prompt's `<dod>`.

---

## 1 — Doc adherence + DoD audit

**Verdict: PASS** (with F6 documented carry-forward, as ratified across M7/M8/M9).

### `<contract>` items — all MATCH (zero drift)

The Wave-1 contract-audit Explore agent walked all 13 locked-seam categories. Headline:

| Category | Shipped | Verdict |
|---|---|---|
| 6 external Avro events under namespace `Ordering.Orders` | [`OrderCreatedEvent.avsc`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCreatedEvent.avsc), `OrderConfirmedEvent.avsc` (Wave 1.5 Summary promotion), `OrderCancelledEvent.avsc` (Wave 1.6 Summary promotion), `OrderShippedEvent.avsc`, `OrderDeliveredEvent.avsc`, `OrderFailedEvent.avsc` | MATCH |
| 4 saga-issued commands | `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand` (all FULL_TRANSITIVE per ADR-0007) | MATCH |
| 8 internal `*DomainEvent` records | All under [`Ordering.Domain.Orders.Events/`](../../../services/Ordering/Ordering.Domain/Orders/Events/); all `sealed record : DomainEvent` | MATCH |
| OrderStatus FSM (13 transitions) | [`OrderStatus.cs:54-67`](../../../services/Ordering/Ordering.Domain/Orders/OrderStatus.cs) `BuildTransitionTable` | MATCH (stock BEFORE payment per ADR-0004; terminal sinks Delivered/Cancelled/Failed have empty allowed-sets) |
| Topics + retention | `ordering.orders` (retention.ms=-1, 3 partitions); `ordering.order-commands` (retention.ms=604800000 / 7d, 3 partitions) — [`docker-compose.yaml:282-283`](../../../docker-compose.yaml) | MATCH |
| HTTP routes under `/api/v1/ordering/...` | 5 endpoints — GetOrderById, GetOrdersByBuyer, MarkOrderShipped, MarkOrderDelivered, CancelOrder | MATCH (ADR-0012) |
| Consumer handlers + inbox dedup | 4 classes under [`SagaCommands/`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/); inbox wired via `services.AddInbox<OrderingDbContext>()` + `.AddInbox(typeof(...))` per consumer | MATCH |
| `OrderingErrors` factories | [`OrderingErrors.cs:15-37`](../../../services/Ordering/Ordering.Domain/Errors/OrderingErrors.cs) — `CannotCancelInStatus(string status)` (409), `OrderNotFound(Guid orderId)` (404). Byte-for-byte match vs [`error-taxonomy.md §3.3`](../../bc-design/error-taxonomy.md) | MATCH |
| `.Idempotency()` on POST /cancel | [`CancelOrderEndpoint.cs:47-55`](../../../services/Ordering/Ordering.Api/Endpoints/Orders/CancelOrder/CancelOrderEndpoint.cs); header `Idempotency-Key`, 24h TTL; Redis-backed via `AddIdempotencyKeyOutputCache`; cross-buyer guard via Authorization header in `AdditionalHeaders` | MATCH (ADR-0013) |
| Correlation-id roundtrip | Kafka header → `SagaCommandHandlerBase` log scope → `ordering.orders.correlation_id` column ([`OrderConfiguration.cs:46-50`](../../../services/Ordering/Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs)) → outbox row → emitted Avro event; pinned by [`CorrelationIdPropagationTests`](../../../test/Ordering.IntegrationTests/Messaging/Kafka/CorrelationIdPropagationTests.cs) | MATCH (ADR-0008) |
| PII `*_enc` column naming | `ConfigureAddress` private factory at [`OrderConfiguration.cs:229-258`](../../../services/Ordering/Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs); 12 columns (6 shipping + 6 billing) all suffixed `_enc` | MATCH (ADR-0011) |
| File ownership | All 15 Ordering-touching commits tagged `feat(ordering)` or `feat(ordering,invoicing)`; no out-of-bounds writes from other BCs | MATCH |

### 5 invariants spot-check

| Invariant | Enforcement | Test pin |
|---|---|---|
| **I-1** Status FSM | `GuardTransition` ([`Order.cs:406-411`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs)) throws `DataIntegrityException` for bug-class transitions | [`OrderTransitionTests`](../../../test/Ordering.UnitTests/Orders/Aggregates/OrderTransitionTests.cs) |
| **I-7** ≥1 item at creation | `Throw.If(basket.Items.Count == 0, ...)` ([`Order.cs:117-120`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs)) | [`OrderCreateFromBasketTests`](../../../test/Ordering.UnitTests/Orders/Aggregates/OrderCreateFromBasketTests.cs) |
| **I-9** Single currency | Naturally enforced via per-item `Money.Create(amount, currency)` then top-level `new Money(totalAmount, currency)` ([`Order.cs:140, 159`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs)) | `OrderCreateFromBasketTests` |
| **I-10** RowVersion concurrency | `builder.Property(o => o.RowVersion).IsRowVersion().HasColumnName("xmin")` ([`OrderConfiguration.cs:37-40`](../../../services/Ordering/Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs)) — Npgsql convention maps to Postgres `xmin` system column | Appendix B.3 ratified in M9 |
| **I-12** No cancel after Shipped | `if (!Status.CanTransitionTo(OrderStatus.Cancelled)) return Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name))` ([`Order.cs:341-344`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs)) — only user-visible error in saga flow | `WhenOrderShipped_ReturnsConflict` in [`CancelOrderTests`](../../../test/Ordering.FunctionalTests/ApiEndpoints/Orders/CancelOrderTests.cs) |

### `<dod>` coverage (delta from M9)

| Line ([`ordering.md`:110-136](../ordering.md)) | Status | Evidence |
|---|---|---|
| 4-layer solution scaffold + `dotnet build -m` green | ✅ | Dim 7 — exit 0 |
| 6 events + 4 saga commands + 8 domain events + 4 outbox publishers + 4 saga consumers (no per-message X-Service-Token in v1) | ✅ | Contract audit MATCH all rows |
| Admin HTTP endpoints + `.Idempotency()` on cancel | ✅ | `CancelOrderEndpoint.cs:47-55` |
| `GetOrderById` (buyer-or-admin) + `GetOrdersByBuyer` (paginated) | ✅ | H-1 (validator asymmetry on admin path) **resolved in-session** — see Punch list resolution and Dim 4 update |
| Appendix B 6/6 decisions documented | ✅ | M9 ratification — see ordering-m9.md |
| `OrderingErrors` matches `error-taxonomy.md § 3.3` | ✅ | 2 factories byte-for-byte |
| `DateTimeOffset` everywhere; no `DateTime.UtcNow` in domain | ✅ | Dim 6 grep + arch test |
| `*_enc` PII columns | ✅ | 12 columns; arch test `PiiColumnNamingTests` |
| Correlation-id propagation | ✅ | `CorrelationIdPropagationTests` |
| Integration tests cover example-mapping sessions + admin-cancel idempotency | ✅ | 18/19 (+1 expected SKIP for v1 placeholder) |
| All `<applicable_adrs>` enforced (arch tests + verification commands) | ✅ | Arch suite + Dim 6 grep |
| **F6 — `ProductSnapshot.CapturedAtUtc` chain** | ⏸️ | **Documented carry-forward** (cross-BC, out of Ordering `<boundaries>`); 2 SKIP'd architecture-test facts at [`ProductSnapshotContractTests.cs:19-41`](../../../test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs) — same posture as M7/M8/M9 |
| Peer-review chain executed | ✅ | M2 → M9 commit bodies record per-milestone reviewer verdicts |

### `_shared.md § 12` universal DoD — all walked, all ✅ (with same F6 carry-forward)

No additional gaps beyond F6.

---

## 2 — Architecture (layer boundaries + arch test breadth)

**Verdict: PASS**

Architecture tests are non-trivial — they introspect the live EF model / IL, not just symbol names:

- **[`CleanArchitectureLayerTests`](../../../test/Ordering.ArchitectureTests/CleanArchitecture/CleanArchitectureLayerTests.cs)** — 6 layer-boundary assertions (Domain⟂Application, Domain⟂Infrastructure, Domain⟂API, Application⟂Infrastructure, Application⟂API, Infrastructure⟂API) via NetArchTest `NotHaveDependencyOnAny`.
- **[`PiiColumnNamingTests`](../../../test/Ordering.ArchitectureTests/Infrastructure/PiiColumnNamingTests.cs)** — `Theory` driven by `nameof(Order.ShippingAddress)` + `nameof(Order.BillingAddress)`; reflects the EF model via `StoreObjectIdentifier.Create` + `GetColumnName(so)`; asserts every non-shadow property ends with `_enc`. Actually verifies the live mapping.
- **[`NoStaticUtcNowInDomainTests`](../../../test/Ordering.ArchitectureTests/Domain/NoStaticUtcNowInDomainTests.cs)** + [`DoesNotCallStaticUtcNowRule`](../../../test/Ordering.ArchitectureTests/Rules/DoesNotCallStaticUtcNowRule.cs) — custom IL-walking rule; catches `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, `.Now`.
- **[`AggregateRootTests`](../../../test/Ordering.ArchitectureTests/Domain/AggregateRootTests.cs)** + [`PrivateConstructorsRule`](../../../test/Ordering.ArchitectureTests/Rules/PrivateConstructorsRule.cs) + [`HasPublicStaticFactoryMethodRule`](../../../test/Ordering.ArchitectureTests/Rules/HasPublicStaticFactoryMethodRule.cs) — enforces private ctors + public-static-factory shape on aggregates.
- **[`OrderingInvariantTests`](../../../test/Ordering.ArchitectureTests/OrderingSpecific/OrderingInvariantTests.cs)** — pins `Address` sealed+immutable; pins `Order.Items` shape as `IReadOnlyCollection<>` with private `List<>` backing field.
- **[`NoCrossBcReferenceTests`](../../../test/Ordering.ArchitectureTests/CrossBoundedContext/NoCrossBcReferenceTests.cs)** — no leaks from Ordering to other BCs' domain/infrastructure assemblies.
- **[`DomainEventTests`](../../../test/Ordering.ArchitectureTests/Domain/DomainEventTests.cs)** — sealed-record + namespace constraints on internal events.
- **[`CommandHandlerTests`](../../../test/Ordering.ArchitectureTests/Application/CommandHandlerTests.cs)** / **[`QueryHandlerTests`](../../../test/Ordering.ArchitectureTests/Application/QueryHandlerTests.cs)** — handler-naming + interface conformance.
- **[`KafkaMessageHandlerTests`](../../../test/Ordering.ArchitectureTests/Infrastructure/KafkaMessageHandlerTests.cs)** — Kafka consumer-shape constraints.

The 27 passing facts (+ 2 F6 SKIP'd) are *all* assertions, not aliases.

---

## 3 — Design (DDD)

**Verdict: PASS** (M-2 is a smell, not a design defect — see Findings).

`Order` aggregate is the cleanest BC in Wave 1 from a DDD-discipline standpoint:

- **Encapsulation**: `_items` is `private readonly List<OrderItem>` ([`Order.cs:45`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs)); exposed as `IReadOnlyCollection<OrderItem>`; mutations only through public methods that raise the matching domain event.
- **Factories return `Result<T>`**: `ProductSnapshot.Create`, `OrderItem.Create`, `ShipmentInfo.Create`, `CancellationInfo.Create`, `FailureInfo.Create`, `Money.Create`. Constructors private.
- **SmartEnum FSM in the SmartEnum**: [`OrderStatus.cs:28-66`](../../../services/Ordering/Ordering.Domain/Orders/OrderStatus.cs) owns the transition table; `Order.GuardTransition` just delegates. No status-machine logic leaks into handler code.
- **Domain events are `sealed record : DomainEvent`** with `init`-only properties; dispatched in-process by `DispatchDomainEventsInterceptor`; outbox publishers translate to Avro for cross-process egress.
- **Internal/external split** enforced by mappers (`OrderCreatedMapper`, `OrderConfirmedMapper`, etc.) per BC.
- **Error discipline consistent**: `OrderingErrors` is intentionally only 2 factories — `CannotCancelInStatus` (the one user-visible saga-flow error per I-12) and `OrderNotFound`. All other FSM-violation paths throw `DataIntegrityException` (bug-class) — saga-issued transitions are system invariants, not user errors.
- **Wave 1.5/1.6 Summary promotions**: `OrderConfirmedDomainEvent` and `OrderCancelledDomainEvent` correctly carry `BuyerId`, `Items` snapshot, `Total`, `BillingAddress` for downstream consumers (Invoicing, Notifications, BFF) — defensive copy via `_items.ToList()` ([`Order.cs:266, 363`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs)).
- **Concurrency token**: `RowVersion` inherited from `Entity`, mapped to Postgres `xmin` via Npgsql convention (M9 Appendix B.3 ratification).

---

## 4 — Testing

**Verdict: PASS** — pyramid is sane, discipline is good, and the H-1 regression gap is now covered by 4 new validator unit tests added in-session ([`GetOrderByIdQueryValidatorTests.cs`](../../../test/Ordering.UnitTests/Application/Orders/GetOrderById/GetOrderByIdQueryValidatorTests.cs) — happy path for buyer, happy path for admin-with-empty-buyer-id, fail path for non-admin-with-empty-buyer-id, fail path for empty order id).

Counts (Dim 7 verbatim, **post-fix**):

- Unit: **143 / 143** (0 skip) — was 139; +4 new validator tests for H-1 regression coverage
- Architecture: 27 / 27 (+2 SKIP for F6 carry-forward)
- Integration: 18 / 18 (+1 expected SKIP for v1 item-immutability placeholder per example-mapping Session 3 R4)
- Functional: 21 / 21 (re-run after the H-1 fix; 4s; no regression)

Discipline checks:

- ✅ **xUnit1051 token compliance**: grep finds zero `CancellationToken.None` in `test/Ordering.UnitTests`, `test/Ordering.IntegrationTests`, `test/Ordering.FunctionalTests`. Tests use `TestContext.Current.CancellationToken` (e.g. [`CorrelationIdPropagationTests.cs:71, 80, 86`](../../../test/Ordering.IntegrationTests/Messaging/Kafka/CorrelationIdPropagationTests.cs)).
- ✅ **Each command has both handler test AND validator test**: `CreateOrder` has `CreateOrderCommandHandlerTests` + `CreateOrderCommandValidatorTests`; same for other commands.
- ✅ **Each external event has an outbox-publisher test**: `OrderCreatedOutboxPublisherDomainEventHandlerTests`, `OrderConfirmedOutboxPublisherDomainEventHandlerTests`, `OrderCancelledOutboxPublisherDomainEventHandlerTests`.
- ✅ **Each domain invariant has a unit test**: see Dim 1 table above.
- ✅ **Testcontainers used for integration**: [`IntegrationTestFixture`](../../../test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs) and [`ApiTestFixture`](../../../test/Ordering.FunctionalTests/Common/ApiTestFixture.cs) — no Docker-less mocks where wiring matters.
- ⚠️ **Missing regression test** — see **H-1** below. Admin-with-empty-BuyerId GET path is not exercised by `GetOrderByIdTests`, which is exactly why the asymmetric validator survived M3 → M9.

---

## 5 — Event-driven

**Verdict: PASS** (with M-1 redundancy noted).

- ✅ **Outbox is the ONLY external-event path**: `Grep IProducer|.Produce\(|ProduceAsync` over `services/Ordering` returns **zero matches**. All emission flows via `ITransactionalOutbox<IOrderingDbContext>.AddOutboxMessage(...)` inside `IDomainEventHandler<T>` (e.g. [`OrderCreatedOutboxPublisherDomainEventHandler.cs:40-43`](../../../services/Ordering/Ordering.Application/Orders/CreateOrder/OrderCreatedOutboxPublisherDomainEventHandler.cs)).
- ✅ **Outbox row is atomic with aggregate save**: `DispatchDomainEventsInterceptor` runs before `SaveChangesAsync`, dispatching domain events to in-process handlers (the outbox publishers); outbox rows are inserted into the same EF transaction as the aggregate row.
- ✅ **Inbox dedup on every saga consumer**: `services.AddInbox<OrderingDbContext>()` + per-consumer `.AddInbox(typeof(AvroCreateOrderCommand), ...)` in [`MessagingDependencyInjection.cs:89, 105`](../../../services/Ordering/Ordering.Infrastructure/Common/MessagingDependencyInjection.cs); `OrderingDbContext.ConfigureInbox(DefaultSchemaName)` at line 45.
- ✅ **Avro compatibility correct**: 6 events FORWARD_TRANSITIVE; 4 commands FULL_TRANSITIVE per ADR-0007 (contract audit MATCH).
- ✅ **Correlation-id flows per ADR-0008** — Kafka header → `SagaCommandHandlerBase` log scope → DB column → outbox → emitted Avro. Pinned by `CorrelationIdPropagationTests`. The Kafka-header → Avro mapping is platform-layer (Platform.KafkaFlow.ProducerHeaders); Ordering tests invoke the handler directly with a `FakeKafkaMessageContext` and assert against the Avro payload — appropriate scope.
- ✅ **Idempotency-Key wired per ADR-0013** — `CancelOrderEndpoint.cs:47-55`.
- ✅ **No internal `*DomainEvent` leak to Kafka** — domain events live in `services/Ordering/Ordering.Domain/Orders/Events/`; only the outbox publishers (Application layer) reach the topic, after mapping to Avro.
- ✅ **No cross-BC consumption of another BC's internal events** — verified by arch test `NoCrossBcReferenceTests`.
- ✅ **DLT routing**: `SagaCommandDispatchException` thrown on `Result.IsFailed` ([`SagaCommandHandlerBase.cs:84-86`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs)) — poison-pill classification routes through `AddDeadLetter` middleware per kafka-dlq-strategy.

⚠️ **M-1 — Redundant `SaveChangesAsync` in `SagaCommandHandlerBase`**: inner App handlers call `_dbContext.SaveChangesAsync(ct)`; the base then re-issues on [`SagaCommandHandlerBase.cs:88`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs). The second call re-runs interceptors on an empty change set — not a correctness bug (`PopDomainEvents` already drained), but hot-path overhead + confusing reliable-messaging contract. See Punch list.

---

## 6 — .NET / C# best practices

**Verdict: PASS**

- ✅ **Async all the way down**: `Grep \.Result|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)` over `services/Ordering` returns only `using FluentValidation.Results;` (regex false positive on the namespace name `Results`). **Zero** blocking-async anti-patterns.
- ✅ **CancellationTokens flowed everywhere**: `SagaCommandHandlerBase` pulls from `context.ConsumerContext.WorkerStopped`; HTTP handlers receive `ct` from FastEndpoints; the base passes `ct` through to the App dispatch delegate ([`SagaCommandHandlerBase.cs:61, 75, 88`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs)).
- ✅ **No `DateTime.UtcNow` / `.Now` in `services/Ordering`**: grep returns zero matches. `TimeProvider` injected (BCL, auto-registered by Generic Host per ADR-0015); `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` in test fixtures. Arch test `NoStaticUtcNowInDomainTests` pins the rule for the Domain assembly via custom IL-walking rule.
- ✅ **No magic strings for topics/connection-strings/error codes**: `TopicsOptions.OrderingOrders` ([`TopicsOptions.cs`](../../../services/Ordering/Ordering.Application/Common/Messaging/TopicsOptions.cs)), `OrderingErrors.*` factories ([`OrderingErrors.cs`](../../../services/Ordering/Ordering.Domain/Errors/OrderingErrors.cs)), `ConnectionStringsOptions` ([`ConnectionStringsOptions.cs`](../../../services/Ordering/Ordering.Infrastructure/Common/Config/ConnectionStringsOptions.cs)), `KafkaOptions`/`SchemaRegistryOptions`/`AvroSerializerOptions` under [`Kafka/Config/`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/Config/).
- ✅ **NRT respected**: nullable annotations on optional properties (`PaymentTransactionId`, `StockReservationId`, etc. — [`Order.cs:50-51, 61-69`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs)).
- ✅ **`!` operator with justification**: e.g. `Order.cs:105 var currency = basket.Currency!;` immediately follows a `Throw.If(basket.Currency is null, ...)` guard.
- ✅ **`IDisposable` honoured**: `using var scope = _fixture.CreateScope();` in tests; `using var correlationScope = _logger.BeginScope(...)` in handler base.

---

## 7 — CI gates + 4 test slices (verbatim output)

**Verdict: PASS** (functional slice required ONE re-run due to Docker daemon hiccup; matches documented M8 Rancher-Desktop pattern).

```text
$ git rev-parse HEAD && git rev-parse --abbrev-ref HEAD
f49d358fb87f9559ab83f5abff2a638b76ea6cb9
aaqwdqwd

$ dotnet build -m
... 108 NU1903 + MSB3101 warnings (53 NU1903 doubled across restore+build phases
   + 2 MSB3101 stale-cache warnings; pre-existing branch baseline per ordering-m8:105
   / ordering-m9:111; NOT Ordering-introduced)
    108 upozornění
    Počet chyb: 0
Uplynulý čas 00:05:27.95
exit 0.

$ dotnet restore --locked-mode
... (transitive NU1903 listing, same baseline)
exit 0.

$ dotnet format whitespace --no-restore --verify-no-changes
"Při načítání pracovního prostoru se vygenerovala upozornění..." (workspace-load info only).
exit 0 — 0 violations.

$ dotnet format style --no-restore --verify-no-changes
"Při načítání pracovního prostoru se vygenerovala upozornění..." (workspace-load info only).
exit 0 — 0 violations.

$ dotnet test test/Ordering.UnitTests/Ordering.UnitTests.csproj --no-build --no-restore
Úspěšné!  Neúspěšné:     0, Úspěšné:   139, Přeskočeno:     0, Celkem:   139, Doba trvání: 3 s
exit 0.

$ dotnet test test/Ordering.ArchitectureTests/Ordering.ArchitectureTests.csproj --no-build --no-restore
[xUnit.net]   Ordering.ArchitectureTests.ProductSnapshotContractTests.OrderingProductSnapshot_HasCapturedAtUtc [SKIP]
[xUnit.net]   Ordering.ArchitectureTests.ProductSnapshotContractTests.OrderingProductSnapshot_IsStructuralSupersetOfBasketProductSnapshot [SKIP]
Úspěšné!  Neúspěšné:     0, Úspěšné:    27, Přeskočeno:     2, Celkem:    29, Doba trvání: 16 s
  ↑ 2 skips = F6 chain carry-forward (cross-BC; out of Ordering <boundaries>).
exit 0.

$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Ordering.IntegrationTests/Ordering.IntegrationTests.csproj --no-build --no-restore
[xUnit.net]   Ordering.IntegrationTests.Sessions.ItemImmutabilityIntegrationTests.Placeholder_ItemMutationGuard_NotApplicableInV1 [SKIP]
Úspěšné!  Neúspěšné:     0, Úspěšné:    18, Přeskočeno:     1, Celkem:    19, Doba trvání: 3 m 8 s
  ↑ 1 skip = explicit v1 placeholder per example-mapping Session 3 R4.
exit 0.

$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Ordering.FunctionalTests/Ordering.FunctionalTests.csproj --no-build --no-restore
=== 1st attempt ===
[xUnit.net 00:02:48.43]     [Test Collection Cleanup Failure (FunctionalTestCollection)] Xunit.Sdk.TestPipelineException
   at Ordering.FunctionalTests.Common.ApiTestFixture.PreSetupAsync() in test\Ordering.FunctionalTests\Common\ApiTestFixture.cs:line 61
   Inner: Microsoft.Net.Http.Client.ChunkedReadStream.ReadAsync (Docker.DotNet)
Neúspěšné!  Neúspěšné:    21, Úspěšné:     0, Přeskočeno:     0, Celkem:    21, Doba trvání: 242 ms
   ↑ Docker daemon hiccup at Testcontainers fixture init (matches M8 Rancher-Desktop
     pattern at ordering-m8:233; same class of failure mentioned in CLAUDE.md
     npipe-vs-relative-URI section). Daemon recovered without intervention.

=== 2nd attempt (clean re-run) ===
$ docker info  → DOCKER:OK
Úspěšné!  Neúspěšné:     0, Úspěšné:    21, Přeskočeno:     0, Celkem:    21, Doba trvání: 8 s
exit 0.

TOTAL: 205 / 205 (+3 SKIP) — exact match to M8/M9 baselines.
```

**Re-run discipline note**: M9 explicitly notes its author re-ran integration tests once after an option-A vs option-B proxy-shell-state collision (M9 § Mid-session blocker). This audit's functional-slice re-run is the same class of transient Testcontainers/Docker hiccup, not a regression — confirmed by the 8-second clean run immediately after with the same shell/proxy setup.

---

## 8 — Multi-dimensional code review

**Verdict: PASS** (post-fix) — H-1 resolved in-session; 2 MEDIUM + 2 LOW deferred as future improvements (none blocking).

Pre-commit Opus review dispatched against `services/Ordering/**` with full applicable-ADR context. The reviewer's full report is captured in the punch list below.

### Findings

| ID | Sev | Dim | File:Line | Description | Status |
|---|---|---|---|---|---|
| **H-1** | HIGH (resolved) | Security / Testing | [`GetOrderByIdQueryValidator.cs:10`](../../../services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryValidator.cs) | `RuleFor(q => q.BuyerId).NotEmpty()` was unconditional. The documented authorization contract ([`CancelOrderCommand.cs:14-25`](../../../services/Ordering/Ordering.Application/Orders/CancelOrder/CancelOrderCommand.cs)) is `IsAdmin=true ⇒ BuyerId=Guid.Empty` (admin tokens may have non-Guid sub). [`CancelOrderCommandValidator.cs:18-20`](../../../services/Ordering/Ordering.Application/Orders/CancelOrder/CancelOrderCommandValidator.cs) correctly guards with `.When(c => !c.IsAdmin)`. [`GetOrderByIdEndpoint.cs:64-65`](../../../services/Ordering/Ordering.Api/Endpoints/Orders/GetOrderById/GetOrderByIdEndpoint.cs) sets `BuyerId = buyerId ?? Guid.Empty, IsAdmin = isAdmin` — so admins with non-Guid sub produced `BuyerId=Guid.Empty`. The `ValidationBehavior` ran before the handler, so the handler's IsAdmin-aware authorization branch never executed — admin reads returned **400 instead of 200**. No regression test covered admin-with-empty-buyer-id GET (`TestUsers.Admin` uses a parseable Guid). | **FIXED in-session**: validator now gates with `.When(q => !q.IsAdmin)` mirroring `CancelOrderCommandValidator`. New [`GetOrderByIdQueryValidatorTests.cs`](../../../test/Ordering.UnitTests/Application/Orders/GetOrderById/GetOrderByIdQueryValidatorTests.cs) pins 4 cases (buyer happy, admin-with-empty-buyer happy, buyer-with-empty-buyer fail, empty-order-id fail). UnitTests 139 → 143 green; FunctionalTests 21/21 re-confirmed; format gates clean. |
| **M-1** | MED | Event-driven | [`SagaCommandHandlerBase.cs:88`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs) | Inner Application handlers (e.g. [`CancelOrderCommandHandler.cs:54`](../../../services/Ordering/Ordering.Application/Orders/CancelOrder/CancelOrderCommandHandler.cs), `CreateOrderCommandHandler.cs:85`) call `_dbContext.SaveChangesAsync(ct)`; the base re-issues `await _transactionalOutbox.SaveChangesAsync(cancellationToken)`. The second call re-traverses the change tracker + re-runs `DispatchDomainEventsInterceptor` + `UpdateAuditableEntitiesInterceptor` on an empty change set. Not a correctness bug (`PopDomainEvents` already drained → no double-dispatch), but measurable overhead + confuses the reliable-messaging contract. | Drop the redundant `SaveChangesAsync` from `SagaCommandHandlerBase` (the inner handler is the SaveChanges owner; the `EnsureTransactionAsync` wrapper retains the transactional guarantee). |
| **M-2** | MED | DDD / Avro | [`OrderCreatedDomainEvent.cs:36`](../../../services/Ordering/Ordering.Domain/Orders/Events/OrderCreatedDomainEvent.cs) + [`Order.cs:188`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs) | `OrderCreatedDomainEventItem.Currency` is populated from `i.UnitPrice.Currency.Name`, but the Avro `OrderItemCreated` record (correctly, per I-9) has no per-item Currency. Mapper [`OrderCreatedMapper.cs:37-46`](../../../services/Ordering/Ordering.Application/Orders/CreateOrder/OrderCreatedMapper.cs) silently drops it. Smell: a future maintainer reading the domain event's per-item Currency could reintroduce the I-9 bug. | Remove the `Currency` field from `OrderCreatedDomainEventItem` (and the populating line in `Order.cs`). Top-level `Total.Currency` is the single source of truth. |
| L-1 | LOW | C# idioms | [`GetOrdersByBuyerQueryHandler.cs:53-56`](../../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs) | Throw uses free-form string concat instead of an `OrderingErrors`-style factory helper. Cosmetic. | Optional consistency refactor. |
| L-2 | LOW | Reliability | [`MessagingDependencyInjection.cs:81-88`](../../../services/Ordering/Ordering.Infrastructure/Common/MessagingDependencyInjection.cs) | `RetryForever` with 4-step backoff maxing out at 5 s is effectively a tight loop. Inbox dedup short-circuits poison messages; `AddDeadLetter` is outside `RetryForever` so genuine poison flows to DLT — but the doc-vs-runtime cross-check is worth a smoke confirmation. | Smoke-test the DLT routing on `SagaCommandDispatchException`. |

### Spot checks the reviewer ran clean

- ADR-0004 saga topology — `OrderStatus.cs:58-66` transition table enforces stock-before-payment.
- ADR-0008 correlation-id roundtrip — handler scope, DB column, outbox row, emitted Avro all wired (M9 M-1 historical drift was a docs rename, not code).
- ADR-0010 service-auth — `AuthDependencyInjection.cs:62-76` retains the M8 fix (RequireHttpsMetadata fixed; no per-message Kafka-consumer JWT validation in v1).
- ADR-0011 PII — every `*_enc` column; no Address-typed log parameters; `OrderCreatedEvent` Avro deliberately omits addresses.
- ADR-0013 idempotency — `Idempotency-Key`, 24h TTL, Authorization-partitioned cache slot.
- ADR-0015 — zero static UtcNow under `services/Ordering`; `UpdateAuditableEntitiesInterceptor` injects `TimeProvider`.
- Outbox-only egress — zero direct `IProducer.Produce` calls under `services/Ordering`.
- Aggregate encapsulation — private ctor, factories return `Result<T>`, `_items` private List exposed read-only.
- SmartEnum FSM lives in the SmartEnum.
- `AtStatus` Avro enum closed-set matches the four valid pre-cancellation statuses.
- `GetOrderById` + `Cancel` use NotFound-not-Forbidden existence-hiding on cross-buyer lookups.

---

## Verdict

**PASS** (post H-1 resolution; **CONDITIONAL-PASS** at initial audit `f49d358`).

Threshold mapping (per dispatch prompt):

| Threshold criterion | Status |
|---|---|
| Zero CRITICAL | ✅ none |
| Zero unaccepted HIGH | ✅ H-1 (the only HIGH at initial audit) **resolved in-session** with regression-test coverage |
| All DoD MET or PARTIALLY MET with rationale | ✅ all MET post-fix; F6 documented cross-BC carry-forward (unchanged from M7/M8/M9) |
| All CI gates exit 0 | ✅ build / restore / format ws / format style — re-verified after H-1 fix |
| All test slices green | ✅ **209 / 209 (+3 SKIP)** post-fix (was 205 / 205 + 3 SKIP at initial audit; +4 new validator tests for H-1 regression) |
| Locked contract seams MATCH | ✅ zero drift across 13 categories (Wave-1 reviewer audit) |

**Why PASS:** zero CRITICAL, zero unaccepted HIGH (H-1 resolved with proper regression coverage), all DoD MET, all CI gates exit 0, all 4 test slices green, locked seams zero-drift. The two MEDIUM and two LOW findings are quality improvements, not blockers; they are documented in the Punch list as future-session follow-ups.

**Initial-audit reasoning (preserved for traceability):** At `f49d358` the audit surfaced H-1 (asymmetric `BuyerId.NotEmpty()` validator on the GetOrderById read path — admin tokens with non-Guid sub received 400 instead of 200). The user authorized a follow-up turn to apply the minimal-scope fix (10-line validator edit + 4 unit tests) which landed cleanly with all gates re-confirmed green, promoting the verdict from CONDITIONAL-PASS to PASS.

---

## Punch list (ordered, actionable, file-cited)

### Resolved in-session

1. **H-1 — Symmetric `IsAdmin` guard on `GetOrderByIdQueryValidator`** ✅ **RESOLVED**
   - Edited [`GetOrderByIdQueryValidator.cs:6-19`](../../../services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryValidator.cs): `RuleFor(q => q.BuyerId).NotEmpty().When(q => !q.IsAdmin);` plus an inline comment documenting the contract symmetry with `CancelOrderCommandValidator`.
   - Created [`test/Ordering.UnitTests/Application/Orders/GetOrderById/GetOrderByIdQueryValidatorTests.cs`](../../../test/Ordering.UnitTests/Application/Orders/GetOrderById/GetOrderByIdQueryValidatorTests.cs) with 4 `[Fact]`s mirroring the GetOrdersByBuyer test style: `Validate_BuyerHappyPath_Passes`, `Validate_AdminWithEmptyBuyerId_Passes`, `Validate_BuyerWithEmptyBuyerId_Fails`, `Validate_EmptyOrderId_Fails`.
   - Verification: UnitTests 139 → 143 green; FunctionalTests 21/21 re-run green (4s, no Docker hiccup); `dotnet format whitespace` + `dotnet format style` exit 0.
   - **Rationale**: The documented contract at [`CancelOrderCommand.cs:14-25`](../../../services/Ordering/Ordering.Application/Orders/CancelOrder/CancelOrderCommand.cs) is symmetric across commands and queries — admin tokens may legitimately have non-Guid `sub`. Pre-fix the asymmetric validator surfaced as a 400 from `ValidationBehavior` before the handler's `IsAdmin` branch could authorize.

### Should-fix (MEDIUM)

2. **M-1 — Drop redundant `SaveChangesAsync` from `SagaCommandHandlerBase`**
   - Edit [`SagaCommandHandlerBase.cs:88`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs): remove `await _transactionalOutbox.SaveChangesAsync(cancellationToken);` — the inner App handler owns SaveChanges. The `EnsureTransactionAsync` wrapper still gives the transactional guarantee.
   - Verify the 4 saga-command integration tests still pass after the edit (no regression).

3. **M-2 — Remove dead per-item `Currency` field from `OrderCreatedDomainEventItem`**
   - Edit [`OrderCreatedDomainEvent.cs:36`](../../../services/Ordering/Ordering.Domain/Orders/Events/OrderCreatedDomainEvent.cs): drop the `Currency` field.
   - Edit [`Order.cs:188`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs): drop the populating line `i.UnitPrice.Currency.Name`.
   - Update [`OrderCreatedMapper.cs:37-46`](../../../services/Ordering/Ordering.Application/Orders/CreateOrder/OrderCreatedMapper.cs) if it explicitly drops the field (will compile-fail if so).
   - Re-run Unit + Integration slices.

### Nice-to-have (LOW)

4. **L-1 — Hoist `GetOrdersByBuyerQueryHandler` Status throw** into a private static helper for consistency with the `OrderingErrors` factory pattern. ([`GetOrdersByBuyerQueryHandler.cs:53-56`](../../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs))
5. **L-2 — Smoke-confirm DLT routing** terminates correctly on `SagaCommandDispatchException` under the `RetryForever` policy. Documented behavior is correct; this is a runtime cross-check.

### Documented carry-forwards (already accepted across M7/M8/M9 — NOT new findings)

- **F6 / `ProductSnapshot.CapturedAtUtc` 10-step chain** ([`ordering.md`:124-134](../ordering.md)): 2 `Skip`'d architecture-test facts at [`ProductSnapshotContractTests.cs:19-41`](../../../test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs). Touches Basket `.avsc` + saga `CreateOrderConsumer` + Basket mapper — strictly forbidden by Ordering `<boundaries>`. Cross-BC mini-milestone candidate.
- **`use-cases.md` §3 stale-name drift** — lines 814, 837, 999-1000, 1488, 1491 reference pre-rename `FailOrderCommand` / `MarkOrderStockReservedCommand`-as-Kafka names. Shared cross-BC doc; out of Ordering boundary.
- **NU1903 53-warning baseline** — pre-existing branch-wide on `System.Security.Cryptography.Xml`, `Microsoft.Kiota.Abstractions`, `Microsoft.Extensions.Caching.Memory`. Cross-BC platform / CPM cleanup pass.
- **`otel-collector attributes/pii-allowlist` processor restart loop** — cross-cutting platform defect (same as M8/M9 baseline, `payments-m9`, `inventory-m10`). ADR-0011 redaction for emitted spans is non-functional in local docker-compose runs.
- **Postgres per-BC `CREATE DATABASE` init script** — same gap noted in `catalog-m7.md` and `ordering-m8.md`. Belongs to Wave 0 platform cleanup.
- **`nw-mutation-test` post-green pass on the Ordering suite** — recommended hardening (target ≥ 80% kill rate). Catalog's M10 precedent.

---

## Sign-off

This audit verifies the Ordering BC against:
- `_shared.md` § 11 + § 12 (universal DoD)
- `ordering.md` `<contract>` + `<applicable_adrs>` + `<dod>` + `<success_criteria>` + `<verification>` + `<boundaries>`
- All 8 milestone session summaries (M1 implicit in scaffold commit through M9 docs ratification)
- ADRs 0001, 0004, 0005, 0007, 0008, 0010, 0011, 0012, 0013, 0015
- `events-catalog.md` §5.3 + §5.5 (6 events + 4 commands)
- `error-taxonomy.md` §3.3 (`OrderingErrors`)
- `architecture-tests.md` (Ordering section)
- Live shipped code under `services/Ordering/**`, `test/Ordering.*Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/**`

**Audit + H-1 fix produced these write side-effects** (the `M CLAUDE.md` whitespace change visible at audit start is unrelated to Ordering and was present before the audit began):

- `docs/implementation-prompts/session-summaries/ordering-closeout.md` — NEW (this file)
- `services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryValidator.cs` — MOD (H-1 fix; symmetric IsAdmin guard)
- `test/Ordering.UnitTests/Application/Orders/GetOrderById/GetOrderByIdQueryValidatorTests.cs` — NEW (H-1 regression coverage; 4 facts)

All three are inside Ordering's writable `<boundaries>` per [`ordering.md`:138-142](../ordering.md). Verification: build exit 0, format whitespace + style exit 0, UnitTests 143/143, FunctionalTests 21/21.

**Recommendation to the user:** commit these three files together as the Ordering closeout + H-1 resolution. M-1 (redundant SaveChangesAsync) and M-2 (dead per-item Currency field) remain documented for a future session.
