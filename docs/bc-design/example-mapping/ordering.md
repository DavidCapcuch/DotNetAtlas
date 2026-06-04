# Ordering — Example Mapping Sessions

> Format: Matt Wynne's Example Mapping (Story / Rules / Examples / Questions) with BDD Given/When/Verify/Then for Examples. Each session corresponds to one non-trivial business rule or invariant on the BC's aggregate. These sessions are the seed for executable acceptance-test specs (SpecFlow / Reqnroll) during implementation.
>
> Color legend (echoing the reference images):
> - 📖 Yellow = Story
> - 📐 Blue = Rule (business invariant)
> - 🌱 Green = Example ("The one where...")
> - ❓ Pink = Question (open issue)
> - 💬 White = Answer / Note (resolved question)

---

## Session 1: Status FSM — cannot skip states

### 📖 Story
As a **platform engineer** I want **the Order lifecycle to advance only along the defined state machine** so that **accidental out-of-order saga commands, replayed events, or buggy callers cannot corrupt the canonical record of what happened to an order**.

### 📐 Rules
- **R1** — Every transition is gated by `OrderStatus.CanTransitionTo(target)`; the happy path is `Created → StockReserved → PaymentCompleted → Confirmed → Shipped → Delivered` (per ADR-0004, stock-before-payment).
- **R2** — Any transition where `CanTransitionTo` returns `false` throws `DataIntegrityException` — these are bug-class, not user-actionable, because the Checkout saga is the only caller and saga ordering is a system invariant.
- **R3** — `Cancel` is a legal off-ramp from any of `{Created, StockReserved, PaymentCompleted, Confirmed}` (see Session 2 for the Shipped boundary).
- **R4** — `Failed` is a terminal failure state reachable from `{Created, StockReserved, PaymentCompleted}`; it is NOT reachable from `Confirmed` (by then both stock and payment are green and no saga-driven failure path remains).
- **R5** — Terminal states (`Delivered`, `Cancelled`, `Failed`) have no outbound transitions; any attempt throws `DataIntegrityException`.
- **R6** — Backwards transitions (e.g., `Shipped → Confirmed`) are always forbidden — the FSM is directional.

### 🌱 Examples

#### The one where the saga tries to skip stock reservation

- **Given** an order `O1` with `Status = Created`
- **When** `order.MarkPaymentCompleted(paymentTxId=T1, utcNow)` is called directly
- **Verify** R1, R2
- **Then** `CanTransitionTo(PaymentCompleted)` from `Created` returns `false`, and the method throws `DataIntegrityException` — no status change, no domain event raised.

#### The one where the happy path runs end-to-end

- **Given** an order `O1` created from a valid `BasketSnapshot` with `Status = Created`
- **When** the saga posts `MarkStockReserved(R1)`, then `MarkPaymentCompleted(T1)`, then `Confirm()` in order
- **Verify** R1
- **Then** each call succeeds, status advances `Created → StockReserved → PaymentCompleted → Confirmed`, and each call raises its corresponding `*DomainEvent` (`OrderStockReservedDomainEvent`, `OrderPaymentCompletedDomainEvent`, `OrderConfirmedDomainEvent`).

#### The one where Confirmed advances to Shipped (fulfillment path)

- **Given** an order `O1` with `Status = Confirmed`
- **When** admin calls `order.MarkShipped(carrier="DHL", trackingNumber="TRK-42", utcNow)`
- **Verify** R1
- **Then** `CanTransitionTo(Shipped)` from `Confirmed` returns `true`, `Status` becomes `Shipped`, `ShipmentInfo` is set, and `OrderShippedDomainEvent` is raised.

#### The one where someone tries to walk backwards

- **Given** an order `O1` with `Status = Shipped`
- **When** `order.Confirm(utcNow)` is called (e.g., a stale saga retry)
- **Verify** R2, R6
- **Then** `CanTransitionTo(Confirmed)` from `Shipped` returns `false`, and the method throws `DataIntegrityException` — `Status` remains `Shipped`.

### ❓ Questions
*(None.)*

---

## Session 2: Cannot cancel after Shipped

### 📖 Story
As a **buyer or administrator** I want **the ability to cancel an order up to the point it ships, but not after** so that **the fulfillment pipeline can treat shipped parcels as committed and the planned Returns/RMA flow (see [roadmap.md § 2.2](../../roadmap.md)) handles post-ship issues separately**.

### 📐 Rules
- **R1** — `Order.Cancel(reason, utcNow)` is allowed only when `Status ∈ {Created, StockReserved, PaymentCompleted, Confirmed}`.
- **R2** — Calling `Cancel` from `Shipped` or `Delivered` returns `Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name))` — this is a user-actionable error (409 Conflict at the HTTP surface), not a bug.
- **R3** — The business justification: once goods are in a carrier's hands the checkout saga cannot unilaterally recall them; compensation through the saga (refund + release reservation) is no longer a closed loop.
- **R4** — Post-ship issues (damaged parcel, wrong item, refused delivery) must go through the Returns/RMA flow — planned scope per [roadmap.md § 2.2](../../roadmap.md).
- **R5** — A successful `Cancel` raises `OrderCancelledDomainEvent(OrderId, BuyerId, Reason, AtStatus, CancelledAtUtc)` which is transformed into the external `OrderCancelledEvent` on `ordering.orders`; downstream consumers (Inventory, Payments) perform the compensation dictated by `AtStatus`.

