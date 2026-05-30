# ADR-0006: Event Sourcing for Inventory

## Status

Accepted (2026-04-18)

## Context

The eShop reference solution (v1) showcases multiple persistence and architecture patterns across bounded contexts:

- **Catalog** — CQRS read projections over a traditional OLTP write model
- **Basket** — Redis-backed aggregate with an SQL fallback
- **Ordering** — rich status FSM on a traditional OLTP aggregate
- **Inventory** — **Event Sourcing** with projections (this ADR)
- **Payments** — traditional OLTP (existing from Weather, reused)
- **Notifications** — event-driven consumer (existing from Weather, reused)

Per `docs/eshop-general-plan.md` § "New Patterns Showcased (vs Weather)", Inventory is the **single Event-Sourced example** in the entire solution. Every other BC uses traditional ORM-backed aggregates with the platform outbox pattern. This is deliberate — the reference solution illustrates that ES is a specialized tool, not a default.

Inventory was chosen as the ES example because the domain aligns with the pattern's strengths (see `docs/bc-design/inventory.md` § 2):

- **Genuine audit-trail requirement** — stock movements are compliance-sensitive; operations, payments, and ops routinely ask "what happened to this SKU last Tuesday at 14:00?"
- **Reservations, confirmations, and releases map naturally to an event stream per product** — the domain is fundamentally about discrete, ordered, auditable movements rather than "current state that sometimes changes"
- **Temporal queries are useful** — "what was available at checkout time?" is a first-class question, not a forensic one
- **Projection variety** — ops want one view, Catalog wants another (sellable yes/no), payments may want a third, procurement a fourth; a single event stream naturally feeds them all
- **Compensation is a domain concern, not an afterthought** — the checkout saga MUST reverse reservations deterministically, and an event-based history makes "undo" honest rather than implicit

But because this is a *reference* solution, it must ALSO teach readers *when NOT to use ES*. "Event Sourcing for everything" is a common anti-pattern that turns vanilla CRUD domains into expensive audit logs with cryptic debugging. This ADR therefore does two things: it justifies ES for Inventory specifically, AND it records the trade-offs and anti-indications so learners don't over-apply the pattern to their own work.

**Scope clarification.** This ADR covers the Inventory BC only. It does NOT propose event sourcing for:

- Catalog (CQRS read projections over OLTP is sufficient; product data is read-dominated)
- Basket (ephemeral session state; ES adds no value over Redis)
- Ordering (a rich status FSM on OLTP gives the same auditability without replay tooling)
- Payments (existing OLTP from Weather, reused without modification)
- Notifications (pure downstream consumer with no domain state)

If a future bounded context in this solution requests ES, it must justify the fit against the drivers and anti-indications below and file its own ADR — ES should never be adopted by inertia.

## Decision Drivers (ranked)

1. **Showcase the ES pattern meaningfully** — ES is the teaching target for this BC; it must genuinely fit the domain, not be forced
2. **Audit trail** — stock movements need a complete, tamper-evident history for regulatory and dispute-resolution reasons
3. **Compensation support** — the saga-driven reserve/release/confirm flows benefit from an explicit event model rather than "update row to previous value"
4. **Don't mislead learners** — the reference must distinguish "ES good here" from "ES good everywhere"; every other BC deliberately uses traditional OLTP
5. **Operational realism** — ES requires event-store discipline (versioning, replay, migrations); the solution must teach this honestly, not paper over it
6. **Interoperate with existing platform** — must reuse `Platform.SharedKernel`, the transactional outbox, Kafka + Avro + Schema Registry without reinventing infrastructure

## Considered Options

### Option 1: Full Event Sourcing with projections (chosen)

The aggregate's state is NOT stored directly; it is rehydrated by folding an append-only stream of events.

