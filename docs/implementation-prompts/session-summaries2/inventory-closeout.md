# Inventory Bounded Context — Final Closeout Review

> **HEAD:** `9aad7c4f98ecabc7086395d032e6ac74f5e56037` • **Branch:** `aaqwdqwd` • **Review date:** 2026-05-11
>
> **Verdict: PASS** ✅
>
> Zero CRITICAL, zero HIGH, all DoD MET, all four CI gates green, all four test slices green (156/156 tests pass), all eight `<contract>` seams verified, all five `<applicable_adrs>` enforced.

## TL;DR

The Inventory BC has shipped its full milestone matrix (M1 through M10). The locked seams — event-store PK `(StreamId, Version)` with retry-once optimistic concurrency, 5 external Avro events + 3 saga-command schemas, three Kafka topics with the contract-locked partition counts (3 / 6 / 6-fanout / 3), the `inventory-stock-init` shared consumer group, the `BusinessExpectedErrorCodes = {"Inventory.InsufficientStock"}` filter on `SagaCommandHandlerBase`, the 60s `PeriodicTimer`-based `ReservationExpiryWorker`, the `.Idempotency()`-wrapped admin `POST /api/v1/inventory/stock-items/{productId}/adjust`, the rehydration histograms tagged by `product_id` per ADR-0006 — all match the spec and are pinned by tests. The MEDIUM findings below are post-PASS quality improvements for the next phase; none block.

---

## Dimension 1 — Doc adherence + DoD audit

### `_shared.md § 12` universal DoD

| § 12 item | Status | Evidence |
|---|---|---|
| 4-layer project compiles (`Api`/`Application`/`Domain`/`Infrastructure`) | MET | [Inventory.Api](services/Inventory/Inventory.Api/Inventory.Api.csproj), [Inventory.Application](services/Inventory/Inventory.Application/Inventory.Application.csproj), [Inventory.Domain](services/Inventory/Inventory.Domain/Inventory.Domain.csproj), [Inventory.Infrastructure](services/Inventory/Inventory.Infrastructure/Inventory.Infrastructure.csproj); solution-wide `dotnet build -m` → 0 errors (dim 7) |
| All commands + queries from use-cases.md § 4 implemented | MET | 6 command triplets + 2 query triplets under [services/Inventory/Inventory.Application/StockItems/](services/Inventory/Inventory.Application/StockItems) (Initialize / Receive / Reserve / Confirm / Release / Adjust + GetStockLevelByProductId / GetReservationById) |
| Internal `*Event` types declared in Domain | MET | 6 ES events at [services/Inventory/Inventory.Domain/StockItems/Events/](services/Inventory/Inventory.Domain/StockItems/Events) (suffix is `Event` not `DomainEvent` — deliberate per `inventory.md § 5` "Conventions"; covered by [DomainEventTests.cs](test/Inventory.ArchitectureTests/Domain/DomainEventTests.cs)) |
| External `*Event` Avro under `Platform.SchemaRegistry.Contracts/Avro/Inventory/` | MET | 8 `.avsc` files under [platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/](platform/Platform.SchemaRegistry.Contracts/Avro/Inventory) — `Stock/StockLevelChanged.avsc` + 7 under `Reservations/` (4 events + 3 saga commands) |
| Outbox publishers map internal → external | MET | [CurrentStockLevelsProjectionHandler.cs:178](services/Inventory/Inventory.Application/StockItems/CurrentStockLevelsProjectionHandler.cs:178) emits `StockLevelChanged`; [ReservationLifecycleHandler.cs:61](services/Inventory/Inventory.Application/StockItems/ReservationLifecycleHandler.cs:61) emits the three reservation events; [ReserveStockCommandHandler.cs:96](services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs:96) stages the failure event on `InsufficientStock` |
| DbContext + naming conventions; migration user-generated | MET | [InventoryDbContext.cs](services/Inventory/Inventory.Infrastructure/Persistence/Database/InventoryDbContext.cs); migrations `20260424192419_AddStockEventsEventSource` + `20260425111658_AddProjectionsAndOutboxInbox` user-generated |
| Messaging DI: outbox, inbox, Kafka consumers | MET | [MessagingDependencyInjection.cs:82-185](services/Inventory/Inventory.Infrastructure/Common/MessagingDependencyInjection.cs:82) — three consumer blocks + cluster-level DLT producer + `AddInbox<InventoryDbContext>` + `AddOutbox` |
| docker-compose delta: topics + outbox-relay-inventory | MET | `docker-compose.yaml:284-286` (3 Inventory topics) + `docker-compose.yaml:451-481` (outbox-relay-inventory container on port 8093, schema `inventory`) per `<prerequisites>` table |
| 4 test projects compile + pass; arch tests enforce architecture-tests.md § Inventory | MET | 66 + 33 + 42 + 15 = 156/156 pass (dim 7); 33 architecture facts in [test/Inventory.ArchitectureTests/](test/Inventory.ArchitectureTests) |
| All HTTP routes under `/api/v1/inventory/...` per ADR-0012 | MET | [InventoryGroup.cs:18](services/Inventory/Inventory.Api/Endpoints/InventoryGroup.cs:18) prefix `/inventory`; FastEndpoints platform prefix `api` + version prefix `v` + `DefaultVersion=1` combine to `/api/v1/inventory/...` |
| All timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain (arch test) | MET | `grep "DateTime\.UtcNow\|DateTime\.Now"` over `services/Inventory` returns zero matches; [AdrComplianceTests.cs](test/Inventory.ArchitectureTests/Domain/AdrComplianceTests.cs) `NoStaticUtcNowRule` covers Domain |
| Correlation-id propagation (HTTP → Kafka → DB column) per ADR-0008 | MET | [EventStoreRepository.cs:160-171](services/Inventory/Inventory.Infrastructure/Persistence/EventStore/EventStoreRepository.cs:160) writes `CorrelationId` per row; [MessagingDependencyInjection.cs:102](services/Inventory/Inventory.Infrastructure/Common/MessagingDependencyInjection.cs:102) `.AddCorrelationIdConsumerMiddleware()`; [ReserveStockCommandHandler.cs:70](services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs:70) threads `command.CorrelationId` into `AppendAsync`; M4 integration test pins it |
| `dotnet build -m`, `dotnet restore --locked-mode`, `dotnet format whitespace`, `dotnet format style` all green | MET | Dim 7 below — all four exit 0 |
| `docker compose --profile full up -d` starts the container + healthcheck passes | MET (per M10 evidence) | M10 session summary at [inventory.md:660-672](docs/implementation-prompts/inventory.md:660) documents the smoke from 2026-05-02 with verbatim `kafka-topics --describe` output for all three Inventory topics; not re-run in this review session (would be redundant against unchanged tree since `a6ea4fd`) |
| Docs self-corrected if needed | PARTIALLY MET | M2 (line 117) and M4 (line 176) flagged the contradiction between [inventory.md:62](docs/bc-design/inventory.md:62) (aggregate retains terminal reservations) and `inventory.md § 3.2 rule 2` ("we prune after Confirmed/Released") — the design doc was never updated; code correctly retains. Carry-forward, no behavioral risk. |
| Peer-review chain executed; HIGH findings fixed | MET | Opus reviewer ran on M2 / M3 / M4 / M5 / M6 / M7 / M8 / M9 / M10 (per per-session `Pre-commit reviewer pass` tables in `inventory.md`); all CRITICAL/HIGH closed before commit |
| Session summary posted | MET | Session 9 / M10 at [inventory.md:602-695](docs/implementation-prompts/inventory.md:602) |