### 🌱 Examples

#### The one where the order is cancelled before stock reservation

- **Given** an order `O1` with `Status = Created`
- **When** `order.Cancel(reason="buyer abandoned", utcNow)` is called
- **Verify** R1, R5
- **Then** `Status` becomes `Cancelled`, `OrderCancelledDomainEvent(AtStatus=Created)` is raised, and the resulting external `OrderCancelledEvent` is enqueued on the outbox (Inventory has nothing to release, Payments has nothing to refund).

#### The one where an admin cancels a confirmed order

- **Given** an order `O1` with `Status = Confirmed` (stock reserved AND payment completed)
- **When** admin calls `order.Cancel(reason="operator override", utcNow)`
- **Verify** R1, R5
- **Then** `Status` becomes `Cancelled`, `OrderCancelledDomainEvent(AtStatus=Confirmed)` is raised, and the downstream saga triggers both reservation release (Inventory) and refund (Payments).

#### The one where cancellation is attempted after shipping

- **Given** an order `O1` with `Status = Shipped` (parcel handed to `DHL`, tracking `TRK-42`)
- **When** `order.Cancel(reason="buyer changed mind", utcNow)` is called
- **Verify** R2, R3, R4
- **Then** the method returns `Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name))`, `Status` remains `Shipped`, and no domain event is raised — the caller must direct the buyer to the planned Returns flow (see [roadmap.md § 2.2](../../roadmap.md)).

#### The one where cancellation is attempted after delivery

- **Given** an order `O1` with `Status = Delivered`
- **When** `order.Cancel(reason="buyer dispute", utcNow)` is called
- **Verify** R2, R4
- **Then** the method returns `Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name))` (same error — both post-ship states are rejected), `Status` remains `Delivered`.

### ❓ Questions
*(None — cancellation policy is explicit: buyers and admins may cancel up to `Confirmed`; everything beyond goes through Returns/RMA per [roadmap.md § 2.2](../../roadmap.md).)*

---

## Session 3: Modify items locked after StockReserved

### 📖 Story
As a **platform engineer** I want **the line items, quantities, and unit prices on an Order to be immutable once Inventory has reserved the stock** so that **the physical reservation and the commercial record can never disagree about what was reserved**.

### 📐 Rules
- **R1** — `Order.Items` is a read-only projection of a private list; once `Status` transitions to `StockReserved` (or beyond), no operation may add, remove, or modify any `OrderItem`.
- **R2** — Any future command that would mutate items while `Status >= StockReserved` must throw `DataIntegrityException` — this is a bug-class guard (I-2 in ordering.md § 3.1), not a user error.
- **R3** — The business rationale: Inventory's `StockReservedEvent` commits a precise `(ProductId, Quantity)` hold against a `ReservationId`; mutating the Order's items desynchronizes the Inventory reservation from the commercial intent and breaks compensation.
- **R4** — Today there are NO item-mutation commands on the Order aggregate (the Order is built from a `BasketSnapshot` in the factory and then frozen); R1/R2 are a **future-guard** so that any later-added command automatically inherits the invariant.
- **R5** — The addresses, buyer, the Order's `Id` (the pre-assigned `OrderId` / saga key), and total are likewise immutable after creation (I-3, I-4, I-5, I-6) for the same synchronization reason.

### 🌱 Examples

#### The one where a future AddItem command fires on a Created order

- **Given** an order `O1` with `Status = Created` and a hypothetical `AddOrderItemCommand(productId=P2, quantity=1)` handler exists
- **When** the handler invokes a hypothetical `order.AddItem(...)`
- **Verify** R1 (hypothetically permitted by the invariant — no such command exists today)
- **Then** the item is appended; R1 does not forbid it at `Created` since stock is not yet reserved (today's surface does not expose this command, so this is a design-consistency check only).

#### The one where a mutation is attempted after StockReserved

- **Given** an order `O1` with `Status = StockReserved` and reservation `R1` held in Inventory for `(P1, qty=3)`
- **When** a future `order.RemoveItem(P1)` (hypothetical) is called
- **Verify** R1, R2, R3
- **Then** the method throws `DataIntegrityException` — the Order's items must not diverge from the Inventory reservation; the Order remains unchanged at `(P1, qty=3)`.

#### The one where a quantity change is attempted after Confirmed

- **Given** an order `O1` with `Status = Confirmed`, payment captured, reservation confirmed
- **When** a future `order.ChangeItemQuantity(P1, newQuantity=2)` (hypothetical) is called
- **Verify** R1, R2, R3
- **Then** the method throws `DataIntegrityException` — a confirmed order is a signed commercial commitment; modifying items would require coordinated reversals in Inventory and Payments, which is the Returns/RMA concern (planned scope per [roadmap.md § 2.2](../../roadmap.md)).

### ❓ Questions
*(None — current scope deliberately omits any item-mutation command so the invariant is trivially satisfied; the examples document the guard that any future command must respect.)*
