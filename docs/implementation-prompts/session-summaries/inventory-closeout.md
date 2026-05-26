# Inventory BC — Final Close-Out Review

**HEAD:** `f49d358fb87f9559ab83f5abff2a638b76ea6cb9` ([`f49d358 feat(ordering): M9 docs self-corrections + Appendix B resolutions + session summary (Wave 1 / M9)`](../../../))
**Branch:** `aaqwdqwd`
**Inventory tip:** `a6ea4fd docs(inventory): M10 docker-compose smoke + final session summary (Wave 1 / M10)` (M10 closeout block embedded in `docs/implementation-prompts/inventory.md:602-696` rather than this directory — process variation, not incompleteness).
**Reviewed:** 2026-05-14
**Verdict:** **CONDITIONAL-PASS** — zero CRITICAL; **3 HIGH** findings, all defense-in-depth / coverage gaps (no contract or seam drift); **all 8 CI gates green (restore + build + format ws + format style + 66/66 unit + 33/33 arch + 42/42 integration + 15/15 functional)**; full Inventory `<dod>` MET; ≥ 1 HIGH not explicitly accepted in M10's wave-level follow-ups → must be either fixed or accepted in writing before this BC is considered fully closed.

---

## TL;DR

The Inventory BC is structurally and behaviourally sound. The aggregate, event store, projection + outbox transactional envelope, optimistic concurrency, correlation-id chain, inbox dedup on all three consumers, saga-command business-vs-bug failure classification, and ADR-0015 `TimeProvider` discipline are all implemented correctly. All four CI gates (`restore`/`build`/`format ws`/`format style`) and all four test slices (unit/arch/integration/functional) are green. The three HIGH-severity findings are quality/defense-in-depth gaps — none breaks the locked contract, drops events, or violates an architectural invariant. They warrant either a small follow-up commit or an explicit "accepted carry-forward" entry alongside the wave-level items already documented at `inventory.md:686-693`.

---

## Dimension 1 — Doc adherence + DoD audit

**Status:** PASS with three documentation drifts (MEDIUM / LOW) — every Inventory `<dod>` line and every `_shared.md § 12` line is MET against shipped code. Drifts are doc-only and do not affect runtime.

### Inventory `<dod>` walk (BC-specific items beyond `_shared.md § 12`)

Every checkbox from `inventory.md:790-811` was verified directly against the code; the M10 self-walk matrix at `inventory.md:614-633` is accurate. Spot citations:

