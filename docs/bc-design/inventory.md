# Inventory Bounded Context

> **Purpose:** Stock-level authority — the single source of truth for how much of each product is physically on hand, how much is reserved for in-flight orders, and when that stock arrives, leaves, or is adjusted.
>
> **Pattern showcase:** **Event Sourcing** — the only Event-Sourced BC in the eShop reference solution. The aggregate's state is not stored; it is rehydrated by folding an append-only stream of events. Commands produce new events; projections build denormalized read models asynchronously. Rationale and trade-offs: see [ADR-0006](../adr/0006-event-sourcing-for-inventory.md).
>
> **Companion glossary:** [glossary-inventory.md](./glossary-inventory.md)

---

## 1. Ubiquitous Language Summary

Inventory speaks the language of **physical stock**, not commerce. Key terms:

- **StockItem** — the aggregate representing all physical and logical state for one product's stock. Keyed by `ProductId` (shared with Catalog).
- **OnHand** — the count of physical units currently in the warehouse.
- **Reserved** — the count of units committed to in-flight orders but not yet picked/shipped.
- **Available** — the count a customer could still reserve; computed as `OnHand - Reserved`.
- **Reservation** — a time-bounded hold of N units against a specific `OrderId`. Has a TTL (default 15 minutes) after which it is auto-released.
- **Stock receipt** — an inbound movement adding to `OnHand` (deliveries from suppliers, returns re-shelved).
- **Stock adjustment** — an out-of-band correction to `OnHand` by ops/admin (damage write-off, count correction). Signed delta.
- **Reservation lifecycle** — Active → Confirmed (order shipped, stock physically decremented) or Active → Released (compensation, expiry, or cancellation).
- **Event store** — the append-only stream of ES events that IS the StockItem's state.
- **Projection** — a denormalized read model derived from the event stream, rebuildable at any time.
- **Replay** — the act of reading a stream and folding it into an aggregate (rehydration) or a projection (rebuild).

Language boundary with Catalog: Catalog says "Product" (the sellable concept, with price, description, category). Inventory says "StockItem" (the physical counterpart, keyed by the same `ProductId`). The two words describe distinct aspects of the same underlying thing — this is a translator boundary, not a naming conflict.

Language boundary with Ordering: Ordering says "OrderLine" (a contract to deliver N units at a price). Inventory says "Reservation" (a physical hold of N units for an `OrderId`). The checkout saga is the translator.

---

## 2. Subdomain Classification

- **Type:** Core supporting subdomain with strong invariants.
- **Why ES here (v1 reference):** The domain has four properties that align with ES's strengths (per ADR-0006):
  1. **Audit-trail demand** — operations, payments, and compliance routinely ask "what happened to this SKU last Tuesday at 14:00?" A natural event stream IS the answer.
  2. **Temporal queries** — "what was available at checkout time?" is answerable by folding events up to a timestamp.
  3. **Multiple projections needed** — current levels (hot query), reservation audit (ops), stock movement history (payments), low-stock alerts (procurement). All derive from the same stream.
  4. **Compensation support** — the checkout saga MUST be able to reverse reservations deterministically. Release-by-event is more honest than "update row to previous value" (which has no trace).

The "when NOT to use ES" side of this decision is called out in § 9 (Pattern Showcase) and ADR-0006.

---

## 3. Aggregates

### 3.1 StockItem (Aggregate Root)

**Identity:** `ProductId` (Guid). Shared with Catalog's `Product.ProductId` — the key bridging the two BCs. Inventory never introduces a separate "StockItemId."

