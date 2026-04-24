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
- Reservation TTL = 15 min default (configurable)
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
- [ADR-0010](../adr/0010-service-to-service-auth.md) — inbound JWT validation for admin endpoints (scope `inventory.commands.*`); saga-command Kafka consumer validates the `X-Service-Token` header from `checkout-saga` client; Catalog-event consumer does NOT require service-auth (event topic, not command)
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
- [ ] 5 external Avro events + 3 saga-command Avro + outbox publishers for all 5 externals
- [ ] 3 saga-command Kafka consumers + 1 Catalog-event consumer (ProductCreated — STUBBED until Catalog lands) + 1 Ordering-event consumer (OrderCancelled → release-if-still-reserved — STUBBED until Ordering lands) — ALL with inbox dedup; saga-command consumers validate service-auth token
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
