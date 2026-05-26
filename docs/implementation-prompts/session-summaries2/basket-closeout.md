# Basket BC — Final Closeout Review

> HEAD: `9aad7c4` · Branch: `aaqwdqwd` · Reviewer pass date: 2026-05-11
>
> **Verdict: FAIL.** Four CRITICAL defects shipped to `main`-bound branch. BC must not be considered closed.

---

## TL;DR

The Basket BC ships its contract surface and most of its DoD as designed — domain model, ACL, outbox + side-car schema, all 6 commands + 1 query, FastEndpoints wiring, the four CI gates green, and 205 / 205 tests pass against an idle Docker daemon. **However**, four independently exploitable CRITICAL defects passed every pre-commit and final review gate before this closeout: (1) `CheckoutBasketCommandHandler` has no concurrency guard and two parallel checkouts produce two outbox rows / two saga starts, (2) `BasketConcurrencyError` is unmapped in `ResultsExtensions` and returns **500 instead of the documented 409** to clients, (3) `RedisBasketRepository.DeleteAsync` lets Redis exceptions propagate out *after* the outbox row is already committed (user sees 500, saga is already running), and (4) every Basket domain event ignores the injected `TimeProvider` and falls back to the platform default `DateTimeOffset.UtcNow` — violating ADR-0015 in spirit and leaking wall-clock into the `BasketCheckoutInitiatedEvent.InitiatedAtUtc` Avro field.

A Wave-2 saga agent **cannot** consume `BasketCheckoutInitiatedEvent` end-to-end safely until (1)–(3) are fixed; (4) is recoverable but compromises test determinism and ADR compliance.

---

## Dimension 1 — Doc adherence + DoD audit

### `_shared.md § 12` — universal DoD

| Item | Status | Evidence |
|---|---|---|
| 4-layer project compiles | MET | All Basket projects build clean ([Basket.Api.csproj](services/Basket/Basket.Api/Basket.Api.csproj), Application, Domain, Infrastructure). |
| All commands + queries from use-cases.md § 2 implemented | MET | 6 commands + 1 query — all present under [Basket.Application/Baskets/](services/Basket/Basket.Application/Baskets/). |
| All internal `*DomainEvent` types declared in Domain | MET | 7 events under [Basket.Domain/Baskets/Events/](services/Basket/Basket.Domain/Baskets/Events/). |
| All external `*Event` Avro schemas | MET | [BasketCheckoutInitiatedEvent.avsc](platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc) matches `events-catalog.md § 5.2.1` field-for-field. |
| Outbox publishers map internal → external | MET | [BasketCheckoutInitiatedOutboxPublisherDomainEventHandler.cs](services/Basket/Basket.Application/Baskets/Checkout/BasketCheckoutInitiatedOutboxPublisherDomainEventHandler.cs) + [BasketCheckoutInitiatedMapper.cs](services/Basket/Basket.Application/Baskets/Checkout/BasketCheckoutInitiatedMapper.cs). |
| DbContext + naming conventions | MET | [BasketDbContext.cs:34-57](services/Basket/Basket.Infrastructure/Persistence/Database/BasketDbContext.cs) — snake_case + exception-processor + `basket` default schema. |
| Messaging DI: outbox, inbox | MET | [MessagingDependencyInjection.cs:27-53](services/Basket/Basket.Infrastructure/Common/MessagingDependencyInjection.cs) wires outbox + inbox against `BasketDbContext`. |
| docker-compose delta: topics + outbox-relay container | MET | `basket.sessions` topic on `kafka-init` ([docker-compose.yaml:281](docker-compose.yaml)) + [outbox-relay-basket](docker-compose.yaml:388-418) service. |
| 4 test projects compile + pass | MET | Unit 143/143 ✅, Architecture 36/36 ✅, Integration 3/3 ✅, Functional 23/23 ✅ — total 205/205 against an idle Docker daemon. See **Dimension 7** for verbatim output (Docker resource contention was observable only under concurrent slice execution on this Windows host). |
| All HTTP routes under `/api/v1/{bc}/...` | MET | [BasketGroup.cs:9](services/Basket/Basket.Api/Endpoints/Baskets/BasketGroup.cs) + FastEndpoints versioning `Prefix = "v"`, `DefaultVersion = 1`, `RoutePrefix = "api"` ([FastEndpointsDependencyInjection.cs:38-41](services/Basket/Basket.Api/Common/FastEndpointsDependencyInjection.cs)). |
| All timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain (ADR-0015) | **PARTIALLY MET** | Arch test [TimePolicyTests](test/Basket.ArchitectureTests/Domain/TimePolicyTests.cs) passes (no IL-level call in `Basket.Domain`), but **every domain event raise omits `OccurredOnUtc = utcNow`** ([Basket.cs:116, 215-221, 241-245, 276-282, 346-350, 368, 414-422](services/Basket/Basket.Domain/Baskets/Basket.cs)) and falls back to the platform base default `DateTimeOffset.UtcNow` ([Platform.SharedKernel/Base/DomainEvents/DomainEvent.cs:8](platform/Platform.SharedKernel/Base/DomainEvents/DomainEvent.cs)). Other BCs (Catalog, Ordering) set this explicitly — Basket is the outlier. **See CRITICAL #4 in Dimension 8.** |
| Correlation-id propagation (ADR-0008) | MET (component-wired, not E2E asserted) | `app.UseCorrelationId()` ([Program.cs:48](services/Basket/Basket.Api/Program.cs)); ACL adapter has `.AddCorrelationIdPropagation()` ([CatalogClientDependencyInjection.cs:57](services/Basket/Basket.Infrastructure/ExternalServices/Catalog/CatalogClientDependencyInjection.cs)); outbox relay copies into Kafka header (platform). No single end-to-end test pins HTTP → Redis → outbox → Kafka header — documented as M9 carry-forward. |
| `dotnet build` / `restore --locked-mode` / `format whitespace` / `format style` green | MET | All exit 0 — see **Dimension 7**. |
| `docker compose --profile full up -d` starts + healthcheck passes | **PARTIALLY MET** | Not re-executed in this closeout pass; M8 summary documented the smoke succeeded for Basket-relevant services. `basket-api` container is NOT in `docker-compose.yaml` (M8 noted this; deferred to DEVOPS wave). |
| Docs self-corrected if needed | MET | M9 reconciled handler-vs-`use-cases.md § 2.1.{2,4,5}` divergence ([basket-m9.md § Fixed](docs/implementation-prompts/session-summaries/basket-m9.md)). |
| Peer-review chain executed | MET | M8/M9 ran Opus + Haiku reviewers per `_shared.md § 11`; this closeout is the final independent pass. |

