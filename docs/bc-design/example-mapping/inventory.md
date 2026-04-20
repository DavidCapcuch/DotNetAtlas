# Inventory — Example Mapping Sessions

> Format: Matt Wynne's Example Mapping (Story / Rules / Examples / Questions) with BDD Given/When/Verify/Then for Examples. Each session corresponds to one non-trivial business rule or invariant on the BC's aggregate. These sessions are the seed for executable acceptance-test specs (SpecFlow / Reqnroll) during implementation.
>
> Color legend (echoing the reference images):
> - 📖 Yellow = Story
> - 📐 Blue = Rule (business invariant)
> - 🌱 Green = Example ("The one where...")
> - ❓ Pink = Question (open issue)
> - 💬 White = Answer / Note (resolved question)

---

## Session 1: Reservation TTL auto-release

### 📖 Story
As a **platform operator** I want **unconfirmed reservations to auto-release after a bounded time window** so that **a buyer who abandons mid-checkout does not hold stock indefinitely and other shoppers can purchase the same units**.

### 📐 Rules
- **R1** — The default TTL is **15 minutes** from `ReservedAtUtc`; `ExpiresAtUtc` is written into `StockReservedEvent` at reservation time and is an immutable fact on the stream.
- **R2** — `ReservationExpiryWorker` polls every 60 seconds for rows where `Status = 'Active' AND ExpiresAtUtc < now()` in the `reservation_audit` projection and issues `ReleaseReservationCommand(reason=Expiry)` for each.
- **R3** — Expiry MUST produce a real `ReservationReleasedEvent` (with `ReleaseReason = Expiry`) on the stream and on `inventory.reservations` — there are NO silent state changes.
- **R4** — Once expired and released, a reservation is `Released` — subsequent `ConfirmReservationCommand` on that reservation returns `Result.Fail(InventoryErrors.ReservationNotActive)`.
- **R5** — Expiry is idempotent — a re-processed `ReleaseReservationCommand` for an already-`Released` reservation is treated as a no-op (at-least-once delivery safeguard, per § 10.3 of inventory.md).

### 🌱 Examples

#### The one where the saga confirms before expiry

- **Given** reservation `R1` on product `P1` for qty `2`, reserved at `T0` with `TTL = 15m` (so `ExpiresAtUtc = T0 + 15m`); saga issues `ConfirmReservationCommand(R1)` at `T0 + 5m`
- **When** the command is handled at `T0 + 5m`
- **Verify** R1
- **Then** `ReservationConfirmedEvent` is appended, `Status` becomes `Confirmed` in `reservation_audit`, and when the `ReservationExpiryWorker` ticks at `T0 + 15m` it does NOT select `R1` (status is no longer `Active`).

#### The one where the buyer abandons and TTL fires

- **Given** reservation `R2` on product `P2` for qty `1`, reserved at `T0` with `ExpiresAtUtc = T0 + 15m`; no saga activity on `R2` since creation
- **When** `ReservationExpiryWorker` ticks at `T0 + 16m`
- **Verify** R2, R3
- **Then** the worker issues `ReleaseReservationCommand(R2, reason=Expiry)`, `ReservationReleasedEvent(ReleaseReason=Expiry)` is appended at the next version, projections update `R2.Status = 'Released'`, and external `ReservationReleasedEvent(ReleaseReason=Expiry)` is published on `inventory.reservations`.

#### The one where confirm arrives after expiry

- **Given** reservation `R3` expired and was released with `ReleaseReason=Expiry` at `T0 + 16m`; the saga's `ConfirmReservationCommand(R3)` then arrives at `T0 + 17m` (e.g., delayed by retries)
- **When** the confirm command is handled
- **Verify** R4
- **Then** the aggregate sees `Reservations[R3].Status == Released`, and the command returns `Result.Fail(InventoryErrors.ReservationNotActive)` — no `ReservationConfirmedEvent` is appended, the saga treats the response as a failure and fails the order.

#### The one where a duplicate release attempt arrives