- PostgreSQL table `inventory.stock_events` acts as the event store — no external EventStoreDB (see `docs/bc-design/inventory.md` § 8.1)
- Projection tables `inventory.current_stock_levels` and `inventory.reservation_audit` provide the denormalized read models
- `StockItem` rehydrates from the stream; commands produce new events; projections update in the SAME DB transaction as the append
- Stream id = `ProductId`; version is monotonic per stream (1..N) with UNIQUE `(StreamId, Version)` enforcing optimistic append

**Pros:**
- Complete history in the DB itself; no need for a separate audit pipeline
- Rebuildable projections — schema and logic changes are disposable
- Temporal queries are natural (fold events up to a timestamp)
- Explicit compensation model (release reasons are distinguishable events)
- No dual-write problem — event store, projections, and outbox share one DB transaction

**Cons:**
- Higher operational complexity (backup, retention, replay tooling)
- Real learning curve for developers unfamiliar with ES
- Per-aggregate optimistic concurrency can thrash on hot aggregates
- Event schema evolution demands discipline (upcasters, versioning)

### Option 2: Traditional OLTP + outbox pattern

`StockItem` aggregate stored as a row in an `inventory.stock_items` table. Matches what every other BC in the solution does today.

- EF Core loads the row directly; no rehydration
- Domain events raised in-process and added to the outbox for Kafka publishing
- Audit trail achieved externally via the outbox + an archival Kafka consumer that writes to cold storage
- Compensation performed by "update row back to previous value" or by running an offsetting command

**Pros:**
- Simple, familiar, fast — the default for the rest of the solution
- Debugging is trivial (`SELECT * FROM stock_items WHERE id = X`)
- No event versioning / upcaster discipline required
- Matches team skillset if ES experience is limited

**Cons:**
- No per-aggregate event history inside the DB — audit requires external archive + replay pipeline
- Temporal queries require reconstructing state from archival data
- "Projection rebuild" idiom is absent; schema changes require migrations plus ad-hoc backfill
- Compensation is opaque — "update row" leaves no trace of WHY it was updated

### Option 3: Event-carried state transfer (choreography, no aggregate)

Every stock change is published to Kafka; other services maintain their own stock views. No central `StockItem` aggregate at all.

- Each consumer (Catalog, checkout saga, ops dashboard) holds its own copy of stock state
- No single authority for invariants — each service applies events independently
- No command surface — state changes are just events

**Rejected at the top of the evaluation:**

- Violates aggregate-consistency boundaries — no single place enforces `Available = OnHand - Reserved >= 0`
- No single authority for "is this reservation valid?" under concurrent requests
- ADR-0001 already rejects choreography for cross-service workflows for structurally similar reasons

## Evaluation Matrix

| Driver (ranked)                          | Option 1: ES + projections                                | Option 2: OLTP + outbox                                      | Option 3: Choreography            |
|------------------------------------------|-----------------------------------------------------------|--------------------------------------------------------------|-----------------------------------|
| 1. Showcase ES meaningfully              | ✅ Native fit — teaches ES on a domain that actually wants it | ❌ Doesn't showcase ES at all                                 | ❌ Doesn't showcase ES; not even an aggregate |
| 2. Audit trail                           | ✅ The stream IS the audit                                 | ⚠️ Achievable via archive topic; no in-DB history              | ⚠️ Scattered across consumers; no single authority |
| 3. Compensation support                  | ✅ Explicit release event; deterministic reversal          | ⚠️ Possible but opaque — "update row" has no trace by default | ❌ No aggregate = no atomic compensation |
| 4. Don't mislead learners                | ✅ Paired with "when NOT to use ES" guidance below          | ✅ Would be the safe default but teaches nothing new           | ❌ Teaches an anti-pattern |
| 5. Operational realism                   | ⚠️ Introduces replay/versioning discipline (documented)     | ✅ Familiar Postgres ops                                      | ❌ Operationally hardest — distributed debugging |
| 6. Interoperate with existing platform   | ✅ Same Postgres + outbox; no new infra                    | ✅ Same as every other BC                                     | ⚠️ Would require inventing coordination primitives |

## Decision

