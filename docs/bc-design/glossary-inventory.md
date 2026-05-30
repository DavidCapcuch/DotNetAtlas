# Inventory Bounded Context — Ubiquitous Language Glossary

> Scope: the Inventory BC only. Terms here are how Inventory speaks — not how Catalog, Basket, Ordering, or the checkout saga speak, even when those BCs use the same word for a different concept. Translator boundaries are called out explicitly.

---

## Addendum — review-resolved clarifications

### ReservationStatus

Internal aggregate state tracker on `ReservationInfo`: `Active`, `Confirmed`, `Released`. **NOT published** on any external event — consumers infer status from the event type they receive (`StockReservedEvent` → Active, `ReservationConfirmedEvent` → Confirmed, `ReservationReleasedEvent` → Released). This design avoids versioning an enum across service boundaries; if a new status is added internally, external consumers are not affected.

### ReleaseReason

Enum carried on `ReservationReleasedDomainEvent`: `Compensation` (saga-driven rollback after a downstream failure — payment fail, confirmation fail), `Expiry` (automatic release after 15-minute TTL via the `ReservationExpiryWorker` background job), `Cancellation` (explicit user or admin action on a live order). Distinguishes the three legitimate release paths for audit, metrics, and downstream reaction.

---

## Aggregate & state

### StockItem
The aggregate root of the Inventory BC. Represents all state for one product's physical and logical stock: how much is on hand, how much is reserved, and the lifecycle of each reservation. Keyed by `ProductId` (shared with Catalog). The aggregate is **event-sourced** — its state is not stored directly; it is rehydrated by folding an append-only stream of events.

### OnHand
The count of physical units currently in the warehouse for a `StockItem`. Increases on `StockReceivedDomainEvent`; decreases on `ReservationConfirmedDomainEvent` (shipped) and on negative `StockAdjustedDomainEvent` (write-off). NEVER decreases on `StockReservedDomainEvent` — reserved stock is still physically present.

### Reserved
The sum of `Quantity` across all reservations in `Status = Active`. Increases on `StockReservedDomainEvent`; decreases on `ReservationConfirmedDomainEvent` and `ReservationReleasedDomainEvent`.

### Available
A computed value: `Available = OnHand - Reserved`. The number a new reservation could still claim. Enforced by the aggregate's invariants to be `>= 0` at all times (no over-reservation). Exposed as a materialized column in the `current_stock_levels` projection.

### Version
The monotonic 1-based count of events applied to a stream. Used for optimistic concurrency (UNIQUE `(StreamId, Version)` on the event-store table). Zero means the stream does not yet exist (no `StockItemInitializedDomainEvent` appended).

---

## Reservations

### Reservation
A time-bounded hold of N units of a `ProductId` against a specific `OrderId`. Created by the checkout saga via `ReserveStockCommand`. Has a lifecycle: `Active` → `Confirmed` (stock shipped) OR `Active` → `Released` (compensation, expiry, or cancellation). A reservation is NOT an order line — it is Inventory's translation of an order line into a physical commitment.

### ReservationId
The unique identifier of a reservation, supplied by the saga (`Guid`, `Guid.CreateVersion7()`). Distinct from `OrderId`. Modelled as a strongly-typed wrapper `record ReservationId(Guid Value)` to prevent accidental confusion with other Guids at call sites.

### ReservationStatus
Enum `{ Active, Confirmed, Released }`. Drives allowed state transitions — `ConfirmReservationCommand` and `ReleaseReservationCommand` require `Active`. Held on `ReservationInfo` in-memory and on `reservation_audit` projection.

### ReleaseReason
Enum `{ Compensation, Expiry, Cancellation }` carried on every `ReservationReleasedDomainEvent`. Critical for auditing and for the checkout saga's ability to distinguish genuine compensation (its own rollback) from TTL timeouts (customer didn't pay fast enough) from explicit customer cancellation.

### TTL (Time-To-Live)
The duration after which an unconfirmed reservation is automatically released. Default **15 minutes** from `ReservedAtUtc`. Stored on `StockReservedDomainEvent.ExpiresAtUtc` at reservation time — once written, the expiry is a fact, not a moving target. Enforced by the `ReservationExpiryWorker` hosted service, not by DB triggers: every expiry produces a real `ReservationReleasedDomainEvent(reason=Expiry)` so no state change is silent.

### ReservationExpiryWorker
The hosted service that scans `reservation_audit` for expired `Active` reservations every 60 seconds and issues `ReleaseReservationCommand` with `ReleaseReason=Expiry`. The mechanism that guarantees TTL enforcement while keeping all state changes auditable.

---

## Events & event sourcing

### ES event / Event-sourced event
A record stored in the `inventory.stock_events` table that IS part of the aggregate's state. When rehydrating a `StockItem`, these events are folded via reducer functions to reconstruct the aggregate. Unlike external events, ES events are not versioned Avro schemas — they are internal `DomainEvent`-derived records. Inventory is the only BC in the eShop solution that uses the suffix `Event` (not `DomainEvent`) for these records to mark them as the persistence model.

### Reducer
The pure function `(state, event) → state` that produces the next aggregate state given the current state and one event. In code, this is typically named `Apply(event)` on the aggregate. Reducers are the ONLY place state mutates. Command methods are the ONLY place new events are produced.

### Event store
The append-only table `inventory.stock_events` that holds the stream of ES events per `StockItem`. Primary key `(StreamId, Version)`. NOT an external product (no EventStoreDB) — deliberately kept in the shared PostgreSQL instance so event append + projection update + outbox insert share one DB transaction.

### Stream
The sequence of all ES events for one `StockItem`, keyed by `StreamId = ProductId`. Rehydration reads a stream in order; the event store holds one stream per product.