- **Given** reservation `R4` already released with `ReleaseReason=Expiry` at `T0 + 16m`; a duplicate `ReleaseReservationCommand(R4, reason=Expiry)` arrives at `T0 + 17m` (worker retry after crash, or saga retry)
- **When** the duplicate command is handled
- **Verify** R5
- **Then** the handler detects the command id in `command_inbox` (or observes `R4.Status == Released`) and treats it as a no-op — no new event is appended, no external event is published.

### ❓ Questions
*(None.)*

---

## Session 2: Cannot oversell (Available = OnHand − Reserved)

### 📖 Story
As a **shopper** I want **to be prevented from ordering more units than are actually available** so that **I never receive an order confirmation for stock the warehouse cannot ship and the business never oversells**.

### 📐 Rules
- **R1** — `Available` is a computed quantity, not a stored value — it is always `OnHand - Reserved` on the rehydrated aggregate.
- **R2** — `ReserveStockCommand` requires `Available >= Quantity`; otherwise it returns `Result.Fail(InventoryErrors.InsufficientStock)` and appends no ES event.
- **R3** — A rejected reservation emits an external `StockReservationFailedEvent(RequestedQuantity, AvailableQuantity)` on `inventory.reservations` so the saga can compensate (fail the order).
- **R4** — Stock can never go negative by construction — `StockAdjustedEvent` requires `(OnHand + delta) - Reserved >= 0` (§ 5.6); any attempt to drive stock below reservations throws `DataIntegrityException`.
- **R5** — Concurrent reserves on the same `ProductId` serialize via the UNIQUE `(StreamId, Version)` constraint on the event store; the losing writer retries exactly once, and if the re-check fails it returns `Result.Fail(InsufficientStockError)` (per ADR-0006 + § 10.2).
- **R6** — Until the projection catches up, rehydration from the event stream is authoritative — a reserve that arrives right after a receive sees the fresh `OnHand`.

### 🌱 Examples

#### The one where sufficient stock is available

- **Given** `StockItem(P1)` at `Version=3` rehydrated as `{OnHand=10, Reserved=3, Available=7}`
- **When** saga issues `ReserveStockCommand(P1, R1, qty=7, O1)`
- **Verify** R1, R2
- **Then** `StockReservedEvent(P1, R1, qty=7, O1, ExpiresAtUtc=now+15m)` is appended at `Version=4`; projections show `{OnHand=10, Reserved=10, Available=0}`; external `StockReservedEvent` is published.

#### The one where the request exceeds Available

- **Given** `StockItem(P1)` at `Version=3` with `{OnHand=10, Reserved=3, Available=7}`
- **When** saga issues `ReserveStockCommand(P1, R2, qty=8, O2)`
- **Verify** R2, R3
- **Then** the aggregate returns `Result.Fail(InventoryErrors.InsufficientStock)`, no ES event is appended (stream stays at `Version=3`), and external `StockReservationFailedEvent(RequestedQuantity=8, AvailableQuantity=7)` is published on `inventory.reservations`.

#### The one where two reserves race on the last units

- **Given** `StockItem(P1)` at `Version=5` with `{OnHand=7, Reserved=0, Available=7}`; two handlers `H_A` and `H_B` concurrently rehydrate at `Version=5` and each attempts `ReserveStockCommand(qty=5)`
- **When** both handlers attempt `INSERT` at `Version=6`
- **Verify** R5
- **Then** one INSERT succeeds (`Available=2` afterward); the other hits `UNIQUE(StreamId, Version)` violation, retries exactly once, re-reads at `Version=6` (`Available=2`), discovers `2 < 5`, and returns `Result.Fail(InsufficientStockError)` + emits external `StockReservationFailedEvent`.

#### The one where a fresh receipt unlocks a subsequent reserve

- **Given** `StockItem(P1)` at `Version=1` (initialized empty) with `{OnHand=0, Reserved=0}`; admin issues `ReceiveStockCommand(P1, qty=10, source="receiving-dock")`
- **When** saga issues `ReserveStockCommand(P1, R1, qty=5, O1)` immediately after the receive event is appended
- **Verify** R6
- **Then** the reserve handler rehydrates the stream (sees `StockReceivedEvent` at `Version=2`, `OnHand=10`), evaluates `Available=10 >= 5`, and appends `StockReservedEvent` at `Version=3` — no stale projection reads gate the decision.