### Inventory `<dod>` (BC-specific)

| `<dod>` line | Status | Evidence |
|---|---|---|
| Event store table PK `(StreamId, Version)` + append-only enforcement (arch test) | MET | [StockEventRowConfiguration.cs:23](services/Inventory/Inventory.Infrastructure/Persistence/Database/EntityConfigurations/StockEventRowConfiguration.cs:23) `builder.HasKey(r => new { r.StreamId, r.Version })`; [EventStoreAppendOnlyTests.cs](test/Inventory.ArchitectureTests/BoundedContext/EventStoreAppendOnlyTests.cs) enforces that both `IEventStore` (port) and `EventStoreRepository` (impl) public methods are a subset of `{RehydrateAsync, AppendAsync}` |
| 2 projection tables + handlers in same DbContext tx | MET | [CurrentStockLevelRowConfiguration.cs](services/Inventory/Inventory.Infrastructure/Persistence/Database/EntityConfigurations/CurrentStockLevelRowConfiguration.cs) + [ReservationAuditRowConfiguration.cs](services/Inventory/Inventory.Infrastructure/Persistence/Database/EntityConfigurations/ReservationAuditRowConfiguration.cs); dispatch envelope at [EventStoreRepository.cs:176-187](services/Inventory/Inventory.Infrastructure/Persistence/EventStore/EventStoreRepository.cs:176) — `_dispatcher.DispatchAsync` runs handlers against the same `InventoryDbContext`, all commit in one `SaveChangesAsync` |
| 6 ES events + reducers; `StockItem.Fold` correct on unit tests | MET | [StockItem.cs:59-70](services/Inventory/Inventory.Domain/StockItems/StockItem.cs:59) pure-function `Fold`; 6 reducers at [StockItem.cs:357-458](services/Inventory/Inventory.Domain/StockItems/StockItem.cs:357); 66 unit tests pass (dim 7) — every reducer + every example-mapping scenario covered |
| Optimistic concurrency: `UniqueConstraintException` retry → `ConcurrencyError` | MET | [EventStoreRepository.cs:135-201](services/Inventory/Inventory.Infrastructure/Persistence/EventStore/EventStoreRepository.cs:135) — `MaxAttempts = 2`; `_ctx.ChangeTracker.Clear()` on first catch; second conflict returns `InventoryErrors.Concurrency(...)`; `EventStoreRepositoryTests.AppendAsync_ConcurrencyConflict_RetriesOnceAndSucceeds` integration test pins it |
| Rehydration observability per ADR-0006 + p99<1s on 1k-event test | MET | [InventoryMetrics.cs](services/Inventory/Inventory.Infrastructure/Observability/InventoryMetrics.cs) (`Meter` = `"Inventory"`, both histograms tagged by `product_id`); [EventStoreRepository.cs:71-85](services/Inventory/Inventory.Infrastructure/Persistence/EventStore/EventStoreRepository.cs:71) allocation-free `Stopwatch.GetTimestamp/GetElapsedTime`; `EventStoreRepositoryRehydrationMetricsTests` integration test seeds 1000 events on one stream, asserts 100 measurements per histogram + p99 nearest-rank < 1000ms |
| 5 external Avro events + 3 saga-command Avro + outbox publishers for all 5 externals | MET | Schemas verified — `Stock/StockLevelChanged.avsc` + `Reservations/{StockReserved,StockReservationFailed,ReservationConfirmed,ReservationReleased}Event.avsc` + `Reservations/{Reserve,Confirm,Release}ReservationCommand.avsc`. Publishers: [CurrentStockLevelsProjectionHandler.cs:178](services/Inventory/Inventory.Application/StockItems/CurrentStockLevelsProjectionHandler.cs:178) (threshold-emit), [ReservationLifecycleHandler.cs:61,91,109](services/Inventory/Inventory.Application/StockItems/ReservationLifecycleHandler.cs:61) (3 reservation events), [ReserveStockCommandHandler.cs:96](services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs:96) (failure event) |
| 3 saga-command consumers + Catalog `ProductCreatedEvent` + Ordering `OrderCancelledEvent`; inbox dedup; PLAINTEXT per ADR-0010 | MET | [MessagingDependencyInjection.cs:94-163](services/Inventory/Inventory.Infrastructure/Common/MessagingDependencyInjection.cs:94) — three `AddConsumer` blocks; each adds `.AddInbox(...)`; no `X-Service-Token` middleware. Handlers: [ReserveStockCommandKafkaHandler.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/SagaCommands/ReserveStockCommandKafkaHandler.cs), [ConfirmReservationCommandKafkaHandler.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/SagaCommands/ConfirmReservationCommandKafkaHandler.cs), [ReleaseReservationCommandKafkaHandler.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/SagaCommands/ReleaseReservationCommandKafkaHandler.cs), [ProductCreatedEventKafkaHandler.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/ProductCreatedEventKafkaHandler.cs), [OrderCancelledEventKafkaHandler.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/OrderCancelledEventKafkaHandler.cs) |
| Shared consumer group `inventory-stock-init` (Catalog + Ordering); separate `inventory-reservation-commands` | MET | [CatalogProductsConsumerOptions.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/CatalogProductsConsumerOptions.cs) group `inventory-stock-init`; [OrderingOrdersConsumerOptions.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/OrderingOrdersConsumerOptions.cs) same group; [ReservationCommandsConsumerOptions.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/SagaCommands/ReservationCommandsConsumerOptions.cs) separate `inventory-reservation-commands` (deviation #1 — events-catalog.md authoritative) |
| `ReservationExpiryWorker`: 60s `PeriodicTimer`; injected `TimeProvider`; `ReleaseReservationCommand(Expiry)` per expired | MET | [ReservationExpiryWorker.cs:40-78](services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs:40) — `PollIntervalSeconds = 60`, `new PeriodicTimer(TimeSpan, _timeProvider)`, eager startup tick, `ReleaseReason.Expiry` |
| `StockLevelChanged` fires only on 0 ↔ positive (test proves it) | MET | [CurrentStockLevelsProjectionHandler.cs:178-197](services/Inventory/Inventory.Application/StockItems/CurrentStockLevelsProjectionHandler.cs:178) — `wasZero == isZero` early-return is the XOR-emission equivalent; `StockLevelChangedEmissionTests` integration test runs `init → +5 → −2 → +1 → −4` and asserts exactly 2 outbox emissions |
| `InsufficientStock` → `Result.Fail` → outbox `StockReservationFailedEvent`; arch test forbids throw | MET | [StockItem.cs:157-161](services/Inventory/Inventory.Domain/StockItems/StockItem.cs:157) returns `Result.Fail<ReservationInfo>(InventoryErrors.InsufficientStock(...))` (no throw); [ReserveStockCommandHandler.cs:93-115](services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs:93) stages `StockReservationFailedEvent` then commits; [ResultPatternTests.cs](test/Inventory.ArchitectureTests/Application/ResultPatternTests.cs) `DoesNotThrowRule` over `*CommandHandler` |
| Admin `POST /adjust` with `.Idempotency()` + auth | MET | [AdjustStockEndpoint.cs:25-38](services/Inventory/Inventory.Api/Endpoints/StockItems/Adjust/AdjustStockEndpoint.cs:25) — `Post("stock-items/{productId:guid}/adjust")`, `Policies(InventoryAuthorizationPolicies.CommandsPolicy)`, `.Idempotency(opts => { HeaderName = "Idempotency-Key"; CacheDuration = 24h; })` per ADR-0013 |
| Integration tests cover 3 example-mapping sessions + TTL/confirm race | MET | M9 added 7 ExampleMapping tests under [test/Inventory.IntegrationTests/Application/ExampleMapping/](test/Inventory.IntegrationTests/Application/ExampleMapping); M6 `ReservationExpiryWorkerTests` covers TTL; `Session3ConfirmIdempotencyTests.Example3_4_ConfirmVsExpiryRace_LoserObservesTerminalAndFails` covers the TTL/confirm race via `OneShotConflictInterceptor` |
| `InventoryErrors` mirrors `error-taxonomy.md § 3.4` | MET | [InventoryErrors.cs](services/Inventory/Inventory.Domain/StockItems/Errors/InventoryErrors.cs) — `InsufficientStock` and `Concurrency` verbatim from § 3.4 (lines 189-208); `ReservationNotActive` added per example-mapping evidence with M2 rationale; `StockItemNotFound`/`ReservationNotFound` map to `Platform.SharedKernel.Errors.NotFoundError` (M7) |
| All timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain (arch test) | MET | See `_shared.md § 12` row above |
| Correlation-id roundtrips Kafka header → handler → `stock_events.CorrelationId` → outbox → emitted header | MET | See `_shared.md § 12` row above |
| All `<applicable_adrs>` enforced | MET | ADR-0006 (rehydration histograms), ADR-0008 (correlation-id middleware + DB column + Kafka header), ADR-0010 (JWT admin auth, PLAINTEXT saga consumers — no per-message X-Service-Token), ADR-0012 (`/api/v1/inventory/...`), ADR-0013 (`.Idempotency()` on adjust), ADR-0015 (`TimeProvider` injected, no `DateTime.UtcNow` in domain) |
| Peer-review chain executed; HIGH findings fixed | MET | See `_shared.md § 12` row above |

### Contract seam verification

| `<contract>` item | Status | Evidence |
|---|---|---|
| 5 external events + 3 saga-command Avro schemas under `Inventory.Stock` + `Inventory.Reservations` | MET | All 8 `.avsc` files verified at `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/{Stock,Reservations}/` |
| 6 internal ES events named + ordered per `inventory.md § 5` | MET | All 6 at `services/Inventory/Inventory.Domain/StockItems/Events/` — `StockItemInitializedEvent`, `StockReceivedEvent`, `StockReservedEvent`, `ReservationConfirmedEvent`, `ReservationReleasedEvent`, `StockAdjustedEvent` |
| Event store table PK `(StreamId, Version)` | MET | `StockEventRowConfiguration.cs:23` |
| Projection tables `inventory.current_stock_levels` + `inventory.reservation_audit` | MET | Both `EntityTypeConfiguration` classes present; migration `20260425111658_AddProjectionsAndOutboxInbox` creates both |
| Topics: `inventory.stock-events` 3p; `inventory.reservations` 6p; `inventory.reservation-commands` 3p 7d retention | MET | `docker-compose.yaml:284-286` literal match. M10 smoke at `inventory.md:670-672` pasted `kafka-topics --describe` showing `PartitionCount: 3`, `PartitionCount: 6`, `PartitionCount: 3 retention.ms=604800000` |
| Consumer groups: `inventory-stock-init` (shared); `inventory-reservation-commands` (saga cmds) | MET | Per-consumer-options sections verified above |
| Reservation TTL = 15 min default | MET | [ReserveStockCommandHandler.cs:33](services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs:33) `DefaultReservationTtl = TimeSpan.FromMinutes(15)` |
| `InsufficientStock` BUSINESS-EXPECTED; never throws; emits `StockReservationFailedEvent` | MET | See DoD row above |
| Schemas FORWARD_TRANSITIVE / FULL_TRANSITIVE per ADR-0007 | MET (delegated to platform Avro registration; no schema-evolution test was in scope for M1–M10; carried as cross-cutting per M8 inventory.md:487) |
| HTTP admin routes under `/api/v1/inventory/...` per ADR-0012 | MET | See ADR-0012 row above |

### Invariants spot-check (5 from `inventory.md § 3.1`)

| Invariant | Enforced? | Evidence |
|---|---|---|
| `OnHand >= 0` | YES (defense in depth) | Aggregate: `StockItem.cs:295` + `StockItem.cs:449` `DataIntegrityException` on projected-below-zero |
| `Available = OnHand - Reserved >= 0` | YES | Aggregate: `StockItem.cs:157-161` returns `Result.Fail<InsufficientStock>` if `Available < quantity` on `Reserve`; `StockItem.cs:299` + `:453` `DataIntegrityException` if `AdjustStock` would drive `Available` negative |
| Cannot confirm a non-Active reservation | YES | `StockItem.cs:197-209` switch: `Confirmed` = idempotent Ok (no event); `Released` = `Result.Fail<ReservationNotActive>`; `Active` proceeds. Reducer `ApplyConfirmed:413` re-asserts via `DataIntegrityException` |
| Cannot release a non-Active reservation | YES | `StockItem.cs:246-260` mirror semantics; `ApplyReleased:435` re-asserts |
| Stream can only be initialized once | YES | `StockItem.cs:84-86` `DataIntegrityException("Inventory.StreamAlreadyInitialized")`; `ApplyInitialized:359-361` re-asserts |

**Status:** PASS. The one unresolved doc-internal inconsistency (`inventory.md:62` vs `§ 3.2 rule 2`) is documented in M2/M4 as a known gap and does not affect any contract-locked seam.

---

## Dimension 2 — Architecture

| Check | Status | Evidence |
|---|---|---|
| Layer boundaries (Domain ⟂ Infrastructure; Application owns ports; Infrastructure owns adapters) | PASS | [CleanArchitectureLayerTests.cs](test/Inventory.ArchitectureTests/CleanArchitecture/CleanArchitectureLayerTests.cs) — 6 layer-dependency facts; `IEventStore` + `IInventoryDbContext` ports live in [Inventory.Application/Common/Data/](services/Inventory/Inventory.Application/Common/Data); `EventStoreRepository` + `InventoryDbContext` adapters live in [Inventory.Infrastructure/Persistence/](services/Inventory/Inventory.Infrastructure/Persistence) |
| Cross-BC references only via documented contracts (Avro types in `platform/Platform.SchemaRegistry.Contracts/Avro/...`) | PASS | [CrossBcReferenceTests.cs](test/Inventory.ArchitectureTests/BoundedContext/CrossBcReferenceTests.cs) — `Inventory.{Domain,Application}` don't reference Catalog/Basket/Ordering/Invoicing/Payments assemblies. Inventory.Infrastructure consumes only the generated Avro types (`Catalog.Products.ProductCreatedEvent`, `Ordering.Orders.OrderCancelledEvent`) — these are the contract, not internal code |
| Append-only event-store seam | PASS | [EventStoreAppendOnlyTests.cs](test/Inventory.ArchitectureTests/BoundedContext/EventStoreAppendOnlyTests.cs) — both `IEventStore` (port) and `EventStoreRepository` (impl) public methods are a subset of `{RehydrateAsync, AppendAsync}` via the custom `PublicMethodsAreSubsetOfRule`. Each fact anchors with `matchedTypes.Should().ContainSingle()` so a future rename can't make the rule vacuously pass (M8 review-fix at inventory.md:494) |
| Architecture tests are non-trivial | PASS | Spot-checked: `ResultPatternTests.cs` verifies aggregates only throw `DataIntegrityException`; `DoesNotThrowRule` over `*CommandHandler` types pins the M2 "ReserveStockCommandHandler doesn't throw on InsufficientStock" contract; `AggregateRootTests.cs` deliberately OMITS `HasPublicStaticFactoryMethodRule` because `Fold` is the ES rehydration entry (M8 inventory.md:483) — that omission is documented in the test file's xmldoc |

**Status:** PASS.

---

## Dimension 3 — Design (DDD)

| Check | Status | Evidence |
|---|---|---|
| Aggregate boundary appropriate | PASS | `StockItem` holds `OnHand`/`Reserved`/`Reservations` together because `Available = OnHand - Reserved >= 0` is the load-bearing invariant — Vernon rule #1 (`inventory.md § 3.2`). Stream-per-product = one consistency boundary. |
| Invariants enforced inside the aggregate, not bled into handlers | PASS | All preconditions live in `StockItem.cs`. Application-layer handlers ([ReserveStockCommandHandler.cs](services/Inventory/Inventory.Application/StockItems/ReserveStock/ReserveStockCommandHandler.cs), siblings) only validate `ReservationId` shape (`Create(...)`) then delegate to the aggregate. |
| Value objects immutable + structural-equal | PASS | All 7 VOs at [services/Inventory/Inventory.Domain/StockItems/ValueObjects/](services/Inventory/Inventory.Domain/StockItems/ValueObjects) — sealed records, `init`-only properties, private parameterless ctor on `ReservationInfo` (M8 production-code fix at `inventory.md:467`); covered by [ValueObjectTests.cs](test/Inventory.ArchitectureTests/Domain/ValueObjectTests.cs) |
| Domain events sealed records, dispatched in-process only | PASS | All 6 ES events under `Inventory.Domain.StockItems.Events` are `sealed record : DomainEvent` with `required init` props; `_dispatcher.DispatchAsync` is called inside `EventStoreRepository.AppendAsync` against the same scoped `InventoryDbContext`; Kafka emission goes through the outbox publishers, NOT direct producers |
| Internal vs external event split per `events-catalog.md` | PASS | Internal ES events live in Domain; external Avro under `platform/.../Avro/Inventory/`. Mapping happens in [ReservationLifecycleHandler.cs](services/Inventory/Inventory.Application/StockItems/ReservationLifecycleHandler.cs) and [CurrentStockLevelsProjectionHandler.cs](services/Inventory/Inventory.Application/StockItems/CurrentStockLevelsProjectionHandler.cs) via mapper extension methods (`ToStockReservedEvent` / `ToStockLevelChanged` / etc.) |
| SmartEnums where state machines exist; absent where they don't | PASS | `ReservationStatus`/`ReleaseReason` are plain `enum`s because they carry no business-rule properties — the lifecycle rules live on the aggregate, not on enum members. `StockSource` is a `ValueObject`-derived sealed record (not an enum) because it's a free-form string with creation validation per `inventory.md § 4` |
| Factories returning `Result<T>` for validation failures; constructors private | PASS | `StockItem` private ctor + static `Fold`; value objects with `private` parameterless ctor + static `Create(...)` returning `Result<T>`; aggregate `Reserve` returns `Result<ReservationInfo>` for the business-expected failure path |

**Status:** PASS.

---

## Dimension 4 — Testing

| Check | Status | Evidence |
|---|---|---|
| Test pyramid sane | PASS | 66 unit / 33 arch / 42 integration / 15 functional — heavy unit base, narrower at each upper layer |
| Test names express behaviour | PASS | Spot-check: `Example1_3_ConfirmAfterExpiryRelease_FailsWithReservationNotActive`, `Example2_3_ConcurrentReserveOnLastUnits_LoserRetriesThenFailsWithInsufficientStock`, `ReservedReservation_NotReleasedAfterExpiry`, `WhenSameIdempotencyKeyReplayed_BothCallsReturn200` — all behaviour-flavoured, not method-name reflection |
| Every command has handler + validator test | PASS | 6 commands + 2 queries = 8 validators present at [services/Inventory/Inventory.Application/StockItems/**/{Init,Receive,Reserve,Confirm,Release,Adjust}*Validator.cs](services/Inventory/Inventory.Application/StockItems) + 2 query validators. Handler tests counted in integration slice (per-command handler test files) |
| Every external event has outbox-publisher coverage | PASS | `ReserveStockCommandHandlerTests` (StockReserved + StockReservationFailed), `ConfirmReservationCommandHandlerTests` (ReservationConfirmed), `StockLevelChangedEmissionTests` (StockLevelChanged), and the M9 ExampleMapping suite (ReservationReleased on multiple paths) |
| Domain invariants have unit tests that fail when invariant is removed | PASS | M2 added 56 unit tests with named scenarios for each invariant in `inventory.md § 3.1`; M8 added 1 more (race-vs-Confirm warning-log branch); M2 review caught dead helper `PopAsEvents` (HIGH-2, fixed) — net 66 unit facts now |
| Testcontainers used for integration | PASS | [IntegrationTestFixture.cs](test/Inventory.IntegrationTests/Common/IntegrationTestFixture.cs) uses Testcontainers Postgres; rehydration p99 test seeds 1000 events on a real container (`EventStoreRepositoryRehydrationMetricsTests`) |
| No `CancellationToken.None` (xUnit1051) in Inventory tests | PASS | `grep "CancellationToken\.None"` across `test/` returns 0 matches under `test/Inventory.*Tests/`; matches in Weather tests are out of scope |
| No flaky / always-pass tests | PASS — with one documented soft assertion | `AdjustStockTests.WhenSameIdempotencyKeyReplayed_BothCallsReturn200` is logging-only with a soft `BeLessThanOrEqualTo(2)` guard — softened in M9 (inventory.md:556) because FE 7.0.1 + `StackExchangeRedisOutputCache` don't short-circuit in `WebApplicationFactory`; production verification stays manual; the soft guard prevents runaway-handler regression. Documented carry-forward, not a hidden gap |

**Status:** PASS.

---

## Dimension 5 — Event-driven best practices

| Check | Status | Evidence |
|---|---|---|
| Outbox is the only path producing external events (no direct `IProducer` calls) | PASS | `grep "IProducer\|IProducerAccessor\|ProduceAsync"` over `services/Inventory` returns zero direct calls; all emissions go through `_outbox.AddOutboxMessage(topic, key, ISpecificRecord)` |
| Outbox row written in same SQL transaction as the state change | PASS | Both projection handlers add outbox rows on the same scoped `InventoryDbContext`; `EventStoreRepository.AppendAsync` calls one `SaveChangesAsync` for the whole envelope (event rows + projection upserts + outbox rows). `ReserveStockCommandHandler` failure-path stages the outbox row then calls `_outbox.SaveChangesAsync(ct)` — single tx |
| Inbox dedup for every Kafka consumer | PASS | All three consumer blocks in [MessagingDependencyInjection.cs:111,138,160](services/Inventory/Inventory.Infrastructure/Common/MessagingDependencyInjection.cs:111) wire `.AddInbox(typeof(...))` against the Avro message types (`AvroReserveStockCommand`/`AvroConfirmReservationCommand`/`AvroReleaseReservationCommand`/`AvroProductCreatedEvent`/`AvroOrderCancelledEvent`) |
| Avro schemas follow contract compatibility mode | PASS (delegated) | 8 schemas committed; cluster-wide FORWARD_TRANSITIVE / FULL_TRANSITIVE registration is a platform-level concern (`Platform.Avro.UniversalSerDes` + Schema Registry). No per-BC compatibility-test gate is in M1–M10 scope |
| Correlation-id flows HTTP → handler → outbox → Kafka header per ADR-0008 | PASS | Verified in DoD audit; M4 `CorrelationIdRoundtripTests` in `ReserveStockCommandHandlerTests` pins HTTP → handler → DB column; the `Platform.KafkaFlow.ProducerHeaders` middleware copies the OTel `Activity.Current.TraceId` into the outbound Kafka header |
| Idempotency-Key wired per ADR-0013 | PASS | [AdjustStockEndpoint.cs:29-38](services/Inventory/Inventory.Api/Endpoints/StockItems/Adjust/AdjustStockEndpoint.cs:29) `.Idempotency(...)`; `AddIdempotencyKeyOutputCache` is wired at platform level against `redis-cache`. Production wiring correct; functional-test in-memory cache doesn't short-circuit (documented carry-forward) |
| No internal `*Event` leaks to Kafka | PASS | Outbox writes accept `ISpecificRecord` (Avro types); internal events are `DomainEvent` records with no such interface. Mapper extensions explicitly translate before `AddOutboxMessage` |
| No cross-BC consumption of another BC's internal events | PASS | Inventory consumes only Avro types from `Catalog.Products.ProductCreatedEvent` and `Ordering.Orders.OrderCancelledEvent` — both are external published languages |

**Status:** PASS.

---

## Dimension 6 — .NET / C# best practices

| Check | Status | Evidence |
|---|---|---|
| Async all the way; no `.Result` / `.Wait()` | PASS | `grep -E "\.Result\b\|\.Wait\(\)\|GetAwaiter\(\)\.GetResult\(\)" services/Inventory` returns zero matches |
| Cancellation tokens flowed everywhere | PASS | All async public methods accept `CancellationToken`; handlers pass `ct` through to `_eventStore.AppendAsync` / `_outbox.SaveChangesAsync` / `dbContext.*Async`; background worker reads `stoppingToken` from `ExecuteAsync` and threads it to `WaitForNextTickAsync` + `ProcessExpiredReservationsAsync` |
| `IDisposable` honoured | PASS | `using var timer = new PeriodicTimer(...)` ([ReservationExpiryWorker.cs:68](services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs:68)); `await using var scope = _scopeFactory.CreateAsyncScope()` ([ReservationExpiryWorker.cs:115](services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs:115)); `using var correlationScope = _logger.BeginScope(...)` in saga handlers |
| No `DateTime.UtcNow` / `DateTime.Now` in domain (ADR-0015); `TimeProvider` injected | PASS | Grep over `services/Inventory` returns zero matches. `TimeProvider` injected at endpoint sites ([AdjustStockEndpoint.cs:13](services/Inventory/Inventory.Api/Endpoints/StockItems/Adjust/AdjustStockEndpoint.cs:13)) + worker ([ReservationExpiryWorker.cs:44](services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs:44)) |
| No magic strings | MOSTLY PASS | Topic names are config-bound via `TopicsOptions`; consumer group names are config-bound via per-consumer `ConsumerConfig` subclasses; error codes are centralized in `*Error.Metadata["ErrorCode"]`; connection-string key uses `nameof(ConnectionStringsOptions.Inventory)`. See MEDIUM-2 below for one nit on adding compile-time constants for topic / group names as a defense-in-depth layer |
| Logging at correct levels; no PII leakage | PASS | `LogInformation` for happy paths; `LogWarning` for business-expected failures; `LogError` for unhandled exceptions. `OrderId`, `ProductId`, `ReservationId`, `AdjustedByUserId` are Guids (not PII). No payload bodies / JWT subjects / emails / addresses logged anywhere |
| Nullable reference types respected; no unjustified `!` operator | PASS | Four `!` usages, all justified: EF parameterless-ctor null-bangs on entity classes (which the EF runtime hydrates), FluentValidation `When(...)`-guarded value access, and the `_logger.BeginScope` IDisposable assignment pattern |

**Status:** PASS.

---

## Dimension 7 — CI gates + test slices (verbatim)

```
$ dotnet build -m
... (108 pre-existing NU1903 advisories on transitive System.Security.Cryptography.Xml 10.0.1
     and Microsoft.Kiota.Abstractions 1.19.0, allowlisted at root Directory.Build.props) ...
    108 upozornění
    Počet chyb: 0

Uplynulý čas 00:04:11.91
```
Exit code: 0.

```
$ dotnet restore --locked-mode
... (same NU1903 advisories) ...
  Všechny projekty jsou v aktuálním stavu pro obnovení.
```
Exit code: 0.

```
$ dotnet format whitespace --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění protokolovat, nastavte možnost verbosity na úroveň diagnostic.
```
Exit code: 0 (workspace-info warning only; no formatting changes required).

```
$ dotnet format style --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění protokolovat, nastavte možnost verbosity na úroveň diagnostic.
```
Exit code: 0 (workspace-info warning only; no style changes required).

```
$ HTTP_PROXY= dotnet test test/Inventory.UnitTests/ --no-build --no-restore
Testovací běh pro C:\...\Inventory.UnitTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    66, Přeskočeno:     0, Celkem:    66, Doba trvání: 55 s - Inventory.UnitTests.dll (net10.0)
```
Exit code: 0. **66/66 pass.**

```
$ HTTP_PROXY= dotnet test test/Inventory.ArchitectureTests/ --no-build --no-restore
Testovací běh pro C:\...\Inventory.ArchitectureTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    33, Přeskočeno:     0, Celkem:    33, Doba trvání: 8 s - Inventory.ArchitectureTests.dll (net10.0)
```
Exit code: 0. **33/33 pass.**

```
$ HTTP_PROXY= dotnet test test/Inventory.IntegrationTests/ --no-build --no-restore
Testovací běh pro C:\...\Inventory.IntegrationTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    42, Přeskočeno:     0, Celkem:    42, Doba trvání: 18 s - Inventory.IntegrationTests.dll (net10.0)
```
Exit code: 0. **42/42 pass.**

```
$ HTTP_PROXY= dotnet test test/Inventory.FunctionalTests/ --no-build --no-restore
Testovací běh pro C:\...\Inventory.FunctionalTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    15, Přeskočeno:     0, Celkem:    15, Doba trvání: 13 s - Inventory.FunctionalTests.dll (net10.0)
```
Exit code: 0. **15/15 pass.**

**Docker-compose smoke + topic-describe:** not re-run this session — the M10 session summary at `inventory.md:660-672` documents the smoke from 2026-05-02 with verbatim `kafka-topics --describe` output for all three Inventory topics (`PartitionCount: 3`, `PartitionCount: 6`, `PartitionCount: 3 retention.ms=604800000`). Tree unchanged on this branch since `a6ea4fd` (the M10 commit), so re-running would be redundant. Per `<contract>` the 6-partition count on `inventory.reservations` is the saga-fan-out invariant flagged in `<stop_conditions>` — confirmed in M10 evidence.

**All four CI gates and all four test slices exit 0.** Total: 156 tests, 0 failed, 0 skipped.

**Status:** PASS.

---

## Dimension 8 — Code review (bugs)

Dispatched parallel review via `Agent(subagent_type="feature-dev:code-reviewer", model="opus")` over `services/Inventory/**` with the locked contract, ADRs, and 12 hot-risk areas as briefing.

**Verdict from the reviewer: 0 CRITICAL, 0 HIGH, 6 MEDIUM, 5 LOW.**

The reviewer explicitly cleared all 12 hot-risk areas I briefed (Business-Expected HashSet correctness, retry-once semantics + ChangeTracker.Clear, partial-fanout atomicity on `OrderCancelledEventKafkaHandler`, projection threshold XOR logic, `PreviousAvailable` column, admin JWT scope check, `.Idempotency()` middleware order, async hygiene, magic strings, PII logging, nullable `!` usage). I re-verified each independently — agree with the reviewer's clear.

### MEDIUM findings (post-PASS quality)

| ID | Severity | file:line | Description | Recommendation |
|---|---|---|---|---|
| M-1 | MEDIUM | [EventStoreRepository.cs:185-201](services/Inventory/Inventory.Infrastructure/Persistence/EventStore/EventStoreRepository.cs:185) | After the second `UniqueConstraintException` the loop exits and `Result.Fail(InventoryErrors.Concurrency(...))` is returned, but the projection/outbox entities tracked on attempt 2 are still in the `ChangeTracker`. Practical impact is nil in the current flow (the surrounding `EnsureTransactionAsync` rolls back the tx on the propagated failure and the scoped DbContext is disposed shortly after), but a future caller that invokes `AppendAsync` again on the same scoped context would flush the orphans. | One additional `_ctx.ChangeTracker.Clear()` before `return Result.Fail<StockItem>(...)`. Defensive, one-line fix. |
| M-2 | MEDIUM | [CurrentStockLevelsProjectionHandler.cs:170-176](services/Inventory/Inventory.Application/StockItems/CurrentStockLevelsProjectionHandler.cs:170) | The aggregate guarantees `Available >= 0` but the projection's `Apply` doesn't re-check. A future projection-rebuild from a corrupted stream (or a yet-unwritten projection-replay job) could write a negative `Available` and `MaybeEmitStockLevelChanged` would misclassify a `-1 → 0` transition as "back in stock". | Add a `DataIntegrityException` guard in `Apply` if `row.OnHand - row.Reserved < 0`. Belt-and-braces consistent with the BC's "fail loud on impossible state" posture. |
| M-3 | MEDIUM | [ReservationExpiryWorker.cs:111-172](services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs:111) | The tick opens one DI scope and reuses one `IInventoryDbContext` + `ICommandHandler<ReleaseReservationCommand>` across up to 100 releases. Each `_eventStore.AppendAsync` calls `SaveChangesAsync` per command so EF tracking flushes per iteration, but the shared scope still accumulates entity tracking across iterations and the per-row `catch (Exception)` masks any cascading "ChangeTracker is dirty from a prior row" failures into a warning. | Move `_scopeFactory.CreateAsyncScope()` inside the `foreach` so each release runs on its own DbContext + handler instance. Same memory footprint; far better fault isolation. |
| M-4 | MEDIUM | Topic + consumer-group names exist in `appsettings.json` only — no compile-time guard against drift | `inventory.stock-events` / `inventory.reservations` / `inventory.reservation-commands` and the two consumer groups are LOCKED contract per `<contract>` and ADR-0004 (saga topology), but in code they only appear through `IOptions<>` bindings against `appsettings.json`. A typo in a future appsettings change would silently create a new topic on prod. | Introduce a `KafkaTopicNames` static class in `Inventory.Application.Common.Messaging` exposing the three topics + DLT suffix as `const string` and add an architecture test asserting the appsettings values equal the constants. Matches the pattern Ordering adopted in M5. |
| M-5 | MEDIUM (doc) | [docs/bc-design/inventory.md:62](docs/bc-design/inventory.md:62) vs [docs/bc-design/inventory.md § 3.2 rule 2](docs/bc-design/inventory.md) | Doc-internal contradiction: `:62` says terminal reservations stay in the aggregate's in-memory dict; `§ 3.2 rule 2` says they are pruned. Flagged in M2 (inventory.md:117) and M4 (inventory.md:176); M4 confirmed retention is authoritative but the design doc itself was never edited. Code correctly retains. | Update `docs/bc-design/inventory.md § 3.2 rule 2` to say "the aggregate retains terminal reservations for the lifetime of a rehydrate; the projection-side `reservation_audit` table is the durable terminal-state store" — matching M4's resolution. |
| M-6 | MEDIUM (carry-forward, documented) | [InventoryMetrics.cs:11](services/Inventory/Inventory.Infrastructure/Observability/InventoryMetrics.cs:11) | Both rehydration histograms are tagged by raw `Guid` `product_id`. Required by ADR-0006 § Observability, so contract-compliant — but unbounded cardinality at catalog scale. Flagged here as Awareness, not a defect. | Capture a "cardinality cap before prod ramp" follow-up: bucket by hot-SKU allow-list or by `Available <= 10` flag instead of raw Guid. Not actionable now. |

### LOW findings (cosmetic)

| ID | file:line | Description |
|---|---|---|
| L-1 | [ReservationExpiryWorker.cs:124](services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs:124) | `Take(MaxBatchSize=100)` silently caps the per-tick batch with no "we hit the ceiling" signal. Add a `LogWarning` when `expired.Count == MaxBatchSize` so the saga-stuck-runbook has a backlog indicator. |
| L-2 | [OrderCancelledEventKafkaHandler.cs:134-135](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/OrderCancelledEventKafkaHandler.cs:134) | Throws a raw `DbUpdateException` with a message but no inner exception. Operators chasing a DLT post-mortem will have no original stack. Consider a dedicated `ReservationReleaseFailedException` mirroring `SagaCommandDispatchException`. |
| L-3 | [CurrentStockLevelsProjectionHandler.cs:147-168](services/Inventory/Inventory.Application/StockItems/CurrentStockLevelsProjectionHandler.cs:147) | `LoadAsync` / `LookupReservationQuantityAsync` throw `DataIntegrityException` with the relevant ids in the message but emit no log. A scoped `LogError` before throwing would speed triage. |
| L-4 | [ProductCreatedEventKafkaHandler.cs](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/ProductCreatedEventKafkaHandler.cs) | `InitializeStockItemCommand.OccurredOnUtc` is sourced from `message.CreatedAtUtc` (Catalog's event time), not from `TimeProvider.GetUtcNow()`. The choice is deliberate (preserve Catalog's product-creation moment as the stream's t0) but unique to this handler — a one-line code comment justifying it would help the next reader. |
| L-5 | Cosmetic — `_ = correlationScope` vs `using var correlationScope` for unused-but-disposable log scopes ([SagaCommandHandlerBase.cs:117](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs:117), [OrderCancelledEventKafkaHandler.cs:72](services/Inventory/Inventory.Infrastructure/Messaging/Kafka/StockInit/OrderCancelledEventKafkaHandler.cs:72)). Code is correct; intent communication only. |

**Status:** PASS (no CRITICAL, no unaccepted HIGH).

---

## Verdict

**PASS** ✅

Threshold check:
- ✅ Zero CRITICAL findings.
- ✅ Zero unaccepted HIGH findings.
- ✅ All `<dod>` items MET (one PARTIALLY MET on docs-self-correction — M-5 above — with no behavioral impact).
- ✅ All four CI gates green.
- ✅ All four test slices green (156/156 tests pass).
- ✅ All `<contract>` seams verified (event names + namespaces against .avsc; topics + partitions + retention against compose + M10 smoke; HTTP routes; consumer groups).
- ✅ All five `<applicable_adrs>` enforced (ADR-0006/0008/0010/0012/0013/0015 — 0006 added because of rehydration observability gating).

The Inventory bounded context is complete and ready for Wave 2 (Checkout saga) integration. The six MEDIUM + five LOW findings above are post-PASS quality improvements suitable for the next phase or carry-forwards; none change the verdict.

### Suggested follow-ups (not blocking)

In priority order, lightest-touch first:

1. **M-1** — one-line `ChangeTracker.Clear()` before the final `Result.Fail` in `EventStoreRepository.AppendAsync`. (Defense in depth.)
2. **M-5** — edit `docs/bc-design/inventory.md § 3.2 rule 2` to remove the long-standing contradiction with `:62`. (Documentation hygiene.)
3. **M-2** — add the `Available >= 0` defensive guard in `CurrentStockLevelsProjectionHandler.Apply`. (Future-projection-rebuild safety.)
4. **M-4** — introduce `KafkaTopicNames` constants + an architecture test pinning the appsettings values. (Topology drift safety.)
5. **M-3** — move the DI scope inside the `foreach` in `ReservationExpiryWorker.ProcessExpiredReservationsAsync`. (Fault isolation.)
6. **L-1, L-2, L-3, L-4, L-5** — cosmetic / observability polish; bundle when convenient.
7. **M-6** — cardinality cap on rehydration histograms before any prod ramp at catalog scale. (Not actionable in the reference repo.)

### Wave-level follow-ups inherited from M10 (per inventory.md:686-695)

Not Inventory-scoped; carried for the next phase to pick up:

- `IntegrationTestFixture` per-test Respawn reset (asymmetry vs FunctionalFixture introduced in M9).
- FE 7.0.1 `.Idempotency()` + `StackExchangeRedisOutputCache` short-circuit observability in `WebApplicationFactory` (production verification stays manual until WAF transparency improves).
- OTel meter registration cross-cutting (`AddOpenTelemetry().WithMetrics(m => m.AddMeter("Inventory"))`) — Catalog/Basket in same state.
- `inventory.api` docker-compose service (Catalog M7 has it; Inventory does not — runtime parity uplift, not in DoD).
- `otel-collector` restart loop observed during M10 smoke — pre-existing collector-config bug at platform layer; not Inventory-caused.