### Append-only
The discipline that historical events are NEVER modified or deleted. Corrections happen by appending NEW events (e.g., `StockAdjustedDomainEvent`), never by rewriting history. This is the foundation of auditability.

### Replay
The act of reading events from a stream (or across all streams) and feeding them through reducer functions. Two uses: (1) **rehydrating** an aggregate by replaying its single stream at command-handling time; (2) **rebuilding** a projection by replaying all streams through a projection handler.

### Projection
A denormalized read model derived from the event stream by an in-process `IDomainEventHandler<T>`. Lives in the `inventory` PostgreSQL schema alongside the event store but in separate tables. Examples: `CurrentStockLevelsView`, `ReservationAuditView`. Projections are **disposable** — if corrupted, they can be truncated and rebuilt by replaying the event stream.

### Snapshot
(Deferred to v2.) A stored state snapshot used to accelerate rehydration on long streams — load latest snapshot + replay only events with `Version > snapshot.Version`. Not in v1 because reference simplicity outweighs performance in the baseline showcase.

---

## Commands & the write path

### InitializeStockItemCommand
Opens a new stream for a `ProductId` on first reference. Triggered by the inbox consumer on Catalog's `ProductCreatedEvent`. Idempotent — a second initialization is a no-op.

### ReceiveStockCommand
Admin/ops action recording an inbound stock movement. Produces `StockReceivedDomainEvent`, increases `OnHand`.

### ReserveStockCommand
Saga-issued. Attempts to hold N units for an order. The only command that can return `Result.Fail(InsufficientStockError)` — all other rejections are `DataIntegrityException` (bugs).

### ConfirmReservationCommand
Saga-issued after successful payment. Commits the reservation — `Reserved` and `OnHand` both decrement. Produces `ReservationConfirmedDomainEvent`.

### ReleaseReservationCommand
Saga-issued on compensation, OR expiry-worker-issued on timeout, OR admin-issued on explicit cancellation. Drops an `Active` reservation without shipping. Produces `ReservationReleasedDomainEvent` with a `ReleaseReason`.

### AdjustStockCommand
Admin/ops correction with a signed `Delta` — damage write-off, recount, transfer-out. Produces `StockAdjustedDomainEvent`. Cannot push `OnHand` or `Available` negative.

### Optimistic concurrency
The concurrency model: reservations append at `Version+1` based on state observed at `Version=V`. The DB's UNIQUE `(StreamId, Version)` constraint rejects conflicting appends. On conflict, the handler retries ONCE; if still conflicting, it returns `Result.Fail(ConcurrencyError)` to the caller (saga), which decides whether to retry at its level or compensate. No pessimistic locks.

---

## External surface

### External event
A Kafka-published Avro-serialized event designed for cross-service consumption. Namespaced under `Inventory.Stock` or `Inventory.Reservations`. Emitted via the transactional outbox from in-process handlers subscribed to ES events. Distinct from ES events — external events are the **contract**, ES events are the **truth**.

### Threshold crossing
The rule that governs when `StockLevelChanged` is emitted: only on `Available` crossing `0 ↔ positive`, not on every arithmetic change. Prevents bus spam; downstream consumers (Catalog's `IsSellable`) only care about sell/no-sell state, not every integer tick.

### Compensation
The saga's act of reversing a previously completed step. In Inventory's vocabulary, compensation = `ReleaseReservationCommand` with `ReleaseReason=Compensation`. Produces an auditable event (not a silent rollback) so the event stream always tells the truth about what happened and why.

### Inbox consumer
The component in `Inventory.Infrastructure` that subscribes to upstream external events — in Inventory's case, only Catalog's `ProductCreatedEvent`. Translates upstream events into Inventory commands, using an `inventory.command_inbox` table for idempotency.

---

## Value objects and primitives

### Quantity
A non-negative (or strictly positive depending on use) integer wrapped in `record Quantity(int Value)`. Avoids passing naked `int` around command signatures; defines arithmetic ops scoped to the domain.

### StockSource
A free-form string token on `StockReceivedDomainEvent` (e.g., `"receiving-dock"`, `"returns"`, `"transfer-in"`). Not an enum — kept deliberately open in v1 to avoid premature taxonomy work.

### StockItemSnapshot
A read-only DTO `record StockItemSnapshot(Guid ProductId, int OnHand, int Reserved, int Available, int Version)`. Returned by `GetStockLevelQuery`. NOT the same as an "event-sourcing snapshot" — this is a read-model projection, not a rehydration accelerator.

### ReservationInfo
In-memory immutable record on the rehydrated aggregate: `(ReservationId, ProductId, Quantity, OrderId, ReservedAtUtc, ExpiresAtUtc, Status)`. Produced during event folding; queried during `ConfirmReservation` / `ReleaseReservation` handling.

---

## Translator / boundary notes

### Product (Catalog) vs StockItem (Inventory)
Catalog's `Product` and Inventory's `StockItem` describe the same underlying thing viewed through two lenses — commerce vs physical custody. They share a key (`ProductId`) but no shared model. Crossing the boundary goes through events, never through a shared object graph.

### OrderLine (Ordering) vs Reservation (Inventory)
An Ordering `OrderLine` is a contract to deliver N units at a price. An Inventory `Reservation` is a physical hold of N units against an `OrderId`. The checkout saga is the translator — it creates one reservation per order line, fans in the outcomes, and drives the state machine.

### CorrelationId
A `Guid` carried on saga-initiated events and optionally stored on `inventory.stock_events.CorrelationId`. Not a domain concept — it's a diagnostics/tracing token that lets ops say "show me every event touched by checkout saga run X." Shared across BCs; Inventory neither generates nor interprets it, only persists it.