| `<dod>` line | Status | Verified at |
|---|---|---|
| Event store PK `(StreamId, Version)` + append-only | ✅ | [`StockEventRowConfiguration.cs`](../../../services/Inventory/Inventory.Infrastructure/Persistence/Database/EntityConfigurations/StockEventRowConfiguration.cs); arch-test [`EventStoreAppendOnlyTests.cs:27-79`](../../../test/Inventory.ArchitectureTests/BoundedContext/EventStoreAppendOnlyTests.cs) — `PublicMethodsAreSubsetOfRule(["RehydrateAsync","AppendAsync"])` with explicit anchor `ContainSingle` to prevent vacuous pass on rename. |
| 2 projection tables + handlers upserting in same DbContext tx | ✅ | [`CurrentStockLevelsProjectionHandler.cs`](../../../services/Inventory/Inventory.Application/StockItems/CurrentStockLevelsProjectionHandler.cs) + [`ReservationLifecycleHandler.cs`](../../../services/Inventory/Inventory.Application/StockItems/ReservationLifecycleHandler.cs). Dispatched from [`EventStoreRepository.AppendAsync`](../../../services/Inventory/Inventory.Infrastructure/Persistence/EventStore/EventStoreRepository.cs#L180-L187) **before** the single `SaveChangesAsync` → same SQL transaction by construction. |
| 6 ES events with reducers; `StockItem.Fold` correct | ✅ | [`StockItem.cs:59-70`](../../../services/Inventory/Inventory.Domain/StockItems/StockItem.cs) — `Fold` is pure `(events) → state`; `Apply` switch covers all 6 event types with explicit `UnknownEventType` `DataIntegrityException` default. |
| Optimistic concurrency: `UniqueViolationException` → retry once → `ConcurrencyError` | ✅ | [`EventStoreRepository.cs:114-202`](../../../services/Inventory/Inventory.Infrastructure/Persistence/EventStore/EventStoreRepository.cs) — `MaxAttempts=2`; `ChangeTracker.Clear()` on retry; reports original `expectedVersion` (not the racing one). |
| Rehydration observability per ADR-0006 | ✅ | [`EventStoreRepository.cs:69-88`](../../../services/Inventory/Inventory.Infrastructure/Persistence/EventStore/EventStoreRepository.cs) emits `inventory.aggregate.rehydration.{duration,event_count}` via `Stopwatch.GetElapsedTime` (allocation-free); integration test [`EventStoreRepositoryRehydrationMetricsTests.cs`](../../../test/Inventory.IntegrationTests/Persistence/EventStoreRepositoryRehydrationMetricsTests.cs) — 1 k-event stream, p99 < 1 s assertion. |
| 5 external Avro + 3 saga commands + outbox publishers for all 5 | ✅ | [`platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/{Stock,Reservations}/*.avsc`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Inventory) — 8 schemas; multiplexed publishers in `ReservationLifecycleHandler` (3 reservation events) + `CurrentStockLevelsProjectionHandler.MaybeEmitStockLevelChanged` (1 stock event) + `ReserveStockCommandHandler` assembles `StockReservationFailedEvent` on the InsufficientStock branch (1 stock event). |
| 3 saga consumers + Catalog `ProductCreatedEvent` + Ordering `OrderCancelledEvent`, ALL with inbox dedup; PLAINTEXT per ADR-0010 | ✅ | [`MessagingDependencyInjection.cs:94-163`](../../../services/Inventory/Inventory.Infrastructure/Common/MessagingDependencyInjection.cs) — three `.AddConsumer(...)` blocks each wired `.AddInbox(typeof(...))`; no SASL/SSL config; matches ADR-0010 v1 baseline. |
| Shared consumer group `inventory-stock-init` | ✅ | Both `CatalogProductsConsumerOptions` + `OrderingOrdersConsumerOptions` bind a single group name; explicit deviation #1 from `eshop-master-design.md § E.10` per `inventory.md:70`. |
| `ReservationExpiryWorker`: 60 s `PeriodicTimer`; injected `TimeProvider`; emits `ReleaseReservationCommand(Expiry)` per row | ✅ | [`ReservationExpiryWorker.cs:38-176`](../../../services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs) — eager startup tick; `PeriodicTimer(TimeSpan, TimeProvider)` constructor; `_timeProvider.GetUtcNow()`; `ICommandHandler<ReleaseReservationCommand>` dispatch with `ReleaseReason.Expiry`. |
| `StockLevelChanged` fires only on 0 ↔ positive crossings | ✅ | [`CurrentStockLevelsProjectionHandler.cs:178-197`](../../../services/Inventory/Inventory.Application/StockItems/CurrentStockLevelsProjectionHandler.cs) — `MaybeEmitStockLevelChanged` checks `wasZero == isZero` → early return. Integration test [`StockLevelChangedEmissionTests.cs:33-68`](../../../test/Inventory.IntegrationTests/Application/StockLevelChangedEmissionTests.cs) drives `init → +5 → −2 → +1 → −4` and asserts exactly 2 outbox rows (the 0→5 and 4→0 crossings). |
| `InsufficientStock` → `Result.Fail` → outbox `StockReservationFailedEvent`; arch test forbids throw | ✅ | [`ReserveStockCommandHandler.cs:93-115`](../../../services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs) assembles the Avro `StockReservationFailedEvent`, adds to outbox, commits via `_outbox.SaveChangesAsync`. Arch test [`ResultPatternTests.cs:42-61`](../../../test/Inventory.ArchitectureTests/Application/ResultPatternTests.cs) bans `ArgumentException`/`InvalidOperationException`/`ArgumentNullException` from any `*CommandHandler`/`*QueryHandler` via `DoesNotThrowRule` (Mono.Cecil IL scan, walks async state machines — verified via `BaseTest.cs:204-253`). |
| Admin `POST /api/v1/inventory/stock-items/{productId}/adjust` with `.Idempotency()` + auth | ✅ (with HIGH defense-in-depth gap — see Dim 8) | [`AdjustStockEndpoint.cs:25-38`](../../../services/Inventory/Inventory.Api/Endpoints/StockItems/Adjust/AdjustStockEndpoint.cs). |
| Integration tests cover all 3 example-mapping sessions + TTL/confirm race | ✅ | `test/Inventory.IntegrationTests/Application/ExampleMapping/{Session1ReservationTtl,Session2CannotOversell,Session3ConfirmIdempotency}Tests.cs` + the TTL/confirm race in `Session1` + `ReservationExpiryWorkerTests`. |
| `InventoryErrors` mirrors `error-taxonomy.md § 3.4` | ✅ with documented add | [`InventoryErrors.cs`](../../../services/Inventory/Inventory.Domain/StockItems/Errors/InventoryErrors.cs) — verbatim `InsufficientStockError` + `ConcurrencyError`; `ReservationNotActiveError` added per example-mapping evidence (Sessions 1 + 3 demand `Result.Fail`, not throw). The addition is documented in `ReservationNotActiveError.cs:11-14`. |
| All timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain (arch test) | ✅ | [`AdrComplianceTests.cs:18-30`](../../../test/Inventory.ArchitectureTests/Domain/AdrComplianceTests.cs) uses `NoStaticUtcNowRule` (banned IL opcodes: `get_UtcNow`, `get_Now`, `get_Today` on `DateTime` + `DateTimeOffset`), walks compiler-generated nested types so async state machines are covered. |
| Correlation-id roundtrips: Kafka header → handler → `stock_events.CorrelationId` column → outbox → emitted event header | ✅ | Two integration tests: in-process at [`ReserveStockCommandHandlerTests.cs:153-190`](../../../test/Inventory.IntegrationTests/Application/ReserveStockCommandHandlerTests.cs) (`CorrelationIdRoundtripsFromCommandToStockEventsRow`); Kafka-handler level at [`ReserveStockCommandKafkaHandlerTests.cs:79`](../../../test/Inventory.IntegrationTests/Messaging/Kafka/ReserveStockCommandKafkaHandlerTests.cs). |
| All `<applicable_adrs>` enforced | ✅ | ADR-0008 (correlation-id roundtrip — verified above), ADR-0010 (auth — JWT bearer + scope policies per `InventoryAuthorizationPolicies.cs`; PLAINTEXT Kafka), ADR-0012 (routes under `/api/v1/inventory/` via `InventoryGroup`), ADR-0013 (`.Idempotency()` wired on `AdjustStockEndpoint`), ADR-0015 (`TimeProvider` injected; arch-test enforced), ADR-0006 (rehydration histograms). |
| Peer-review chain executed; HIGH findings fixed | ✅ per M10 closeout | M2/M3/M4/M5/M6/M7/M8/M9 all ran the Opus pre-commit reviewer per `inventory.md:633`; M10 added the Haiku `nw-software-crafter-reviewer` cross-check at `inventory.md:676-684`. |

### `_shared.md § 12` walk — universal DoD

All 17 universal items verified MET against shipped code. No additional findings beyond Inventory `<dod>` overlap.

### Topic + container contract verification

`docker-compose.yaml:284-286` matches the locked `<contract>` verbatim:

```
inventory.stock-events       partitions=3  retention.ms=-1         (infinite)
inventory.reservations       partitions=6  retention.ms=-1         (saga fan-out — `<stop_conditions>` invariant satisfied)
inventory.reservation-commands  partitions=3  retention.ms=604800000  (7 days per D-9)
```

`docker-compose.yaml:535-563` registers `outbox-relay-inventory` with `OutboxRelay__SchemaName=inventory`, `OutboxRelay__TableName=OutboxMessages`. All 8 Avro schemas present under `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/`.

### Doc drifts (not contract-blocking)

| Severity | File | Drift | Recommendation |
|---|---|---|---|
| MEDIUM | `docs/bc-design/architecture-tests.md § 2.4` (last bullet) | Spec says projection handlers reside in `Inventory.Application.Projections`. Code lives in `Inventory.Application.StockItems` (deliberate — see [`DomainEventHandlerTests.cs:13-20`](../../../test/Inventory.ArchitectureTests/BoundedContext/DomainEventHandlerTests.cs) "one multiplexed handler per projection" rationale). Arch test enforces the newer namespace. | Update `architecture-tests.md § 2.4` to reflect the M2 multiplexed-handler decision. Inventory.md self-correction policy (`_shared.md § 8.3`) was not triggered for this doc. |
| LOW | `docs/bc-design/error-taxonomy.md` § 1 master table row 37 | Says "Reservation not Active on confirm/release | Inventory | Bug | 5xx | DLT". M9 introduced `ReservationNotActiveError` as **business-expected** `Result.Fail` for the terminal-status case (confirm-on-Released, release-on-Confirmed) — class docstring acknowledges deviation (`ReservationNotActiveError.cs:11-14`); the taxonomy doc was not updated. | Add a row for `ReservationNotActiveError` to the taxonomy + amend row 37 to clarify "unknown reservation id" (still bug-class) vs "terminal-status mismatch" (business-expected). |
| LOW | `docs/implementation-prompts/session-summaries/` | Every other Wave-1 BC writes session summaries to `session-summaries/{bc}-m*.md`. Inventory embedded its M10 closeout block inside `docs/implementation-prompts/inventory.md:602-696`. Verifiable but inconsistent with the wave convention. | Process-only; either accept the inline pattern or extract to `inventory-m10.md` post-hoc. Not blocking. |

---

## Dimension 2 — Architecture

**Status:** PASS.

The arch-test project (`test/Inventory.ArchitectureTests/`) has **13 test files / 33 facts**, all green. Walked every file:

- **Layer boundaries** ([`CleanArchitectureLayerTests.cs`](../../../test/Inventory.ArchitectureTests/CleanArchitecture/CleanArchitectureLayerTests.cs), 6 facts) — Domain ⟂ Application/Infrastructure/Api; Application ⟂ Infrastructure/Api; Infrastructure ⟂ Api. Real `NotHaveDependencyOn` assertions, not vacuous.
- **Aggregate discipline** ([`AggregateRootTests.cs`](../../../test/Inventory.ArchitectureTests/Domain/AggregateRootTests.cs), 3 facts) — sealed, externally immutable, private parameterless ctor (custom `PrivateConstructorsRule` walks `TypeDefinition.GetConstructors()`).
- **Value-object discipline** ([`ValueObjectTests.cs`](../../../test/Inventory.ArchitectureTests/Domain/ValueObjectTests.cs), 3 facts) — sealed, immutable, no public ctor.
- **Domain-event discipline** ([`DomainEventTests.cs`](../../../test/Inventory.ArchitectureTests/Domain/DomainEventTests.cs), 4 facts) — name ends `Event`, sealed, immutable, lives under `Inventory.Domain.StockItems.Events`.
- **Result-pattern enforcement** ([`ResultPatternTests.cs`](../../../test/Inventory.ArchitectureTests/Application/ResultPatternTests.cs), 3 facts) — aggregates only throw `DataIntegrityException` (`OnlyThrowsRule`); handlers never throw `ArgumentException`/`InvalidOperationException`/`ArgumentNullException` (`DoesNotThrowRule`); handlers return `Task<Result>`/`Task<Result<T>>` (`HandlerReturnsResultRule`).
- **Append-only event store** ([`EventStoreAppendOnlyTests.cs`](../../../test/Inventory.ArchitectureTests/BoundedContext/EventStoreAppendOnlyTests.cs), 2 facts) — `IEventStore` and `EventStoreRepository` public methods are subset of `{RehydrateAsync, AppendAsync}`; explicit anchor `ContainSingle` guard against vacuous-pass on rename. Locks the architecture-tests.md § 2.4 invariant precisely.
- **Cross-BC reference** ([`CrossBcReferenceTests.cs`](../../../test/Inventory.ArchitectureTests/BoundedContext/CrossBcReferenceTests.cs), 2 facts) — Inventory.Domain + Inventory.Application do not reference any other BC's `.Domain`/`.Application`. Scoped to Domain+Application by design (Infrastructure intentionally consumes Avro records from `Catalog.Products` + `Ordering.Orders` namespaces via `Platform.SchemaRegistry.Contracts`).
- **Domain-event-handler discipline** ([`DomainEventHandlerTests.cs`](../../../test/Inventory.ArchitectureTests/BoundedContext/DomainEventHandlerTests.cs), 2 facts) — sealed handlers; live under `Inventory.Application.StockItems`.
- **ADR-0015 compliance** ([`AdrComplianceTests.cs`](../../../test/Inventory.ArchitectureTests/Domain/AdrComplianceTests.cs), 1 fact) — `NoStaticUtcNowRule` IL-scans for `get_UtcNow`/`get_Now`/`get_Today` calls in `System.DateTime` + `System.DateTimeOffset`; walks compiler-generated nested types so async state-machine bodies are covered (see [`BaseTest.cs:33-47`](../../../test/Inventory.ArchitectureTests/BaseTest.cs)).
- **Command/query/validator naming** — 3 file groups, 7 facts total (commands have a corresponding handler; handlers/validators end with conventional suffix; handlers sealed).

**Quality of arch-test discipline:** the custom Mono.Cecil rules (`OnlyThrowsRule`, `DoesNotThrowRule`, `NoStaticUtcNowRule`, `HandlerReturnsResultRule`, `PublicMethodsAreSubsetOfRule`, `PrivateConstructorsRule`) recurse into nested compiler-generated types — without this, the rules would silently no-op against today's 100%-async handler set. This is exemplary; it is the kind of detail that fails on most teams' arch-test projects.

The "approximately 18-22 tests" target from `architecture-tests.md § 3` is exceeded at 33 — Inventory invested more here than the spec required, which is appropriate given the ES-aggregate stakes.

---

## Dimension 3 — DDD design

**Status:** PASS.

[`StockItem`](../../../services/Inventory/Inventory.Domain/StockItems/StockItem.cs) is an exemplary event-sourced aggregate:

- **Aggregate boundary** — single aggregate `StockItem` keyed by `ProductId` (= StreamId). All four Vernon rules satisfied per `inventory.md § 3.2`. Invariant `Available = OnHand - Reserved >= 0` is enforced inside the aggregate, not bled into handlers (see `Reserve` at line 130-174).
- **Private parameterless ctor** at `StockItem.cs:37`; only construction path is `Fold(events)` at line 59.
- **Reducer purity** — `Apply(event)` is the sole state mutator (`StockItem.cs:326-355`). `Raise(event)` = `Apply` + `AddDomainEvent`, keeping in-memory and stream state aligned by construction.
- **Invariant placement** — bug-class violations (unknown reservation id, re-initialize, stream not initialized, quantity ≤ 0, OrderId empty, TTL ≤ 0, OnHand below zero, OnHand below Reserved) throw `DataIntegrityException` via `Throw.If(...)`. Business-expected outcomes (`Available < quantity`, terminal-status mismatch) return `Result.Fail` with a typed `InsufficientStockError` / `ReservationNotActiveError`. The aggregate never throws `ArgumentException` / `InvalidOperationException`.
- **Idempotency** — `ConfirmReservation` is a no-op (Result.Ok, no event) when already `Confirmed`; `ReleaseReservation` is a no-op when already `Released`; `Initialize` is a no-op when `Version > 0` (see [`InitializeStockItemCommandHandler.cs:35-39`](../../../services/Inventory/Inventory.Application/StockItems/InitializeStockItem/InitializeStockItemCommandHandler.cs)). All three are correct safe-on-redelivery patterns.
- **Domain events** — 6 sealed records under `Inventory.Domain.StockItems.Events`, inherit `DomainEvent` base, named `*Event` (per BC convention — `inventory.md § 5`). Persistence shape AND in-process dispatch shape are unified by design.
- **Value objects** — `Quantity`, `ReservationId`, `ReservationInfo`, `StockSource`, `StockItemSnapshot` derive `ValueObject`; sealed, immutable. `ReservationStatus` + `ReleaseReason` are plain enums (intentional — they're symbolic, not value-bearing). Factories return `Result<T>` for validation failures.
- **No external events leak** — the arch test `DomainEventHandlerTests` ensures `IDomainEventHandler<T>` implementations live under `Inventory.Application.StockItems`; the `CrossBcReferenceTests` ensures no aggregate raises an `ISpecificRecord` (Avro) type.

**Observation (no defect):** the `Quantity` value object is exposed by the API but the aggregate operates on `int` internally. This is a deliberate trade-off documented in the `<wave_progress>` Session 1 decisions; it does not violate DDD, it sacrifices a small amount of type-purity for projection-handler performance.

---

## Dimension 4 — Testing

**Status:** PASS-with-CONCERNS.

| Slice | Count | Status |
|---|---|---|
| Unit | 66/66 | ✅ green, 4 s |
| Architecture | 33/33 | ✅ green, 2 s |
| Integration | 42/42 | ✅ green, 49 s (after one Testcontainers cold-start retry per `CLAUDE.md`) |
| Functional | 15/15 | ✅ green, 31 s |

**Pyramid balance** — 66/33/42/15 is reasonable for an ES BC with one aggregate and 3 saga-command consumers. Functional layer is the thinnest (acknowledged below).

**xUnit1051 hygiene in test bodies** — every Inventory test that I sampled passes `TestContext.Current.CancellationToken`; ripgrep for `CancellationToken\.None` returned zero matches across all four test projects.

**Mutation-test intent** — three load-bearing invariants spot-checked and each has a test that would fail if the invariant were removed:

1. `Available >= 0` — covered by `StockItemTests.Reserve_WhenAvailableLessThanQuantity_ReturnsInsufficientStock_NoEvent` + the integration `Session2CannotOversellTests`.
2. Idempotent `ConfirmReservation` — covered by `StockItemTests.ConfirmReservation_WhenAlreadyConfirmed_IsNoOpReturnsOk_NoEvent` + integration `Session3ConfirmIdempotencyTests`.
3. No double-`Initialize` — covered by `StockItemTests.Initialize_WhenAlreadyInitialized_ThrowsDataIntegrityException`.

**Findings**

| Severity | File:line | Finding | Recommendation |
|---|---|---|---|
| HIGH | [`test/Inventory.IntegrationTests/Messaging/Kafka/ConfirmReservationCommandKafkaHandlerTests.cs`](../../../test/Inventory.IntegrationTests/Messaging/Kafka/ConfirmReservationCommandKafkaHandlerTests.cs) | Only the happy-path is tested. No coverage for the `ReservationNotActive` business-failure branch (confirm-on-Released → `ReservationNotActiveError` → `SagaCommandHandlerBase` does NOT classify this as business-expected → throws → DLT). The `SagaCommandHandlerBaseTests` generic wrapper tests cover the classification logic, so the gap is one of per-handler integration coverage, not algorithm correctness. | Add `ReservationAlreadyReleased_ThrowsSagaCommandDispatchException` + `NonBusinessError_Throws*` cases mirroring the structure of `ReserveStockCommandKafkaHandlerTests`. |
| HIGH | [`test/Inventory.FunctionalTests/ApiEndpoints/StockItems/AdjustStockTests.cs:137-138`](../../../test/Inventory.FunctionalTests/ApiEndpoints/StockItems/AdjustStockTests.cs) | The Idempotency-Key replay test asserts `handlerCalls.Should().BeLessThanOrEqualTo(2)` with a soft regression guard. A completely broken idempotency filter (both POSTs execute the handler) passes this assertion. The carry-forward at `inventory.md:596,689` documents the FastEndpoints 7.0.1 transparency issue — **this is already an accepted M9 follow-up**. | Tighten to `.Be(1)` + non-empty Redis-keys assertion if/when FE/output-cache become transparent in `WebApplicationFactory`. Accepted as carry-forward. |
| MEDIUM | [`test/Inventory.IntegrationTests/Application/StockLevelChangedEmissionTests.cs:60-67`](../../../test/Inventory.IntegrationTests/Application/StockLevelChangedEmissionTests.cs) | The outbox-row assertion filters by `KafkaKey == productId AND TopicName == "inventory.stock-events"` and checks `.HaveCount(2)`. The Avro `Type` is not asserted. Practical exposure is bounded — `inventory.stock-events` is dedicated to `StockLevelChanged` per `events-catalog.md § 5.4` and only `MaybeEmitStockLevelChanged` ever writes to that topic — so a regression that emits the wrong type to the right topic is implausible. Still a hardening gap. | Add `&& m.Type == "Inventory.Stock.StockLevelChanged"` to the predicate (matches the `ConfirmReservationCommandKafkaHandlerTests.cs:79-80` pattern). |
| MEDIUM | [`test/Inventory.FunctionalTests/Common/InventoryApiFixture.cs:70-71, 91`](../../../test/Inventory.FunctionalTests/Common/InventoryApiFixture.cs) | `_pgContainer.StartAsync()`, `_redisContainer.StartAsync()`, and `dbContext.Database.MigrateAsync()` are awaited without `TestContext.Current.CancellationToken`. The integration fixture at `IntegrationTestFixture.cs:47,121` passes the token correctly. xUnit1051 didn't fire because these are fixture-setup overrides (`PreSetupAsync`/`SetupAsync`), not test-method bodies — the analyzer narrows its scan. Still a cancellation-hygiene drift relative to the integration fixture's discipline. | Pass `TestContext.Current.CancellationToken` to all three awaits (and the `OpenAsync()` on line 102 for symmetry). |
| MEDIUM | [`test/Inventory.IntegrationTests/Common/IntegrationTestFixture.cs`](../../../test/Inventory.IntegrationTests/Common/IntegrationTestFixture.cs) | No Respawn — all 42 integration tests share one container and rely on `Guid.NewGuid()` discipline for isolation. The functional fixture got Respawn in M9; the integration fixture asymmetry is **already an accepted M9 carry-forward** at `inventory.md:597,688`. | Track as wave-level fixture-symmetry cleanup. Accepted as carry-forward. |
| MEDIUM | Functional layer thinness | 15 functional tests cover `GET stock-level`, `GET reservation`, `POST receive`, `POST adjust`. There are no HTTP-end-to-end tests for `POST reserve`, `POST confirm-reservation`, or `POST release-reservation` — but **those HTTP endpoints don't exist** per `InventoryAuthorizationPolicies.cs:28-33` ("the realm declares `inventory.commands.{confirm,release}` for the saga's Kafka-driven path, not for HTTP. There is no admin HTTP endpoint for confirm or release in M7"). So this is not actually a coverage gap. Endpoint inventory: `GetReservation` + `GetStockLevel` + `ReceiveStock` + `AdjustStock` = 4 routes × ~4 tests each ≈ 15 — appropriate. | No action required. |

**Test-quality conclusion**: HIGH #1 (Confirm Kafka handler) and HIGH #2 (Adjust idempotency) — the second is already an explicit accepted carry-forward; the first should be either fixed or added to the accepted-carry-forward list. The two MEDIUMs are quality hardening.

---

## Dimension 5 — Event-driven discipline

**Status:** PASS.

- **Outbox is sole egress** — all five external Avro events flow through `ITransactionalOutbox<IInventoryDbContext>.AddOutboxMessage(topic, key, ISpecificRecord)`. Multiplexed handlers (`CurrentStockLevelsProjectionHandler.MaybeEmitStockLevelChanged`, `ReservationLifecycleHandler.Handle×3`) and the `ReserveStockCommandHandler.HandleAsync` failure-branch are the only outbox producers. Grep across `services/Inventory/**` returned no direct `IProducer<,>.ProduceAsync` / `Confluent.Kafka.Producer` calls.
- **Transactional envelope** — `EventStoreRepository.AppendAsync` dispatches every emitted domain event through `IDomainEventDispatcher.DispatchAsync` (line 180-183) **before** calling `_ctx.SaveChangesAsync` (line 187). All projection upserts + outbox rows the handlers register flow into the single `SaveChangesAsync` transaction → atomic by construction. On `UniqueConstraintException`, `_ctx.ChangeTracker.Clear()` (line 197) discards all uncommitted projection + outbox rows so they are re-evaluated against the now-current state on the retry attempt.
- **Inbox dedup** — all three Kafka consumers register `Platform.KafkaFlow.Inbox.EFCore` middleware: saga-commands (`.AddInbox(typeof(AvroReserveStockCommand), typeof(AvroConfirmReservationCommand), typeof(AvroReleaseReservationCommand))`), `catalog.products` (`.AddInbox(typeof(AvroProductCreatedEvent))`), `ordering.orders` (`.AddInbox(typeof(AvroOrderCancelledEvent))`). Each consumer additionally has handler-side guards: `InitializeStockItemCommandHandler` short-circuits on `Version > 0`; `OrderCancelledEventKafkaHandler` filters `Status == Active` so already-released reservations drop out on redelivery.
- **Correlation-id roundtrip** — `AddCorrelationIdConsumerMiddleware()` on every consumer (`MessagingDependencyInjection.cs:102, 129, 151`); the middleware reads from the `x-correlation-id` Kafka header and pushes it into `CorrelationIdContextKeys` (per ADR-0008). The command then carries it to `EventStoreRepository.AppendAsync`, which stamps `StockEventRow.CorrelationId`. The outbox publisher copies the value into the emitted Avro event (verified by `ReserveStockCommandHandlerTests.CorrelationIdRoundtripsFromCommandToStockEventsRow` + `ReserveStockCommandKafkaHandlerTests` line 79).
- **No internal `*DomainEvent` leak to Kafka** — arch test enforces aggregates don't reference `ISpecificRecord` via the cross-BC test; multiplexed handlers explicitly call `.ToStockReservedEvent()` etc. mapper methods that translate internal → external; no `IDomainEventHandler<T>` ever calls `AddOutboxMessage` with `T` directly.
- **Cross-BC consumer hygiene** — Inventory consumes `Catalog.Products.ProductCreatedEvent` + `Ordering.Orders.OrderCancelledEvent` — both are external Avro types from `Platform.SchemaRegistry.Contracts`, not internal `*DomainEvent` types. The `MessagingDependencyInjection.cs:18-22` `using AvroProductCreatedEvent = Catalog.Products.ProductCreatedEvent;` aliases make this explicit.
- **Retry/DLT correctness** — `SagaCommandHandlerBase.IsBusinessExpectedFailure` allowlists `"Inventory.InsufficientStock"` (and only that code). On business-expected failure the wrapper commits the staged outbox row instead of throwing; on any other failure it throws `SagaCommandDispatchException` → KafkaFlow `DeadLetter` middleware routes to `.DLT`. The `OrderCancelledEventKafkaHandler` intentionally diverges (throws `DbUpdateException` so KafkaFlow's `RetryForever` re-runs the message — partial-success fan-out is naturally idempotent against `Status == Active` filter).
- **Schema compatibility** — all 8 `.avsc` files committed and match `events-catalog.md § 5.4 / 5.6`. Git log shows no breaking changes to any Inventory schema since the M4 introduction. FORWARD_TRANSITIVE (events) + FULL_TRANSITIVE (commands) compatibility per ADR-0007 is enforceable but is a Wave-0 platform-level Schema Registry configuration concern; the schema files themselves are well-formed.
- **Hot-aggregate retry** — `EventStoreRepository.AppendAsync` retries once on `UniqueConstraintException` (PK collision) with `ChangeTracker.Clear()`; a second conflict returns `Result.Fail(InventoryErrors.Concurrency(...))`. Caller (saga) treats `Inventory.Concurrency` as retryable per ADR-0008. ADR-0006 § 10.4 acknowledges hot-aggregate thrash is a known v1 limitation.

---

## Dimension 6 — .NET / C# best practices

**Status:** PASS.

- **Async-all-the-way** — grep across `services/Inventory/**` returned zero matches for `\.Result\b`, `\.Wait\(\)`, `\.GetAwaiter\(\)\.GetResult\(\)` in non-test code.
- **`TimeProvider`** — injected from Generic Host; `ReservationExpiryWorker` uses `PeriodicTimer(TimeSpan, TimeProvider)` ctor + `_timeProvider.GetUtcNow()`. No `DateTime.UtcNow` in domain (arch-test enforced).
- **Nullable hygiene** — minimal `!` usage. The two found instances:
  - [`ReservationInfo.cs:23`](../../../services/Inventory/Inventory.Domain/StockItems/ValueObjects/ReservationInfo.cs) `public ReservationId ReservationId { get; init; } = null!;` — null-forgiveness sentinel for NRT satisfaction on a record type. Safe (factory-only construction path; `with`-expression copies a non-null field) but theoretical risk if a future contributor default-constructs. **MEDIUM cosmetic — see Dim 8**.
  - [`ReserveStockCommandValidator.cs:13`](../../../services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandValidator.cs) `c.TimeToLive!.Value` — unnecessary `!` (validator's `.When(c => c.TimeToLive.HasValue)` already guards). **LOW cosmetic — see Dim 8**.
- **DI lifetimes** — `EventStoreRepository` is Scoped; `ReservationExpiryWorker` (`BackgroundService`) resolves scoped deps via `IServiceScopeFactory.CreateAsyncScope()` inside `ProcessExpiredReservationsAsync`. Scope is `await using` → disposal guaranteed. No singleton-captures-scoped pattern.
- **Magic strings** — topic names live in `TopicsOptions` bound via `AddOptionsWithValidateOnStart`. Connection-string keys live in `ConnectionStringsOptions`. DLT suffix is bound, not hard-coded. Error codes live in `InventoryErrors` factory.
- **Logging hygiene** — all `ILogger.LogXxx(...)` calls use structured message templates with named holes. No string-interp / concat into logger arguments. (Note: `AdjustedByUserId` is logged at Info level — see Dim 8 MEDIUM.)
- **EF Core** — `AsNoTracking()` on every read query path; `FindAsync` results are null-checked or fall through to `DataIntegrityException`. No EF migration generated by code (per CLAUDE.md).
- **`Throw.If` vs raw `throw`** — `Throw.If` for invariant checks; raw `throw` only inside pattern-match branches and helper fallbacks where the structure benefits from it (e.g., `ApplyConfirmed`).
- **Exception design** — Domain throws only `DataIntegrityException`. Infrastructure throws `SagaCommandDispatchException` (poison classifier) + `DbUpdateException` (retry classifier). No swallowed exceptions in `ReservationExpiryWorker` that could mask real bugs (`OperationCanceledException` is re-thrown when cancellation is requested; other exceptions are logged and the loop continues).

---

## Dimension 7 — CI gates (verbatim output)

All four format/build gates and three of four test slices verified green; integration tests pending (see addendum below).

### Gate 1 — `dotnet restore --locked-mode`

```
[snip — 53 NU1903 warnings on System.Security.Cryptography.Xml (repo-wide allowlist, not Inventory-specific)]
Count of errors: 0
Build completed
Exit code: 0
```

Solution-wide restore clean; only the centralised NU1903 advisories on the `System.Security.Cryptography.Xml` transitive (allowlisted in this repo's NuGet audit baseline). No Inventory-specific lock-file drift.

### Gate 2 — `dotnet build -m --no-restore`

```
[snip — 53 NU1903 warnings + the standard project-output lines]
    53 upozornění                  (= 53 warnings)
    Počet chyb: 0                  (= 0 errors)
Uplynulý čas 00:06:14.08            (= elapsed 6 minutes 14 seconds)
```

Exit code 0.

### Gate 3 — `dotnet format whitespace --no-restore --verify-no-changes`

Exit code 0 (no whitespace diffs).

### Gate 4 — `dotnet format style --no-restore --verify-no-changes`

Exit code 0 (no style diffs).

### Gate 5 — `dotnet test test/Inventory.UnitTests/ --no-build --no-restore`

```
Testovací běh pro C:\...\test\Inventory.UnitTests\bin\Debug\net10.0\Inventory.UnitTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    66, Přeskočeno:     0, Celkem:    66, Doba trvání: 4 s - Inventory.UnitTests.dll (net10.0)
```

**66/66 passed, 0 failed, 0 skipped, 4 seconds.**

### Gate 6 — `dotnet test test/Inventory.ArchitectureTests/ --no-build --no-restore`

```
Testovací běh pro C:\...\test\Inventory.ArchitectureTests\bin\Debug\net10.0\Inventory.ArchitectureTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    33, Přeskočeno:     0, Celkem:    33, Doba trvání: 2 s - Inventory.ArchitectureTests.dll (net10.0)
```

**33/33 passed, 0 failed, 0 skipped, 2 seconds.**

### Gate 7 — `dotnet test test/Inventory.IntegrationTests/ --no-build --no-restore`

```
Testovací běh pro C:\...\test\Inventory.IntegrationTests\bin\Debug\net10.0\Inventory.IntegrationTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    42, Přeskočeno:     0, Celkem:    42, Doba trvání: 49 s - Inventory.IntegrationTests.dll (net10.0)
```

**42/42 passed, 0 failed, 0 skipped, 49 seconds.**

Note: a first attempt of this gate was interrupted ("Active test run interrupted. Test host process exited with error.") on the reviewer's Windows + corp-proxy environment. The retry — same `dotnet test` command but launched from a freshly proxy-stripped shell per `CLAUDE.md`'s Testcontainers troubleshooting note — completed cleanly. M10 baseline at `inventory.md:667` was 42/42 in 7 s on the maintainer's environment; the 49 s here reflects the reviewer environment's Testcontainers cold-start cost, not a regression.

### Gate 8 — `dotnet test test/Inventory.FunctionalTests/ --no-build --no-restore`

```
Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    15, Přeskočeno:     0, Celkem:    15, Doba trvání: 31 s - Inventory.FunctionalTests.dll (net10.0)
```

**15/15 passed, 0 failed, 0 skipped, 31 seconds.**

---

## Dimension 8 — Code review (parallel-reviewer aggregation)

Three of four reviewers landed. Reliability/event-driven reviewer is still running; addendum below.

### Severity-deduplicated findings

| Severity | File:line | Description | Recommendation |
|---|---|---|---|
| HIGH | [`services/Inventory/Inventory.Api/Endpoints/StockItems/Adjust/AdjustStockEndpoint.cs:29-38`](../../../services/Inventory/Inventory.Api/Endpoints/StockItems/Adjust/AdjustStockEndpoint.cs) | `.Idempotency()` wires the dedup cache but does NOT 400 on a missing `Idempotency-Key` header. `Basket.Api/CheckoutBasketEndpoint.cs` and `Ordering.Api/CancelOrderEndpoint.cs` already implement the explicit `HttpContext.Request.Headers.ContainsKey("Idempotency-Key")` guard. AdjustStock — the one Inventory endpoint where ADR-0013 says "clients MUST send Idempotency-Key" — does not. A retry that omits the header silently bypasses dedup → potential duplicate stock adjustment. **Not exploitable from an unauthenticated principal (auth still required), but a real defense-in-depth gap vs. the repo's own convention.** | Add the same `ContainsKey` + `AddError`/`Send.ErrorsAsync(400)` pattern as `CheckoutBasketEndpoint.cs:57-63`. NOT in M10's `<dod>` so technically out-of-scope, but recommended to ship before next milestone. |
| HIGH | (Dim 4) | `ConfirmReservationCommandKafkaHandlerTests` missing failure-path coverage. | See Dim 4. |
| HIGH | (Dim 4) | `AdjustStockTests.WhenSameIdempotencyKeyReplayed_BothCallsReturn200` soft regression guard. | See Dim 4 — **already an accepted M9 carry-forward** at `inventory.md:596,689`. |
| MEDIUM | [`services/Inventory/Inventory.Api/appsettings.json:40,45`](../../../services/Inventory/Inventory.Api/appsettings.json) + [`InventoryDbContextDesignTimeFactory.cs:23`](../../../services/Inventory/Inventory.Infrastructure/Persistence/Database/InventoryDbContextDesignTimeFactory.cs) | Placeholder credentials committed to source — `"ClientSecret": "ClientSecretThatShouldBeInVaultAndNotExposed"` + Postgres dev password. Not production values, but they're explicit strings rather than user-secrets placeholders. | Replace with `"REPLACE_WITH_SECRET_MANAGER_VALUE"` and document `dotnet user-secrets` in a `dev-secrets.md`. Pattern-match against Basket/Ordering — if they have the same, treat as repo-wide. |
| MEDIUM | [`services/Inventory/Inventory.Application/StockItems/AdjustStock/AdjustStockCommandHandler.cs:44-46`](../../../services/Inventory/Inventory.Application/StockItems/AdjustStock/AdjustStockCommandHandler.cs) | `AdjustedByUserId` (caller-supplied Guid in the HTTP request body, not extracted from JWT `sub`) is logged at `Information` level. Audit-spoofing surface: a caller can supply any Guid and have it logged as the "performer". | Either validate `AdjustedByUserId == ctx.User.FindFirst("sub").Value` at the endpoint level, or demote to `Debug`. |
| MEDIUM | [`services/Inventory/Inventory.Infrastructure/Common/PersistenceDependencyInjection.cs:56`](../../../services/Inventory/Inventory.Infrastructure/Common/PersistenceDependencyInjection.cs) | `EnableSensitiveDataLogging(!isDeployedEnvironment)` is correct, but if `IsDeployedEnvironment()` is ever misconfigured, the `stock_events.Payload` jsonb (which embeds `CorrelationId`/`OrderId`/`ReservationId`) leaks to logs. Acceptable risk for a dev-only path; add a defensive comment for review attention. | No change required; add a one-line comment citing the `isDeployedEnvironment` gate as the defensive contract. |
| MEDIUM | [`services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/OrderCancelledEventKafkaHandler.cs:93, 119`](../../../services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/OrderCancelledEventKafkaHandler.cs) | Two `await`s inside the `EnsureTransactionAsync` lambda lack `.ConfigureAwait(false)` while every other await in the same file has it. KafkaFlow worker thread → no deadlock risk, but style-drift. | Add `.ConfigureAwait(false)` to lines 93 + 119. |
| MEDIUM | [`services/Inventory/Inventory.Domain/StockItems/ValueObjects/ReservationInfo.cs:23`](../../../services/Inventory/Inventory.Domain/StockItems/ValueObjects/ReservationInfo.cs) | `public ReservationId ReservationId { get; init; } = null!;` — null-forgiveness sentinel. Factory-only construction makes this theoretically safe; consider `required init` to remove the `!`. | Cosmetic; not blocking. |
| LOW | [`services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandValidator.cs:13`](../../../services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandValidator.cs) | `c.TimeToLive!.Value` — `!` is unnecessary on a `TimeSpan?` after a `.When` guard. | Remove the `!`. |
| LOW | (Dim 1) | Doc drifts — see Dim 1 findings table. | See Dim 1. |

### Reviewers run (parallel sub-agents)

1. Security review (`a539418ee44b0dd71`) — completed. Verdict: CONCERNS (1 HIGH + 3 MEDIUMs above).
2. .NET / DDD / async hygiene review (`a0408b540b8c4b513`) — completed. Verdict: PASS (2 MEDIUMs + 1 LOW above).
3. Test-quality review (`abb994653cd0ea66b`) — completed. Verdict: CONCERNS (post-verification: 1 HIGH carry-forward + 1 HIGH new + several MEDIUMs above; reviewer's 2 "CRITICAL" labels reclassified after direct file-read verification — see review-narrative below).
4. Reliability / event-driven review (`ab2aad65f180f0c2e`) — completed. Verdict: CONCERNS. **Zero CRITICAL across all event-driven concerns** (outbox-as-sole-egress, transactional envelope, correlation-id chain, inbox coverage, no internal-event leak, cross-BC consumer hygiene, retry/DLT correctness, idempotency, schema compatibility, hot-aggregate retry — all green). The reviewer flagged one HIGH that I reclassified to MEDIUM after direct verification (see below), plus one new MEDIUM.

#### Additional findings from reliability reviewer

| Severity | File:line | Finding | Recommendation |
|---|---|---|---|
| MEDIUM (reviewer said HIGH; reclassified after verification) | [`services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs:110`](../../../services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs) | "Double `SaveChangesAsync` on InsufficientStock path." The reviewer noted that in the Kafka-handler dispatch path, `_outbox.SaveChangesAsync` at line 110 is followed by `SagaCommandHandlerBase.ExecuteAsync` calling `_transactionalOutbox.SaveChangesAsync` again at line 142 (the second is a no-op on a clean ChangeTracker). **Verified**: `ReserveStockCommand` is also dispatched directly from `Inventory.IntegrationTests` fixture-setup paths and seed methods (e.g., `ConfirmReservationCommandKafkaHandlerTests.SeedActiveReservationAsync`), which do NOT go through `SagaCommandHandlerBase`. Removing line 110 would break those paths (the staged outbox row would never commit). **Reclassified MEDIUM as defensive architectural redundancy**; the in-handler SaveChangesAsync makes the handler self-contained for direct-dispatch callers, while the wrapper's second call is a benign no-op in production. | No change required. Optionally, add a one-line comment to line 110 calling out the self-contained-handler design intent so a future maintainer doesn't "simplify" it. |
| MEDIUM | [`services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs:151-160`](../../../services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs) | A reservation that consistently fails with `Inventory.Concurrency` on every 60 s tick (e.g., a permanently conflicting writer) leaks as `Status='Active'` indefinitely, with no DLT/alerting path — the saga-command path routes to DLT after retry, the expiry-worker path just logs a warning and tries again next tick. Low-frequency but no operator escalation signal. | Add a metric (`inventory.reservation.expiry.failure_count` tagged by ProductId) on persistent `ConcurrencyError`, OR cap retry-count per reservation and promote to a side-table after N misses for ops attention. Track as wave-level resilience-uplift carry-forward. |

### Critical-claim verification (trust-but-verify)

The test-quality reviewer flagged two **CRITICAL** items; both were verified directly against the source and reclassified:

- "CRITICAL — `StockLevelChangedEmissionTests` missing Avro type filter on outbox assertion" → reread of `StockLevelChangedEmissionTests.cs:60-67` confirms the filter is `KafkaKey + TopicName` only. **Reclassified MEDIUM**: `inventory.stock-events` is dedicated to `StockLevelChanged` per `events-catalog.md § 5.4`; only `CurrentStockLevelsProjectionHandler.MaybeEmitStockLevelChanged` writes to it; a regression that emits the wrong type to the right topic is bounded by the architecture itself.
- "CRITICAL — `ConfirmReservationCommandKafkaHandlerTests` missing failure-path coverage" → reread of the file confirms only `[Fact] HappyPath_AuditConfirmedAndOutboxEmitted`. **Reclassified HIGH**: the `SagaCommandHandlerBase.IsBusinessExpectedFailure` classification logic is shared and covered by `SagaCommandHandlerBaseTests`; the gap is per-handler integration coverage rather than algorithmic uncoverage. Still a real gap.

---

## Verdict — **CONDITIONAL-PASS**

**Thresholds applied:** zero CRITICAL, **3 HIGH** (one already-accepted carry-forward + one new defense-in-depth + one test-coverage gap), all gates green (integration pending — see Dim 7 addendum), all DoD MET per the M10 self-walk.

**Why not PASS:** the security HIGH (missing `Idempotency-Key` 400-guard on `AdjustStockEndpoint`) is a real defense-in-depth gap relative to repo convention (Basket + Ordering already guard) and is NOT explicitly accepted as a carry-forward in `inventory.md:686-693`. The test-quality HIGH (missing Confirm-handler failure-path test) is similarly not in the accepted-carry-forward list. Both warrant either a small commit or an explicit acceptance entry before this BC is considered fully closed.

**Why not FAIL:** zero CRITICAL findings; no contract or seam drift (all 8 Avro schemas match `events-catalog.md`; topics + partitions + retention match `docker-compose.yaml` verbatim; consumer-group naming matches the contract); zero test red; all DoD items MET against shipped code; arch-test rules are non-vacuous and IL-walk async state machines.

---

## Punch list (ordered, actionable)

1. **HIGH** — Add explicit `Idempotency-Key` header `ContainsKey` 400-guard to [`services/Inventory/Inventory.Api/Endpoints/StockItems/Adjust/AdjustStockEndpoint.cs`](../../../services/Inventory/Inventory.Api/Endpoints/StockItems/Adjust/AdjustStockEndpoint.cs:29) — mirror [`services/Basket/Basket.Api/...CheckoutBasketEndpoint.cs:57-63`](../../../services/Basket/Basket.Api). ~10 lines of code + one functional test for the 400-branch. OR: add an explicit acceptance row to `inventory.md:686-693` documenting the rationale for deferring.
2. **HIGH** — Add `ReservationAlreadyReleased_ThrowsSagaCommandDispatchException` + `NonBusinessError_Throws*` cases to [`test/Inventory.IntegrationTests/Messaging/Kafka/ConfirmReservationCommandKafkaHandlerTests.cs`](../../../test/Inventory.IntegrationTests/Messaging/Kafka/ConfirmReservationCommandKafkaHandlerTests.cs), mirroring the structure already used in `ReserveStockCommandKafkaHandlerTests`. OR: explicitly accept per the carry-forward pattern.
3. **HIGH (carry-forward, already accepted)** — Tighten `AdjustStockTests.WhenSameIdempotencyKeyReplayed_BothCallsReturn200` from `.BeLessThanOrEqualTo(2)` to `.Be(1)` once FastEndpoints 7.0.1's `.Idempotency()` becomes transparent in `WebApplicationFactory`. Already in M9 follow-ups.
4. **MEDIUM** — Add Avro `Type` filter to `StockLevelChangedEmissionTests` outbox assertion: `&& m.Type == "Inventory.Stock.StockLevelChanged"`.
5. **MEDIUM** — Pass `TestContext.Current.CancellationToken` to the three awaits in `InventoryApiFixture.cs:70-71, 91` (and line 102's `OpenAsync`).
6. **MEDIUM** — Add Respawn to `IntegrationTestFixture` (already an accepted M9 carry-forward at `inventory.md:597,688`).
7. **MEDIUM** — Audit-spoof concern: either validate `AdjustedByUserId` against JWT `sub` or demote the log line to `Debug` in `AdjustStockCommandHandler.cs:44-46`.
8. **MEDIUM** — Add `.ConfigureAwait(false)` to `OrderCancelledEventKafkaHandler.cs:93, 119`.
9. **MEDIUM** — Update `docs/bc-design/architecture-tests.md § 2.4` to reflect Inventory's `Inventory.Application.StockItems` namespace decision (the M2 "one multiplexed handler per projection" choice).
10. **LOW** — Update `docs/bc-design/error-taxonomy.md § 1` row 37 to distinguish unknown-reservation (bug-class) from terminal-status-mismatch (business-expected via `ReservationNotActiveError`).
11. **LOW** — Replace placeholder credentials in `appsettings.json` + `InventoryDbContextDesignTimeFactory.cs` with `dotnet user-secrets` references and a `dev-secrets.md`.
12. **LOW** — Remove unnecessary `!` from `ReserveStockCommandValidator.cs:13` and `ReservationInfo.cs:23` (use `required init` for the latter).

---

## Addendum

All eight CI gates green, all four parallel reviewers landed; no pending items. Verdict is final: **CONDITIONAL-PASS**.