We will use **Option 1: Full Event Sourcing with projections** for Inventory ONLY. All other bounded contexts in the eShop reference solution (Catalog, Basket, Ordering, Payments, Notifications) use traditional OLTP with the platform outbox pattern.

## Rationale

**Inventory's domain is a natural fit for ES.** Per `docs/bc-design/inventory.md` § 14.4, the BC has a small, bounded set of event types (`StockItemInitialized`, `StockReceived`, `StockReserved`, `ReservationConfirmed`, `ReservationReleased`, `StockAdjusted`) that map 1:1 to real business operations. There are no `StockItemUpdatedEvent` generic diffs — every event is something a warehouse operator or a saga can point to and explain. Each event has a defined reducer and a clear triggering command. This is the sign of a genuine event-sourced domain as opposed to CRUD with extra steps.

**Stream granularity is per-product, which keeps contention bounded.** Each `StockItem` has its own stream keyed by `ProductId`, so two concurrent reservations on different products never interact. Concurrent reservations on the *same* product resolve via the UNIQUE `(StreamId, Version)` constraint with a single optimistic retry (`inventory.md` § 10). There is one honest exception — a single flash-sale SKU under thousands of concurrent reservations — and we document it as a known limitation rather than paper over it (§ 10.4 of the BC design). The "losing" command returns `Result.Fail(InsufficientStockError)` which the saga converts into a compensation path; this is the intended behaviour, not a bug.

**Audit and temporal queries are genuine needs, not hypothetical.** "Show me every movement for SKU X last month" and "what was available at checkout time T" are routine questions in real inventory operations. A fold over the event stream answers both directly. With OLTP we would need to reconstruct this from external archives — more infrastructure, less authoritative, and liable to drift from the write model over time. The event stream IS the audit log, not a derived artifact that might get out of sync.

**Reservation TTL and projection rebuild are clean ES idioms.** The `ReservationExpiryWorker` issues a real `ReleaseReservationCommand` on expiry, producing a durable `ReservationReleasedDomainEvent(reason=Expiry)` — there are no silent state changes (§ 11 of the BC design). When a projection's schema or computation changes, we TRUNCATE it and replay events through the same handler that runs at steady state (§ 9.3 and § 14.3). This is the property that makes ES uniquely valuable: read models are disposable, derived, and evolvable without touching the write model or the aggregate's code.

**No new infrastructure is introduced.** The event store is a Postgres table in the same database as projections and the outbox. There is no EventStoreDB, no Kafka-as-source-of-truth, no dual-write problem between an event store and a relational projection. Everything — event append, projection upsert, outbox insert — lives in one Postgres transaction. This keeps the operational surface area of the solution small, and it means an engineer debugging an incident can use familiar tools (`psql`, standard backups, standard migrations) while still benefiting from ES semantics.

## Consequences

### Positive

- Complete, auditable, tamper-evident history of every stock movement, reservation, and adjustment in the DB itself
- Temporal queries ("what was available at 3pm?") are answered by folding events with `OccurredAtUtc <= T`
- Projections are rebuildable after schema or logic changes without touching the write model or the aggregate
- New projection types (analytics, low-stock alerting, per-warehouse views) can be added later by subscribing the same event stream — no aggregate changes required
- Saga compensation is explicit in the event model: a `ReservationReleasedDomainEvent(reason=Compensation)` is distinguishable from `reason=Expiry` and `reason=Cancellation`, and each consumer can route accordingly
- No dual-write problem: the event store, projections, and outbox all live in one Postgres transaction (see `inventory.md` § 14.1)
- Forces developers to learn append-only reasoning, a transferable skill even for non-ES domains

### Negative