**Lifetime:** Lives for as long as the product exists in Catalog. Created lazily on first interaction (`InitializeStockItemCommand`, triggered by Catalog's `ProductCreatedEvent` inbox consumer).

**State (derived, not stored):** All fields below are computed by folding the event stream. They are read-only on a rehydrated aggregate.

| Field | Type | Meaning |
|-------|------|---------|
| `ProductId` | `Guid` | Aggregate identity (= stream id). |
| `OnHand` | `int` (non-negative) | Physical units present in the warehouse. |
| `Reserved` | `int` (non-negative) | Sum of `Quantity` across all `Active` reservations. |
| `Available` | `int` (computed: `OnHand - Reserved`) | Units a new reservation could still claim. |
| `Reservations` | `IReadOnlyDictionary<ReservationId, ReservationInfo>` | In-memory lookup of every reservation ever made (kept for status transitions). Entries stay after Confirmed/Released for historical lookups within the aggregate's lifecycle. |
| `Version` | `int` | Count of events applied; 0 for a never-initialized stream, then monotonic 1..N. Used for optimistic concurrency. |

**Invariants** (enforced by the command handlers before emitting events):

1. `OnHand >= 0` at all times.
2. `Reserved >= 0` at all times.
3. `Available = OnHand - Reserved >= 0` at all times (no over-reservation).
4. Cannot confirm a reservation that is not `Active`.
5. Cannot release a reservation that is not `Active`.
6. Cannot confirm or release a `ReservationId` unknown to the stream.
7. A `StockItem` can only be initialized once (second `StockItemInitializedDomainEvent` in a stream is a bug).
8. Events are append-only; historical events are never modified or deleted.

**Public API (command surface):**

| Method | Preconditions | Emitted Event | Next State |
|--------|---------------|---------------|------------|
| `Initialize(ProductId)` | `Version == 0` | `StockItemInitializedDomainEvent` | `OnHand=0, Reserved=0, Version=1`. |
| `ReceiveStock(qty, source, userId?)` | `Version >= 1`, `qty > 0` | `StockReceivedDomainEvent` | `OnHand += qty`. |
| `Reserve(reservationId, qty, orderId, ttl)` | `Version >= 1`, `qty > 0`, `Available >= qty`, `!Reservations.ContainsKey(reservationId)` | `StockReservedDomainEvent` | `Reserved += qty`; reservation added as `Active`. Returns `Result.Fail(InsufficientStockError)` on precondition miss (no event, no state change). |
| `ConfirmReservation(reservationId)` | reservation is `Active` | `ReservationConfirmedDomainEvent` | `Reserved -= qty`, `OnHand -= qty`; reservation -> `Confirmed`. |
| `ReleaseReservation(reservationId, reason)` | reservation is `Active` | `ReservationReleasedDomainEvent` | `Reserved -= qty`; reservation -> `Released`. |
| `AdjustStock(delta, reason, userId?)` | `OnHand + delta >= 0` AND `(OnHand + delta) - Reserved >= 0` | `StockAdjustedDomainEvent` | `OnHand += delta` (delta is signed). |

The aggregate does NOT expose setters. External callers go through commands handled in `Inventory.Application`.

### 3.2 Aggregate-boundary rationale (Vernon's four rules)

1. **Protect true invariants within one boundary.** The invariant `Available = OnHand - Reserved >= 0` requires both numbers to be mutated atomically. Splitting reservation into its own aggregate would allow races where two concurrent reserve commands both observe stale `Reserved`. Keep them together.
2. **Design small aggregates.** The aggregate root holds only integers and a dictionary of reservations; there are no child entities with independent lifecycles. Even with 1,000+ historical reservations, the event stream is a bounded list of small records. The aggregate **retains terminal reservations** (`Confirmed`/`Released`) in its in-memory `Reservations` dictionary for the lifetime of a single rehydrate (matching § 3.1 line 62) — it needs them so `ConfirmReservation` / `ReleaseReservation` can distinguish "unknown `ReservationId`" (bug, throws `DataIntegrityException`) from "known but already terminal" (returns `Result.Fail(ReservationNotActiveError)`). The **durable terminal-state store** is the projection-side `reservation_audit` table updated by `ReservationLifecycleDomainEventHandler`; that's where ops/analytics queries against historical reservations land, not the aggregate.
3. **Reference other aggregates by identity.** `OrderId` on a reservation is a Guid, not an Order object. `ProductId` is the identity shared with Catalog; Inventory never loads the Catalog's Product aggregate. `ReceivedByUserId` is a Guid reference to a user snapshot owned elsewhere.
4. **Use eventual consistency outside the boundary.** Projections update after the event is appended (same transaction for in-process consistency, but READ queries see the post-commit state only). External consumers (Catalog's availability flag, checkout saga) receive summary events asynchronously via Kafka — eventually consistent by design.

---

## 4. Value Objects

| Name | Shape | Notes |
|------|-------|-------|
| `Quantity` | `record Quantity(int Value)` with `Value >= 0` (or `> 0` on `ReceiveStock` / `Reserve`) | Wraps the primitive to avoid passing naked ints. Arithmetic ops defined (`+`, `-`). |
| `ReservationId` | `record ReservationId(Guid Value)` | Strong-typed wrapper with equality and `ToString` — distinguishes from `OrderId` at call sites. Created by the saga; supplied on `ReserveStockCommand`. |
| `ReservationInfo` | `record ReservationInfo(ReservationId ReservationId, Guid ProductId, int Quantity, Guid OrderId, DateTimeOffset ReservedAtUtc, DateTimeOffset ExpiresAtUtc, ReservationStatus Status)` | In-memory on the rehydrated aggregate. Immutable; produced during event folding. |
| `ReservationStatus` | `enum { Active, Confirmed, Released }` | Drives state transitions on `ConfirmReservation` / `ReleaseReservation`. |
| `ReleaseReason` | `enum { Compensation, Expiry, Cancellation }` | Carried on `ReservationReleasedDomainEvent` and `ReleaseReservationCommand`. Critical for ops/auditing — a release is never "just a release." |
| `StockSource` | `string` wrapper (e.g., `"receiving-dock"`, `"returns"`, `"transfer-in"`) | Light enum-like token; free-form for the v1 reference. |
| `StockItemSnapshot` | `record StockItemSnapshot(Guid ProductId, int OnHand, int Reserved, int Available, int Version)` | Read-only projection DTO for queries that don't need the full reservation list. Also the shape returned by `GetStockLevelQuery`. |

---

## 5. Internal Events (Event-Sourced Events)

These records are **both** the aggregate's persistence format AND its in-process domain events. They are stored as rows in `inventory.stock_events` (the write model — these ARE the aggregate state) AND dispatched through `IDomainEventHandler<T>` to update projections and to trigger external-event publication.

All fields carry `OccurredOnUtc` via the base class. Suffix is `*DomainEvent`, matching the cross-BC convention. The CLR simple name is the discriminator persisted in `inventory.stock_events.event_type` and round-tripped by `StockEventSerializer.EventTypeRegistry`.

### 5.1 `StockItemInitializedDomainEvent`

**Purpose:** Bootstraps a new stream when a product is first referenced.

| Field | Type | Notes |
|-------|------|-------|
| `ProductId` | `Guid` | = StreamId. |
| `OccurredOnUtc` | `DateTimeOffset` | Base class. |

**Reducer** (state before → event → state after):
```
{ Version=0 } --StockItemInitializedDomainEvent--> { OnHand=0, Reserved=0, Reservations={}, Version=1 }
```

**Triggered by:** `InitializeStockItemCommand` (fired from inbox consumer on Catalog's `ProductCreatedEvent`).

### 5.2 `StockReceivedDomainEvent`

**Purpose:** Records an inbound stock movement (supplier delivery, return).

| Field | Type | Notes |
|-------|------|-------|
| `ProductId` | `Guid` | = StreamId. |
| `Quantity` | `int` | `> 0`. |
| `Source` | `string` | Free-form; e.g., `"receiving-dock"`, `"returns"`. |
| `ReceivedByUserId` | `Guid?` | Nullable — system-initiated receipts have no user. |
| `OccurredOnUtc` | `DateTimeOffset` | |

**Reducer:**
```
{ OnHand=O, Reserved=R, Version=V } --StockReceivedDomainEvent(qty=Q)--> { OnHand=O+Q, Reserved=R, Version=V+1 }
```

**Triggered by:** `ReceiveStockCommand` (admin / ops API).

### 5.3 `StockReservedDomainEvent`

**Purpose:** Records a successful hold of N units against an order.

| Field | Type | Notes |
|-------|------|-------|
| `ProductId` | `Guid` | = StreamId. |
| `ReservationId` | `Guid` | Saga-supplied; unique per reservation. |
| `Quantity` | `int` | `> 0`. |
| `OrderId` | `Guid` | Owning order — used for saga queries and compensation lookups. |
| `ExpiresAtUtc` | `DateTimeOffset` | = `OccurredOnUtc + TTL` (default 15 min). |
| `OccurredOnUtc` | `DateTimeOffset` | |

**Reducer:**
```
{ OnHand=O, Reserved=R, Reservations=... } --StockReservedDomainEvent(rid, qty=Q, orderId=OID, exp=E)-->
{ OnHand=O, Reserved=R+Q, Reservations=...∪{ rid → Active(Q, OID, E) } }
```

**Precondition (command handler):** `O - R >= Q` AND `rid` is not already in `Reservations`.

**Triggered by:** `ReserveStockCommand` (Checkout saga, one per order line item). On precondition miss the aggregate returns `Result.Fail(InsufficientStockError)` and no ES event is appended.

### 5.4 `ReservationConfirmedDomainEvent`

**Purpose:** Finalizes a reservation — stock physically leaves the warehouse.

| Field | Type | Notes |
|-------|------|-------|
| `ProductId` | `Guid` | = StreamId. |
| `ReservationId` | `Guid` | |
| `ConfirmedAtUtc` | `DateTimeOffset` | Also flows to `OccurredOnUtc`. |

**Reducer** (let `Q` be the reservation's quantity):
```
{ OnHand=O, Reserved=R, rid → Active(Q,...) } --ReservationConfirmedDomainEvent(rid)-->
{ OnHand=O-Q, Reserved=R-Q, rid → Confirmed(Q,...) }
```

**Precondition:** `Reservations[rid].Status == Active`.

**Triggered by:** `ConfirmReservationCommand` (Checkout saga, after payment success).

### 5.5 `ReservationReleasedDomainEvent`

**Purpose:** Drops a reservation without shipping — compensation, expiry, or cancellation.

| Field | Type | Notes |
|-------|------|-------|
| `ProductId` | `Guid` | = StreamId. |
| `ReservationId` | `Guid` | |
| `ReleaseReason` | `enum` | `Compensation` / `Expiry` / `Cancellation`. |
| `ReleasedAtUtc` | `DateTimeOffset` | |

**Reducer:**
```
{ OnHand=O, Reserved=R, rid → Active(Q,...) } --ReservationReleasedDomainEvent(rid, reason)-->
{ OnHand=O, Reserved=R-Q, rid → Released(Q,...) }
```

**Precondition:** `Reservations[rid].Status == Active`.

**Triggered by:** `ReleaseReservationCommand` (Checkout saga compensation, `ReservationExpiryWorker` on TTL expiry, or admin cancel).

### 5.6 `StockAdjustedDomainEvent`

**Purpose:** Admin correction — damage write-off, recount, transfer-out. Signed delta.

| Field | Type | Notes |
|-------|------|-------|
| `ProductId` | `Guid` | = StreamId. |
| `Delta` | `int` | Signed. Negative for write-offs, positive for corrections. |
| `Reason` | `string` | Free-form, required. |
| `AdjustedByUserId` | `Guid?` | The ops user who entered the adjustment. |
| `OccurredOnUtc` | `DateTimeOffset` | |

**Reducer:**
```
{ OnHand=O, Reserved=R } --StockAdjustedDomainEvent(delta=D)--> { OnHand=O+D, Reserved=R }
```

**Precondition:** `O + D >= 0` AND `(O + D) - R >= 0` — cannot adjust stock below reservations.

**Triggered by:** `AdjustStockCommand` (admin / ops API).

---

## 6. External Events (Cross-Service, Kafka via Outbox)

Per § 3 of the master design, cross-service topics carry enriched summary events. These are produced by application-layer handlers that subscribe to the internal ES events and emit Avro-compiled records through the transactional outbox.

**Topic allocations:**
- `inventory.stock-events` — threshold-crossing stock-level signals (for Catalog availability flags, low-stock alerts).
- `inventory.reservations` — reservation lifecycle events (for the checkout saga and optional notifications).

**Avro namespaces:**
- `Inventory.Stock` for stock-level events.
- `Inventory.Reservations` for reservation lifecycle events.

**Schema Registry path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Stock/*.avsc` and `.../Inventory/Reservations/*.avsc`.

### 6.1 `StockLevelChangedEvent` — Topic `inventory.stock-events`

**Triggering rule (NOT one-per-ES-event):** Only fire when `Available` crosses a meaningful threshold — specifically `0 ↔ positive`. Raw stock arithmetic should not flood the bus; downstream consumers (Catalog) only care about "is this sellable yes/no" and (in future) "low-stock threshold crossed."

| Avro field | Type | Notes |
|------------|------|-------|
| `ProductId` | `uuid` | Stream id. |
| `NewOnHand` | `int` | Post-change. |
| `NewReserved` | `int` | Post-change. |
| `NewAvailable` | `int` | Post-change. |
| `ChangedAtUtc` | `timestamp-millis` | = triggering ES event's `OccurredOnUtc`. |

**Consumer:** Catalog (updates `IsSellable` projection).

**Avro schema (`Inventory.Stock.StockLevelChangedEvent.avsc`):**

```json
{
  "type": "record",
  "name": "StockLevelChangedEvent",
  "namespace": "Inventory.Stock",
  "doc": "Emitted when a StockItem's availability crosses a business-meaningful threshold (e.g., zero to positive or positive to zero). Not emitted on every stock change.",
  "fields": [
    {
      "name": "ProductId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Product whose stock level changed. Shared key with Catalog."
    },
    {
      "name": "NewOnHand",
      "type": "int",
      "doc": "Total physical units on hand after the change."
    },
    {
      "name": "NewReserved",
      "type": "int",
      "doc": "Units currently reserved across all active reservations after the change."
    },
    {
      "name": "NewAvailable",
      "type": "int",
      "doc": "Units a new reservation could still claim (OnHand - Reserved) after the change."
    },
    {
      "name": "ChangedAtUtc",
      "type": { "type": "long", "logicalType": "timestamp-millis" },
      "doc": "UTC timestamp of the triggering event."
    }
  ]
}
```

### 6.2 `StockReservedEvent` (external) — Topic `inventory.reservations`

Emitted 1:1 with internal `StockReservedDomainEvent` (ES). This is the positive outcome of a `ReserveStockCommand` — the checkout saga consumes it to advance to the payment step.

| Avro field | Type | Notes |
|------------|------|-------|
| `ProductId` | `uuid` | |
| `ReservationId` | `uuid` | |
| `OrderId` | `uuid` | Saga correlation key. |
| `Quantity` | `int` | |
| `ExpiresAtUtc` | `timestamp-millis` | Saga MUST confirm or release before this. |
| `ReservedAtUtc` | `timestamp-millis` | |

**Consumer:** Checkout saga (`saga/SagaOrchestrators/Checkout/`).

**Avro schema (`Inventory.Reservations.StockReservedEvent.avsc`):**

```json
{
  "type": "record",
  "name": "StockReservedEvent",
  "namespace": "Inventory.Reservations",
  "doc": "Emitted when units have been successfully reserved for an order. Consumed by the checkout saga as the positive outcome of ReserveStockCommand. The reservation is time-bounded by ExpiresAtUtc.",
  "fields": [
    {
      "name": "ProductId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Product that was reserved. Shared key with Catalog."
    },
    {
      "name": "ReservationId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Unique id of this reservation. Used by ConfirmReservationCommand and ReleaseReservationCommand."
    },
    {
      "name": "OrderId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Owning order (saga correlation id). Enables fan-in of multiple line-item reservations per order."
    },
    {
      "name": "Quantity",
      "type": "int",
      "doc": "Units reserved."
    },
    {
      "name": "ExpiresAtUtc",
      "type": { "type": "long", "logicalType": "timestamp-millis" },
      "doc": "UTC timestamp after which the reservation is automatically released by the TTL worker unless confirmed or explicitly released."
    },
    {
      "name": "ReservedAtUtc",
      "type": { "type": "long", "logicalType": "timestamp-millis" },
      "doc": "UTC timestamp when the reservation was created."
    }
  ]
}
```

### 6.3 `StockReservationFailedEvent` — Topic `inventory.reservations`

Emitted when `ReserveStockCommand` returns a failure (insufficient stock). There is NO corresponding ES event — a failed reservation doesn't mutate the aggregate. This is an application-level signal assembled from the command failure.

| Avro field | Type | Notes |
|------------|------|-------|
| `ProductId` | `uuid` | |
| `OrderId` | `uuid` | Saga correlation key. |
| `RequestedQuantity` | `int` | |
| `AvailableQuantity` | `int` | What was actually available at the time of rejection. |
| `FailedAtUtc` | `timestamp-millis` | |

**Consumer:** Checkout saga (triggers compensation path: cancel order).

**Avro schema (`Inventory.Reservations.StockReservationFailedEvent.avsc`):**

```json
{
  "type": "record",
  "name": "StockReservationFailedEvent",
  "namespace": "Inventory.Reservations",
  "doc": "Emitted when a reservation request cannot be fulfilled because Available < RequestedQuantity. Consumed by the checkout saga to trigger compensation. No corresponding ES event (failed reservations do not mutate the aggregate).",
  "fields": [
    {
      "name": "ProductId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Product for which the reservation failed."
    },
    {
      "name": "OrderId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Owning order (saga correlation id)."
    },
    {
      "name": "RequestedQuantity",
      "type": "int",
      "doc": "Units the saga attempted to reserve."
    },
    {
      "name": "AvailableQuantity",
      "type": "int",
      "doc": "Units actually available at the time of rejection (OnHand - Reserved). Included so the saga/UI can report the shortfall precisely."
    },
    {
      "name": "FailedAtUtc",
      "type": { "type": "long", "logicalType": "timestamp-millis" },
      "doc": "UTC timestamp when the reservation attempt was rejected."
    }
  ]
}
```

### 6.4 `ReservationConfirmedEvent` (external) — Topic `inventory.reservations`

Emitted 1:1 with internal `ReservationConfirmedDomainEvent` (ES).

| Avro field | Type | Notes |
|------------|------|-------|
| `ProductId` | `uuid` | |
| `ReservationId` | `uuid` | |
| `OrderId` | `uuid` | |
| `ConfirmedAtUtc` | `timestamp-millis` | |

**Consumer:** Checkout saga (informational). The saga's `ReservationConfirmedConsumer` updates its per-reservation tracking on `AwaitingConfirmation`, but it does **not** gate the transition — Ordering's `OrderConfirmedEvent` is the gate (see [checkout-saga.md § 4](checkout-saga.md) + [events-catalog.md § 2](events-catalog.md)). A "your order is being prepared" buyer notification would route via the command-driven pattern in [notifications.md § 2](notifications.md) — Inventory would emit a `NotifyUserCommand` (v2; [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)) from a dedicated outbox publisher; not wired in v1.

**Avro schema (`Inventory.Reservations.ReservationConfirmedEvent.avsc`):**

```json
{
  "type": "record",
  "name": "ReservationConfirmedEvent",
  "namespace": "Inventory.Reservations",
  "doc": "Emitted when a reservation is confirmed (stock physically committed to the order). OnHand is decremented by Quantity as part of the same transaction.",
  "fields": [
    {
      "name": "ProductId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Product whose reservation was confirmed."
    },
    {
      "name": "ReservationId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Reservation that was confirmed."
    },
    {
      "name": "OrderId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Owning order."
    },
    {
      "name": "ConfirmedAtUtc",
      "type": { "type": "long", "logicalType": "timestamp-millis" },
      "doc": "UTC timestamp of confirmation."
    }
  ]
}
```

### 6.5 `ReservationReleasedEvent` (external) — Topic `inventory.reservations`

Emitted 1:1 with internal `ReservationReleasedDomainEvent` (ES). Carries the reason so consumers distinguish genuine compensation from timeout expiry.

| Avro field | Type | Notes |
|------------|------|-------|
| `ProductId` | `uuid` | |
| `ReservationId` | `uuid` | |
| `OrderId` | `uuid` | |
| `ReleaseReason` | `enum { Compensation, Expiry, Cancellation }` | |
| `ReleasedAtUtc` | `timestamp-millis` | |

**Consumer:** Checkout saga — receives confirmation that compensation has completed (or learns that a reservation timed out so it can fail-fast the order).

**Avro schema (`Inventory.Reservations.ReservationReleasedEvent.avsc`):**

```json
{
  "type": "record",
  "name": "ReservationReleasedEvent",
  "namespace": "Inventory.Reservations",
  "doc": "Emitted when a reservation is released without being shipped. ReleaseReason distinguishes deliberate compensation (saga rollback), automatic expiry (TTL worker), and explicit customer/operator cancellation.",
  "fields": [
    {
      "name": "ProductId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Product whose reservation was released."
    },
    {
      "name": "ReservationId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Reservation that was released."
    },
    {
      "name": "OrderId",
      "type": { "type": "string", "logicalType": "uuid" },
      "doc": "Owning order."
    },
    {
      "name": "ReleaseReason",
      "type": {
        "type": "enum",
        "name": "ReleaseReason",
        "symbols": ["Compensation", "Expiry", "Cancellation"]
      },
      "doc": "Compensation = saga rollback. Expiry = TTL worker auto-release. Cancellation = customer or ops explicit action."
    },
    {
      "name": "ReleasedAtUtc",
      "type": { "type": "long", "logicalType": "timestamp-millis" },
      "doc": "UTC timestamp of release."
    }
  ]
}
```

---

## 7. Commands (shapes detailed in [use-cases.md](./use-cases.md))

Each command appends exactly one ES event — see § 5 (each subsection's **Triggered by:** line names its command) for the event details, fields, and reducer. This table summarises trigger source + the conditional external-event side-effects from § 6.

| Command | Trigger | External event side-effects |
|---------|---------|-----------------------------|
| `InitializeStockItemCommand` | Inbox consumer: Catalog's `ProductCreatedEvent` | (none by default) |
| `ReceiveStockCommand` | Admin/ops API call | `StockLevelChangedEvent` iff threshold crossed |
| `ReserveStockCommand` | Checkout saga | `StockReservedEvent` (success) OR `StockReservationFailedEvent` (insufficient stock) + `StockLevelChangedEvent` if it crosses zero |
| `ConfirmReservationCommand` | Checkout saga (after payment) | `ReservationConfirmedEvent` + `StockLevelChangedEvent` if threshold crossed |
| `ReleaseReservationCommand` | Checkout saga compensation OR `ReservationExpiryWorker` OR admin cancel | `ReservationReleasedEvent` + `StockLevelChangedEvent` if it goes back to positive |
| `AdjustStockCommand` | Admin/ops API call | `StockLevelChangedEvent` if threshold crossed |

`ReserveStockCommand` is the only command that can return `Result.Fail(InsufficientStockError)` — all others either succeed or throw a `DataIntegrityException` (bug: saga issuing a release for an unknown reservation, admin adjusting to negative, etc.).

---

## 8. Event Store Schema

### 8.1 Write-side table: `inventory.stock_events`

Schema `inventory` in the shared PostgreSQL database (same DB as projections — they differ only by schema and by table). No external event store product (no EventStoreDB). This is a deliberate choice for the reference solution: one infra dependency, Postgres-native transactions across event append + projection update + outbox message.

| Column | Type | Nullability | Notes |
|--------|------|-------------|-------|
| `StreamId` | `uuid` | NOT NULL | = `ProductId`. One stream per StockItem. |
| `Version` | `int` | NOT NULL | Monotonic per stream, 1-based. Enforced by UNIQUE `(StreamId, Version)`. |
| `EventType` | `text` | NOT NULL | Discriminator — the Avro-style record name (e.g., `"StockReservedDomainEvent"`). Used by the deserializer to pick the target record type. |
| `Payload` | `jsonb` | NOT NULL | Serialized internal event (MemoryPack-encoded bytes round-tripped through base64 or native `bytea` — for v1 reference: serialize internal record to JSON in jsonb column for legibility during debugging). Contains everything: the event-specific fields AND `OccurredOnUtc`. |
| `OccurredAtUtc` | `timestamptz` | NOT NULL | Copy of `event.OccurredOnUtc` — promoted to a column for efficient temporal queries (`WHERE OccurredAtUtc < @time`). |
| `AppendedAtUtc` | `timestamptz` | NOT NULL DEFAULT `now()` | DB-side insert timestamp. Differs from `OccurredAtUtc` when an event is backdated (e.g., replayed during tests). |
| `CorrelationId` | `uuid` | NULL | Checkout-saga correlation id when the command came from a saga. Enables "give me every event touched by this saga run" queries. |

**Primary key:** `(StreamId, Version)`.

**Indexes:**
- PK (above) — clustered on StreamId for range rehydration.
- `idx_stock_events_occurred_at ON (OccurredAtUtc)` — temporal queries.
- `idx_stock_events_correlation ON (CorrelationId) WHERE CorrelationId IS NOT NULL` — saga forensics.
- `idx_stock_events_event_type ON (EventType)` — projection rebuild by event kind.

**Append rules (enforced in the write path, not purely by the DB):**

1. Read the current max `Version` for the stream: `SELECT COALESCE(MAX(Version), 0) FROM inventory.stock_events WHERE StreamId = @pid`.
2. Apply commands in memory; the aggregate produces one or more events.
3. Attempt INSERT with `Version = max+1, max+2, ...`. The UNIQUE `(StreamId, Version)` constraint rejects duplicates — see § 10 Concurrency.
4. **Transactional envelope:** the INSERTs, the projection UPSERTs, AND the outbox INSERTs happen in the same DB transaction. There is no "dual-write" — event store and outbox are the same database.

**Rehydration query:**

```sql
SELECT Version, EventType, Payload, OccurredAtUtc
FROM inventory.stock_events
WHERE StreamId = @productId
ORDER BY Version ASC;
```

Fold the result into a fresh `StockItem` instance by applying each payload through its reducer.

### 8.2 Optional: snapshots (planned scope)

Not included today — every rehydration reads the full stream. For streams expected to grow beyond ~1000 events (high-velocity SKUs), a planned `inventory.stock_snapshots (StreamId, Version, SnapshotJson, TakenAtUtc)` table would rehydrate as "load latest snapshot + replay events with `Version > snapshot.Version`." Deliberately deferred so the reference solution demonstrates ES in its purest form. See [roadmap.md § 2.3 Inventory](../roadmap.md).

---

## 9. Read Projections

Projections are **denormalized read models** derived from the event stream. They live in the same `inventory` PostgreSQL schema, behind separate tables. They are rebuildable — if a projection falls out of sync (bug, schema change), drop the table and replay the event stream through the projection handler.

Each projection is updated by an in-process `IDomainEventHandler<T>` that subscribes to the relevant ES events. The handler runs in the **same DB transaction** as the event append — keeping the projection strongly consistent for in-process queries. (External consumers, via Kafka, remain eventually consistent.)

### 9.1 `CurrentStockLevelsView` — table `inventory.current_stock_levels`

**Purpose:** Hot-path query — "what's the stock level for ProductId X right now?" Used by Catalog availability flags, BFF product page, ops dashboards.

| Column | Type | Notes |
|--------|------|-------|
| `ProductId` | `uuid` PK | = StreamId. |
| `OnHand` | `int` NOT NULL | Current physical units. |
| `Reserved` | `int` NOT NULL | Sum of active reservations. |
| `Available` | `int` GENERATED ALWAYS AS (`OnHand - Reserved`) STORED | Materialized computed column for indexable reads. |
| `LastUpdatedUtc` | `timestamptz` NOT NULL | = last applied event's `OccurredAtUtc`. |
| `LastVersion` | `int` NOT NULL | Stream version of the last applied event — lets the handler detect out-of-order or duplicate applies and skip them idempotently. |

**Updated by:** Every ES event (upsert).
- `StockItemInitializedDomainEvent` → INSERT row with zeros.
- `StockReceivedDomainEvent` → `OnHand += qty`.
- `StockReservedDomainEvent` → `Reserved += qty`.
- `ReservationConfirmedDomainEvent` → `OnHand -= qty`, `Reserved -= qty`.
- `ReservationReleasedDomainEvent` → `Reserved -= qty`.
- `StockAdjustedDomainEvent` → `OnHand += delta`.
Always set `LastUpdatedUtc = event.OccurredAtUtc`, `LastVersion = event.Version`.

**Query:** `GetStockLevelQuery(ProductId) : StockLevelDto` — returns `StockItemSnapshot` value object.

**Indexes:**
- PK on `ProductId`.
- `idx_current_stock_levels_available ON (Available) WHERE Available <= 10` — partial index for low-stock alerts.

### 9.2 `ReservationAuditView` — table `inventory.reservation_audit`

**Purpose:** Ops query — "what's the status of reservation R?" and "all reservations for order O?" Also the source of truth for the `ReservationExpiryWorker` to find expired reservations.

| Column | Type | Notes |
|--------|------|-------|
| `ReservationId` | `uuid` PK | |
| `ProductId` | `uuid` NOT NULL | |
| `Quantity` | `int` NOT NULL | |
| `OrderId` | `uuid` NOT NULL | |
| `Status` | `text` NOT NULL CHECK (`IN ('Active','Confirmed','Released')`) | |
| `ReservedAtUtc` | `timestamptz` NOT NULL | |
| `ExpiresAtUtc` | `timestamptz` NOT NULL | The TTL expiry — driving query for the worker. |
| `ResolvedAtUtc` | `timestamptz` NULL | When Confirmed/Released; null while Active. |
| `ReleaseReason` | `text` NULL | Only populated when Status = Released. |

**Updated by:**
- `StockReservedDomainEvent` → INSERT row with `Status='Active'`.
- `ReservationConfirmedDomainEvent` → UPDATE `Status='Confirmed', ResolvedAtUtc=event.OccurredAtUtc`.
- `ReservationReleasedDomainEvent` → UPDATE `Status='Released', ResolvedAtUtc=event.OccurredAtUtc, ReleaseReason=event.ReleaseReason`.

**Queries:**
- `GetReservationByIdQuery(ReservationId) : ReservationDto`.
- `GetReservationsByOrderQuery(OrderId) : IReadOnlyList<ReservationDto>` — saga debugging.
- Worker: `SELECT ReservationId, ProductId FROM inventory.reservation_audit WHERE Status = 'Active' AND ExpiresAtUtc < now()`.

**Indexes:**
- PK on `ReservationId`.
- `idx_reservation_audit_order ON (OrderId)` — fan-in by order.
- `idx_reservation_audit_active_expiry ON (ExpiresAtUtc) WHERE Status = 'Active'` — expiry-worker scan.

### 9.3 Projection rebuild

Because projections are pure functions of the event stream, they are disposable. Procedure:

1. `TRUNCATE inventory.current_stock_levels;` (or the affected view).
2. Run a one-shot replay job: `SELECT ... FROM inventory.stock_events ORDER BY StreamId, Version ASC` and feed each payload through the same `IDomainEventHandler<T>` used at runtime.
3. Resume normal event-append traffic (queue up during rebuild, apply after).

For small streams (v1 reference), this is a minutes-scale operation. The pattern scales with snapshotting later (deferred).

---

## 10. Concurrency Model

### 10.1 Optimistic concurrency via version

The event store's UNIQUE `(StreamId, Version)` is the **only** concurrency primitive. No pessimistic locks, no row-level `SELECT ... FOR UPDATE`. The contract:

1. Handler rehydrates the aggregate by reading the stream → current `Version = V`.
2. Handler executes command → aggregate produces new events at versions `V+1, V+2, ...`.
3. Handler attempts INSERT. If the DB rejects with a unique-constraint violation, another writer got there first and the state we based decisions on is stale.

### 10.2 Conflict resolution

- **Retry once:** re-read the stream, re-apply the command against the now-current state, re-attempt INSERT.
- **If it fails again:** return `Result.Fail(ConcurrencyError)` to the caller (saga). The saga treats this as a transient failure and retries the whole step, or — if the retries exceed a policy threshold — triggers compensation.
- **Why only one retry in-handler:** longer retry loops belong to the saga layer, which has visibility into the full flow and policy-level decisions.

### 10.3 Idempotency

- **Inbound commands** carry a command id (GUIDv7). The command handler, before touching the aggregate, records the command id in an `inventory.command_inbox` table within the same transaction. A duplicate command id becomes a no-op.
- **Projection handlers** are idempotent by design via `LastVersion` — if `event.Version <= row.LastVersion`, skip. This protects against at-least-once delivery from the internal dispatcher.

### 10.4 Hot-aggregate warning

If a single SKU receives heavy concurrent reservation traffic (flash sale, limited-edition drop), the optimistic retry loop can thrash. **This is a known limitation of ES with per-aggregate streams** and is called out explicitly in ADR-0006 as a reason the pattern is NOT a good fit for all domains. Mitigation options considered (but deferred past v1): per-aggregate command queuing, reservation batching, sharding by geography. The reference solution documents the concern without implementing mitigation — the point is to showcase the baseline pattern honestly, including its failure modes.

---

## 11. Reservation TTL Policy

Reservations are **time-bounded**. The default TTL is **15 minutes** from `ReservedAtUtc`. The TTL is enforced by a dedicated background worker, not by a DB trigger — because expiry MUST produce a real `ReservationReleasedDomainEvent` so there are no silent state changes.

### 11.1 `ReservationExpiryWorker` (hosted service in `Inventory.Infrastructure`)

**Schedule:** Polls every 60 seconds (via `PeriodicTimer`).

**Algorithm each tick:**

1. Query: `SELECT ReservationId, ProductId FROM inventory.reservation_audit WHERE Status = 'Active' AND ExpiresAtUtc < now() LIMIT 100`.
2. For each row, issue `ReleaseReservationCommand(ReservationId, ProductId, ReleaseReason.Expiry)` through the normal command pipeline.
3. The command flows through the aggregate, appends a `ReservationReleasedDomainEvent` (ES) with reason `Expiry`, updates projections, and emits an external `ReservationReleasedEvent` on `inventory.reservations`.

### 11.2 Guarantees

- **No silent releases.** Every expiry produces a durable, auditable event.
- **Saga-aware.** The checkout saga receives the external `ReservationReleasedEvent` with `ReleaseReason=Expiry` and knows to fail the order (the customer took too long to pay).
- **At-least-once.** If the worker crashes mid-tick, its next run picks up the same expired reservations. The command handler is idempotent (see § 10.3).

### 11.3 TTL configurability

The 15-minute default lives in `InventoryOptions.ReservationTtl` in `appsettings.*.json`. It is the TTL applied at reservation time (not looked up by the worker) — once written into `StockReservedDomainEvent.ExpiresAtUtc`, the reservation's expiry is a fact carried by the event itself, not a moving target.

---

## 12. Integration Points

### 12.1 Upstream dependencies (inbound events Inventory consumes)

| From | Event | Why | Handling |
|------|-------|-----|----------|
| Catalog | `ProductCreatedEvent` | Need to open a stream for the new product. | Inbox consumer in `Inventory.Infrastructure` → issues `InitializeStockItemCommand`. Idempotent on `ProductId`. |
| Ordering | `OrderCancelledEvent` (on `ordering.orders`) | Release any still-`Active` reservation when an order is cancelled before shipment — covers the buyer-initiated cancel that does not flow through saga compensation. | `OrderCancelledEventKafkaHandler` queries `reservation_audit WHERE OrderId = msg.OrderId AND Status = Active` and dispatches one `ReleaseReservationCommand` per row. Idempotent on re-query. |

Beyond these events, Inventory consumes the saga-issued reservation commands on `inventory.reservation-commands` (`ReserveStockCommand`, `ConfirmReservationCommand`, `ReleaseReservationCommand` — see § 9). Inventory is a downstream of Catalog (structurally), a direct Published-Language consumer of Ordering's `OrderCancelledEvent`, and an upstream of the checkout saga (behaviorally).

### 12.2 Downstream consumers (who reads Inventory's external events)

| Consumer | Events | Purpose |
|----------|--------|---------|
| **Catalog** | `StockLevelChangedEvent` | Update `IsSellable` / availability projection for product cards. Relationship pattern: Catalog is **downstream, OHS-consumer** of Inventory's stock-level topic. |
| **Checkout saga** | `StockReservedEvent`, `StockReservationFailedEvent`, `ReservationConfirmedEvent`, `ReservationReleasedEvent` | Drives the reserve → pay → confirm OR compensate path. Relationship pattern: Saga is **Customer** of Inventory's reservation topic (Inventory is Supplier). Saga conforms to Inventory's schema. |
| **Notifications** (deferred) | (none in v1) | "Your order is being prepared" notification would route via the command-driven pattern in [notifications.md § 2](notifications.md) — Inventory would emit a channel-agnostic `NotifyUserCommand` (v2; [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)) rather than have Notifications subscribe to `inventory.reservations`. Not wired in v1. |

> **Consumers are canonical in [events-catalog.md § 2](events-catalog.md)** — the table above mirrors it for convenience; § 2 wins on any divergence.

### 12.3 Context map relationships

- **Catalog ↔ Inventory** — Shared Kernel on `ProductId` (the Guid is the only shared concept). Otherwise Published Language — Inventory publishes `inventory.stock-events`, Catalog subscribes.
- **Inventory ↔ Checkout saga** — Customer/Supplier with Inventory as upstream Supplier. Saga conforms to the Avro schemas on `inventory.reservations`.
- **Inventory ↔ Ordering** — Published Language on the cancel path: Inventory consumes Ordering's `OrderCancelledEvent` on `ordering.orders` directly (release-on-cancel, § 12.1). The reserve/confirm path flows through the saga (`inventory.reservation-commands`).
- **Inventory ↔ Payments** — No direct coupling.

---

## 13. Infrastructure Notes

### 13.1 Storage

- PostgreSQL schema `inventory` in the shared DB.
- Tables: `inventory.stock_events` (write model), `inventory.current_stock_levels` (projection), `inventory.reservation_audit` (projection), `inventory.command_inbox` (idempotency), plus the standard outbox/inbox tables.
- Migrations generated deterministically by the user (per CLAUDE.md — NEVER by agents).

### 13.2 Kafka topics

Inventory owns `inventory.stock-events` (consumed by Catalog) and `inventory.reservations` (consumed by the checkout saga). Per-topic partitions / retention / class are canonical in [kafka-topology.md](../kafka-topology.md); producers / consumers / keys in [events-catalog.md § 2](events-catalog.md). Design note: `inventory.reservations` is keyed by `OrderId` so every reservation event for one order co-partitions (preserving per-order ordering) and runs extra partitions for saga fan-out.

### 13.3 Outbox relay registration

Inventory does NOT host a relay in `Inventory.Api/Program.cs`; it registers only the outbox via `services.AddOutbox(...)` (writing to `inventory.outbox_messages`). A standalone `outbox-relay-inventory` container (`OutboxRelay__SchemaName=inventory`) drains that schema to Kafka — one relay container per service schema, per [events-catalog § 6](events-catalog.md). Nothing new here except the topic names.

### 13.4 Hosted services

- `ReservationExpiryWorker` — as described in § 11.1.
- (No other hosted services specific to Inventory in v1.)

### 13.5 Technology selection

**Explicitly deferred to solution-architect.** This document specifies persistence semantics (append-only stream in Postgres, projections in Postgres) but not framework choices (EF Core vs Dapper for the write path, serialization format for `Payload`, snapshotting library). The ES pattern does NOT mandate a particular .NET library — only the discipline of append-only writes and projection-rebuild capability.

---

## 14. Pattern Showcase: Event Sourcing

This is the ONLY ES bounded context in the eShop reference. It exists both to demonstrate the pattern AND to give readers honest guidance about when NOT to reach for it.

### 14.1 The write cycle

```
    Command (e.g., ReserveStockCommand)
           │
           ▼
    ┌─────────────────────────────────────────────────────────────┐
    │  1. Load: SELECT * FROM inventory.stock_events              │
    │     WHERE StreamId = @productId ORDER BY Version ASC;       │
    │  2. Fold events into a fresh StockItem instance.            │
    │  3. Call aggregate.Reserve(rid, qty, orderId, ttl);         │
    │       → aggregate validates invariants.                     │
    │       → aggregate produces new StockReservedDomainEvent.          │
    │  4. BEGIN TRANSACTION                                       │
    │     4a. INSERT into inventory.stock_events (V+1, payload)   │
    │         — if UNIQUE(StreamId,Version) fires → retry once.   │
    │     4b. UPSERT inventory.current_stock_levels.              │
    │     4c. UPSERT inventory.reservation_audit.                 │
    │     4d. INSERT external StockReservedEvent into outbox.     │
    │  5. COMMIT                                                  │
    └─────────────────────────────────────────────────────────────┘
           │
           ▼
    Outbox relay later publishes to Kafka `inventory.reservations`.
```

Everything inside the transaction either commits atomically or rolls back as a whole. The event store and the projections never diverge within a process.

### 14.2 The read cycle (rehydration)

```
    Command handler receives ReserveStockCommand(productId=P)
           │
           ▼
    ┌─────────────────────────────────────────────────────────────┐
    │  rows = SELECT * FROM inventory.stock_events                 │
    │           WHERE StreamId = P ORDER BY Version ASC;           │
    │                                                              │
    │  var item = new StockItem();  // empty                       │
    │  foreach (row in rows)                                       │
    │      item.Apply(Deserialize(row));  // reducer               │
    │                                                              │
    │  // item.OnHand, Reserved, Available, Version now reflect    │
    │  // the full history of P.                                   │
    └─────────────────────────────────────────────────────────────┘
```

`Apply(event)` is a pure function: `(state, event) → state`. It is the ONLY place state mutates. Command methods are the ONLY place new events are produced. The split keeps the aggregate deterministic and testable.

### 14.3 The rebuild cycle (when projections are corrupted)

```
    TRUNCATE inventory.current_stock_levels;
    (or DROP + recreate if schema changed)
           │
           ▼
    ┌─────────────────────────────────────────────────────────────┐
    │  for stream in distinct(StreamId):                          │
    │      for event in SELECT ... WHERE StreamId = stream        │
    │                     ORDER BY Version ASC:                   │
    │          projectionHandler.Handle(event);                   │
    └─────────────────────────────────────────────────────────────┘
           │
           ▼
    Resume live traffic (queued during rebuild).
```

Because every projection handler is the SAME code that runs at steady state, the rebuild uses no special logic — only replay. A schema bug in the projection is fixed once and applied retroactively via rebuild. This is the property that makes ES valuable for evolving read models.

### 14.4 When this pattern is appropriate

Per ADR-0006, ES is appropriate here because Inventory has:

- **Auditability as a first-class requirement.** "What happened and when?" is a routine business question, not a forensic one.
- **Temporal queries.** Projections can be built "as of time T" by folding events with `OccurredAtUtc <= T`.
- **Multiple consumers of the same stream.** Current levels, audit, low-stock alerts, and future projections all derive from one stream.
- **Compensation as a domain concern.** The saga needs deterministic reversal; an event-based history makes "undo" honest.
- **Append-only dominant workload.** Reads (rehydration) are bounded and cacheable; writes are naturally append-friendly.

### 14.5 When this pattern is NOT appropriate

Equally important to document (the reference-solution value is partly educational):

- **Low-change domains.** If an entity is written once and read thousands of times (a product description, a user profile), the overhead of a stream-per-entity buys nothing. Traditional OLTP + outbox is simpler.
- **Teams new to the pattern.** ES has a real learning curve — reducers, versioning, rebuilds, snapshots, eventual consistency. An inexperienced team attempting ES on its first microservice is a risk. Prefer CRUD + outbox until the domain demands more.
- **Hot aggregates with extreme write contention.** Per-stream optimistic concurrency doesn't scale horizontally; a flash-sale SKU can thrash retries. (See § 10.4.) Mitigation exists but adds complexity.
- **Domains where the event model would drift from the business model.** If you find yourself inventing "events" to match CRUD operations (`EntityUpdatedEvent` with diff payload), you do not have an event-sourced domain — you have CRUD with extra steps.
- **Schema evolution pain.** Events are immutable, so breaking changes require upcasting / versioning discipline. Under-invested teams break themselves.
- **Regulatory requirements for data deletion.** GDPR right-to-erasure on an append-only store is non-trivial. Strategies exist (crypto-shredding, tombstones) but complicate the model.

ADR-0006 captures this guidance formally. This section summarizes it so readers of the BC design can see the trade-off without leaving the document.

---

## 15. Testable specifications (Given/When/Then — seeds for Stage 2)

```
Scenario: Successful reservation reduces Available
  Given  StockItem(P) has stream: [Initialized, Received(100)]
    And  current state: OnHand=100, Reserved=0, Available=100, Version=2
  When   ReserveStockCommand(P, rid=R1, qty=10, orderId=O1) is handled
  Then   StockReservedDomainEvent(P, R1, 10, O1, exp=now+15m) is appended at Version=3
    And  current_stock_levels row for P shows OnHand=100, Reserved=10, Available=90
    And  reservation_audit has row (R1, Active, expires=now+15m)
    And  external StockReservedEvent is enqueued on inventory.reservations

Scenario: Reservation exceeding Available is rejected
  Given  StockItem(P) has stream: [Initialized, Received(5)]
    And  current state: OnHand=5, Reserved=0, Available=5
  When   ReserveStockCommand(P, rid=R1, qty=10, orderId=O1) is handled
  Then   Result.Fail(InsufficientStockError) is returned
    And  no event is appended (stream still at Version=2)
    And  external StockReservationFailedEvent is enqueued with RequestedQuantity=10, AvailableQuantity=5

Scenario: Confirmation commits physical stock
  Given  StockItem(P) has stream: [Initialized, Received(50), Reserved(R1, 10, O1)]
    And  reservation R1 is Active
  When   ConfirmReservationCommand(P, R1) is handled
  Then   ReservationConfirmedDomainEvent is appended
    And  current_stock_levels shows OnHand=40, Reserved=0, Available=40
    And  reservation_audit shows R1 status=Confirmed

Scenario: TTL expiry releases reservation
  Given  reservation R1 on StockItem(P) is Active with ExpiresAtUtc in the past
    And  ReservationExpiryWorker runs its tick
  When   the worker issues ReleaseReservationCommand(P, R1, reason=Expiry)
  Then   ReservationReleasedDomainEvent(reason=Expiry) is appended
    And  external ReservationReleasedEvent with ReleaseReason=Expiry is published
    And  reservation_audit shows R1 status=Released, ReleaseReason=Expiry

Scenario: Concurrent reservations resolve by optimistic conflict
  Given  StockItem(P) at Version=5 with Available=1
  When   two handlers concurrently read Version=5 and issue ReserveStockCommand(qty=1)
  Then   one INSERT at Version=6 succeeds
    And  the other hits UNIQUE(StreamId, Version) violation, retries once
    And  on retry, Available=0 → Result.Fail(InsufficientStockError)
    And  the losing command emits external StockReservationFailedEvent
```

---

## 16. Out of scope / deferred

Planned scope is catalogued in [roadmap.md § 2.3 Inventory](../roadmap.md):

- **Snapshots** (§ 8.2) — not in current scope.
- **Multi-warehouse / location-aware stock** — today one logical warehouse per `ProductId`. The planned extension introduces `LocationId` into the stream.
- **Low-stock threshold events** — the `StockLevelChangedEvent` schema is ready but only fires on the 0 ↔ positive crossover. Configurable per-SKU thresholds are planned scope.
- **Batch reservations** (one command reserves across multiple products) — today issues one command per product; the saga fans in. Transactional atomicity across streams is NOT guaranteed — by design, to showcase saga-style compensation.
- **GDPR / PII tombstoning on the event store** — Inventory events carry no PII beyond user GUIDs; crypto-shredding is the planned mechanism per [ADR-0011](../adr/0011-pii-handling-gdpr.md).

---

## 17. Error types

Inventory's error class set is the authoritative table in **[error-taxonomy.md § 1 + § 3.4](error-taxonomy.md)** (look for the `InventoryErrors` rows + the C# sketch in § 3). Single source of truth; do not duplicate here.

Key Inventory-specific semantics (the rest lives in error-taxonomy.md):

- **`InsufficientStock` is the load-bearing BUSINESS-EXPECTED error** — NOT retried; the saga translates it into compensation (release any other reservations, cancel order). See [checkout-saga.md § 6](checkout-saga.md) compensation matrix.
- **`ConcurrencyConflict` is retried ONCE** (ES stream `(StreamId, Version)` unique-constraint violation → rehydrate → re-attempt); a second conflict surfaces via `Result.Fail`.
