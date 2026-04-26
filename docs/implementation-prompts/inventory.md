# Master System Prompt — Implement the **Inventory** Bounded Context

> Paste this as the first message in a fresh Claude Code session for `C:\Users\dcapc\Desktop\Git\DotNetAtlas`.

<thinking_first>
Before writing any code, do these in your **first response** — explicitly, in order:

1. **Read every file under `<reading_order>`** in order. State your understanding of what's locked vs open.
2. **Verify prerequisites.** List anything in `<prerequisites>` that isn't satisfied. STOP and ask if any.
3. **Surface contradictions** (`file:line`).
4. **Confirm applicable ADRs.** For each ADR in `<applicable_adrs>`, name what it implies for THIS BC's code.
5. **State your plan.** Group `<dod>` items into commit milestones. Confirm with the user before starting code.
6. **Acknowledge stop conditions** from this prompt and `_shared.md § 9`.
</thinking_first>

<mission>
You implement the **Inventory** bounded context — **the single Event-Sourced example** in the solution per [ADR-0006](../adr/0006-event-sourcing-for-inventory.md). Your output is a working, tested, compiling service that showcases **event sourcing with per-ProductId streams** + **projection tables built in-process** + **reservation TTL auto-release via background worker** + **saga-driven reservation lifecycle**. When the session ends, the Checkout saga can reserve stock, confirm reservations, release on compensation, and expired reservations auto-release.
</mission>

<prerequisites>
- Wave 0 platform prep merged. Specifically: BCL `TimeProvider` is auto-registered by the Generic Host; `Platform.ServiceDefaults` has correlation-id + service-auth; 3 new Inventory topics (`inventory.stock-events`, `inventory.reservations`, `inventory.reservation-commands`) + `outbox-relay-inventory` container; Keycloak `inventory-service` client.
- Catalog's `ProductCreatedEvent` Avro schema registered (Inventory consumes to initialize streams). Inventory can scaffold + unit-test in parallel; integration-test the Catalog-event consumer after Catalog lands.

**Verified repo state as of 2026-04-24** (from Wave-1 session 1 exploration):

| Prereq | Status | Evidence |
|---|---|---|
| Kafka topics (3 × correct partitions + retention) | SATISFIED | `docker-compose.yaml:284-286` |
| `outbox-relay-inventory` container | SATISFIED | `docker-compose.yaml:451-481` (schema=`inventory`, DB=`Inventory`, port 8093) |
| Correlation-id middleware | SATISFIED | `platform/Platform.ServiceDefaults/CorrelationId/*` |
| Service-auth middleware | SATISFIED | `platform/Platform.ServiceDefaults/Auth/ServiceAuthServiceCollectionExtensions.cs` |
| Outbox/Inbox platform libs | SATISFIED | `platform/Platform.ReliableMessaging.{Outbox,Inbox}.EFCore/*` |
| `TimeProvider` auto-registration | SATISFIED | Generic Host default in `.NET 10` |
| Keycloak `inventory-service` client | SATISFIED | `src/keycloak/realm-export.json` — client exists; scopes `inventory.read`, `inventory.commands.{reserve,confirm,release}` defined in realm clientScopes. **Verify** the `optionalClientScopes` array includes these on the client itself; patch realm export if missing. |
| Catalog `ProductCreatedEvent.avsc` | **MISSING** | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/` contains only `.gitkeep`. See `<wave_progress>` for the stub strategy. |
| Ordering `OrderCancelledEvent.avsc` | **MISSING** | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/` empty. Same stub treatment. |
</prerequisites>

<wave_progress>
Living log. Each session appends its own block before ending; next-session agent reads top-to-bottom and continues from the last completed milestone.

### Session 1 (2026-04-24) — M1 complete