- Operational overhead: the event store requires a backup strategy, a retention policy, and migration tooling distinct from the rest of the solution
- Debugging is harder: aggregate state is derived, not printed — a developer inspecting a bug must read the stream and fold mentally (or run a replay) rather than `SELECT * FROM stock_items WHERE id = X`
- Even strongly-consistent in-process projections create eventual-consistency surprises in Kafka-consuming tests — a cross-service consumer sees projection state only after commit + relay + consume
- Event schema evolution is a discipline: breaking changes require upcasters or dual-publish; teams new to ES frequently get this wrong
- Inventory's persistence stack is deliberately different from every other BC — readers must understand that this is intentional, not accidental inconsistency
- Rehydration cost grows linearly with stream length; future streams may need snapshots (deferred)

### Risks

- **Learners over-apply ES.** A common failure mode for reference-solution readers is to treat every BC they build as ES. Mitigation: the "When NOT to Use ES" section below is mandatory reading, and every other BC in this reference uses traditional OLTP as a deliberate counterexample.
- **Hot-aggregate contention.** Per-`ProductId` streams bound contention well in the common case, but a single best-seller under a flash sale could still thrash the optimistic retry loop. Mitigation: v1 documents this as a known limitation (`inventory.md` § 10.4); v2 options (command queuing, reservation batching, geographic sharding) are listed but deferred.
- **Projection drift.** If a projection handler has a bug, the projection can silently diverge from the truth (the event stream). The classic symptom is "reservations look off by one but nobody notices until quarter-end." Mitigation: a nightly replay-validation job comparing projection state to a sample event-fold is listed as a v2 operational concern and is documented now so it isn't forgotten.
- **Replay cost grows with history.** If a single stream grows to millions of events (unlikely for stock — more likely for a long-lived heavily-reserved SKU), rehydration slows linearly. Mitigation in v1: emit `inventory.aggregate.rehydration.duration` and `inventory.aggregate.rehydration.event_count` histograms on every rehydrate, with a paging alert at p99 > 1s over a 15-minute window (see § Implementation Notes → Observability and the snapshot threshold). Snapshotting every N events is specified in `inventory.md` § 8.2 as a v2 work item that the alert opens.
- **GDPR data deletion.** Append-only streams make "delete this user's events" hard. Inventory v1 carries no PII beyond user GUIDs, so the risk is small; crypto-shredding is the v2 approach if PII ever enters an event.
- **Team knowledge concentration.** ES has a steeper onboarding curve than OLTP. If the one engineer who understands the replay pipeline leaves, the bus factor becomes a real operational risk. Mitigation: `inventory.md` and this ADR together form the self-contained onboarding material; a runbook for projection rebuild is an explicit operational artifact.

## Guidance — When NOT to Use Event Sourcing

**IMPORTANT for learners.** This section exists because the single biggest failure mode of a reference solution showcasing ES is that readers apply it to every new service they build. ES is a power tool with a real learning curve — reach for it only when the domain asks for it. The following anti-indications are distilled from `docs/bc-design/inventory.md` § 14.5 and elaborated for this ADR:

- **Low-change / read-dominated domains** — if an entity is written once and read thousands of times (a product description, a user profile, a static configuration), the overhead of a stream-per-entity buys nothing. Traditional OLTP + outbox is simpler, faster, and just as auditable via archive topics. Rule of thumb: if the write rate is low enough that you can't justify a per-aggregate stream's existence, use OLTP.

- **Hot aggregates with extreme write contention** — a single aggregate receiving thousands of writes per second will see concurrency failures that ES does not fix (and can worsen via append-and-retry loops). Per-aggregate optimistic concurrency doesn't scale horizontally the way a partitioned OLTP table does. Consider ES only if you can shard the stream or if your throughput per aggregate is bounded.

- **CRUD-in-disguise models** — if the "events" you are inventing are `CustomerNameChanged`, `CustomerEmailChanged`, `CustomerAddressChanged` one-field-at-a-time with diff payloads, you are not event-sourcing a domain — you are using ES as an expensive audit log. The events don't correspond to business operations; they correspond to form fields. Use OLTP with audit triggers (or a change-data-capture pipeline) instead.