### ❓ Questions
*(None.)*

---

## Session 3: Confirm decrements OnHand by Quantity (idempotent)

### 📖 Story
As a **warehouse operator** I want **confirming a reservation to physically decrement `OnHand` by the reserved quantity exactly once** so that **the system of record reflects the stock that actually left the warehouse and duplicate confirm signals do not double-count**.

### 📐 Rules
- **R1** — `ConfirmReservationCommand` transitions a reservation from `Active` to `Confirmed` and appends `ReservationConfirmedEvent` to the stream.
- **R2** — On confirm, `OnHand` decreases by the reservation's `Quantity` AND `Reserved` decreases by the same amount — so `Available` is mathematically unchanged (both operands drop equally).
- **R3** — Confirm is idempotent — replaying a confirm for an already-`Confirmed` reservation is a no-op (the command-inbox + the aggregate's state check collapse the duplicate to nothing).
- **R4** — Attempting to confirm a reservation whose status is `Released` or `Expired` returns `Result.Fail(InventoryErrors.ReservationNotActive)` — no event appended, no external event published.
- **R5** — A race between confirm and expiry (confirm command arriving at the same instant the worker fires for the same reservation) is resolved by the event store's UNIQUE `(StreamId, Version)` constraint — one wins at version `V+1`, the other sees the post-commit state and fails deterministically.

### 🌱 Examples

#### The one where confirm commits the reservation

- **Given** `StockItem(P1)` rehydrated as `{OnHand=10, Reserved=3, Available=7}` with reservation `R1` for qty `3`, `Status=Active`
- **When** saga issues `ConfirmReservationCommand(R1)` after payment success
- **Verify** R1, R2
- **Then** `ReservationConfirmedEvent(P1, R1)` is appended; projections show `{OnHand=7, Reserved=0, Available=7}`; `R1.Status=Confirmed` in `reservation_audit`; external `ReservationConfirmedEvent` is published on `inventory.reservations`.

#### The one where confirm is replayed

- **Given** reservation `R1` was already confirmed in the previous saga tick; `R1.Status == Confirmed` in the projection and on the rehydrated aggregate
- **When** a duplicate `ConfirmReservationCommand(R1)` arrives (saga retry or inbox replay)
- **Verify** R3
- **Then** the handler observes `Reservations[R1].Status != Active`, treats the command as a no-op, appends no event, publishes no external event — `OnHand`, `Reserved`, `Available` are unchanged from the first confirm.

#### The one where confirm arrives on a released reservation

- **Given** reservation `R2` was released (e.g., `ReleaseReason=Cancellation`) earlier in the saga flow; `R2.Status == Released`
- **When** a stray `ConfirmReservationCommand(R2)` arrives (out-of-order saga retry)
- **Verify** R4
- **Then** the command returns `Result.Fail(InventoryErrors.ReservationNotActive)`, no event is appended, and no external event is published — the saga interprets the failure as "reservation no longer valid" and does not advance.

#### The one where confirm and expiry race

- **Given** reservation `R3` at `StockItem(P3)`, `Version=10`, with `ExpiresAtUtc = T_now`; the `ReservationExpiryWorker` and the saga's `ConfirmReservationCommand(R3)` both dispatch within the same instant
- **When** both handlers rehydrate at `Version=10` and each appends their event
- **Verify** R5
- **Then** exactly one INSERT at `Version=11` succeeds (either `ReservationConfirmedEvent` or `ReservationReleasedEvent(Expiry)`); the losing handler hits `UNIQUE(StreamId, Version)` violation, retries once, observes the now-terminal status of `R3`, and returns `Result.Fail(InventoryErrors.ReservationNotActive)` — the stream contains exactly one resolution event for `R3`.

### ❓ Questions
*(None.)*