**Delivered:**
- 4 Inventory service projects scaffolded to mirror the Catalog template: `services/Inventory/Inventory.{Domain,Application,Infrastructure,API}/` each with `IAssemblyMarker.cs` + proper project/platform references
- 4 test projects: `test/Inventory.{UnitTests,ArchitectureTests,IntegrationTests,FunctionalTests}/` with `Placeholder.cs` stubs
- `DotNetAtlas.slnx` updated — new `/services/Inventory/` folder with 4 projects + 4 test projects under `/test/`
- `Inventory.API/Program.cs` minimal (matches Catalog's M1-level scaffold, 9 lines, `MapGet("/")` only)
- Full-solution `dotnet restore` + `dotnet build -m` → **0 errors** (pre-existing NU1903 vulnerability warnings remain, allowed by root `WarningsNotAsErrors`)

**Plan file** (detailed decisions + file inventory): `C:\Users\david.capcuch\.claude\plans\implement-the-inventory-bounded-snug-scone.md`

**Design decisions resolved (persisted here so fresh sessions don't re-explore):**

| Question | Resolution | Rationale |
|---|---|---|
| Rehydration API shape | static `StockItem.Fold(IEnumerable<IDomainEvent>) → StockItem` | Pure-function; testable with curated event list; matches `<example_design_decision>` |
| Event-store persistence | EF Core entity `StockEventRow` with `jsonb` Payload column — thin `EventStoreRepository` (not Dapper) | Same DbContext tx as projections + outbox; no dual-write; `jsonb` keeps events legible in DB |
| Conflict detection | catch `EntityFrameworkCore.Exceptions.PostgreSQL.UniqueConstraintException` | Package already in `services/Directory.Packages.props`; no hand-rolled exception parsing |
| Retry-once policy | in `EventStoreRepository.AppendAsync`: catch → rehydrate → re-apply command → re-attempt. Second conflict → `Result.Fail(InventoryErrors.ConcurrencyConflict)` | Per `inventory.md § 10.2` |
| Projection handler organization | ONE multiplexed handler per projection (one for `current_stock_levels`, one for `reservation_audit`) — each handles all 6 ES event types internally | Upsert logic co-located; simpler registration |
| `ReservationExpiryWorker` cadence | 60s via `PeriodicTimer`, `TimeProvider.CreatePeriodicTimer(TimeSpan.FromSeconds(60))` | Confirmed per `inventory.md § 11.1` |
| Idempotency on projections | `LastVersion` column on `current_stock_levels`; skip if `event.Version <= row.LastVersion` | Per `inventory.md § 10.3` |
| Admin `.Idempotency()` | YES on `POST /api/v1/inventory/stock-items/{productId}/adjust`, backed by `redis-cache` | Per ADR-0013 |
| Aggregate `Reservations` dict after Confirm/Released | Retain (don't prune) so handlers can distinguish "unknown rid" from "not Active" | Per `inventory.md § 3.1`; ReservationAuditView is the ops SoT |
| Keycloak scope for admin `AdjustStock` | Re-use `inventory.commands.reserve` for v1 (not a dedicated admin scope) | Simplifies realm; future wave can split if needed |

**Deviations from locked/stated contracts — acknowledged and accepted:**

1. **Consumer-group reuse** — User (session 1) unlocked the "distinct consumer groups" clause. The single group `inventory-stock-init` is now reused for BOTH the `ProductCreatedEvent` consumer AND the `OrderCancelledEvent` consumer. This deviates from `eshop-master-design.md § E.10` guidance. **Rationale:** `events-catalog.md:96` already lists it this way with an "(reuses group? — see § 7)" parenthetical; matching the events-catalog keeps one source of truth in the BC-design layer. See `<contract>` below — updated accordingly.
2. **Catalog `ProductCreatedEvent.avsc` stubbed** — Schema absent upstream; the `InitializeStockItemCommandHandler` is implemented and DI-callable (unit-testable), and a Kafka consumer class exists in `Messaging/Kafka/Catalog/ProductCreatedEventKafkaHandler.cs` with a logs-and-skips body awaiting the Avro-generated type. The `SimulateProductCreatedInitializesStream` integration test (success-criteria line 108) is SKIPPED until Catalog lands its schema. **When Catalog ships:** drop the stub, wire the real Avro type, re-enable the integration test.
3. **Ordering `OrderCancelledEvent.avsc` stubbed** — Same pattern as #2.

**Next milestone: M2** — Domain layer (`StockItem`, ES events, `Fold`, reducers) + unit tests. Project is `services/Inventory/Inventory.Domain` (scaffolded empty, just `IAssemblyMarker.cs`). Unit-test project is `test/Inventory.UnitTests` (delete `Placeholder.cs`).

### Session 2 (2026-04-24) — M2 complete

**Delivered:**
- `services/Inventory/Inventory.Domain/StockItems/StockItem.cs` — sealed `AggregateRoot<Guid>` with private ctor, static `Fold(IEnumerable<DomainEvent>)`, six command methods (`Initialize`, `ReceiveStock`, `Reserve`, `ConfirmReservation`, `ReleaseReservation`, `AdjustStock`), private `Raise` + `Apply` reducers that keep in-memory state and the emitted event stream in sync by construction
- 6 ES events under `StockItems/Events/`: `StockItemInitializedEvent`, `StockReceivedEvent`, `StockReservedEvent`, `ReservationConfirmedEvent`, `ReservationReleasedEvent`, `StockAdjustedEvent` — all `sealed record : DomainEvent` with `required init` props; fields match `inventory.md § 5.1-5.6` verbatim
- 7 value objects under `StockItems/ValueObjects/`: `Quantity`, `ReservationId`, `ReservationInfo`, `ReservationStatus` (enum), `ReleaseReason` (enum), `StockSource` (with `ReceivingDock` / `Returns` / `TransferIn` canonical constants), `StockItemSnapshot`
- 3 error types under `StockItems/Errors/`: `InsufficientStockError` + `ConcurrencyError` (verbatim from `error-taxonomy.md § 3.4` lines 189-208) + new `ReservationNotActiveError` (same `sealed record : IError` pattern); `InventoryErrors` static factory class as the discovery surface
- 56 unit tests across `test/Inventory.UnitTests/StockItems/{Aggregates,ValueObjects,Errors}/` — every reducer covered, every example-mapping scenario (Sessions 1–3) as a `[Fact]`/`[Theory]`, plus pure-Fold + command/Fold round-trip tests
- `test/Inventory.UnitTests/Placeholder.cs` deleted

**Design decisions resolved in M2** (persisted so fresh sessions don't re-litigate):

| Question | Resolution | Rationale |
|---|---|---|
| `InventoryErrors` surface — doc inconsistency between `error-taxonomy § 3.4`, `example-mapping/inventory.md`, and M1 plan | Follow `error-taxonomy § 3.4` verbatim for the two locked types (`InsufficientStockError`, `ConcurrencyError`); **add** `ReservationNotActiveError` as a third `sealed record : IError` — warranted by example-mapping Sessions 1.R4 and 3.R4 which return `Result.Fail`, not throws. Bug-class conditions (unknown rid, re-init, adjust below zero) throw `DataIntegrityException` with specific `ErrorCode` strings (e.g., `Inventory.ReservationUnknown`, `Inventory.StreamAlreadyInitialized`) | User approved in pre-plan brainstorming. M3+ saga-command handlers translate these `IError` records into external `StockReservationFailedEvent` Avro messages without refactoring the error surface. |
| `ReceiveStock` command parameter type | Accepts `StockSource` VO (not raw `string`) | Reviewer-surfaced (Opus pre-commit, IMPORTANT-3). Domain-purity win: aggregate trusts VO-level non-empty + max-length validation; event still persists raw `string` via `StockSource.Value`. |
| `ReleaseReservation` idempotent replay semantics | Same-rid re-release on already-`Released` is `Result.Ok` with no event — reason is **not** re-checked because `ReservationInfo` does not retain the original `ReleaseReason` (that lives on the stream + future `ReservationAuditView`). Safe because saga issues are at-least-once. | Reviewer caught misleading comment that implied reason-check (HIGH-1); corrected doc to match example-mapping Session 1.R5 literal semantics. |
| `DateTimeOffset` plumbing | Every command method takes `DateTimeOffset occurredOnUtc` as a parameter (no ambient `IClock` / `TimeProvider` in the aggregate) | Matches Catalog's `Product.UpdatePrice(newPrice, utcNow)` pattern at `services/Catalog/Catalog.Domain/Products/Product.cs:113`. Makes the aggregate pure and test-deterministic; M3 command handlers will read `TimeProvider.GetUtcNow()` and pass it through. |
| `_reservations` dict retention after Confirm/Released | Retained on the rehydrated aggregate (not pruned) | Per `inventory.md:62`. Enables distinguishing "unknown rid" vs "terminal status" without round-tripping through the projection. Doc `inventory.md § 3.2 rule 2` contradicts this ("prune after Confirmed/Released") — accepted as a known doc-internal inconsistency; the aggregate-lifetime retention is the authoritative behavior for M2 tests. Flag to revisit in M4 when `ReservationAuditView` exists. |

**Infra-adjacent fix executed under M2** (user-approved mid-milestone):
- `platform/Directory.Packages.props` — bumped every `OpenTelemetry.*` pin from 1.12.0 / 1.12.0-beta.{1,2} to the current-latest stable (1.15.1-3) or beta (1.15.x-beta.1). A new **NU1902** advisory (GHSA-g94r-2vxg-569j, medium severity on `OpenTelemetry.Api 1.12.0`) had surfaced after M1's green restore and was breaking `dotnet restore --locked-mode` across every project in the repo. Not caused by M2's code changes; the root `Directory.Build.props:11` allowlists only NU1903 and adding NU1902 to the list would be equivalent-but-worse (the allowlist is a workaround, a patched version is the real fix). User chose the patched-version path.

**Pre-commit reviewer pass** (`Agent(subagent_type="feature-dev:code-reviewer", model="opus")`):
- 0 CRITICAL, 2 HIGH, 4 IMPORTANT, 7 LOW findings
- All HIGH fixed (misleading comment on `ReleaseReservation`, dead private helper `PopAsEvents`)
- Accepted IMPORTANT fixes: `ReceiveStock` now takes `StockSource` VO; `ReservationNotActiveError` metadata stores raw enum value (not `.ToString()`) for symmetry with other typed errors
- Added LOW-priority test coverage: `Reserve_WhenOrderIdEmpty_ThrowsDataIntegrityException`
- LOW nits deferred or left as-is (documented in this block)

**Verification — paste of actual output (command → result):**

| Gate | Command | Result |
|---|---|---|
| Restore (locked) | `dotnet restore --locked-mode` | ✅ 0 errors |
| Build | `dotnet build -m --no-restore` | ✅ 0 errors, 45 pre-existing NU1903 warnings (allowlisted) |
| Format whitespace | `dotnet format whitespace --no-restore --verify-no-changes` | ✅ 0 changes |
| Format style | `dotnet format style --no-restore --verify-no-changes` | ✅ 0 changes |
| Unit tests | `dotnet test test/Inventory.UnitTests/` | ✅ 56/56 passed, 0 failed, 0 skipped |

**Known DoD gaps carried forward to M3+:**
- `inventory.md § 3.2 rule 2` contradicts `inventory.md:62` on whether to prune terminal reservations from the aggregate — resolve when `ReservationAuditView` ships (M4).
- Architecture-test for "append-only on `stock_events`" + "no `DateTime.UtcNow` in `Inventory.Domain.dll`" + "`ReserveStockCommandHandler` doesn't throw" — deferred to M8.

**Next milestone: M3** — Event-store repository (`EventStoreRepository.{AppendAsync, RehydrateAsync}`) with retry-once on `UniqueConstraintException` from `EntityFrameworkCore.Exceptions.PostgreSQL`; thin EF entity `StockEventRow` with `jsonb` Payload; integration test against Testcontainers Postgres covering optimistic-conflict retry.

### Session 3 (2026-04-25) — M3 + M4 complete (M3 retroactively documented; M4 main delivery)

> M3 was committed in commit `67bebcc feat(inventory): M3 event-store repo + Testcontainers integration tests` on 2026-04-24 22:30 by a session that did not append its own wave_progress block; M4 picked the work up. Treat this block as the authoritative record for both.

**Delivered (M4 main scope — Application layer + enabling Infrastructure delta + Avro contracts):**

- **8 Avro contracts** (5 external events + 3 saga commands) under `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/{Stock,Reservations}/` — each with hand-written `ISpecificRecord` C# wrapper mirroring the avrogen output style; shared `Inventory.Reservations.ReleaseReason` enum used by both `ReservationReleasedEvent` and `ReleaseReservationCommand` (both .avsc files declare the enum inline so each schema registers independently — see deviation note below)
- **`Inventory.Application` layer**:
    - `Common/Data/IEventStore.cs` + `Common/Data/IInventoryDbContext.cs` ports (Application → Infrastructure dependency direction)
    - `Common/Messaging/TopicsOptions.cs` (`InventoryStockEvents`, `InventoryReservations`, `DltTopicSuffix`)
    - `Common/ApplicationDependencyInjection.cs` — `extension(IServiceCollection)` with `AddApplication()`
    - `Common/ReadModels/{CurrentStockLevelRow,ReservationAuditRow}.cs` (POCOs)
    - 6 command triplets: `InitializeStockItem`, `ReceiveStock`, `AdjustStock`, `ReserveStock`, `ConfirmReservation`, `ReleaseReservation`
    - 4 mappers (internal → external Avro): `StockLevelChanged`, `StockReserved`, `ReservationConfirmed`, `ReservationReleased`
    - **2 multiplexed handlers** (one each for projection + lifecycle):
        - `CurrentStockLevelsProjectionHandler` — `IDomainEventHandler<>` for ALL 6 ES events; owns the `current_stock_levels` row + the `0↔positive` threshold check + `StockLevelChanged` outbox emission
        - `ReservationLifecycleHandler` — `IDomainEventHandler<>` for the 3 reservation events; owns `reservation_audit` row maintenance + the external `StockReservedEvent`/`ReservationConfirmedEvent`/`ReservationReleasedEvent` outbox emissions (keyed by `OrderId`)
- **`Inventory.Infrastructure` delta**:
    - `EventStoreRepository.AppendAsync` rewritten to inject `IDomainEventDispatcher` and dispatch internal events between `PopDomainEvents()` and `SaveChangesAsync()` so projection handlers + outbox publishers fire inside the same DB transaction as the event-store insert. Implements the new `IEventStore` port. On `UniqueConstraintException` retry: `_ctx.ChangeTracker.Clear()` (was: per-row detach) — handlers may have added projection + outbox rows on the failed attempt; clearing is the only correct reset.
    - `InventoryDbContext` now implements `IInventoryDbContext` + `IInboxDbContext` and exposes the 4 new DbSets (`CurrentStockLevels`, `ReservationAudit`, `InboxMessages`, `OutboxMessages`); `OnModelCreating` calls `ConfigureOutbox` + `ConfigureInbox`
    - `EntityConfigurations/{CurrentStockLevelRow,ReservationAuditRow}Configuration.cs` — partial indexes for `available <= 10` and `status = 'Active'` driving the M6 `ReservationExpiryWorker`
    - `Common/MessagingDependencyInjection.cs` — `AddMessaging(...)` registers `AddInbox<InventoryDbContext>` + `AddOutbox(...)` (Kafka consumer wiring deferred to M5)
    - `Messaging/Kafka/Config/{KafkaOptions,SchemaRegistryOptions,AvroSerializerOptions}.cs` — mirror Ordering's shape
    - `PersistenceDependencyInjection.cs` — registers the `IEventStore` + `IInventoryDbContext` port mappings
    - User-generated migration `20260425111658_AddProjectionsAndOutboxInbox` adds the 4 new tables; (re-generated once because an IDE-revert mid-session wiped the cumulative `InventoryDbContextModelSnapshot.cs` while leaving the migration's Designer.cs intact — `dotnet ef migrations remove` + `add` restored consistency)
- **5 integration tests** under `test/Inventory.IntegrationTests/`:
    - `Common/IntegrationTestFixture.cs` extended to register `AddApplication()`, `IInventoryDbContext`, `AddOutbox` and a fake `IOutboxWriter` (registered BEFORE `AddOutbox` so the platform's `TryAddSingleton` skips its real `OutboxWriter`)
    - `Common/FakeOutboxWriter.cs` — bypasses Avro + Schema Registry; inserts `OutboxMessage` rows with `TopicName + KafkaKey + Type` preserved (empty `AvroPayload`). M4 verifies "right message lands in right topic"; end-to-end Avro byte-fidelity is M7 (matches Ordering's M4 precedent)
    - `Common/NoOpDomainEventDispatcher.cs` — used by the M3 race-tests that construct `EventStoreRepository` manually with intercepted DbContext; needed because `EventStoreRepository` now requires a dispatcher
    - `Application/ReserveStockCommandHandlerTests.cs` (3 tests) — happy path (event store + both projections + outbox commit atomically), `InsufficientStock` (no stock_events row, outbox has failure event), correlation-id roundtrip (Kafka header → `stock_events.correlation_id`)
    - `Application/ConfirmReservationCommandHandlerTests.cs` — full transition: audit Status=Confirmed, OnHand decrement, external event in outbox
    - `Application/StockLevelChangedEmissionTests.cs` — multi-step sequence proving the threshold rule fires exactly twice across `init → +5 → −2 → +1 → −4`

**Design decisions resolved in M4** (persisted so fresh sessions don't re-litigate):

| Question | Resolution | Rationale |
|---|---|---|
| How do projection handlers + outbox publishers see each other's writes inside one ES write cycle? | Dispatch internal events from `EventStoreRepository.AppendAsync` BETWEEN `PopDomainEvents()` and `SaveChangesAsync()` via `IDomainEventDispatcher`. All handlers write to tracked `DbSet`s; one `SaveChangesAsync` commits everything atomically. | Matches `inventory.md § 8.1` "Transactional envelope". `DispatchDomainEventsInterceptor` doesn't work for ES because aggregates aren't tracked entities. |
| What happens to projection + outbox rows added by handlers on attempt 1 when attempt 2 retries after `UniqueConstraintException`? | `_ctx.ChangeTracker.Clear()` on the catch — wipes everything tracked, the next rehydrate starts clean. | Per-row detach (Reviewer of M3's idea) doesn't generalise; handlers can both `Add` and mutate, and we don't track which rows came from which handler. |
| How do `StockLevelChangedOutboxPublisher` and `ReservationLifecycleHandler` avoid a registration-order race when they handle the same event? | Merge each concern into a single multiplexed handler: `CurrentStockLevelsProjectionHandler` owns BOTH the projection mutation AND the threshold-check + outbox emission for `StockLevelChanged`. `ReservationLifecycleHandler` owns BOTH the audit projection AND the external reservation-event emission. | Eliminates the ordering question entirely. Trade-off accepted: two concerns per handler class, but they share the same input event and the same output channel (outbox), so the cohesion is high. |
| Threshold detection without aggregate replay? | New column `current_stock_levels.PreviousAvailable` — tracked atomically alongside `Available` by the projection handler. The publisher reads both and emits iff `prev==0 XOR new==0`. | O(1) per event vs. O(N) replay. Documented in `CurrentStockLevelRow` xmldoc as a deviation from `inventory.md § 9.1`. |
| `InsufficientStock` failure path — where does the outbox write happen? | In `ReserveStockCommandHandler` directly. The aggregate emits no ES event on failure (nothing to dispatch); the handler reads the `Available` value off the typed error and assembles the external `StockReservationFailedEvent`. Other failure types (Concurrency, future business errors) intentionally do NOT map to a saga response — see in-line comment. | Keeps ES events as the sole source of truth for state mutations; outbox is the cross-service signal channel. |
| CQRS behavior chain (Tracing → Logging → Metrics → Validation) | **Skipped in M4.** The platform's `AddCqrs*Behavior` extensions unconditionally call `services.Decorate(typeof(ICommandHandler<,>), ...)` which throws when no `<,>` handlers are registered, and Inventory M4 ships only `ICommandHandler<>` (no responses). The platform's behavior types are `internal sealed`, so Inventory cannot bypass with `TryDecorate`. | Re-enable in M7 when admin endpoints introduce queries + response-bearing commands. Deferral noted in `ApplicationDependencyInjection.cs` xmldoc. |
| Avro saga-command namespace | `Inventory.Reservations` per `events-catalog.md:94` (locked authority). `use-cases.md:1333` says `Inventory.Commands` — accepted as a doc-internal inconsistency; events-catalog wins for cross-BC seams. | Future `events-catalog.md` regen should propagate to `use-cases.md`. |
| Outbox in tests | Replace `IOutboxWriter` with `FakeOutboxWriter` BEFORE `AddOutbox` → platform's `TryAddSingleton` skips real `OutboxWriter`. Fake inserts `OutboxMessage` row directly with topic + key + CLR type preserved (empty `AvroPayload`). | Avoids needing a Schema Registry container for M4. End-to-end Avro fidelity validated in M7 alongside Kafka consumer wiring. Mirrors Ordering's M4 precedent (see `test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs:53`). |

**Deviations from locked/stated contracts — acknowledged and accepted:**

1. **`current_stock_levels.PreviousAvailable` column** — not in `inventory.md § 9.1`. Added to enable threshold detection without state replay. Documented in `CurrentStockLevelRow.cs` xmldoc.
2. **Saga-command namespace** — `Inventory.Reservations` per `events-catalog.md`; `use-cases.md:1333` is stale.
3. **`ReleaseReason` enum embedded twice** — `ReservationReleasedEvent.avsc` and `ReleaseReservationCommand.avsc` both declare the enum inline. avrogen quirk (each .avsc registers independently, so each `_SCHEMA` payload must be self-contained). At runtime the schemas register as `Inventory.Reservations.ReleaseReason` in both subjects; runtime fine. **Maintenance gotcha:** if the enum gets a new symbol, regenerate BOTH .cs files (logged in LOW-3 from M4 reviewer).
4. **`AvroPayload` is empty in fake-outbox tests** — masks any real Avro serialization issue. Acceptable for M4 (M7 verifies end-to-end). Reviewer flagged + accepted.
5. **CQRS behavior chain skipped in M4** — see decisions table above. Re-enable in M7.

**Doc-internal contradiction resolved (`inventory.md § 3.2 rule 2` vs `inventory.md:62`):** the aggregate retains terminal-status reservations in its in-memory dict (per :62) for the lifetime of a rehydrate; the projection-side `reservation_audit` table is the durable terminal-state store. M2 picked retention; M4 confirms by deferring to the audit table for lookups in confirm/release publishers. The `§ 3.2 rule 2` wording ("prune after Confirmed/Released") is read as advice for projection rebuilds, not aggregate lifetime.

**Pre-commit reviewer pass** (`Agent(subagent_type="feature-dev:code-reviewer", model="opus")`):
- 0 CRITICAL, 0 HIGH, 2 IMPORTANT, 4 LOW findings
- IMPORTANT-1 fixed: `CurrentStockLevelRow.LastVersion` xmldoc rewritten to reflect that no idempotency guard is currently implemented (the field is reserved for a future replay-rebuild path; replay is impossible on the hot path because handlers run in the same tx as the ES append)
- IMPORTANT-2 fixed: `ReserveStockCommandHandler` failure-branch comment expanded to explicitly state that non-`InsufficientStock` errors are NOT mapped to a saga-visible event (Concurrency = transient, DataIntegrityException = poison/DLT)
- LOW-1 (defensive `GetRequiredSection` on outbox config callbacks) — accepted as M5 follow-up
- LOW-2 (inbox/outbox table-name casing — `IX_InboxMessages_*` PascalCase from platform vs Inventory's snake_case ix_*) — accepted as platform follow-up
- LOW-3 (regenerate both `ReleaseReason` schema embeddings on enum change) — documented in this block + `ReleaseReason.cs` will get a comment in a future maintenance pass
- LOW-4 (postgres image tag pin drift across BC fixtures) — accepted, cosmetic

**Verification — paste of actual output (command → result):**

| Gate | Command | Result |
|---|---|---|
| Restore (locked) | `dotnet restore --locked-mode` | ✅ exit 0 |
| Build | `dotnet build -m` | ✅ 0 errors, 90 pre-existing NU1903 warnings (allowlisted) |
| Format whitespace | `dotnet format whitespace --no-restore --verify-no-changes` | ✅ exit 0 |
| Format style | `dotnet format style --no-restore --verify-no-changes` | ✅ exit 0 |
| Unit tests | `dotnet test test/Inventory.UnitTests/` | ✅ 56/56 passed (no M2 regressions) |
| Integration tests | `dotnet test test/Inventory.IntegrationTests/` (with `HTTP_PROXY=` unset for Testcontainers Docker.DotNet `npipe://` URI compatibility) | ✅ 11/11 passed (6 from M3 + 5 new in M4) |

**Known DoD gaps carried forward to M5+:**

- 5 Kafka consumers (3 saga-command intake handlers + Catalog `ProductCreatedEvent` + Ordering `OrderCancelledEvent` — both still stubbed pending upstream BC schemas) → M5
- ~~Service-auth on saga-command consumers~~ — **dropped per ADR-0010 lines 102-106**: per-message `X-Service-Token` validation is the wrong layer. v1 = PLAINTEXT broker (ADR-0009 reference profile); production = broker SASL/OAUTHBEARER + per-service ACLs.
- **Rehydration observability** ([ADR-0006 §Observability and the snapshot threshold](../adr/0006-event-sourcing-for-inventory.md)) — emit `inventory.aggregate.rehydration.duration` (ms histogram) + `inventory.aggregate.rehydration.event_count` (events folded) from `EventStoreRepository.RehydrateAsync`, tagged by `ProductId`. Add an alert/integration-test asserting p99 duration < 1s over a representative replay (1k-event stream). Provides the signal that opens the v2 snapshot work item; no snapshot code in v1. → M5
- `ReservationExpiryWorker` hosted service + integration test with `FakeTimeProvider` → M6
- Admin HTTP endpoints (`POST /api/v1/inventory/stock-items/{productId}/{receive,adjust}`) + `.Idempotency()` per ADR-0013 → M7
- CQRS behavior chain reactivation in `ApplicationDependencyInjection.AddApplication` → M7 (when responses arrive)
- Architecture tests (append-only `stock_events`, no `DateTime.UtcNow` in domain, `ReserveStockCommandHandler` doesn't throw) → M8
- `example-mapping/inventory.md` Sessions 1-3 → M9
- `docker compose --profile full up -d` smoke + final session summary → M10

**Next milestone: M4 wave_progress contains the M4 deliverables; the next session executes M5** — Infrastructure layer (KafkaFlow consumer wiring for `inventory.reservation-commands` saga commands × 3, `catalog.products` `ProductCreatedEvent` consumer — STUBBED until Catalog ships its Avro schema, `ordering.orders` `OrderCancelledEvent` consumer — STUBBED until Ordering ships its schema; service-auth header validation; outbox-relay-inventory health check; integration test that puts a fake saga-command on the topic and verifies the resulting reservation-audit row + external Avro outbox emission).

### Session 4 (2026-04-26) — M5 complete

**Delivered (M5 main scope — Infrastructure layer Kafka consumer wiring + integration tests):**

- **3 per-consumer config classes** (`Confluent.Kafka.ConsumerConfig` subclasses) under `services/Inventory/Inventory.Infrastructure/Messaging/Kafka/{SagaCommands,StockInit}/`:
    - `ReservationCommandsConsumerOptions` — section `KafkaReservationCommandsConsumer`, group `inventory-reservation-commands`, topic `inventory.reservation-commands`
    - `CatalogProductsConsumerOptions` — section `KafkaCatalogProductsConsumer`, group `inventory-stock-init` (shared per deviation #1), topic `catalog.products`
    - `OrderingOrdersConsumerOptions` — section `KafkaOrderingOrdersConsumer`, **same group** `inventory-stock-init` per deviation #1, topic `ordering.orders`
- **`SagaCommandMappers`** — pure static Avro→AppCommand mappers for the 3 saga commands (`Reserve`/`Confirm`/`Release`), explicit `ReleaseReason` switch (Avro enum → domain enum) raising `DataIntegrityException` on unknown symbols, defensive `DateTime.SpecifyKind` for Avro `DateTime` → `DateTimeOffset`
- **`SagaCommandHandlerBase<TAvroCommand>`** — generic transactional-outbox wrapper (parameterized over Avro command type) with `EnsureTransactionAsync` envelope, variable log-context dictionary (Inventory's saga commands have non-uniform id shape — see decision below), and **business-expected-error filter** so `Result.Fail(InsufficientStockError)` does NOT throw and roll back the staged saga response
- **`SagaCommandDispatchException`** — thrown on non-business `Result.Fail` so KafkaFlow's DLT middleware routes the message to `<topic>.Inventory.DLT`
- **3 saga-command Kafka handlers** (`Inventory.Reservations.{Reserve,Confirm,Release}` Avro types):
    - `ReserveStockCommandKafkaHandler`, `ConfirmReservationCommandKafkaHandler`, `ReleaseReservationCommandKafkaHandler` — each ~14 LoC, single-method `Handle` delegating to `SagaCommandHandlerBase.ExecuteAsync` with the appropriate log-context keys
- **2 cross-BC event handlers (REAL — both Avro schemas now shipped, deviations #2 & #3 from Session 1 RESOLVED)**:
    - `ProductCreatedEventKafkaHandler` — `Catalog.Products.ProductCreatedEvent` → `InitializeStockItemCommand`. Reuses `SagaCommandHandlerBase` for the transactional envelope; idempotent against re-delivery via the application handler's `Version > 0` guard.
    - `OrderCancelledEventKafkaHandler` — `Ordering.Orders.OrderCancelledEvent` → fan-out releases. Does NOT use `SagaCommandHandlerBase`: queries `reservation_audit WHERE OrderId = X AND Status = Active`, dispatches one `ReleaseReservationCommand(reason=Cancellation)` per row, throws `DbUpdateException` (retry-eligible) on partial failure so KafkaFlow's `RetryForever` re-runs the message and the `WHERE Status = Active` requery makes it naturally idempotent.
- **`InfrastructureDependencyInjection.AddInfrastructure`** — public composition root chaining `AddDatabase` + `AddMessaging`. No health checks (M7).
- **`MessagingDependencyInjection.AddMessaging` extended** — KafkaFlow cluster + 3 consumer blocks (one per topic — distinct topics need distinct `.AddConsumer` blocks). Each consumer's middleware order: `AddSchemaRegistryAvroDeserializer → AddCorrelationIdConsumerMiddleware → AddDeadLetter → RetryForever(DbUpdateException, NpgsqlException, TimeoutException) → AddInbox(...) → AddTypedHandlers(Scoped)`. Cluster-level DLT producer with `AddProducerHeaders("Inventory")`.
- **`Inventory.API/Program.cs`** — minimal wire-up: `AddApplication() + AddInfrastructure() + kafkaBus.StartAsync()` guarded by `!app.Environment.IsTesting()`. ServiceDefaults / FastEndpoints / auth land in M7. `public partial class Program;` marker added for `WebApplicationFactory<Program>` use in future tests.
- **`appsettings.json`** — appended `Kafka` (Brokers, SchemaRegistry, AvroSerializer), `Topics`, and the 3 `Kafka*Consumer` sections.
- **Integration tests** — `IntegrationTestFixture` extended to register the 5 Kafka handler types as Scoped (matching KafkaFlow's `WithHandlerLifetime(InstanceLifetime.Scoped)`); new `FakeKafkaMessageContext` (NSubstitute-backed `IMessageContext` stub with `Headers` and `ConsumerContext.WorkerStopped`); 10 new tests across 6 files covering happy paths + InsufficientStock business-failure path + DLT-routing contract:
    - `ReserveStockCommandKafkaHandlerTests` (2: HappyPath + InsufficientStock_DoesNotThrow)
    - `ConfirmReservationCommandKafkaHandlerTests` (1: HappyPath)
    - `ReleaseReservationCommandKafkaHandlerTests` (1: Compensation_AuditReleased)
    - `ProductCreatedEventKafkaHandlerTests` (2: NewProduct + DuplicateDelivery)
    - `OrderCancelledEventKafkaHandlerTests` (2: TwoActiveReservations + NoActiveReservations)
    - `SagaCommandHandlerBaseTests` (2: ResultFail_Throws + HappyPath)

**Design decisions resolved in M5** (persisted so fresh sessions don't re-litigate):

| Question | Resolution | Rationale |
|---|---|---|
| Cross-BC consumers — real or stubbed? | **REAL for both.** Both `Catalog.Products.ProductCreatedEvent.avsc` and `Ordering.Orders.OrderCancelledEvent.avsc` exist in `platform/Platform.SchemaRegistry.Contracts/Avro/`. Session 1 deviations #2 + #3 are SUPERSEDED. | Wave 1 shipped both schemas; the "stubbed pending upstream BCs" wording in the original prompt is stale. Pre-plan ambient verification via `Glob` confirmed the Avro `.cs` types are generated. |
| Kafka-side service-auth (X-Service-Token) | **Dropped entirely** — user removed `X-Service-Token` from Kafka headers as bad design. DoD line 314 wording reconciled to drop the validation. | Per ADR-0010 lines 102-106: v1 = PLAINTEXT broker (ADR-0009 reference profile); production-deployment gate is broker SASL/OAUTHBEARER + per-service ACLs, NOT per-message header validation. Ordering's saga-command handlers also don't validate any token. |
| `OrderCancelledEvent` dispatch model — in-process loop vs. self-loop through Kafka? | **In-process loop, NOT wrapped by `SagaCommandHandlerBase`.** Handler queries active reservations, dispatches one `ReleaseReservationCommand` per row inline; throws `DbUpdateException` on `Result.Fail` so KafkaFlow's `RetryForever` re-runs the message. | Self-loop through Kafka would have Inventory talk to itself (architectural noise; breaks `events-catalog.md:93` which lists only saga + ops as producers of `ReleaseReservationCommand`). The wrapper isn't used because its DLT-on-failure semantics would hard-fail the whole cancellation if any single release failed; retry + audit-table requery is naturally idempotent. |
| `SagaCommandHandlerBase` — how to handle `Result.Fail(InsufficientStockError)` without DLT-routing the staged saga response? | Hard-coded `BusinessExpectedErrorCodes` HashSet (`Inventory.InsufficientStock` for now); on `Result.Fail` whose `Errors` contains a known business-expected code, log + commit + return without throw. | The application handler stages `StockReservationFailedEvent` to outbox AND returns `Result.Fail` (M4 contract, preserves typed error visibility for in-process callers). If the wrapper threw, the inbox tx would roll back the outbox row → saga never sees the failure event. New business-expected errors must be added to the HashSet explicitly. |
| `ExecuteAsync` log-context shape — Ordering uses fixed `(CorrelationId, OrderId)`; Inventory's commands have variable id shape | Wrapper takes `Dictionary<string, object?>` of log keys; caller passes exactly the ids that command carries (Reserve has OrderId+ProductId+ReservationId; Confirm/Release have ProductId+ReservationId; ProductCreated has ProductId+Sku; OrderCancelled has OrderId+AtStatus). | Generalises over Ordering's signature without much extra cost. Each handler's call-site is one literal dictionary. |
| Test approach — Testcontainers Kafka+SR vs. direct handler invocation? | **Direct handler invocation.** Match Ordering's M5 precedent at `test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs:19-20`. Tests resolve typed handlers from DI and call `Handle(IMessageContext, T)` directly with synthetic `FakeKafkaMessageContext` (NSubstitute-backed). | Real Kafka + Schema Registry containers add 30-60s test runtime + container coordination complexity; the typed handler is the meaningful integration point (Avro deserialization + KafkaFlow plumbing are platform code). End-to-end byte-fidelity is M7. |
| `IntegrationTestFixture` extension shape | Register all 5 Kafka handlers as `Scoped` (matches KafkaFlow's `WithHandlerLifetime(InstanceLifetime.Scoped)`); `FakeKafkaMessageContext.Create` includes a non-null `MessageId` header even though tests bypass the inbox middleware. | The MessageId-on-context invariant guards against future refactors that wire the middleware in. NSubstitute is already a project reference. |
| EF Core `EndsWith` translation in tests | Tests filter outbox rows by full FQN equality (`m.Type == "Inventory.Reservations.ReservationConfirmedEvent"`) instead of `EndsWith("ReservationConfirmedEvent")`. | `FakeOutboxWriter` stores the full type name; FQN equality is translatable to SQL and unambiguous. The first test pass surfaced an EF translation error that switching to FQN equality fixed cleanly. |

**Deviations from locked/stated contracts — acknowledged and accepted:**

1. **`OrderCancelledEventKafkaHandler` does NOT use `SagaCommandHandlerBase`** — see decisions table. Documented in the handler's xmldoc.
2. **`SagaCommandHandlerBase` business-expected-error filter** — adds an Inventory-specific concept (the `BusinessExpectedErrorCodes` HashSet) absent from Ordering's wrapper. Justified because Inventory's `ReserveStockCommand` is the only saga command in the solution today that semantically commits an outbox row alongside `Result.Fail`. New business errors must be added to the HashSet explicitly per `inventory.md error-taxonomy`.
3. **Session 1 deviations #2 + #3 RESOLVED** — `Catalog.Products.ProductCreatedEvent` and `Ordering.Orders.OrderCancelledEvent` are both wired as REAL handlers (not stubs); the original `inventory.md` DoD wording at line 314 ("STUBBED until Catalog/Ordering lands") is stale. Wave_progress is the authoritative record.

**Pre-commit reviewer pass** (`Agent(subagent_type="feature-dev:code-reviewer", model="opus")`):
- 0 CRITICAL, 1 HIGH, 2 IMPORTANT, 2 LOW findings.
- **HIGH-1 fixed**: tightened the `BusinessExpectedErrorCodes` HashSet docstring on `SagaCommandHandlerBase` to make the addition criteria explicit — listing the three failure modes (business-expected with staged outbox = add; non-staged Result.Fail = don't add, DLT-route; bug-class throw = never reaches the filter). Prevents future maintainers from silently swallowing concurrency / validation errors.
- **IMPORTANT-2 fixed**: rewrote `SagaCommandHandlerBaseTests` to actually drive the documented `Result.Fail → SagaCommandDispatchException` contract. Test suite now covers all three observable failure modes (business-expected with staged outbox; non-business `Result.Fail` triggering DLT throw; unhandled exception propagating unchanged). Old test asserted `ThrowAsync<Exception>()` on a path that triggered `DataIntegrityException` rather than the wrapper's `SagaCommandDispatchException` — false sense of safety.
- **IMPORTANT-1 deferred to M6 follow-up**: integration test for the partial-fan-out failure path of `OrderCancelledEventKafkaHandler` (release succeeds for product A, fails for product B → throw → KafkaFlow re-runs the message; `WHERE Status = Active` requery makes it idempotent). The design is sound on paper but unverified by test. M6's `ReservationExpiryWorker` work introduces similar fan-out semantics — bundle the regression test there.
- **LOW-1 deferred** (`OrderingOrdersConsumerOptions` single-handler subscription on a multi-event topic — operational nuisance only; non-handled types pass through inbox no-op + KafkaFlow log warnings).
- **LOW-2 deferred** (defensive comment about `_transactionalOutbox.SaveChangesAsync` skip in OrderCancelled no-op branch — `AsNoTracking` snapshot guarantees no DbContext mutations).

**Verification — paste of actual output (command → result):**

| Gate | Command | Result |
|---|---|---|
| Restore (locked) | `dotnet restore --locked-mode` | ✅ exit 0, no lock-file changes |
| Build (M5 slice) | `dotnet build {Inventory.API, IntegrationTests, UnitTests} -m --no-restore` | ✅ 0 errors per project (full-solution `dotnet build -m` fails on PRE-EXISTING errors in Invoicing/Catalog.ArchitectureTests/Basket.ArchitectureTests/Payments uncommitted work — outside M5 boundary, untouched) |
| Format whitespace | `dotnet format whitespace --no-restore --verify-no-changes` | ✅ exit 0 |
| Format style | `dotnet format style --no-restore --verify-no-changes` | ✅ exit 0 |
| Unit tests | `dotnet test test/Inventory.UnitTests/` | ✅ 56/56 passed (no M2 regressions) |
| Integration tests | `dotnet test test/Inventory.IntegrationTests/` | ✅ 22/22 passed (M3:6 + M4:5 + M5:11) |

**Known DoD gaps carried forward to M6+:**

- `ReservationExpiryWorker` hosted service + integration test with `FakeTimeProvider` → **M6** (next)
- Admin HTTP endpoints (`POST /api/v1/inventory/stock-items/{productId}/{receive,adjust}`) + `.Idempotency()` per ADR-0013 → M7
- ServiceDefaults + FastEndpoints + auth + output-cache + health endpoints in `Program.cs` → M7
- Health check endpoint for `outbox-relay-inventory` → M7
- CQRS behavior chain reactivation in `ApplicationDependencyInjection.AddApplication` → M7 (when responses arrive)
- Rehydration observability (ADR-0006) — `inventory.aggregate.rehydration.{duration,event_count}` histograms + p99 < 1s integration test on a 1k-event stream → M8 (paired with arch tests)
- Architecture tests (append-only `stock_events`, no `DateTime.UtcNow` in domain, `ReserveStockCommandHandler` doesn't throw) → M8
- `example-mapping/inventory.md` Sessions 1-3 → M9
- `docker compose --profile full up -d` smoke + final session summary → M10

**Next milestone: M6** — `ReservationExpiryWorker` background hosted service polling `reservation_audit WHERE Status='Active' AND ExpiresAtUtc < now()` every 60s using injected `TimeProvider`; publishes `ReleaseReservationCommand(reason=Expiry)` per expired row via the outbox. Integration test uses `FakeTimeProvider` to advance past the TTL boundary and asserts the worker emits exactly one release per expired reservation.

### Session 5 (2026-04-26) — M6 complete

**Delivered (M6 main scope — `ReservationExpiryWorker` hosted service + integration tests):**

- `services/Inventory/Inventory.Infrastructure/BackgroundJobs/ReservationExpiryWorker.cs` — sealed internal `BackgroundService`. `ExecuteAsync` runs an **eager startup tick** (so a pod restart doesn't add up to 60s of TTL latency) and then loops on `new PeriodicTimer(TimeSpan.FromSeconds(60), _timeProvider)` — the `TimeProvider`-aware `PeriodicTimer` constructor is the .NET 8+ test seam (`FakeTimeProvider.Advance` deterministically fires the tick). One `IServiceScopeFactory.CreateAsyncScope()` per tick resolves scoped `IInventoryDbContext` + `ICommandHandler<ReleaseReservationCommand>`; per-row `try/catch` re-throws cancellation and swallows everything else (logged at Warning for `Result.Fail`, Error for unhandled `Exception`). Hard-coded `PollIntervalSeconds = 60` + `MaxBatchSize = 100` per `inventory.md § 11.1`.
- `services/Inventory/Inventory.Infrastructure/Common/InfrastructureDependencyInjection.cs` — added `services.AddHostedService<ReservationExpiryWorker>()` after `.AddMessaging(...)`. xmldoc updated to mention the new worker; M7 health-checks deferral wording preserved.
- `test/Inventory.IntegrationTests/BackgroundJobs/ReservationExpiryWorkerTests.cs` — 5 new integration tests under `[Collection(nameof(IntegrationTestCollection))]`, reusing the existing `IntegrationTestFixture`. Each test seeds via the existing Initialize → Receive → Reserve flow (`TimeToLive = 15min`), constructs the worker manually with a **local `FakeTimeProvider`** (no fixture mutation), and calls `ProcessExpiredReservationsAsync` directly — bypasses the BackgroundService loop entirely. Tests:
    - `SingleExpiredReservation_IsReleasedWithExpiryReason` — DoD line 396 happy path
    - `MultipleExpiredReservations_AllReleasedInOneTick` — fan-out across 3 reservations
    - `ExpiredAndUnexpired_OnlyExpiredReleased` — `ExpiresAtUtc < now()` filter
    - `AlreadyReleasedReservation_NoDoubleRelease` — `Status='Active'` filter excludes terminal-status rows
    - `ConfirmedReservation_NotReleasedAfterExpiry` — race vs Confirm (DoD line 400, filter half — see IMP-5 acceptance below)

**Design decisions resolved in M6** (persisted so fresh sessions don't re-litigate):

| Question | Resolution | Rationale |
|---|---|---|
| `TimeProvider.CreatePeriodicTimer(...)` API shape | Method does not exist on the BCL `TimeProvider`. Use `new PeriodicTimer(TimeSpan, TimeProvider)` instead — the .NET 8+ constructor overload that respects the injected provider. | First implementation attempt with `_timeProvider.CreatePeriodicTimer(...)` failed `dotnet build` with CS1061; the constructor overload is the canonical `FakeTimeProvider`-driven test seam. |
| Eager startup tick? | YES — run one `ProcessExpiredReservationsAsync` immediately on `ExecuteAsync` entry, then loop with `WaitForNextTickAsync`. | `PeriodicTimer.WaitForNextTickAsync` waits the full period before yielding the first tick. Without the eager tick, every pod restart would add up to 60s to the TTL release SLA. Per Opus reviewer IMP-1 (M6 pre-commit pass). |
| Test seam pattern | Extract `internal Task ProcessExpiredReservationsAsync(CancellationToken)` and have tests call it directly with a manually-constructed worker (local `FakeTimeProvider`, fixture-resolved `IServiceScopeFactory`, `NullLogger`). | Avoids spinning up the BackgroundService loop in tests; deterministic single-tick semantics. Fixture stays untouched so M3-M5 tests keep their real-clock contract. |
| Per-row error handling | Per-row `try/catch`: `OperationCanceledException` re-thrown; everything else logged + skipped so one bad row doesn't stall the batch. `Result.Fail` paths logged at Warning, never thrown. | Matches `inventory.md § 11.2` "At-least-once" guarantee. Audit row stays `Active` until command commits, so a re-tick after a swallowed exception naturally retries. Aggregate's `ReleaseReservation` is idempotent on already-`Released`/-`Confirmed` reservations. |
| `OccurredOnUtc` value on the worker-issued command | `_timeProvider.GetUtcNow()` (the scan time), not `row.ExpiresAtUtc`. | Same convention as saga compensation + admin cancel; `ExpiresAtUtc` is the *business* expiry moment but `OccurredOnUtc` is the event-emission moment. ADR-0015 only requires UTC, not "use ExpiresAtUtc". Documented in IMP-6 (accepted, no change). |
| `CorrelationId` value on the worker-issued command | `null`. TTL expiry has no upstream correlation context. | `IEventStore.AppendAsync` documents this contract; matches how `Inventory.Application` currently treats ops-originated writes. |
| Hard-coded constants vs options class | Hard-coded `PollIntervalSeconds = 60` + `MaxBatchSize = 100`. | YAGNI — single-number knobs locked by `inventory.md § 11.1`. Easy to extract to options later if operations needs runtime tuning. |
| `AsNoTracking()` on the audit-row scan | YES. | The worker reads only `(ReservationId, ProductId)` projection — never mutates `ReservationAuditRow` directly. The audit-row mutation happens later inside `ReleaseReservationCommandHandler → EventStoreRepository.AppendAsync → ReservationLifecycleHandler` in *its* DbContext scope. No tracking conflict possible. |

**Pre-commit reviewer pass** (`Agent(subagent_type="feature-dev:code-reviewer", model="opus")`):
- 0 CRITICAL, 0 HIGH, 6 IMPORTANT (all confidence 80, none blockers), 0 LOW correctness items.
- **IMP-1 fixed**: added eager startup tick (see decision table above) + extracted `TryRunTickAsync(string tickKind, CancellationToken)` helper to avoid duplicating the try/catch envelope between startup + scheduled paths. Both call sites annotate the tick kind in log messages for ops triage.
- **IMP-2 accepted (no change)**: `OperationCanceledException` discriminator on the outer token (not the exception's own token) is correct by design; revisit if/when `ReleaseReservationCommand` grows a per-command timeout. No code today exercises that path.
- **IMP-3 accepted (no change)**: `RunOneTickAsync` test helper opens a temporary scope just to resolve `IServiceScopeFactory` (the singleton would resolve from any scope). Cosmetic; tests pass and the temporary scope's disposal is harmless because the factory is rooted on the singleton container.
- **IMP-4 accepted (no change)**: `OrderBy(r => r.ExpiresAtUtc)` ordering is sensible default ("oldest TTLs released first under fairness pressure") but not contractual per `inventory.md § 11.1`. The fan-out test uses identical `reservedAtUtc` and so cannot prove ordering — acceptable given no contract. Stagger seed times if ordering ever becomes contract.
- **IMP-5 accepted (defer to M7/M8 follow-up)**: the 5 integration tests prove DoD line 396 + the *filter half* of DoD line 400 (worker excludes already-Confirmed/Released audit rows). The *retry-handler half* (worker reads row as Active → Confirm commits → worker dispatches Release → aggregate returns `Result.Fail(ReservationNotActiveError)` → worker logs+skips) is unreached — no seam exists to deterministically interleave Confirm between scan and dispatch from outside the worker. The cleanest fix is a unit test of the worker against a stub `ICommandHandler<ReleaseReservationCommand>` returning `Fail(ReservationNotActiveError)`; deferred because Inventory has no `Inventory.Infrastructure.UnitTests` project today and adding one is outside M6's boundary. Bundle this alongside the M8 architecture-test work (which also adds new test patterns).
- **IMP-6 accepted (no change)**: `OccurredOnUtc = nowUtc` (vs `row.ExpiresAtUtc`) is the same convention as saga compensation + admin cancel; reviewer notes "current choice is defensible". Not a contract violation.

**Verification — paste of actual output (command → result):**

| Gate | Command | Result |
|---|---|---|
| Restore (locked) | `dotnet restore --locked-mode` | ✅ exit 0; only pre-existing NU1903 advisories on transitive `System.Security.Cryptography.Xml` 9/10.0.x (allowlisted at root `Directory.Build.props`) |
| Build (Inventory slice) | `dotnet build {Inventory.Infrastructure, Inventory.API, Inventory.IntegrationTests, Inventory.UnitTests} -m --no-restore` (per-project; full-solution `dotnet build -m` still fails on PRE-EXISTING errors in Invoicing/Catalog.ArchitectureTests/Basket.ArchitectureTests/Payments uncommitted work — outside M6 boundary, untouched, matches M5 precedent line 272) | ✅ 0 errors per project |
| Format whitespace | `dotnet format whitespace services/Inventory/Inventory.Infrastructure/Inventory.Infrastructure.csproj --no-restore --verify-no-changes` | ✅ exit 0 |
| Format style | `dotnet format style services/Inventory/Inventory.Infrastructure/Inventory.Infrastructure.csproj --no-restore --verify-no-changes` | ✅ exit 0 |
| Unit tests | `dotnet test test/Inventory.UnitTests/` | ✅ 56/56 passed (no M2 regressions) |
| Integration tests | `dotnet test test/Inventory.IntegrationTests/` (with `HTTP_PROXY=` unset for Testcontainers Docker.DotNet `npipe://` URI compatibility) | ✅ 27/27 passed (M3:6 + M4:5 + M5:11 + **M6:5**) |

**Known DoD gaps carried forward to M7+:**

- Admin HTTP endpoints (`POST /api/v1/inventory/stock-items/{productId}/{receive,adjust}`) + `.Idempotency()` per ADR-0013 → M7
- ServiceDefaults + FastEndpoints + auth + output-cache + health endpoints in `Program.cs` → M7
- Health check endpoint for `outbox-relay-inventory` → M7
- CQRS behavior chain reactivation in `ApplicationDependencyInjection.AddApplication` → M7 (when responses arrive)
- Rehydration observability (ADR-0006) — `inventory.aggregate.rehydration.{duration,event_count}` histograms + p99 < 1s integration test on a 1k-event stream → M8
- Architecture tests (append-only `stock_events`, no `DateTime.UtcNow` in domain, `ReserveStockCommandHandler` doesn't throw) → M8
- **M6 follow-up — race-vs-Confirm retry-handler unit test** (IMP-5 above): worker against stub `ICommandHandler<ReleaseReservationCommand>` returning `Fail(ReservationNotActiveError)` to cover the warning-log branch. Bundle with M8's architecture-test work (introduces new test patterns).
- `example-mapping/inventory.md` Sessions 1-3 → M9
- `docker compose --profile full up -d` smoke + final session summary → M10

**Next milestone: M7** — Admin HTTP endpoints (`POST /api/v1/inventory/stock-items/{productId}/{receive,adjust}` + `GET` queries as needed) under `/api/v1/inventory/...` per ADR-0012, with FastEndpoints `.Idempotency()` on the adjust endpoint per ADR-0013 (backed by `redis-cache`) and Keycloak `inventory.commands.*` scope authorization per ADR-0010. Bring up `ServiceDefaults` (correlation-id middleware, health checks, OTel) + reactivate the CQRS behavior chain (Tracing → Logging → Metrics → Validation → Handler) in `ApplicationDependencyInjection.AddApplication` since responses now exist on queries. Outbox-relay-inventory health-check endpoint. Functional tests for the admin endpoints.
</wave_progress>

<role_in_system>
Inventory is the **stock authority** — truth about what's on hand, reserved, confirmed-sold. Teaching purpose: **demonstrate Event Sourcing where it genuinely fits** (audit, temporal queries, per-stream optimistic concurrency), paired with ADR-0006's explicit **"when NOT to use ES"** guidance so learners don't over-apply the pattern.

Upstream: Catalog's `ProductCreatedEvent` → Inventory initializes a stream per ProductId.
Downstream: Checkout saga consumes reservation lifecycle events; Catalog consumes `StockLevelChanged` (threshold crossings only).
</role_in_system>

<contract>
LOCKED at the seams.

- 5 external events + 3 saga-command schemas under `Inventory.Stock` + `Inventory.Reservations` namespaces per `events-catalog.md § 5.4 + 5.6`
- 6 internal ES events (persisted as write model) per `inventory.md § 5` — named + ordered; reducer semantics per § 5
- Event store table `inventory.stock_events (StreamId, Version, EventType, Payload, OccurredAtUtc, AppendedAtUtc, CorrelationId)` with **PK `(StreamId, Version)`** — the optimistic-concurrency mechanism
- Projection tables: `inventory.current_stock_levels` + `inventory.reservation_audit`
- Topics: `inventory.stock-events` (3 partitions) + `inventory.reservations` (6 partitions — saga fan-out) + `inventory.reservation-commands` (3 partitions, 7-day retention)
- Consumer groups: `inventory-stock-init` (ProductCreated + OrderCancelled — single shared group per `events-catalog.md:96`; deviates from `eshop-master-design.md § E.10` — see `<wave_progress>` deviation #1), `inventory-reservation-commands` (saga cmds)
- Reservation TTL = 15 min default (configurable). **Cross-BC invariant** (ADR-0004 §Implementation Notes + [saga-stuck-runbook.md § 6 line 297](../bc-design/saga-stuck-runbook.md)): the value here is enforced upstream by `saga/SagaOrchestrators.UnitTests/Checkout/CheckoutTimeoutInvariantTests.cs` (see [checkout-saga.md `<dod>`](checkout-saga.md)) against the formula `Sum(saga step timeouts) + 2 × CompensationSeconds < TTL`. **If this TTL is shortened, coordinate with the saga team — the test will fail the build until saga timeouts are retuned.**
- `InsufficientStock` is **BUSINESS-EXPECTED**, not a bug — returns `Result.Fail(...)` → external `StockReservationFailedEvent`; NEVER throws
- Schemas FORWARD_TRANSITIVE (events) + FULL_TRANSITIVE (commands) per [ADR-0007](../adr/0007-avro-compatibility-modes.md)
- HTTP routes (admin only) under `/api/v1/inventory/...` per ADR-0012
</contract>

<design_open>
You own these. Justify each in your session summary. Items resolved in prior sessions are recorded in `<wave_progress>` with rationale — do not re-litigate.

RESOLVED (see `<wave_progress>` Session 1):
- ~~Event-store repository shape~~ — EF Core `StockEventRow` + `EventStoreRepository.{AppendAsync, RehydrateAsync}`, same-tx envelope
- ~~`StockItem.Fold` shape~~ — static `Fold(IEnumerable<IDomainEvent>) → StockItem`
- ~~Projection handler organization~~ — one multiplexed handler per projection table
- ~~`ReservationExpiryWorker` cadence~~ — 60s `PeriodicTimer`
- ~~Retry-once policy~~ — in-repository; catch `UniqueConstraintException` from `EntityFrameworkCore.Exceptions.PostgreSQL`
- ~~Admin `.Idempotency()`~~ — YES on `AdjustStock`, backed by `redis-cache`

STILL OPEN:
- `InventoryErrors` API surface — ensure it mirrors `error-taxonomy.md § 3.4` exactly; pick factory-method vs record-class style when M2 starts
- Architecture-tests rule set: `stock_events` append-only (no `Update`/`Delete` on repository) — the exact NetArchTest predicate
- Additional `example-mapping/inventory.md` sessions for edge cases (e.g., "what if `ReceiveStock` + `ReserveStock` arrive simultaneously on a brand-new stream?")
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/inventory.md` — **especially § 5 (ES events + reducers), § 8 (event store schema), § 9 (projections), § 10 (concurrency), § 11 (TTL policy), § 14 (pattern showcase + "when NOT to use ES")**
3. `docs/bc-design/glossary-inventory.md` + `example-mapping/inventory.md`
4. `docs/bc-design/events-catalog.md` § 5.4 + § 5.6
5. `docs/bc-design/use-cases.md` § 4 — **especially § 4.3 (saga-command intake)**
6. `docs/bc-design/error-taxonomy.md § 3.4` — `InventoryErrors`
7. `docs/bc-design/checkout-saga.md` § 5 (fan-out) + § 6 (compensation)
8. `docs/eshop-master-design.md` § 3 + § 11.7 + § E.10 (consumer-group fix)
9. `docs/adr/0006-event-sourcing-for-inventory.md` + `0004` + `0007`
10. `docs/bc-design/saga-stuck-runbook.md` — your TTL races with saga compensation
11. **All ADRs in `<applicable_adrs>` below**
</reading_order>

<applicable_adrs>
Cross-cutting decisions to apply:

- [ADR-0008](../adr/0008-correlation-id-propagation.md) — every saga-command handler reads `CorrelationId` from the Kafka header; `stock_events.CorrelationId` column persists it; outbox publisher copies it into `StockReservedEvent` / `StockReservationFailedEvent` / `ReservationConfirmedEvent` / `ReservationReleasedEvent` Avro headers
- [ADR-0010](../adr/0010-service-to-service-auth.md) — inbound JWT validation for admin endpoints (scope `inventory.commands.*`); saga-command Kafka consumers run on PLAINTEXT in v1 — **no per-message `X-Service-Token` validation** ([ADR-0010 lines 102-106](../adr/0010-service-to-service-auth.md:102) + ADR-0009 reference profile). Production hardening = enable broker SASL/OAUTHBEARER + per-service Kafka topic ACLs. Catalog-event consumer also runs on PLAINTEXT (event topic, not command).
- [ADR-0012](../adr/0012-api-versioning.md) — admin routes under `/api/v1/inventory/...`
- [ADR-0013](../adr/0013-idempotency-key-http.md) — apply FastEndpoints `.Idempotency()` to admin `POST /api/v1/inventory/stock-items/{productId}/adjust` (stock-adjust double-click guard) backed by `redis-cache`
- [ADR-0015](../adr/0015-time-timezone-policy.md) — `stock_events.OccurredAtUtc` + `AppendedAtUtc` are `timestamptz`; `ReservationExpiryWorker` injects BCL `TimeProvider` and calls `GetUtcNow()` (makes expiry deterministic in tests via `FakeTimeProvider`); reservation TTL arithmetic uses `DateTimeOffset`
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. Inventory-specific:

| Phase | Skill | When |
|---|---|---|
| Designing the event store | `backend-development:event-store-design` | FIRST — event store patterns: schema, append, rehydration, concurrency |
| Designing projections | `backend-development:cqrs-implementation` | projection handler patterns; in-transaction vs async catch-up |
| Building replay mechanics | `backend-development:projection-patterns` | rebuild strategy for projections from the event stream |
| Before writing `ReserveStockCommand` handler | `superpowers:brainstorming` | race conditions are subtle — concurrent reserves, TTL expiry mid-confirm; explore options before committing |
</skills>

<autonomous_evolution>
Inventory-specific triggers:

- **Rehydration API shape** — static `Fold(events)` vs instance `Apply(event)` builder pattern. Pick, justify.
- **Projection rebuild strategy** — `inventory.md § 9.3` says `TRUNCATE` + re-apply; verify feasible with your schema constraints (FK from audit to stock_levels? — if yes, rebuild order matters).
- **Hot aggregate warning** — ADR-0006 + `inventory.md § 10.4` acknowledge per-ProductId contention on a flash-sale SKU. If integration tests demonstrate this, document the observed behavior (retry exhaustion rate, latency).
- **TTL vs CompensationTimeout** — if saga's `CompensationTimeout` (300s) approaches reservation TTL (900s) under slow refund paths, flag. Consider whether extending TTL for orders in active saga is warranted (it ISN'T for v1 — document the rationale).
- **Consumer-group reuse** — `inventory-stock-init` is shared across the ProductCreated and OrderCancelled consumers (see `<wave_progress>` deviation #1). If a future operational concern surfaces — e.g., offset coupling prevents replaying one consumer without the other — escalate and split the group at that point.
</autonomous_evolution>

<success_criteria>
- A Wave-2 (Checkout saga) agent can drive full reserve → confirm and reserve → release compensation flows.
- Catalog's `ProductCreatedEvent` initializes a new stream when consumed (integration test running Catalog + Inventory together). **DEFERRED** until Catalog lands its Avro schema — see `<wave_progress>` deviation #2.
- `StockLevelChanged` fires **only on 0 ↔ positive** threshold crossings (never on every stock move).
- Expired reservations auto-release via `ReservationExpiryWorker` — verifiable by advancing `FakeTimeProvider` forward in an integration test.
- `InsufficientStock` flows through as a `Result.Fail` → outbox → Kafka event, never as an exception (architecture test enforced).
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] Event store table scaffolded with PK `(StreamId, Version)` + append-only enforcement (arch test)
- [ ] 2 projection tables + handlers upserting within same DbContext transaction as event append
- [ ] 6 ES events with reducers; `StockItem.Fold` verified correct on unit tests
- [ ] Optimistic concurrency: `UniqueViolationException` → retry once; second conflict → `InventoryErrors.ConcurrencyConflict`
- [ ] **Rehydration observability** per [ADR-0006 §Observability and the snapshot threshold](../adr/0006-event-sourcing-for-inventory.md): `EventStoreRepository.RehydrateAsync` emits `inventory.aggregate.rehydration.duration` (ms histogram) + `inventory.aggregate.rehydration.event_count` histograms tagged by `ProductId`. Integration test asserts p99 duration < 1s on a 1k-event stream. The metric + threshold open the v2 snapshot work item; no snapshot code in v1.
- [ ] 5 external Avro events + 3 saga-command Avro + outbox publishers for all 5 externals
- [ ] 3 saga-command Kafka consumers + 1 Catalog-event consumer (ProductCreated — **REAL** as of M5; Avro shipped Wave 1) + 1 Ordering-event consumer (OrderCancelled → release-if-still-reserved — **REAL** as of M5; Avro shipped Wave 1) — ALL with inbox dedup. **No** per-message service-auth token validation per ADR-0010 lines 102-106 (v1 = PLAINTEXT broker; production = broker SASL/OAUTHBEARER + ACLs).
- [ ] Shared consumer group `inventory-stock-init` for BOTH Catalog and Ordering event consumers, separate from `inventory-reservation-commands` (commands group) — see `<wave_progress>` deviation #1
- [ ] `ReservationExpiryWorker` (hosted service): polls every 1 min using injected BCL `TimeProvider`; publishes `ReleaseReservationCommand(ReleaseReason.Expiry)` per expired
- [ ] `StockLevelChanged` fires **only on 0 ↔ positive** threshold crossings (integration test proves it)
- [ ] `InsufficientStock` → `Result.Fail` → outbox `StockReservationFailedEvent` (NEVER throws; arch test forbids throwing domain errors from this handler)
- [ ] Admin `POST /api/v1/inventory/stock-items/{productId}/adjust` with `.Idempotency()` + authorization
- [ ] Integration tests cover all 3 `example-mapping/inventory.md` sessions + the race between TTL expiry and confirm
- [ ] `InventoryErrors` mirrors `error-taxonomy.md § 3.4`
- [ ] All timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain code (arch test)
- [ ] Correlation-id roundtrips: Kafka header → handler → `stock_events.CorrelationId` column → outbox → emitted event header
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:** `services/Inventory/**`, `test/Inventory.*.Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/**`, `docker-compose.yaml` (touch only if topic / relay / consumer group drifted from Wave 0), `Directory.Packages.props` (Inventory-specific), `docs/bc-design/inventory.md` + glossary + example-mapping.

**Do not touch:** other services, saga, platform code (except `.avsc`), other BCs' Avro schemas.
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- The `inventory.reservations` topic doesn't have 6 partitions (per `events-catalog.md` D-2; under-partitioning breaks saga fan-out).
- ADR-0006's "when NOT to use ES" guidance contradicts something in `inventory.md` you're about to implement (means a doc drift — escalate).
- BCL `TimeProvider` is not auto-registered by the Generic Host (Wave 0 prerequisite missing / host setup drift).
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Milestones (✓ = completed in prior sessions; see `<wave_progress>` for delivery details):

1. ✓ Scaffold 4 layers + project references; `dotnet build` green — Session 1
2. Domain layer (`StockItem`, ES event types, `Fold` method, reducers) + unit tests
3. Event-store repository (append + rehydrate + retry-once) + integration test against Testcontainers Postgres
4. Application layer (saga-command handlers, projection handlers, outbox publishers) + integration test
5. Infrastructure layer (DbContext, EF mappings, Kafka consumers × 5 — 2 STUBBED pending upstream BCs) + integration test
6. `ReservationExpiryWorker` hosted service + integration test with `FakeTimeProvider`
7. Admin HTTP endpoints + `.Idempotency()` + functional tests
8. Architecture tests (append-only, no throws from InsufficientStock handler, no `DateTime.UtcNow`)
9. Integration tests for `example-mapping` sessions + TTL/confirm race
10. docker-compose smoke + session summary

Each session picks up at the first unchecked milestone. At session end, append a new block to `<wave_progress>` and check off the milestones completed.
</session_management>

<verification>
```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Inventory.UnitTests/
dotnet test test/Inventory.ArchitectureTests/
dotnet test test/Inventory.IntegrationTests/
dotnet test test/Inventory.FunctionalTests/
docker compose --profile full up -d
# Verify topics with correct partition counts
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic inventory.stock-events      # expect 3 partitions
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic inventory.reservations       # expect 6 partitions
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic inventory.reservation-commands  # expect 3 partitions
# Smoke: fake ProductCreatedEvent → stream initialized
# (see integration test SimulateProductCreatedInitializesStream)
```

Paste actual output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
**Question:** Rehydration API shape — static `StockItem.Fold(events)` vs instance builder `var agg = new StockItem(); foreach (e in events) agg.Apply(e)`?

**Bad answer:** "Static Fold is simpler."

**Good answer:** "Static `StockItem.Fold(IEnumerable<IDomainEvent> events) -> StockItem`. Reasons: (1) pure-function rehydration — no partial-state risk; caller cannot observe an aggregate mid-rehydration; (2) testability — unit tests pass a curated event list and assert final state; no construct-then-mutate sequence; (3) matches event-store-design skill's canonical pattern. Trade-off accepted: slightly more boilerplate inside `Fold` (switch-on-event type) but it's localized; the builder pattern would scatter apply logic across `Apply(StockItemInitializedEvent)` / `Apply(StockReservedEvent)` etc. overloads. Verified by `StockItemFoldTests` — 6 event types × realistic sequences."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`. Inventory-specific notes:

- ES-specific decisions: retry-count, rehydration API, TTL worker cadence
- Race conditions encountered (TTL vs confirm, concurrent reserves on last-unit) + how handled
- Whether you needed snapshots (should be NO in v1 per ADR-0006 — confirm)
- Hot-aggregate behaviour under load testing if tested — retry exhaustion rate
- Consumer-group reuse verified — single `inventory-stock-init` group for BOTH Catalog + Ordering consumers (per `<wave_progress>` deviation #1)
- ADR-0015 time policy — `FakeTimeProvider` used in TTL-expiry tests for determinism

Proceed.
</session_summary>