- **Inexperienced teams** — ES demands discipline: event versioning, upcasting, replay tooling, projection-rebuild runbooks, out-of-order handling, and a clear mental model of "the stream IS the state." Without that discipline, you get silent data corruption that only surfaces months later during a replay. Prefer CRUD + outbox until your team has navigated an ES system they didn't build.

- **No audit requirement** — if no one will ever ask "what happened and when?" beyond "what's the latest state?", the overhead is not justified. Audit requirements should be real (regulation, dispute resolution, ops forensics), not hypothetical or speculative. Build for the known requirements, not for imagined ones.

- **GDPR / erasure-heavy domains** — ES makes "delete this user's events" difficult because events are append-only by design. Forget-about tables and crypto-shredding are mitigations, but they add complexity that a GDPR-heavy domain (customer records, personal messages, support conversations) rarely justifies. OLTP + standard erasure pipelines are typically a better fit.

- **Domains that need cross-aggregate transactional atomicity** — ES aggregates are transactional units. If your use case regularly requires atomic updates across multiple aggregates, you are fighting the model; use a single-aggregate-per-transaction OLTP design, a saga (for cross-boundary coordination), or re-examine your boundaries.

- **Domains where the event model would drift from the business model over time** — if you can't predict today what kinds of projections you'll want tomorrow, ES is powerful. But if the business model is genuinely stable and read patterns are known (product catalog, reference data), ES is overkill — pick the read model you need and store it directly.

If none of the above apply AND the domain has genuine auditability, temporal-query, or compensation-driven-replay requirements, ES can be a good fit. Otherwise, start with traditional OLTP + outbox — it is the default everywhere else in this reference solution for exactly these reasons.

## Implementation Notes

### Storage and schema

- **Event store table:** `inventory.stock_events` with primary key `(StreamId, Version)` — the UNIQUE constraint is the sole optimistic-append primitive (`inventory.md` § 8.1). No pessimistic locks, no `SELECT ... FOR UPDATE`.
- **Projections:** `inventory.current_stock_levels` (hot path — product page, availability) and `inventory.reservation_audit` (ops queries, expiry-worker scan). Both live in the same schema as the event store.
- **Payload format:** `jsonb` column for v1 (legible during debugging, greppable during incidents, simple to inspect with `psql`). A future move to `bytea` + MemoryPack is a pure encoding change — the event schema stays the same.
- **Additional columns promoted from payload:** `OccurredAtUtc` (temporal index), `CorrelationId` (saga forensics), `EventType` (projection-rebuild filter, deserializer discriminator).
- **Indexes:** clustered PK on `(StreamId, Version)` for range rehydration; `OccurredAtUtc` for temporal queries; partial `CorrelationId WHERE NOT NULL` for saga joins; `EventType` for selective projection rebuilds.

### Write path

- **Aggregate rehydration:** `SELECT Version, EventType, Payload, OccurredAtUtc FROM inventory.stock_events WHERE StreamId = @productId ORDER BY Version ASC`, then deserialize each row's payload and fold into a fresh `StockItem` via its reducer (`inventory.md` § 14.2).
- **Transactional envelope:** event-store INSERTs, projection UPSERTs, and outbox INSERTs happen in ONE Postgres transaction. There is no dual-write — the event store and the outbox are the same database.
- **Concurrency retry budget:** one in-handler retry on UNIQUE violation, then fail-fast with `Result.Fail(ConcurrencyError)` so the saga layer decides retry vs compensate (`inventory.md` § 10.2). Longer retry loops belong to the saga, which has visibility into the full flow.
- **Idempotency:** every inbound command carries a command id (GUIDv7) recorded in `inventory.command_inbox` within the same transaction; duplicates become no-ops.

### Read path and projections

- **Projection handlers:** in-process `IDomainEventHandler<TEvent>` implementations upsert rows in the SAME DB transaction as the event-store append. Keeps in-process reads strongly consistent with writes.
- **Handler idempotency:** `LastVersion` column on each projection row — if `event.Version <= row.LastVersion`, skip. Protects against at-least-once delivery from the internal dispatcher.
- **Replay procedure:** to rebuild a projection, TRUNCATE the projection table and re-apply all events per stream in version order through the same handler used at steady state (`inventory.md` § 9.3). Document this as a runbook, not tribal knowledge.