### `basket.md <dod>` — BC-specific DoD

| Item | Status | Evidence |
|---|---|---|
| `BasketDbContext` no `DbSet<Basket>` (arch test) | MET | [BasketDbContext.cs](services/Basket/Basket.Infrastructure/Persistence/Database/BasketDbContext.cs) + [BasketSpecificRulesTests.BasketDbContext_HasNo_DbSetOfBasket](test/Basket.ArchitectureTests/BasketSpecific/BasketSpecificRulesTests.cs:30-48) — green. |
| `basket:{userId}` key with 30-day sliding TTL on `redis-basket` | MET | [RedisBasketRepository.SaveAsync:110-116](services/Basket/Basket.Infrastructure/Persistence/RedisBasketRepository.cs) sets `Duration = TtlDays (default 30 d)` + AOF + `noeviction` in [docker-compose.yaml:52](docker-compose.yaml). |
| `CheckoutBasketCommand` returns 400 if `Idempotency-Key` missing; replay returns cached | MET (with caveat) | In-handler check at [CheckoutBasketEndpoint:57-64](services/Basket/Basket.Api/Endpoints/Baskets/Checkout/CheckoutBasketEndpoint.cs); functional test `WhenIdempotencyKeyMissing_Returns400` pins it. Replay assertion is best-effort per the test's own comment — see `CheckoutBasketTests.WhenSameIdempotencyKeyReplayed_ReturnsAccepted_OrCachedResponse`. |
| Post-checkout Redis delete AFTER SQL outbox commit; delete failure does NOT roll back | **PARTIALLY MET** | Ordering correct in [CheckoutBasketCommandHandler.cs:101-112](services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs) — but only handles `Result.Fail`; **exceptions from `DeleteAsync` propagate uncaught and produce a 500 to the caller after the outbox row has already committed**. See CRITICAL #3 in Dimension 8. |
| `IProductCatalogQueryPort` + `ProductCatalogHttpAdapter` with service-auth | MET | [ProductCatalogHttpAdapter.cs](services/Basket/Basket.Infrastructure/ExternalServices/Catalog/ProductCatalogHttpAdapter.cs) + [CatalogClientDependencyInjection.cs:51-58](services/Basket/Basket.Infrastructure/ExternalServices/Catalog/CatalogClientDependencyInjection.cs) — typed HttpClient + `.AddCorrelationIdPropagation()` + `.AddServiceAuth(scope)`. |
| Integration tests cover both example-mapping sessions + checkout idempotency | **PARTIALLY MET** | Application-layer rules covered; infrastructure rules (AOF persistence on Redis restart, LRU eviction) pinned by compose config rather than by automated tests. Documented as accepted in M9 § "Example-mapping coverage disposition". |
| `BasketErrors` mirrors `error-taxonomy.md § 3.1` | MET (with carry-forward) | [BasketErrors.cs](services/Basket/Basket.Domain/Baskets/Errors/BasketErrors.cs) implements every factory; `ItemNotFound(productId)` was added but `error-taxonomy.md § 3.1` sketch still misses it (logged as M9 carry-forward, out of M9's authorized writable scope). |
| All HTTP routes under `/api/v1/basket/...` | MET | Verified in functional tests (when they run). |
| Correlation-id roundtrips HTTP → outbox → Kafka header | MET (no E2E assertion) | Component-wired per ADR-0008; no single integration test pins the full path. |
| All applicable ADRs enforced | **PARTIALLY MET** | ADR-0015 — see CRITICAL #4 (wall-clock leaks via the platform base). Others enforced. |
| Peer-review chain; HIGH findings fixed | MET (per M8/M9 records) | Reviewer chain ran; M8 + M9 closed all HIGH findings they surfaced — this closeout surfaces new ones. |

### Contract walk (LOCKED items)

| Locked item | Status |
|---|---|
| Event `BasketCheckoutInitiatedEvent` under `Basket.Sessions` namespace | MET — [BasketCheckoutInitiatedEvent.avsc:2-4](platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc) |
| Topic `basket.sessions`, 3 partitions, 30-day retention | MET — [docker-compose.yaml:281](docker-compose.yaml) + `Topics.BasketSessions = "basket.sessions"` in appsettings |
| Includes nested `CheckoutAddress` + `PaymentMethodId` | MET — schema lines 105-114 + 124-130 |
| FORWARD_TRANSITIVE compatibility (ADR-0007) | NOT VERIFIED IN-REPO — relies on schema-registry server config; no `.avsc`-level annotation. |
| 6 commands + 1 query under `/api/v1/basket/...` | MET |
| `CheckoutBasketCommand` requires `Idempotency-Key` header → 400 if missing | MET — in-handler check pinned by test |
| Addresses + payment-method as pass-through couriers (ADR-0005) | MET — courier semantics throughout `BasketCheckedOutDomainEvent` → outbox mapper |
| File ownership (services/Basket only) | MET — git log on branch shows zero non-Basket / non-docs changes by the BC owner |

### Invariants spot-check (5 of 9 from basket.md § 10)

| # | Invariant | Enforcement point | Verified |
|---|---|---|---|
| 1 | `UserId != Guid.Empty` | [Basket.cs:104](services/Basket/Basket.Domain/Baskets/Basket.cs) `Throw.If` + [Basket.cs:139](services/Basket/Basket.Domain/Baskets/Basket.cs) in `Rehydrate` | ✅ test `Create_WhenEmptyUserId_ThrowsDataIntegrityException` |
| 3 | Items.Count ≤ 50 | [Basket.cs:183-186](services/Basket/Basket.Domain/Baskets/Basket.cs) | ✅ test `AddItem_AtMaxItemsMinusOne_Succeeds_PinningBoundary` + `AddItem_WhenMaxItemsReached_FailsMaxItemsReached` |
| 5 | Uniform currency | [Basket.cs:188-191, 200-203](services/Basket/Basket.Domain/Baskets/Basket.cs) | ✅ test (`CurrencyMismatch` path) |
| 6 | Snapshot price immutable until refresh | [Basket.cs:205-212](services/Basket/Basket.Domain/Baskets/Basket.cs) — preserves `existing.Snapshot` on quantity bump | ✅ test `AddItem_QuantityBumpWithDifferentSnapshotPrice_PreservesFrozenPriceAndBroadcastsIt` |
| 7 | Empty basket cannot be checked out | [Basket.cs:407-410](services/Basket/Basket.Domain/Baskets/Basket.cs) + [CheckoutBasketCommandHandler.cs:77-80](services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs) | ✅ test `WhenBasketIsEmpty_Returns409` (functional) + handler unit tests |

---

## Dimension 2 — Architecture

**PASS** for layer-boundary enforcement, with one observation.

| Rule | Test | Status |
|---|---|---|
| `Domain ⟂ Application/Infrastructure/Presentation` | [CleanArchitectureLayerTests](test/Basket.ArchitectureTests/CleanArchitecture/CleanArchitectureLayerTests.cs) — 6 tests | ✅ |
| Cross-BC isolation | [CrossBCReferenceTests](test/Basket.ArchitectureTests/CrossBC/CrossBCReferenceTests.cs) — `Basket.Domain` and `Basket.Application` cannot reference any other BC's Domain/Application | ✅ |
| `BasketDbContext` no `DbSet<Basket>` | [BasketSpecificRulesTests.BasketDbContext_HasNo_DbSetOfBasket](test/Basket.ArchitectureTests/BasketSpecific/BasketSpecificRulesTests.cs:30) — IL-level scan via Mono.Cecil | ✅ — non-trivial assertion |
| `RedisBasketRepository` no EF Core | [BasketSpecificRulesTests.RedisBasketRepository_HasNo_EfCoreDependency](test/Basket.ArchitectureTests/BasketSpecific/BasketSpecificRulesTests.cs:56) | ✅ |
| Only `ProductCatalogHttpAdapter` references Catalog HTTP DTOs | [BasketSpecificRulesTests.OnlyProductCatalogHttpAdapter_References_CatalogHttpDtos](test/Basket.ArchitectureTests/BasketSpecific/BasketSpecificRulesTests.cs:82) | ✅ |
| ADR-0015 — no `DateTime.UtcNow` in domain (IL scan) | [TimePolicyTests](test/Basket.ArchitectureTests/Domain/TimePolicyTests.cs) | ✅ — but **the test's scope is `Basket.Domain` only; the wall-clock read leaks through `Platform.SharedKernel.DomainEvent.OccurredOnUtc` default**. See CRITICAL #4. |
| Aggregate sealed / immutable externally / private ctor | [AggregateRootTests](test/Basket.ArchitectureTests/Domain/AggregateRootTests.cs) | ✅ |
| Domain events sealed + immutable + naming | [DomainEventTests](test/Basket.ArchitectureTests/Domain/DomainEventTests.cs) | ✅ |

Architecture tests are non-trivial (Mono.Cecil IL inspection in two places) and pass on a clean run (36/36). The single gap is that `TimePolicyTests` cannot catch the indirect wall-clock leak through the platform base.

---

## Dimension 3 — Design (DDD)

**PASS** with one design-level observation (HIGH-1 in Dimension 8).

| Aspect | Verdict |
|---|---|
| Single aggregate boundary | ✅ `Basket` is the sole aggregate; items + snapshot are VOs (Vernon rule 2). |
| Invariants enforced inside the aggregate | ✅ `AddItem`/`ChangeQuantity`/`RefreshPrices`/`Checkout` raise `Result.Fail` with typed errors; handlers do not duplicate invariant checks. |
| Value objects immutable + structural equality | ✅ `BasketItem`, `ProductSnapshot`, `BasketTotal`, `BasketSnapshot` — all sealed records with private-init setters. `BasketSnapshot` overrides `Equals`/`GetHashCode` to compare `ImmutableArray<BasketItem>` element-wise (correct). |
| Domain events sealed records, dispatched in-process only | ✅ Seven events under `Baskets/Events/`. Only `BasketCheckedOutDomainEvent` triggers an external Avro publication. |
| Internal vs external event split | ✅ One external (`BasketCheckoutInitiatedEvent`) per `events-catalog.md § 5.2`. |
| SmartEnums absent because no state machine | ✅ Deliberate per `basket.md § 4`. |
| Factories returning `Result<T>` for validation | ✅ `BasketItem.Create` returns `Result<BasketItem>`; `Basket.Create` throws on impossible inputs (Guid.Empty) and is paired with handler-side validation. |
| Constructors private | ✅ `Basket` and all VOs. |
| Per Touch() observation | **HIGH-1** — `Checkout()` calls `Touch()` (increments Version, refreshes LastModified) but the basket is never persisted (it is deleted after outbox commit). Touch in this code path is inert — and misleading if a future maintainer reads `Version` as a CAS token for checkout. |

---

## Dimension 4 — Testing

**MOSTLY PASS** with one structural gap.

| Aspect | Status |
|---|---|
| Pyramid balance (heavy unit, moderate integration, narrow functional) | ✅ 143 unit / 36 architecture / 3 integration / 23 functional. |
| Names express behaviour | ✅ Strong throughout — e.g., `AddItem_QuantityBumpWithDifferentSnapshotPrice_PreservesFrozenPriceAndBroadcastsIt`. |
| Each command has handler + validator test | ✅ Per-command handler tests under `Baskets/Application/{Cmd}/` + table-driven [ValidatorsTests.cs](test/Basket.UnitTests/Baskets/Application/ValidatorsTests.cs). |
| Each external event has an outbox-publisher test | ✅ [BasketCheckoutInitiatedOutboxPublisherDomainEventHandlerTests](test/Basket.UnitTests/Baskets/Application/Checkout/BasketCheckoutInitiatedOutboxPublisherDomainEventHandlerTests.cs) + [BasketCheckoutInitiatedMapperTests](test/Basket.UnitTests/Baskets/Application/Checkout/BasketCheckoutInitiatedMapperTests.cs). |
| Each domain invariant has a failing-on-removal unit test | ✅ Every invariant in `basket.md § 10` mapped to a [BasketTests.cs](test/Basket.UnitTests/Baskets/Aggregates/BasketTests.cs) case. |
| Testcontainers used for integration; no Docker-less mocks where real wiring matters | ✅ Postgres Testcontainer in [IntegrationTestFixture.cs:38-43](test/Basket.IntegrationTests/Common/IntegrationTestFixture.cs). One shared Redis container for functional fixture (M8-documented compromise; arch test pins ADR-0016 at compile time). |
| `TestContext.Current.CancellationToken` used (xUnit1051) | ✅ — verified by sampling 10+ tests. |
| **No `RedisBasketRepository` integration test against real Redis** | **GAP** — CAS retry, lock contention, parallel-AddItem concurrency invariant ([basket.md `<dod>` line 119]) — NOT exercised. M8/M9 documented as carry-forward; remains a structural gap at closeout. |
| No flaky tests detected (clean runs) | ✅ |
| No silent always-true asserts on success path | ✅ — outbox-row asserts include topic, key, type, payload; mapper test asserts every Avro field. |

---

## Dimension 5 — Event-driven best practices

**MOSTLY PASS** with one HIGH defect.

| Aspect | Status |
|---|---|
| Outbox is the only path producing external events | ✅ No direct `IProducer` calls anywhere in Basket. |
| Outbox row written in same SQL transaction as the state change | ✅ — for Basket, the "state" is in Redis (not SQL); the outbox row is the publication-intent record. The publisher adds the message in [BasketCheckoutInitiatedOutboxPublisherDomainEventHandler.cs:54-57](services/Basket/Basket.Application/Baskets/Checkout/BasketCheckoutInitiatedOutboxPublisherDomainEventHandler.cs) and the handler commits via `_outbox.SaveChangesAsync(ct)` at [CheckoutBasketCommandHandler.cs:101](services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs). EF's implicit single-SaveChanges transaction is enough TODAY (only one row written) but **HIGH-2**: the handler's own XML doc (lines 24-31) promises an `EnsureTransactionAsync` wrap once M6 lands. M6 landed two milestones ago. The wrap was never added — the first future fan-out write (e.g., inbox dedup, audit row) silently breaks the outbox-state guarantee. |
| Inbox dedup for every Kafka consumer | N/A — Basket has zero Kafka consumers in v1 (per [use-cases.md § 2.3](docs/bc-design/use-cases.md)). `AddInbox<BasketDbContext>()` is wired prophylactically. |
| Avro schema FORWARD_TRANSITIVE | NOT VERIFIED IN-REPO (relies on schema-registry server config — same posture as every other BC). |
| Correlation-id flows HTTP → handler → outbox → Kafka header | ✅ (component-wired; no E2E test). |
| Idempotency-Key wired per ADR-0013 | ✅ `/items` (double-click guard via `.Idempotency()`); `/checkout` (REQUIRED — in-handler header check returns 400 if missing). |
| No internal `*DomainEvent` leaks to Kafka | ✅ Only `BasketCheckedOutDomainEvent` is mapped to an external Avro event; the other six in-process events are observability-only. |
| No cross-BC consumption of internal events | ✅ Basket consumes nothing in v1. |

---

## Dimension 6 — .NET / C# best practices

**PASS** with cosmetic LOWs.

| Aspect | Status |
|---|---|
| Async-all-the-way; no `.Result` / `.Wait()` | ✅ — verified by sampling all handlers and adapters. |
| `CancellationToken` flowed everywhere | ✅ — the lock-release `ScriptEvaluateAsync` deliberately drops `ct` to allow best-effort release on cancel; documented behavior. |
| `IDisposable` honoured | ✅ — fixtures dispose Postgres + service providers. |
| `TimeProvider` injected (no `DateTime.UtcNow` in domain) | ✅ at the call-site level (every domain method takes `utcNow`), ⚠️ at the event-construction level (CRITICAL #4). |
| `DateTimeOffset` throughout | ✅ — `DateTime` would fail [TimePolicyTests.Domain_DoesNotReference_BareDateTime](test/Basket.ArchitectureTests/Domain/TimePolicyTests.cs). |
| No magic strings for connection keys / topics / error codes | ✅ — [ConnectionStringNames](services/Basket/Basket.Infrastructure/Common/Config/ConnectionStringsOptions.cs) constants + bound [TopicsOptions](services/Basket/Basket.Application/Common/Messaging/TopicsOptions.cs) + `BasketErrors` factories. (LOW: the file is named `ConnectionStringsOptions.cs` but contains a constants-only class `ConnectionStringNames` — filename violates one-type-per-file.) |
| Logging at correct levels; no PII | ✅ — only `productId` (Guid) and `UserId` (Guid) are logged. No claim values, addresses, or amounts. |
| Nullable reference types respected; no unjustified `!` | ✅ — `!` appears only in `Items.ElementAt(0)` style accesses guarded by invariants. |
| LOW — `InfrastructureDependencyInjection.cs:22-29` documents a deferred rename of `AddBasketPersistence` → `AddBasketRedisPersistence` (Redis vs SQL `AddDatabase` confusion). The XML doc explicitly says "deferred to keep M6 within its boundary"; closeout was the right time to actually do it. |

---

## Dimension 7 — CI gates + test slices (verbatim)

```
$ dotnet build -m
... (NU1903 vulnerability warnings on System.Security.Cryptography.Xml — pre-existing
across Weather.Infrastructure, Ordering.Infrastructure, Weather.UnitTests,
Weather.IntegrationTests, Weather.FunctionalTests, Payments.IntegrationTests,
Payments.FunctionalTests, Ordering.IntegrationTests, Platform.OutboxRelay.Benchmark,
Invoicing.FunctionalTests — none in Basket).
... build of Basket.Domain / Basket.Application / Basket.Infrastructure / Basket.Api
... succeeded.
Exit: 0  (after sequential rerun — initial parallel run produced two file-lock
errors on Inventory.API.pdb and Weather.Infrastructure.dll caused by other test-host
processes that this closeout had started in parallel; rerunning in isolation passed.)
```

```
$ dotnet restore --locked-mode
... All projects restored. 0 errors.
NU1903 vulnerability warnings unchanged (transitive, no Basket project affected).
Exit: 0
```

```
$ dotnet format whitespace --no-restore --verify-no-changes
... 0 violations.
Exit: 0
```

```
$ dotnet format style --no-restore --verify-no-changes
... 0 violations.
Exit: 0
```

```
$ dotnet test test/Basket.UnitTests/
Úspěšné!    - Neúspěšné:     0, Úspěšné:   143, Přeskočeno:     0, Celkem:   143, Doba trvání: 14 s
Exit: 0
```

```
$ dotnet test test/Basket.ArchitectureTests/
Úspěšné!    - Neúspěšné:     0, Úspěšné:    36, Přeskočeno:     0, Celkem:    36, Doba trvání: 26 s
Exit: 0
```

```
$ dotnet test test/Basket.IntegrationTests/   (isolated rerun against calm Docker)
Úspěšné!    - Neúspěšné:     0, Úspěšné:     3, Přeskočeno:     0, Celkem:     3, Doba trvání: 23 s
Exit: 0

Earlier parallel-execution attempts in this closeout pass produced 1 fixture-init
failure (Docker.DotNet BadGateway "timed out dialing Hyper-V socket" from
Docker Desktop on this Windows host under concurrent Testcontainers load).
Confirmed environmental, not a Basket defect — passes 3/3 when integration
slice is the only Docker consumer.
```

```
$ dotnet test test/Basket.FunctionalTests/   (isolated rerun against calm Docker)
Úspěšné!    - Neúspěšné:     0, Úspěšné:    23, Přeskočeno:     0, Celkem:    23, Doba trvání: 6 s
Exit: 0

Earlier parallel-execution attempts produced 23/23 fixture-init failures at
ApiTestFixture.PreSetupAsync for the same Docker-resource-contention reason.
Resolved by running the slice on its own.
```

**Dimension 7 status:** all four CI gates green; all four test slices green. **Total: 143 + 36 + 3 + 23 = 205 / 205 tests pass.** The Docker-dependent slices are sensitive to concurrent Testcontainers fixtures on this Windows host (Hyper-V backend contention) — this is an environmental observation for CI tuning, not a Basket-side defect. Recommendation for the future: serialise `Basket.IntegrationTests` and `Basket.FunctionalTests` invocations on dev machines, or scale Docker Desktop's CPU / memory reservation.

---

## Dimension 8 — Code review (parallel multi-reviewer)

Dispatched `Agent(subagent_type="feature-dev:code-reviewer", model="opus")` on `services/Basket/**` + Avro contracts. Reviewer surfaced **4 CRITICAL + 7 HIGH + 4 MEDIUM + 2 LOW**. Each CRITICAL was independently verified against the source by this closeout reviewer before inclusion below.

### CRITICAL

#### C-1 — Parallel checkouts produce duplicate sagas (double-billing risk)

- **File:** [services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs:60-115](services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs)
- **Description:** `CheckoutBasketCommandHandler.HandleAsync` does NOT use `BasketConcurrencyRetry`, does NOT call `IBasketRepository.SaveAsync`, and does NOT acquire the per-user Redis lock. Two parallel POSTs to `/checkout` for the same user with **different** Idempotency-Keys (or — the more realistic failure mode — a client retry that regenerates the key on transient network failure) both load the same basket, each raises `BasketCheckedOutDomainEvent` with its own `CorrelationId`, each writes a separate outbox row, and the relay publishes **two** `BasketCheckoutInitiatedEvent` records on `basket.sessions` for the same `UserId`. Downstream Checkout Saga starts twice → potential double stock reservation, double payment authorisation, double order. The `.Idempotency()` filter dedupes only identical keys; it is not a basket-state lock.
- **Evidence:** No `expectedVersion` capture, no `SaveAsync(basket, ...)`, no `BasketConcurrencyRetry.ExecuteAsync` wrap. Compare with every other mutating handler (e.g., [AddItemToBasketCommandHandler.cs:41-71](services/Basket/Basket.Application/Baskets/AddItem/AddItemToBasketCommandHandler.cs)).
- **Impact:** Production-grade — converts one user click into double-charge downstream. Not bounded by `.Idempotency()` because the failure mode is a client that regenerates the key.
- **Recommendation:** Wrap the load → dispatch → SaveChangesAsync → DeleteAsync sequence in `BasketConcurrencyRetry` and call `IBasketRepository.SaveAsync(basket, expectedVersion, ct)` between the dispatch and the outbox commit. The loser of two parallel checkouts will receive `BasketConcurrencyError` and (after one retry) surface a 409. Combined with the Touch() inside Checkout (HIGH-1) — Checkout already increments Version; keep that and surface it as the CAS token.

#### C-2 — `BasketConcurrencyError` surfaces as 500 instead of 409

- **Files:** [services/Basket/Basket.Api/Common/Extensions/ResultsExtensions.cs:64-108](services/Basket/Basket.Api/Common/Extensions/ResultsExtensions.cs) + [services/Basket/Basket.Domain/Baskets/Errors/BasketConcurrencyError.cs:11](services/Basket/Basket.Domain/Baskets/Errors/BasketConcurrencyError.cs)
- **Description:** The error-to-status switch handles `ValidationError` / `NotFoundError` / `ConflictError` / `ForbiddenError` / `DomainError`. `BasketConcurrencyError` implements `IError` directly and matches **none** of those cases. The loop completes with `failures.Count == 0`, the `if (!hasDomainError)` branch fires (line 99), and **the response is 500 with the generic message "An unexpected error occurred"**.
- **Evidence:** `basket.md § 5.4`, `error-taxonomy.md § 3.1` (`Basket.Concurrency` → 409), `CheckoutBasketCommandHandler`'s own XML doc, and `BasketConcurrencyError.cs`'s own remark all promise 409. Concurrency-retry-exhaustion is a normal outcome of optimistic concurrency under contention, not an "internal error".
- **Impact:** Every concurrency-retry exhaustion in production fires a 500 alert and obscures the real symptom. APM marks `ActivityStatusCode.Error`. Clients that retry on 5xx (not on 409) will worsen contention.
- **Recommendation:** Add a switch case for `BasketConcurrencyError` (and any future `IError` subtypes) that adds a `ValidationFailure("Basket.Concurrency", bce.Message)` and flips `hasConflict = true`. Status falls through to 409.

#### C-3 — `RedisBasketRepository.DeleteAsync` exception escapes after outbox commit

- **Files:** [services/Basket/Basket.Infrastructure/Persistence/RedisBasketRepository.cs:126-140](services/Basket/Basket.Infrastructure/Persistence/RedisBasketRepository.cs) + [services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs:106-112](services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs)
- **Description:** `DeleteAsync` does NOT wrap `database.KeyDeleteAsync` or `_cache.RemoveAsync` in try/catch. On Redis transient failure the call throws `RedisConnectionException` / `RedisTimeoutException`. The exception propagates through the handler, which only checks `if (deleteResult.IsFailed)` (lines 107-112). FastEndpoints' exception handler returns 500 — **after the outbox row has already been committed by `_outbox.SaveChangesAsync(ct)` on line 101**.
- **Evidence:** The handler XML doc (lines 33-35) explicitly says "A delete failure is logged but NOT propagated — the outbox is the source of truth". The current implementation honours only the `Result.Fail` path; the throw path violates this contract.
- **Impact:** A user sees "checkout failed" (5xx response) while the saga is already running and will charge them. The mismatch is exactly what the outbox pattern is supposed to eliminate.
- **Recommendation:** Catch all exceptions inside `DeleteAsync` and return `Result.Fail(...)` matching the declared signature. The handler's existing `if (deleteResult.IsFailed) → LogWarning + continue` branch then runs and the caller sees the documented 202.

#### C-4 — Domain events ignore `TimeProvider`; wall-clock leaks into `BasketCheckoutInitiatedEvent.InitiatedAtUtc`

- **Files:** every `AddDomainEvent(new ...DomainEvent { ... })` site in [services/Basket/Basket.Domain/Baskets/Basket.cs](services/Basket/Basket.Domain/Baskets/Basket.cs) — lines 116, 215-221, 241-245, 276-282, 346-350, 368, 414-422. Combined with [platform/Platform.SharedKernel/Base/DomainEvents/DomainEvent.cs:8](platform/Platform.SharedKernel/Base/DomainEvents/DomainEvent.cs): `public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;`.
- **Description:** Every other BC explicitly sets `OccurredOnUtc = utcNow` on every event raise (8 sites in Ordering.Order.cs, 7+ in Catalog.Product.cs, 3 in Catalog.Category.cs, all in Inventory + Payments + Invoicing). **Basket has ZERO `OccurredOnUtc = ` settings.** All 7 of Basket's domain events fall back to the platform base default which reads wall-clock at object-init time. `BasketCheckoutInitiatedMapper:45` then projects this onto the Avro `InitiatedAtUtc` field — wall-clock leaks into the saga-initiating Kafka event.
- **Evidence:** Grep `OccurredOnUtc` in `services/Basket/Basket.Domain` returns zero matches; the same grep in Ordering / Catalog returns dozens.
- **Why arch test misses it:** [TimePolicyTests](test/Basket.ArchitectureTests/Domain/TimePolicyTests.cs) scans only the Basket.Domain assembly. The IL instruction `call DateTimeOffset::get_UtcNow()` is compiled into the **platform** base class's auto-init, not into Basket.Domain.
- **Impact:** (1) ADR-0015 violation in spirit — domain reads wall-clock indirectly. (2) Tests that inject a `FakeTimeProvider` and assert on `InitiatedAtUtc` will see non-deterministic values. (3) Real-world timing for saga correlation is wall-clock, not the request-scoped `utcNow` already plumbed through `Basket.Checkout(correlationId, ..., utcNow)`.
- **Recommendation:** On every `AddDomainEvent(new XDomainEvent { ... })` site in `Basket.cs`, add `OccurredOnUtc = utcNow,`. Use the same `utcNow` parameter already passed to each mutating method. Optional follow-up: add a Basket-Architecture test that asserts every emitted domain event in the BC sets `OccurredOnUtc` explicitly (custom rule scanning IL for `OccurredOnUtc = ...` initialiser presence on every event init).

### HIGH

| # | File:line | Finding | Recommendation |
|---|---|---|---|
| H-1 | [Basket.cs:413](services/Basket/Basket.Domain/Baskets/Basket.cs) | `Checkout()` calls `Touch()` (Version++, LastModified=utcNow) but the basket is never persisted on this path. The Version XML doc (line 50-54) promises monotonic increments on successful mutations — `Checkout` increment is inert today and will mislead anyone implementing the C-1 fix who expects `Version` to be a usable CAS token. | Either remove `Touch()` from `Checkout()` (the basket is terminal — Version meaningless after Redis DEL) OR persist via SaveAsync as part of the C-1 fix. |
| H-2 | [CheckoutBasketCommandHandler.cs:24-31, 101](services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs) | The handler's own XML doc promises a `_outbox.Database.EnsureTransactionAsync(...)` wrap "once the concrete BasketDbContext (and any additional SQL writes inside the fan-out) come online". M6 landed two milestones ago. The wrap was never added. EF's implicit single-`SaveChanges` works today because there is exactly one row; the next future SQL write inside the fan-out silently breaks the outbox-state guarantee. | Wrap lines 96-101 in `EnsureTransactionAsync`. Matches Weather + Payments + Invoicing patterns. |
| H-3 | [BasketCheckoutInitiatedOutboxPublisherDomainEventHandler.cs:59-63](services/Basket/Basket.Application/Baskets/Checkout/BasketCheckoutInitiatedOutboxPublisherDomainEventHandler.cs) | The handler logs `LogInformation("Added BasketCheckoutInitiatedEvent to outbox...")` from inside `IDomainEventHandler.Handle`. "Added" here means "queued in the EF change tracker"; if the subsequent `SaveChangesAsync` throws, the row never lands. Splunk-style dashboards that count "checkouts initiated" via this log line over-count. | Either move the LogInformation to the command handler AFTER `SaveChangesAsync` succeeds, or downgrade to `LogDebug` with verb "queued"; emit a `LogInformation("Published")` from the handler post-commit. |
| H-4 | [BasketStateMapper.ToDomain:53](services/Basket/Basket.Infrastructure/Persistence/BasketStateMapper.cs) | `CurrencyCode.FromName(name, ignoreCase: false)` throws on unknown currency. `RedisBasketRepository.GetByUserIdAsync` does not catch. A single Redis key with a since-removed currency throws an unhandled exception out of every Get / mutation for that user. Contract of `IBasketRepository.GetByUserIdAsync` (line 31-35) says "Transport / serialization failures surface as Result.Fail". | Catch in the mapper (or in the repository's TryGet); return `Result.Fail` with an infrastructure error code (e.g., `Basket.Corruption`). Map to 503 in `ResultsExtensions`. |
| H-5 | [CheckoutAddressValidator.cs:19-28](services/Basket/Basket.Application/Baskets/Common/Validators/CheckoutAddressValidator.cs) + [Address](platform/Platform.SharedKernel/ValueObjects/Address.cs) | Length ceilings (200/100/20) and country-code-uppercase semantics are duplicated literally in the validator and in `Address.Create`. Drift is one PR away — the only thing keeping them in sync is the M4 reviewer comment in the validator. | Pin the length constants on `Address` and reference them from both layers. |
| H-6 | [ProductCatalogHttpAdapter.GetManyAsync:143-146](services/Basket/Basket.Infrastructure/ExternalServices/Catalog/ProductCatalogHttpAdapter.cs) | Worst-case URL with 50 items + 36-char GUIDs is ~1900 chars; safe today but a future `MaxItems` bump or a defect that lets more items through silently exceeds reasonable URL caps. | Either POST to `/by-ids` with a JSON body, or chunk in batches. |
| H-7 | [RedisBasketRepository.cs:122, 179](services/Basket/Basket.Infrastructure/Persistence/RedisBasketRepository.cs) | The lock TTL (5s default) and the lock-retry budget (LockMaxRetries × LockRetryDelayMs = 20 × 50ms = 1s default) are misaligned — a holder that takes 1.5s causes a contender to spuriously fail with `BasketConcurrencyError` while the holder still has 3.5s of valid lock. Not unsafe (correctness preserved), but produces spurious concurrency errors under load. | Align retry budget with lock TTL (e.g., LockMaxRetries = lockTtl / LockRetryDelayMs). |

### MEDIUM

| # | File | Finding |
|---|---|---|
| M-1 | [BasketCheckoutInitiatedMapper.cs:33](services/Basket/Basket.Application/Baskets/Checkout/BasketCheckoutInitiatedMapper.cs) | Uses `snapshot.Items[0].Snapshot.Price.Currency.Name` for the basket-wide currency. Invariant 5 guarantees uniform currency, so this is correct today — but `snapshot.Total.Amount.Currency.Name` is the authoritative basket-wide value and is more self-documenting. |
| M-2 | [CorsDependencyInjection.cs:42-45](services/Basket/Basket.Api/Common/CorsDependencyInjection.cs) | `AllowCredentials=true` with `localhost` origins is fine for dev. No startup guard forbids prod misconfig (the existing guard only catches the wildcard-with-credentials combo). |
| M-3 | [RedisBasketRepository.cs:179-183](services/Basket/Basket.Infrastructure/Persistence/RedisBasketRepository.cs) | `ScriptEvaluateAsync` for lock release drops `ct`. Intentional (best-effort release; TTL reclaims on cancel) but warrants a one-line comment. |

### LOW

| # | File | Finding |
|---|---|---|
| L-1 | [ConnectionStringsOptions.cs](services/Basket/Basket.Infrastructure/Common/Config/ConnectionStringsOptions.cs) | Filename does not match the class inside (`ConnectionStringNames`). One-type-per-file convention. |
| L-2 | [InfrastructureDependencyInjection.cs:22-29](services/Basket/Basket.Infrastructure/Common/InfrastructureDependencyInjection.cs) | XML doc promises a `AddBasketPersistence` → `AddBasketRedisPersistence` rename "deferred to keep M6 within its boundary". Closeout is the right time. |

---

## Verdict

**FAIL.**

Verdict thresholds:
- PASS — zero CRITICAL, zero unaccepted HIGH, all DoD MET, all gates green.
- CONDITIONAL-PASS — zero CRITICAL, ≤ N HIGH documented as accepted carry-forwards, all DoD MET or PARTIALLY MET with rationale.
- FAIL — any CRITICAL, OR any DoD NOT MET without acceptance, OR any test red, OR contract-locked seam drifted.

This closeout records **four CRITICAL defects** (C-1 parallel-checkout race, C-2 BasketConcurrencyError→500, C-3 DeleteAsync exception escape, C-4 wall-clock leak in OccurredOnUtc). Two are correctness defects that affect downstream saga safety (C-1, C-3), one is a contract drift between code and `error-taxonomy.md` (C-2), one is an ADR-0015 spirit-violation that leaks wall-clock into the saga-initiating Kafka event (C-4).

All four CI gates and all four test slices are verified green on this host (after isolating the Docker-dependent slices from each other to clear Docker Desktop resource contention). The FAIL verdict rests solely on the four CRITICAL findings — not on the test slices.

---

## Punch list (ordered by ship-blocker priority)

1. **[C-1]** Fix the parallel-checkout race in [CheckoutBasketCommandHandler.cs:60-115](services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs). Wrap in `BasketConcurrencyRetry`. Insert `IBasketRepository.SaveAsync(basket, expectedVersion, ct)` between domain-event dispatch and outbox commit. The loser of a race returns 409 — combined with C-2 fix this surfaces cleanly. Add an integration test that fires two parallel `CheckoutBasketCommand` calls for the same user with different CorrelationIds and asserts exactly one outbox row.
2. **[C-2]** Add `case BasketConcurrencyError bce:` to the switch in [ResultsExtensions.cs:65-95](services/Basket/Basket.Api/Common/Extensions/ResultsExtensions.cs). Flip `hasConflict = true`. Add `ValidationFailure("Basket.Concurrency", bce.Message)`. Backfill a functional test that drives two concurrent mutations to the same key and asserts 409.
3. **[C-3]** Wrap [RedisBasketRepository.DeleteAsync:126-140](services/Basket/Basket.Infrastructure/Persistence/RedisBasketRepository.cs) in try/catch on `RedisException` + `RedisTimeoutException` + `RedisConnectionException` and return `Result.Fail`. The handler's existing fail-path already logs+continues per the XML doc.
4. **[C-4]** On every `AddDomainEvent(new XDomainEvent { ... })` in [Basket.cs](services/Basket/Basket.Domain/Baskets/Basket.cs), add `OccurredOnUtc = utcNow,`. Then write an arch test (Mono.Cecil scan) that fails if any Basket.Domain event init does NOT explicitly set `OccurredOnUtc`. Apply to the other BCs in a follow-up sweep.
5. **[H-1]** Decide on `Checkout().Touch()` — remove or pair with the C-1 SaveAsync.
6. **[H-2]** Wrap [CheckoutBasketCommandHandler.cs:96-101](services/Basket/Basket.Application/Baskets/Checkout/CheckoutBasketCommandHandler.cs) in `_outbox.Database.EnsureTransactionAsync(...)` per the existing XML doc TODO.
7. **[H-3]** Move the "Added BasketCheckoutInitiatedEvent to outbox" LogInformation post-commit.
8. **[H-4]** Catch the `CurrencyCode.FromName` throw in `BasketStateMapper` and surface as `Result.Fail`.
9. **[H-5]** Pin Address length constants on `Platform.SharedKernel.ValueObjects.Address`; reference from validator + VO.
10. **[H-6]** Either POST the by-ids batch or chunk it. Add a regression test with N=100 ids.
11. **[H-7]** Align the lock-retry budget with the lock TTL.
12. **[M-1 / M-2 / M-3 / L-1 / L-2]** Address opportunistically.

---

## Boundary discipline

This closeout reviewer was authorized to write exactly one file — this report. No production code, no tests, no docs, no compose, no platform code were modified. All findings are recommendations to be implemented in a subsequent fix-up commit. The pre-existing uncommitted modifications visible in `git status` at session start (none — working tree clean per HEAD `9aad7c4`) were not touched.

---

*End of Basket BC closeout review.*
