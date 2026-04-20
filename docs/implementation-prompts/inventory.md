# Master System Prompt — Implement the **Inventory** Bounded Context

> Paste this as the first message in a fresh Claude Code session for `C:\Users\david.capcuch\Desktop\Git\DotNetAtlas`.

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
- Wave 0 platform prep merged. Specifically: `Platform.SharedKernel` has `IClock`; `Platform.ServiceDefaults` has correlation-id + service-auth; 3 new Inventory topics (`inventory.stock-events`, `inventory.reservations`, `inventory.reservation-commands`) + `outbox-relay-inventory` container; Keycloak `inventory-service` client.
- Catalog's `ProductCreatedEvent` Avro schema registered (Inventory consumes to initialize streams). Inventory can scaffold + unit-test in parallel; integration-test the Catalog-event consumer after Catalog lands.
</prerequisites>

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
- Consumer groups: `inventory-stock-init` (ProductCreated), `inventory-reservation-commands` (saga cmds), `inventory-order-cancelled` (SEPARATE group, per `eshop-master-design.md § E.10`)
- Reservation TTL = 15 min default (configurable)
- `InsufficientStock` is **BUSINESS-EXPECTED**, not a bug — returns `Result.Fail(...)` → external `StockReservationFailedEvent`; NEVER throws
- Schemas FORWARD_TRANSITIVE (events) + FULL_TRANSITIVE (commands) per [ADR-0007](../adr/0007-avro-compatibility-modes.md)
- HTTP routes (admin only) under `/api/v1/inventory/...` per ADR-0012
</contract>

<design_open>
You own these. Justify each in your session summary.

- Event-store repository shape: append + rehydrate + retry-once-on-conflict
- `StockItem.Fold(IEnumerable<DomainEvent>)` static method (or instance) — your call on the rehydration API
- Projection handler organization — one per event vs multiplexed
- `ReservationExpiryWorker` cadence (`inventory.md` says every minute — confirm or tune)
- Retry-once policy implementation (detect `UniqueViolationException` on `(StreamId, Version)`; rehydrate; re-attempt)
- `InventoryErrors` API
- Architecture tests: `stock_events` is append-only (no `Update`/`Delete` on repository)
- Whether admin stock-adjust endpoint adopts FastEndpoints `.Idempotency()` (recommended for `AdjustStockCommand`)
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
- [ADR-0015](../adr/0015-time-timezone-policy.md) — `stock_events.OccurredAtUtc` + `AppendedAtUtc` are `timestamptz`; `ReservationExpiryWorker` uses `IClock.UtcNow` (makes the expiry deterministic in tests); reservation TTL arithmetic uses `DateTimeOffset`
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
- **Consumer-group distinction** — `inventory-stock-init` (ProductCreated consumer) MUST be distinct from `inventory-order-cancelled` (OrderCancelled consumer) per master-design § E.10. Verify in Kafka setup.
</autonomous_evolution>

<success_criteria>
- A Wave-2 (Checkout saga) agent can drive full reserve → confirm and reserve → release compensation flows.
- Catalog's `ProductCreatedEvent` initializes a new stream when consumed (integration test running Catalog + Inventory together).
- `StockLevelChanged` fires **only on 0 ↔ positive** threshold crossings (never on every stock move).
- Expired reservations auto-release via `ReservationExpiryWorker` — verifiable by setting `IClock.FakeClock` forward in integration test.
- `InsufficientStock` flows through as a `Result.Fail` → outbox → Kafka event, never as an exception (architecture test enforced).
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] Event store table scaffolded with PK `(StreamId, Version)` + append-only enforcement (arch test)
- [ ] 2 projection tables + handlers upserting within same DbContext transaction as event append
- [ ] 6 ES events with reducers; `StockItem.Fold` verified correct on unit tests
- [ ] Optimistic concurrency: `UniqueViolationException` → retry once; second conflict → `InventoryErrors.ConcurrencyConflict`
- [ ] 5 external Avro events + 3 saga-command Avro + outbox publishers for all 5 externals
- [ ] 3 saga-command Kafka consumers + 1 Catalog-event consumer (ProductCreated) + 1 Ordering-event consumer (OrderCancelled → release-if-still-reserved) — ALL with inbox dedup; saga-command consumers validate service-auth token
- [ ] Distinct consumer groups per master-design § E.10 — `inventory-stock-init` and `inventory-order-cancelled` NOT reused
- [ ] `ReservationExpiryWorker` (hosted service): polls every 1 min using `IClock`; publishes `ReleaseReservationCommand(ReleaseReason.Expiry)` per expired
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

- Consumer groups collide (Wave 0 should have set `inventory-stock-init` and `inventory-order-cancelled` as DISTINCT — verify before implementing).
- The `inventory.reservations` topic doesn't have 6 partitions (per `events-catalog.md` D-2; under-partitioning breaks saga fan-out).
- ADR-0006's "when NOT to use ES" guidance contradicts something in `inventory.md` you're about to implement (means a doc drift — escalate).
- `IClock` is not registered in `Platform.ServiceDefaults` (Wave 0 prerequisite missing).
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Suggested commit milestones:

1. Scaffold 4 layers + project references; `dotnet build` green
2. Domain layer (`StockItem`, ES event types, `Fold` method, reducers) + unit tests
3. Event-store repository (append + rehydrate + retry-once) + integration test against Testcontainers Postgres
4. Application layer (saga-command handlers, projection handlers, outbox publishers) + integration test
5. Infrastructure layer (DbContext, EF mappings, Kafka consumers × 5) + integration test
6. `ReservationExpiryWorker` hosted service + integration test with `FakeClock`
7. Admin HTTP endpoints + `.Idempotency()` + functional tests
8. Architecture tests (append-only, no throws from InsufficientStock handler, no `DateTime.UtcNow`)
9. Integration tests for `example-mapping` sessions + TTL/confirm race
10. docker-compose smoke + session summary
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
- Consumer-group distinction verified (`inventory-stock-init` vs `inventory-order-cancelled`)
- ADR-0015 time policy — `FakeClock` used in TTL-expiry tests for determinism

Proceed.
</session_summary>