### Observability and the snapshot threshold

Snapshots are deferred to v2, but the **signal that triggers them** must exist in v1 — otherwise hot-aggregate slowdown is discovered by a paged on-call rather than by an alert. v1 MUST emit:

- **Histogram metric `inventory.aggregate.rehydration.duration`** (units: milliseconds), tagged by `ProductId`, recorded in `EventStoreRepository.RehydrateAsync` around the SELECT-and-fold path.
- **Histogram metric `inventory.aggregate.rehydration.event_count`** (units: events folded), same tagging, so high-latency rehydrations can be cross-correlated with stream length to confirm the cliff is O(N) and not a network blip.

**Threshold (alert): `inventory.aggregate.rehydration.duration` p99 > 1s for any stream over a 15-minute window** → page → open a snapshot-implementation work item, OR shorten retention on that stream's terminal events (release/expire). The threshold is set well below the saga's `StockReservationSeconds: 60` ([ADR-0004:192](0004-checkout-saga-topology.md:192)) so the cliff is detected long before it interacts with saga timeouts.

The decision boundary "ship snapshots OR shorten retention" stays open in v1 on purpose — both are valid responses to the alert; which one is right depends on the workload that triggered it. The alert exists so the trade-off is made deliberately, not silently.

### Cross-service messaging

- **Outbox:** cross-service events (`inventory.stock-events`, `inventory.reservations`) are written to the existing `Platform.Outbox` table in the same transaction and relayed to Kafka by the existing `Platform.OutboxRelay`. Reuses the proven infrastructure — nothing bespoke.
- **Kafka topics:** `inventory.stock-events` (3 partitions, key = `ProductId`, consumed by Catalog) and `inventory.reservations` (6 partitions, key = `OrderId` to co-partition reservation events per order, consumed by the checkout saga). Configured in `docker-compose.yaml`.
- **Schema Registry:** all external Avro events stored at `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Stock/*.avsc` and `.../Inventory/Reservations/*.avsc`. No JSON or Protobuf on the cross-service wire — Avro only per the messaging constraint.
- **Event filtering at the wire:** not every ES event becomes an external event. `StockLevelChangedEvent` is emitted only on threshold crossings (0 ↔ positive) — raw arithmetic does not flood the bus.

### Future work (deliberately deferred)

- **Snapshots:** NOT implemented in v1 by design. Streams are short-lived per reservation and moderate-volume overall — measure before optimizing. The "measure" is concrete: v1 emits `inventory.aggregate.rehydration.duration` (see § Observability above); when p99 exceeds 1s the alert fires and snapshots become a real work item. Specification in `inventory.md` § 8.2 if/when added.
- **Multi-warehouse support:** v1 has one logical warehouse per `ProductId`; adding `LocationId` to the stream is a v2 concern.
- **Projection-drift validation job:** nightly replay-sample comparison is documented as a v2 operational concern.
- **GDPR crypto-shredding:** only needed if/when events carry PII beyond user GUIDs.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — establishes the orchestration model that consumes Inventory's external events; Option 3 (choreography) is rejected in both ADRs for structurally similar reasons (no single owner of the workflow, scattered compensation logic, operationally opaque)
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — Inventory participates in the checkout saga via `ReserveStockCommand` / `ConfirmReservationCommand` / `ReleaseReservationCommand`; ES enables clean, deterministic compensation with distinguishable release reasons (`Compensation` vs `Expiry` vs `Cancellation`), which the saga routes differently
- [Events catalog § 3 — Kafka topics](../bc-design/events-catalog.md) — `inventory.stock-events` and `inventory.reservations` are classified as event-log (retention.ms=-1) so downstream BCs can replay-rebuild; rationale and per-topic table.
