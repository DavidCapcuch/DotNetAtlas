# Ordering Bounded Context

> **Status:** DRAFT (Stage 1 Agent 3)
> **Target section in master design:** [eshop-master-design.md § 5.3](../eshop-master-design.md)
> **Companion ADRs:** [0001](../adr/0001-centralized-saga-orchestration.md), [0004](../adr/0004-checkout-saga-topology.md), [0005](../adr/0005-customer-data-in-ordering.md)
> **Glossary:** [glossary-ordering.md](./glossary-ordering.md)

---

## 1. Purpose & Strategic Classification

| Attribute | Value |
|-----------|-------|
| **Subdomain type** | **Core** — order lifecycle is the commercial heart of the eShop. |
| **Purpose** | Own the lifecycle of an **Order** from creation (at checkout) through fulfillment (delivery) or termination (cancellation / failure), acting as the system of record for what a customer agreed to buy, at what price, to what address, and at what point in the fulfillment journey the commitment currently sits. |
| **Storage** | PostgreSQL, schema `ordering` (shared Postgres instance, per-BC schema — see [ADR-0000 not yet authored; follows general-plan § Infrastructure]). |
| **Primary pattern showcase** | Rich **SmartEnum-guarded status FSM** with multi-event aggregate transitions, factory from `BasketSnapshot`. Write-side aggregate loading uses `Ardalis.Specification` (`OrderByIdSpec`, `OrderByCorrelationIdSpec`); read side uses inline LINQ with SQL-side projection per [ADR-0021](../adr/0021-read-side-no-specifications.md). |
| **Upstream inputs** | `BasketSnapshot` (ACL input from Basket BC at checkout); `StockReserved` / `PaymentCompleted` saga events (routed from the Checkout saga). |
| **Downstream outputs** | `ordering.orders` topic — enriched external events consumed by the Checkout saga, Notifications, BFF cache invalidation, Inventory (compensation on cancel), Payments (compensation on cancel). |

---

## 2. Ubiquitous Language — Summary

Full glossary in [glossary-ordering.md](./glossary-ordering.md). Key terms:

- **Order** — aggregate root; a commitment by a **Buyer** to purchase a set of items at a frozen price, shipped to a fixed address, tracked through a finite lifecycle.
- **Buyer** — the authenticated user who placed the order (`BuyerId` from JWT `sub` claim; see ADR-0005).
- **OrderItem** — a line inside an Order: one product, quantity, unit price, and a captured **ProductSnapshot**.
- **OrderStatus** — the current lifecycle position of an Order; a SmartEnum with guarded transitions.
- **CorrelationId** — the identifier that ties one Order to one run of the **Checkout saga**. Immutable after creation; equal to `SagaCorrelationId`.
- **StockReservation** — external Inventory concept referenced by `ReservationId` after `MarkStockReserved`. Not owned by Ordering.
- **PaymentTransactionId** — Payments-side identifier persisted on the Order after the saga reports a completed payment.
- **Compensation** — the process by which a cancelled Order triggers downstream reversals (release stock, refund payment) — *orchestrated by the saga, not by this BC*.
- **Fulfillment** — the post-confirmation shipping journey: `Confirmed → Shipped → Delivered`.

---

## 3. Aggregates

### 3.1 `Order` (aggregate root)

#### Purpose
A single commitment to purchase, tracked from creation through delivery or termination.

#### Properties

| Property | Type | Mutability | Notes |
|----------|------|-----------|-------|
| `Id` | `Guid` (inherited from `AggregateRoot<Guid>`) | set once in factory (`Guid.CreateVersion7()`) | Order id; used as the outbox key for `ordering.orders` messages. |
| `BuyerId` | `Guid` | **immutable after factory** | JWT `sub` claim, captured at creation. |
| `CorrelationId` | `Guid` | **immutable after factory** | Equals the Checkout saga correlation id. |
| `Items` | `IReadOnlyCollection<OrderItem>` (backed by `private readonly List<OrderItem>`) | **immutable after `MarkStockReserved` (see invariants)** | Line items with frozen product snapshots and prices. |
| `ShippingAddress` | `Address` (VO) | immutable after factory | |
| `BillingAddress` | `Address` (VO) | immutable after factory | |
| `PaymentMethodId` | `Guid` | immutable after factory | Payments-side reference; opaque to Ordering. |
| `PaymentTransactionId` | `Guid?` | set once by `MarkPaymentCompleted` | Reference only — Payments owns the transaction. |
| `StockReservationId` | `Guid?` | set once by `MarkStockReserved` | Reference only — Inventory owns the reservation. |
| `Status` | `OrderStatus` (SmartEnum) | changes via guarded transition methods only | See §5 for the FSM. |
| `Total` | `Money` (VO) | immutable after factory | Invariant: `Total = Σ Items.LineTotal`. All items share one currency. |
| `Cancellation` | `CancellationInfo?` (VO) | set once by `Cancel` | `Reason`, `AtStatus`, `CancelledAtUtc`. |
| `Failure` | `FailureInfo?` (VO) | set once by `Fail` | `ErrorCode`, `ErrorMessage`, `AtStatus`, `FailedAtUtc`. |
| `Shipment` | `ShipmentInfo?` (VO) | set once by `MarkShipped` | `Carrier`, `TrackingNumber`, `ShippedAtUtc`. |
| `CreatedAtUtc` | `DateTimeOffset` | immutable after factory | |
| `StockReservedAtUtc` | `DateTimeOffset?` | set once by `MarkStockReserved` | |
| `PaymentCompletedAtUtc` | `DateTimeOffset?` | set once by `MarkPaymentCompleted` | |
| `ConfirmedAtUtc` | `DateTimeOffset?` | set once by `Confirm` | |
| `DeliveredAtUtc` | `DateTimeOffset?` | set once by `MarkDelivered` | |
| `CreatedUtc` | `DateTimeOffset` | `IAuditableEntity` — set by EF Core interceptor | |
| `LastModifiedUtc` | `DateTimeOffset` | `IAuditableEntity` — updated by EF Core interceptor | |

#### Aggregate-scope invariants

The aggregate enforces the following invariants. Violations are classified as **user errors** (→ `Result.Fail`) vs. **bugs / data integrity** (→ `Throw.If` with `DataIntegrityException`):

| # | Invariant | Enforcement | Kind |
|---|-----------|-------------|------|
| I-1 | **Status transitions follow the FSM.** Any call that sets `Status` must pass `Status.CanTransitionTo(target)`. | `Throw.If(!Status.CanTransitionTo(target), DataIntegrityException)` inside transition methods. Saga-reachable transitions are callers' contract. | Bug — saga dispatching wrong event ordering is a system-wide bug, not a user mistake. |
| I-2 | **Items are immutable after `StockReserved`.** After stock is reserved at Inventory, the set of items, quantities, and prices are frozen. | Future methods that would mutate items must throw `DataIntegrityException` if `Status >= StockReserved` (no such methods exist in v1; this is a future-guard). | Bug. |
| I-3 | **Addresses are immutable after creation.** | No mutator methods exist. Properties have `private` setters. | N/A. |
| I-4 | **BuyerId is immutable.** | No mutator. Captured in factory. | N/A. |
| I-5 | **CorrelationId is immutable and matches the Checkout saga.** | No mutator. Captured in factory. Saga MUST use this id when correlating later events. | N/A. |
| I-6 | **Total equals sum of line totals.** `Total.Amount == Σ Items.LineTotal.Amount`, single currency across all items. | Factory computes `Total` from items; subsequent immutability ensures it stays correct. | Bug (input validation). |
| I-7 | **At least one item at creation.** | `Throw.If(basket.Items.Count == 0, DataIntegrityException)` in factory. | Bug — Basket should never emit an empty checkout. |
| I-8 | **All line items have positive unit price and positive quantity.** | `Throw.If(item.UnitPrice.Amount <= 0, DataIntegrityException)` and `quantity > 0`. | Bug — Catalog/Basket should never produce non-positive prices. |
| I-9 | **All items share one currency.** | `Throw.If(basket.Items.Any(i => i.UnitPrice.Currency != basket.Currency), DataIntegrityException)`. | Bug. |
| I-10 | **Valid country code on addresses.** ISO 3166-1 alpha-2; uppercase. | Validated inside `Address.Create`. | User-facing if address is directly input; the BFF is responsible for uppercasing/validating on its boundary. |
| I-11 | **Terminal status is terminal.** Once `Delivered`, `Cancelled`, or `Failed`, no further transitions are possible. | Enforced by `OrderStatus.CanTransitionTo` returning `false` from all terminal states. | Bug. |
| I-12 | **No cancellation after `Shipped`.** Once the parcel is in the carrier's hands, order commitment is fulfilled; returns are a separate (v2) concern. | `CanTransitionTo(Cancelled)` returns `false` from `Shipped` and `Delivered`. | User error — API returns `409 Conflict` via `Result.Fail`. |

#### Vernon's four rules — aggregate-boundary rationale

| Rule | How `Order` satisfies it |
|------|--------------------------|
| **1. Model true invariants in consistency boundaries.** | The single transactional invariant is **status transition correctness** — the FSM. `OrderItem`, `Address`, `Money`, snapshots do not have independent lifecycles and cannot be in inconsistent states with the rest of the aggregate. External concepts (`Reservation`, `PaymentTransaction`) live in other BCs and are referenced by id — they have independent lifecycles and are *not* in this boundary. |
| **2. Design small aggregates.** | `Order` contains one root + value objects + a list of line items. **No child entities** — `OrderItem` is a value object keyed by position (no identity needed; it is the immutable triple of product snapshot, quantity, and price). |
| **3. Reference other aggregates by identity only.** | `BuyerId`, `StockReservationId`, `PaymentTransactionId`, `PaymentMethodId`, and each `OrderItem.ProductId` are **ids only**. We do not hold references to Buyer, Reservation, Product, or PaymentMethod aggregates. |
| **4. Use eventual consistency outside the boundary.** | Stock reservation, payment completion, notification delivery, and cache invalidation all happen via the saga + external events + outbox. None of them are in the same transaction as Order persistence. |

---

## 4. Value Objects

All value objects live in `Ordering.Domain.ValueObjects`. They are immutable `sealed record`s inheriting `Platform.SharedKernel.Base.ValueObject`, follow the `Create(...) -> Result<T>` factory pattern, and have a private parameterless constructor for EF Core materialization.

### 4.1 `Money`
Shared-kernel value object (planned: `Platform.SharedKernel.ValueObjects.Money`; Wave 0 pins this — see rev-4 plan). Until pinned, implement locally under `Ordering.Domain.ValueObjects.Money` with the same contract:
- `Amount : decimal` — must be `> 0`.
- `Currency : CurrencyCode` — ISO 4217 enum.
- `Add`, `Subtract`, `+`, `-` operators — enforce same-currency invariant, throw `InvalidOperationException` otherwise.
- Factory: `static Result<Money> Create(decimal amount, CurrencyCode currency)` and the `(decimal, string)` overload.

### 4.2 `Address`
- `Street1 : string` — required, max 200 chars.
- `Street2 : string?` — optional, max 200 chars.
- `City : string` — required, max 100 chars.
- `State : string?` — optional (countries without states omit it), max 100 chars.
- `PostalCode : string` — required, max 20 chars.
- `CountryCode : string` — required, exactly 2 uppercase letters (ISO 3166-1 alpha-2).
- Factory: `static Result<Address> Create(string street1, string? street2, string city, string? state, string postalCode, string countryCode)` — validates non-empty, length, and country code format via regex `^[A-Z]{2}$`.
- No comparison against a whitelist in v1; validity is purely structural.

### 4.3 `OrderItem`
- `ProductId : Guid` — reference to Catalog product.
- `ProductSnapshot : ProductSnapshot` — frozen product info (see §4.4).
- `Quantity : int` — must be `> 0`.
- `UnitPrice : Money` — must be positive, currency-bound.
- `LineTotal : Money` — computed as `UnitPrice × Quantity`; stored as a `Money` (so EF Core can map it without `[NotMapped]` hacks and we avoid per-read recomputation for analytic queries).
- Factory: `static Result<OrderItem> Create(Guid productId, ProductSnapshot snapshot, int quantity, Money unitPrice)` — validates quantity, computes `LineTotal`.

### 4.4 `ProductSnapshot`
Frozen, order-time capture of a product. *Duplicated per BC* — no shared kernel for cross-service DTOs (per CLAUDE.md "no shared kernel across services for DTOs"). **Audit-fidelity rule (F6):** Ordering's `ProductSnapshot` is a structural superset of Basket's `ProductSnapshot` — every audit-relevant field captured in Basket must survive the ACL conversion.
- `Sku : string` — max 64 chars.
- `Name : string` — max 200 chars.
- `CapturedAtUtc : DateTimeOffset` — **required**; the timestamp at which Basket originally captured this snapshot from Catalog (not "when the order was created" — that's `Order.CreatedAtUtc`). Answers "when did the user see this price?" for chargebacks and price-change disputes. Sourced from `BasketCheckoutItem.CapturedAtUtc` (Avro). Currently absent from the v1 code; tracked as F6 in [docs/implementation-prompts/ordering.md `<dod>`](../implementation-prompts/ordering.md) with the full implementation chain. Architecture-test tripwire: [test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs](../../test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs).
- Factory: `static Result<ProductSnapshot> Create(string sku, string name, DateTimeOffset capturedAtUtc)`.
- **Design note:** this BC does not snapshot the Catalog's full description, images, or category — we only keep what appears on the order record itself. Basket's snapshot has a different shape (includes image url for basket display); each BC owns its own read-facing projection of the product concept. The structural-superset rule applies only to the audit-relevant subset (sku, name, price, captured-at), not to display-only Basket extras.

### 4.5 `CancellationInfo`
- `Reason : string` — required, max 500 chars.
- `AtStatus : OrderStatus` — the status the Order was in just before cancellation.
- `CancelledAtUtc : DateTimeOffset`.
- Factory: `static Result<CancellationInfo> Create(string reason, OrderStatus atStatus, DateTimeOffset cancelledAtUtc)` — validates non-empty reason.

### 4.6 `FailureInfo`
- `ErrorCode : string` — saga-assigned code (`PAYMENT_FAILED`, `PAYMENT_TIMEOUT`, `STOCK_UNAVAILABLE`, `STOCK_TIMEOUT`, etc.).
- `ErrorMessage : string`.
- `AtStatus : OrderStatus`.
- `FailedAtUtc : DateTimeOffset`.
- Factory: `static Result<FailureInfo> Create(string errorCode, string errorMessage, OrderStatus atStatus, DateTimeOffset failedAtUtc)`.

### 4.7 `ShipmentInfo`
- `Carrier : string` — max 100 chars (v1: free-form; v2 could introduce a SmartEnum).
- `TrackingNumber : string` — max 100 chars.
- `ShippedAtUtc : DateTimeOffset`.
- Factory: `static Result<ShipmentInfo> Create(string carrier, string trackingNumber, DateTimeOffset shippedAtUtc)` — validates non-empty.

### 4.8 `BasketSnapshot` (input DTO for the factory — **not persisted**)
Technically not a value object of this aggregate; it is an ACL-input contract consumed by the `CreateFromBasket` factory. Lives under `Ordering.Domain.Baskets` or `Ordering.Application.Common.Acl` (location decision belongs to the solution-architect).
- `BuyerId : Guid`.
- `Items : IReadOnlyCollection<BasketSnapshotItem>`.
- `Currency : CurrencyCode`.

`BasketSnapshotItem`:
- `ProductId : Guid`, `Sku : string`, `Name : string`, `Quantity : int`, `UnitPriceAmount : decimal`.

---

## 5. SmartEnums

### 5.1 `OrderStatus` (in `Ordering.Domain.Orders.OrderStatus`)

Per [ADR-0004](../adr/0004-checkout-saga-topology.md) Option A (reserve stock BEFORE payment), the values are:

| Name | Value | Terminal? |
|------|-------|-----------|
| `Created` | 0 | No |
| `StockReserved` | 1 | No |
| `PaymentCompleted` | 2 | No |
| `Confirmed` | 3 | No |
| `Shipped` | 4 | No |
| `Delivered` | 5 | **Yes** |
| `Cancelled` | 6 | **Yes** |
| `Failed` | 7 | **Yes** |

#### `CanTransitionTo(OrderStatus target)` — transition table

Rows = current `Status`; columns = target. A `✓` indicates the transition is allowed; blank = forbidden.

| From \\ To | Created | StockReserved | PaymentCompleted | Confirmed | Shipped | Delivered | Cancelled | Failed |
|---|---|---|---|---|---|---|---|---|
| **Created** | | ✓ | | | | | ✓ | ✓ |
| **StockReserved** | | | ✓ | | | | ✓ | ✓ |
| **PaymentCompleted** | | | | ✓ | | | ✓ | ✓ |
| **Confirmed** | | | | | ✓ | | ✓ | |
| **Shipped** | | | | | | ✓ | | |
| **Delivered** (terminal) | | | | | | | | |
| **Cancelled** (terminal) | | | | | | | | |
| **Failed** (terminal) | | | | | | | | |

#### Transition semantics

- **Created → StockReserved** — the saga posts `MarkStockReserved` after Inventory confirms reservation.
- **Created → Cancelled** — buyer aborts before stock reservation completes (rare; the saga window is short).
- **Created → Failed** — Inventory rejected the reservation (insufficient stock, product not found, timeout).
- **StockReserved → PaymentCompleted** — saga posts after Payments reports success.
- **StockReserved → Cancelled** — explicit buyer cancellation after stock was reserved but before payment (the saga must compensate by releasing the reservation).
- **StockReserved → Failed** — Payments payment failed / timed out (saga must compensate by releasing the reservation).
- **PaymentCompleted → Confirmed** — saga posts after both Inventory and Payments are green; conceptually `Confirm` is a fire-and-forget terminal-happy marker the saga uses before emitting its completion event.
- **PaymentCompleted → Cancelled** — buyer or admin cancels post-payment (saga must compensate: release stock AND refund).
- **PaymentCompleted → Failed** — downstream system failure after payment (extremely rare; same compensation as above).
- **Confirmed → Shipped** — post-saga fulfillment: a human/warehouse operation calls `MarkOrderShippedCommand` with carrier + tracking. (*v1 is simulated — a Dev endpoint or admin UI triggers this.*)
- **Confirmed → Cancelled** — admin cancels a confirmed-but-unshipped order (compensation: refund + release).
- **Shipped → Delivered** — carrier confirms delivery (*v1 simulated by `MarkOrderDeliveredCommand`*).
- **No transitions out of `Shipped` except to `Delivered`.** Returns/RMA are Appendix-C v2 scope.

The SmartEnum enforces transitions via a single `CanTransitionTo(target)` method using a switch on `this`, consulting a readonly dictionary `_allowed : IReadOnlyDictionary<OrderStatus, ImmutableHashSet<OrderStatus>>`. No reflection.

---

## 6. Internal Domain Events

All live under `Ordering.Domain.Orders.Events`. All inherit `Platform.SharedKernel.Base.DomainEvents.DomainEvent` (which provides `OccurredOnUtc`). None are Avro-serialized; they are in-process dispatches only.

| Event | Raised by | Payload |
|-------|-----------|---------|
| `OrderCreatedDomainEvent` | `CreateFromBasket` factory | `OrderId`, `BuyerId`, `CorrelationId`, `Items` (item-level snapshot inline: `ProductId`, `Sku`, `Name`, `Quantity`, `UnitPriceAmount`, `LineTotalAmount`, `Currency`), `ShippingAddress`, `BillingAddress`, `Total`, `CreatedAtUtc` |
| `OrderStockReservedDomainEvent` | `MarkStockReserved` | `OrderId`, `CorrelationId`, `ReservationId`, `OccurredOnUtc` |
| `OrderPaymentCompletedDomainEvent` | `MarkPaymentCompleted` | `OrderId`, `CorrelationId`, `PaymentTransactionId`, `OccurredOnUtc` |
| `OrderConfirmedDomainEvent` | `Confirm` | `OrderId`, `CorrelationId`, `BuyerId`, `OccurredOnUtc` |
| `OrderShippedDomainEvent` | `MarkShipped` | `OrderId`, `CorrelationId`, `BuyerId`, `Carrier`, `TrackingNumber`, `ShippedAtUtc`, `OccurredOnUtc` |
| `OrderDeliveredDomainEvent` | `MarkDelivered` | `OrderId`, `BuyerId`, `DeliveredAtUtc`, `OccurredOnUtc` |
| `OrderCancelledDomainEvent` | `Cancel` | `OrderId`, `CorrelationId`, `BuyerId`, `Reason`, `AtStatus` (previous), `CancelledAtUtc`, `OccurredOnUtc` |
| `OrderFailedDomainEvent` | `Fail` | `OrderId`, `CorrelationId`, `BuyerId`, `ErrorCode`, `ErrorMessage`, `AtStatus`, `FailedAtUtc`, `OccurredOnUtc` |

**Consumers of internal events (handlers in `Ordering.Application.Orders.*.*DomainEventHandler`):**

- `OrderCreatedDomainEvent` → `OrderCreatedOutboxPublisherDomainEventHandler` (publishes `OrderCreatedEvent` to `ordering.orders`).
- `OrderStockReservedDomainEvent` → **audit-only** (optional structured log; no external event — saga already knows it reserved stock because it issued the command).
- `OrderPaymentCompletedDomainEvent` → **audit-only** (same reasoning — saga already observed the Payments success event).
- `OrderConfirmedDomainEvent` → `OrderConfirmedOutboxPublisherDomainEventHandler`.
- `OrderShippedDomainEvent` → `OrderShippedOutboxPublisherDomainEventHandler`.
- `OrderDeliveredDomainEvent` → `OrderDeliveredOutboxPublisherDomainEventHandler`.
- `OrderCancelledDomainEvent` → `OrderCancelledOutboxPublisherDomainEventHandler`.
- `OrderFailedDomainEvent` → `OrderFailedOutboxPublisherDomainEventHandler`.

**Why `OrderStockReservedDomainEvent` / `OrderPaymentCompletedDomainEvent` do NOT produce external events:** the checkout saga is the sole interested party for those transitions, and it already observed the upstream `StockReservedEvent` / `PaymentCompletedEvent` from Inventory / Payments directly. Republishing them from Ordering would be redundant noise on the bus. Internal events exist only so that intra-service read projections or audit logs can react in the same transaction.

---

## 7. External (Integration) Events — `ordering.orders` topic

All external events produced by this BC are Avro-serialized and published via `ITransactionalOutbox<IOrderingDbContext>` to a **single topic, `ordering.orders`**, partitioned by `OrderId` (ensures per-order ordering preservation across all lifecycle events). Avro namespace: **`Ordering.Orders`** (follows the convention from master-design § 3.2: `{Domain}.{Aggregate}`). Schemas materialize in `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/*.avsc`.

Notation reminder from master-design § 3.2: external event C# name is `{BusinessMoment}Event` — no `Domain` suffix, no `Integration` suffix.

### 7.1 Master event inventory

| External event | When produced | Consumed by |
|----------------|--------------|-------------|
| `OrderCreatedEvent` | `CreateFromBasket` → `OrderCreatedDomainEvent` handler | **Checkout saga** (starts saga instance; correlates by `CorrelationId`). |
| `OrderConfirmedEvent` | `Confirm` → handler | Notifications (confirmation email); BFF (cache invalidation: `order-history:{buyerId}`); Catalog (aggregate sales analytics — v2). |
| `OrderCancelledEvent` | `Cancel` → handler | Notifications (cancellation email); Inventory (release reservation if still held); Payments (refund if payment completed); BFF (cache invalidation). |
| `OrderShippedEvent` | `MarkShipped` → handler | Notifications (shipment + tracking email). |
| `OrderDeliveredEvent` | `MarkDelivered` → handler | Notifications (delivery confirmation email). |
| `OrderFailedEvent` | `Fail` → handler | Notifications (failure email); BFF (cache invalidation). |

### 7.2 Avro schemas

All `OrderId` / `CorrelationId` / `BuyerId` fields are `string + logicalType: uuid`. All `*AtUtc` timestamp fields are `long + logicalType: timestamp-millis`. All monetary `Amount` fields use `bytes + logicalType: decimal, precision: 19, scale: 4`. Currency is ISO 4217 three-letter code.

#### 7.2.1 `OrderCreatedEvent.avsc`

```json
{
  "type": "record",
  "name": "OrderCreatedEvent",
  "namespace": "Ordering.Orders",
  "doc": "Emitted when a new Order is created from a Basket checkout. Starts the Checkout saga instance.",
  "fields": [
    { "name": "OrderId", "type": { "type": "string", "logicalType": "uuid" }, "doc": "Unique identifier of the Order." },
    { "name": "CorrelationId", "type": { "type": "string", "logicalType": "uuid" }, "doc": "Checkout saga correlation id." },
    { "name": "BuyerId", "type": { "type": "string", "logicalType": "uuid" }, "doc": "User who placed the order (JWT sub)." },
    {
      "name": "Items",
      "type": {
        "type": "array",
        "items": {
          "type": "record",
          "name": "OrderItemCreated",
          "fields": [
            { "name": "ProductId", "type": { "type": "string", "logicalType": "uuid" } },
            { "name": "Sku", "type": "string" },
            { "name": "Name", "type": "string" },
            { "name": "Quantity", "type": "int" },
            { "name": "UnitPriceAmount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 } },
            { "name": "LineTotalAmount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 } }
          ]
        }
      },
      "doc": "Order line items with frozen product snapshots and prices."
    },
    { "name": "TotalAmount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 }, "doc": "Total order amount." },
    { "name": "Currency", "type": "string", "doc": "ISO 4217 currency code shared by all items." },
    { "name": "PaymentMethodId", "type": { "type": "string", "logicalType": "uuid" }, "doc": "Payments-side payment method reference." },
    { "name": "CreatedAtUtc", "type": { "type": "long", "logicalType": "timestamp-millis" }, "doc": "UTC timestamp when the order was created." }
  ]
}
```

#### 7.2.2 `OrderConfirmedEvent.avsc`

```json
{
  "type": "record",
  "name": "OrderConfirmedEvent",
  "namespace": "Ordering.Orders",
  "doc": "Emitted when the Order has been successfully confirmed (stock reserved AND payment completed).",
  "fields": [
    { "name": "OrderId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "CorrelationId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "BuyerId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "ConfirmedAtUtc", "type": { "type": "long", "logicalType": "timestamp-millis" } }
  ]
}
```

#### 7.2.3 `OrderCancelledEvent.avsc`

```json
{
  "type": "record",
  "name": "OrderCancelledEvent",
  "namespace": "Ordering.Orders",
  "doc": "Emitted when the Order is cancelled. Downstream consumers trigger compensation (release stock, refund, notify).",
  "fields": [
    { "name": "OrderId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "CorrelationId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "BuyerId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "Reason", "type": "string", "doc": "Human- or system-assigned cancellation reason." },
    {
      "name": "AtStatus",
      "type": {
        "type": "enum",
        "name": "OrderStatusAtTransition",
        "symbols": [
          "Created",
          "StockReserved",
          "PaymentCompleted",
          "Confirmed"
        ]
      },
      "doc": "OrderStatus just before cancellation. Informs consumers what compensation to perform."
    },
    { "name": "CancelledAtUtc", "type": { "type": "long", "logicalType": "timestamp-millis" } }
  ]
}
```

> **Note on the `AtStatus` enum:** only non-terminal non-shipped statuses are valid sources of cancellation (I-12). The enum is declared inline here and **reused** in `OrderFailedEvent` by reference (`"type": "Ordering.Orders.OrderStatusAtTransition"`). `OrderFailedEvent` cannot reach `Confirmed` (confirmation implies success), but schema-sharing is cleaner than duplicating the enum. Stage 2 Agent 5 (event catalog) may split these enums if evolution diverges.

#### 7.2.4 `OrderShippedEvent.avsc`

```json
{
  "type": "record",
  "name": "OrderShippedEvent",
  "namespace": "Ordering.Orders",
  "doc": "Emitted when the Order is handed to a carrier with a tracking number.",
  "fields": [
    { "name": "OrderId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "CorrelationId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "BuyerId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "Carrier", "type": "string", "doc": "Shipping carrier name (e.g., 'FedEx', 'DHL', 'UPS')." },
    { "name": "TrackingNumber", "type": "string", "doc": "Carrier-assigned tracking number." },
    { "name": "ShippedAtUtc", "type": { "type": "long", "logicalType": "timestamp-millis" } }
  ]
}
```

#### 7.2.5 `OrderDeliveredEvent.avsc`

```json
{
  "type": "record",
  "name": "OrderDeliveredEvent",
  "namespace": "Ordering.Orders",
  "doc": "Emitted when the carrier confirms delivery. Terminal happy-path event for the order lifecycle.",
  "fields": [
    { "name": "OrderId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "BuyerId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "DeliveredAtUtc", "type": { "type": "long", "logicalType": "timestamp-millis" } }
  ]
}
```

> **Note:** `CorrelationId` is intentionally **omitted** from `OrderDeliveredEvent` — the checkout saga is already finalized by the time delivery occurs; delivery is a post-saga fulfillment milestone. Notifications can look up the order by `OrderId` if it needs saga-era context.

#### 7.2.6 `OrderFailedEvent.avsc`

```json
{
  "type": "record",
  "name": "OrderFailedEvent",
  "namespace": "Ordering.Orders",
  "doc": "Emitted when the Order transitions to a terminal Failed state. Downstream consumers notify the buyer and reverse any applied compensations.",
  "fields": [
    { "name": "OrderId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "CorrelationId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "BuyerId", "type": { "type": "string", "logicalType": "uuid" } },
    { "name": "ErrorCode", "type": "string", "doc": "Machine-readable error code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED, PAYMENT_TIMEOUT, CONFIRMATION_TIMEOUT)." },
    { "name": "ErrorMessage", "type": "string", "doc": "Human-readable error message." },
    { "name": "AtStatus", "type": "Ordering.Orders.OrderStatusAtTransition", "doc": "OrderStatus just before failure." },
    { "name": "FailedAtUtc", "type": { "type": "long", "logicalType": "timestamp-millis" } }
  ]
}
```

---

## 8. Pattern Showcase

This BC is the reference implementation of the following patterns:

1. **SmartEnum-guarded status FSM** — `OrderStatus` with `CanTransitionTo` is the most elaborate example in the solution (8 states, 13 transitions). Its pattern — SmartEnum + lookup dictionary + `CanTransitionTo` — is the template other BCs should mirror when they need lifecycle enforcement.
2. **Factory from external ACL input** — `Order.CreateFromBasket(BasketSnapshot, Address, Address, ...)` is the canonical example of "the aggregate's factory takes a **frozen snapshot** of data that belongs to another BC, deep-copies it, and raises a domain event holding the snapshot." This pattern is key for Basket → Ordering (snapshot of product prices) and for Ordering → the saga (snapshot of items in `OrderCreatedEvent`).
3. **Multi-event aggregate transitions** — cancellation and failure are single method calls that produce a single domain event, but the cumulative flow across `Create → MarkStockReserved → MarkPaymentCompleted → Confirm → MarkShipped → MarkDelivered` demonstrates an aggregate whose identity persists across many state-changing events, each with its own invariant guard. This is richer than `AlertSubscriber`'s two-terminal-state lifecycle.
4. **Result pattern for user errors + Throw.If for bugs** — transition methods return `Result` for user-observable errors (e.g., `Cancel` on an already-shipped order is a legit user mistake → `Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name))`), but use `Throw.If(!CanTransitionTo(...))` for cases where the calling saga has a bug.
5. **Outbox-published enriched external events** — six external events in `ordering.orders`, each enriched at domain-event-handler time with just enough data for downstream consumers to act without re-querying Ordering. Matches the discipline in master-design § 3.

---

## 9. Commands, Queries, and Use Cases (outline)

Full command/query catalog with validators, request/response contracts, and endpoints is owned by **Stage 2 Agent 7 (Use Case Catalog)** and will materialize in master-design § 7.3. This BC contributes the following anchor list to inform that work:

### 9.1 Commands

| Command | Trigger | Handler responsibility |
|---------|---------|----------------------|
| `CreateOrderCommand` | HTTP from BFF after basket checkout | Build `BasketSnapshot` and `Address` VOs from the request, call `Order.CreateFromBasket`, persist, `SaveChangesAsync`. The `OrderCreatedDomainEvent` handler enqueues `OrderCreatedEvent` on `ordering.orders`. |
| `MarkOrderStockReservedCommand` | Saga-internal app-command dispatch (in-process from Wave-2 saga; **no** Kafka inbox consumer per [events-catalog.md § 5.5](events-catalog.md) — the four Kafka commands are `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand`) | Load by `OrderId`, call `order.MarkStockReserved(reservationId, utcNow)`. |
| `MarkOrderPaymentCompletedCommand` | Saga-internal app-command dispatch (same shape as above — application-layer command only, not on `ordering.order-commands`) | Load order, call `order.MarkPaymentCompleted(paymentTransactionId, utcNow)`. |
| `ConfirmOrderCommand` | Kafka (saga) → inbox consumer | Load order, call `order.Confirm(utcNow)`. |
| `MarkOrderShippedCommand` | HTTP (Admin) or Dev endpoint | Load order, call `order.MarkShipped(carrier, trackingNumber, utcNow)`. |
| `MarkOrderDeliveredCommand` | HTTP (Admin) or Dev endpoint, or future carrier-webhook adapter | Load order, call `order.MarkDelivered(utcNow)`. |
| `CancelOrderCommand` | HTTP (Buyer or Admin) | Load order, call `order.Cancel(reason, utcNow)` — compensation is emergent from the published `OrderCancelledEvent`. |
| `MarkOrderFailedCommand` | Kafka (saga) → inbox consumer on `ordering.order-commands` | Load order, call `order.Fail(errorCode, errorMessage, utcNow)`. |

### 9.2 Queries

Both read paths are inline LINQ on `IOrderingDbContext.Orders` with SQL-side projection — no `Ardalis.Specification` per [ADR-0021](../adr/0021-read-side-no-specifications.md).

| Query | Read shape | Returns |
|-------|------------|---------|
| `GetOrderByIdQuery(OrderId, BuyerId, IsAdmin)` | [`GetOrderByIdQueryHandler`](../../services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryHandler.cs): `.Where(o => o.Id == query.OrderId).Select(...).FirstOrDefaultAsync()`. Ownership enforced post-SELECT in-memory — `!query.IsAdmin && response.BuyerId != query.BuyerId` returns `OrderNotFound` (same failure as missing row, no existence leak; cross-buyer attempts logged at Warning). | Full order projection (items, addresses, status, timestamps). |
| `GetOrdersByBuyerQuery(BuyerId, OrderStatus?, Skip, Take)` | [`GetOrdersByBuyerQueryHandler`](../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs): `.Where(o => o.BuyerId == buyerId).Where(o => status == null || o.Status == status).OrderByDescending(o => o.CreatedAtUtc).ThenByDescending(o => o.Id).Skip(skip).Take(take).Select(...)`. | Paged summary projection. |

Each handler tags its EF query with `.TagWith(nameof(<HandlerName>))` — emitted as a SQL comment for distributed-tracing visibility.

### 9.3 Public factory & transition methods (aggregate API)

```csharp
public static Order CreateFromBasket(
    Guid correlationId,
    Guid buyerId,
    BasketSnapshot basket,
    Address shippingAddress,
    Address billingAddress,
    Guid paymentMethodId,
    DateTimeOffset utcNow);

public Result MarkStockReserved(Guid reservationId, DateTimeOffset utcNow);
public Result MarkPaymentCompleted(Guid paymentTransactionId, DateTimeOffset utcNow);
public Result Confirm(DateTimeOffset utcNow);
public Result MarkShipped(string carrier, string trackingNumber, DateTimeOffset utcNow);
public Result MarkDelivered(DateTimeOffset utcNow);
public Result Cancel(string reason, DateTimeOffset utcNow);
public Result Fail(string errorCode, string errorMessage, DateTimeOffset utcNow);
```

**Preconditions per method (checked in order):**

- `CreateFromBasket` — `Throw.If` basket empty (I-7), any non-positive price (I-8), mixed currencies (I-9), non-ISO country code on either address (via `Address.Create` → cascaded `Result.Fail`).
- `MarkStockReserved` — `Throw.If(!Status.CanTransitionTo(StockReserved))` (I-1); `Throw.If(reservationId == Guid.Empty)`.
- `MarkPaymentCompleted` — `Throw.If(!Status.CanTransitionTo(PaymentCompleted))`; `Throw.If(paymentTransactionId == Guid.Empty)`.
- `Confirm` — `Throw.If(!Status.CanTransitionTo(Confirmed))`.
- `MarkShipped` — `Throw.If(!Status.CanTransitionTo(Shipped))`; validate carrier+tracking non-empty through `ShipmentInfo.Create`; any VO-validation failure **also** throws `DataIntegrityException` (an admin/Dev UI should pre-validate).
- `MarkDelivered` — `Throw.If(!Status.CanTransitionTo(Delivered))`.
- `Cancel` — `if (!Status.CanTransitionTo(Cancelled)) return Result.Fail(OrderingErrors.CannotCancelInStatus(Status));` — user-observable.
- `Fail` — `Throw.If(!Status.CanTransitionTo(Failed))`.

---

## 10. Integration Points

### 10.1 Context map — relationships

| Counterpart BC | Pattern | Direction | Description |
|---------------|---------|-----------|-------------|
| **Basket** | Anti-Corruption Layer (ACL) on the Ordering side | Basket → Ordering | `BasketSnapshot` is an input DTO re-modeled by Ordering into its own `OrderItem` + `ProductSnapshot`. Basket never speaks Ordering's language. |
| **Inventory** | Customer-Supplier (via the Checkout saga) | Inventory → Ordering | Inventory publishes `StockReservedEvent` / `StockReservationFailedEvent`; the saga correlates and issues `MarkOrderStockReservedCommand` (saga-internal app dispatch) on success or `MarkOrderFailedCommand` (Kafka on `ordering.order-commands`) on failure. Ordering does **not** consume Inventory events directly. |
| **Payments** | Customer-Supplier (via the Checkout saga) | Payments → Ordering | Payments publishes `PaymentCompletedEvent` / `PaymentFailedEvent`; the saga issues `MarkOrderPaymentCompletedCommand` (saga-internal app dispatch) on success or `MarkOrderFailedCommand` (Kafka on `ordering.order-commands`) on failure. |
| **Checkout saga** | Orchestration | Ordering ↔ saga | Saga is *the* consumer of Ordering's external events and *the* driver of status transitions post-creation. Centralized placement per ADR-0001 / ADR-0004. |
| **Notifications** | Published-Language (consumer) | Ordering → Notifications | Notifications subscribes to `ordering.orders` topic, filters by event name, renders buyer-facing emails. |
| **BFF** | Open Host Service (Ordering exposes HTTP query API) + Published-Language (BFF consumes `ordering.orders` for cache invalidation) | BFF ↔ Ordering | BFF calls `GetOrderByIdQuery` / `GetOrdersByBuyerQuery` over HTTP; BFF also listens to `ordering.orders` to invalidate `order-history:{buyerId}` cache entries. |
| **Catalog** | None (no direct relationship) | — | Ordering references products only through the snapshot captured by Basket; no direct call from Ordering to Catalog. |

### 10.2 How the saga issues commands to Ordering

The saga **does not** mutate Ordering's database directly. Two options exist for saga → Ordering communication; both are viable and the final choice belongs to Stage 2 Agent 6 (saga designer):

- **Option X: HTTP commands** — saga POSTs `MarkOrderStockReservedCommand` etc. to an internal Ordering HTTP endpoint. Simpler, request-reply, authenticated with service-to-service JWT.
- **Option Y: Kafka command topic** — saga produces to `ordering.order-commands`, Ordering runs a MassTransit/Kafka consumer over that topic via the inbox. More uniform with the rest of the saga's event-driven surface; resilient to transient Ordering unavailability.

This BC design **supports both**: commands are plain CQRS commands; whether they enter via HTTP or a Kafka consumer adapter is transport-only. Recommendation: **Option Y** for consistency with the saga's orchestration style. If chosen, the topic `ordering.order-commands` must be added to the docker-compose Kafka config (Stage 2 Agent 5).

The command payload in either case uses **`OrderId`** (not `CorrelationId`) as the primary key so consumers can efficiently partition/dedupe. `CorrelationId` is carried alongside for saga alignment but is not the aggregate's identity.

### 10.3 Subscribes to

- **From the saga over `ordering.order-commands` (Kafka)**: `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand` — the four Avro-locked commands per [events-catalog.md § 5.5](events-catalog.md). Consumed via [`Platform.KafkaFlow.Inbox.EFCore`](../../platform) middleware for idempotent dedup.
- **From the saga over in-process app-command dispatch (no Kafka)**: `MarkOrderStockReservedCommand`, `MarkOrderPaymentCompletedCommand` — application-layer commands the Wave-2 saga calls directly; their state transitions are saga-private and produce only audit-only internal domain events (no external events).
- **From the frontend (via BFF → HTTP)**: `CreateOrderCommand`, `CancelOrderCommand`.
- **From admin tooling / Dev**: `MarkOrderShippedCommand`, `MarkOrderDeliveredCommand`.

### 10.4 Publishes to

- **`ordering.orders` (topic)** — all six external events (§7.1).

This BC does **not** produce to any other topic in v1.

---

## 11. Infrastructure Notes

| Concern | Decision / Reference |
|---------|---------------------|
| **Database** | PostgreSQL, shared instance, schema `ordering`. Tables: `orders`, `order_items`. No own-row-locking beyond EF Core's default optimistic concurrency via `IAuditableEntity.LastModifiedUtc` (or a dedicated `RowVersion : uint` — decision to solution-architect). |
| **Outbox** | `Platform.ReliableMessaging.Outbox.EFCore` + `IOrderingDbContext`. Outbox table lives in the same `ordering` schema so that `SaveChangesAsync` is a single transaction that mutates `orders` + outbox row atomically. |
| **Kafka topics** | Produce: `ordering.orders`. Consume: (optional) `ordering.order-commands` from saga (§10.2). |
| **Schema Registry** | Avro schemas under `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/*.avsc`, registered to Confluent Schema Registry; serialization via `Platform.Avro.UniversalSerDes` (mandatory — see general-plan § Messaging constraint). |
| **Authentication** | All HTTP endpoints except the webhook-delivery variant (future) require a valid JWT. `BuyerId` is taken from the `sub` claim; admin endpoints additionally require an `admin` role claim (specifics: solution-architect). |
| **Idempotency** | `CreateOrderCommand` is idempotent on `(CorrelationId)` — if a second create arrives with the same `CorrelationId`, the handler should no-op and return the existing `OrderId` (implementation detail for solution-architect). Saga retries are the driver. |
| **Observability** | `OrderingActivitySource` (KEEP existing name) for tracing; structured logging via `ILogger<T>`; traces tagged with `Order.Id` and `CorrelationId` per existing `TraceTags` pattern. |
| **Migrations** | Per CLAUDE.md, "never touch or generate EF Core migrations — always let the user deterministically generate." This BC design only specifies the domain shape; the user generates migrations from the resulting model. |

---

## Appendix B — Resolved Questions (M9 ratification)

These were intentionally left open during Stage-1 design; Wave-1 milestones M1–M8 shipped sensible defaults per the dispatch prompt's `<autonomous_evolution>` block, and M9 ratifies each here with rationale + back-reference to the shipped code. Numbering preserved so existing external links (`ordering.md#appendix-b`) stay valid.

1. **Saga → Ordering transport** (§10.2) — **RESOLVED: Kafka topic `ordering.order-commands`** (Option Y). Locked by [events-catalog.md § 5.5](events-catalog.md) + [ADR-0004](../adr/0004-checkout-saga-topology.md). HTTP would couple the saga to per-service availability; a Kafka-backed inbox absorbs retries cleanly and matches the saga's event-driven orchestration style. *Citation:* four KafkaFlow inbox consumers under [`services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/`](../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/) — `CreateOrderCommandKafkaHandler`, `ConfirmOrderCommandKafkaHandler`, `CancelOrderCommandKafkaHandler`, `MarkOrderFailedCommandKafkaHandler`; topic + retention pinned in [`docker-compose.yaml`](../../docker-compose.yaml) (`retention.ms=604800000` = 7 days).
2. **Weather-remnant fate** — **RESOLVED (pre-dispatch):** the `services/Order/` project, `AlertSubscription*Saga` sagas, and `order.alert-subscriptions` Kafka topic were fully removed before Wave 1 started. Ordering is greenfield under `services/Ordering/`. No action required.
3. **Row-version concurrency token on `Order`** — **RESOLVED: explicit `RowVersion : uint`** mapped to Postgres's `xmin` system column via Npgsql's `IsRowVersion()` convention (not a stored shadow property). Rationale: explicit row-version makes optimistic-concurrency violations distinguishable from app-level update conflicts in observability, matches the Weather reference mapping, and avoids the implicit-`LastModifiedUtc` interceptor-coupling smell (the audit column would do double duty as a concurrency token, conflating two concerns). *Citation:* [`services/Ordering/Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs:37-40`](../../services/Ordering/Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs).
4. **Order history pagination strategy** — **RESOLVED: offset/limit (`Skip`/`Take`)** in v1; keyset is deferred to v2 once order-history volumes per buyer make consistency-under-insert a real concern. Rationale: admin order-history pages and buyer self-service in v1 hit tens-of-rows-per-buyer at most, well below the threshold where offset's `O(skip)` becomes a problem; offset/limit is a simpler API surface and avoids exposing a cursor format that would later need a v2-bump migration. *Citation:* [`GetOrdersByBuyerQuery.cs:15-17`](../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQuery.cs) (`Skip` + `Take` with default `Take = 20`); applied as inline `.Skip(query.Skip).Take(query.Take)` in `GetOrdersByBuyerQueryHandler` per [ADR-0021](../adr/0021-read-side-no-specifications.md).
5. **Cancellation authorization rules** — **RESOLVED: buyer may cancel up to `Confirmed`; admin may cancel up to `Confirmed`; no one (buyer or admin) after `Shipped`** (invariant I-12). Rationale: this aligns the user-facing policy with the FSM-locked terminal-status rule — once goods are in a carrier's hands, the saga can no longer close the compensation loop unilaterally (Returns/RMA is the v2 mechanism). Admin gets the same window as buyer because admin doesn't override fulfilment-side state; admin's role is operator-override of buyer intent, not parcel reclamation. *Citation:* [`Order.Cancel(...)`](../../services/Ordering/Ordering.Domain/Orders/Order.cs) returns `Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name))` from `Shipped`/`Delivered` (mapped to 409 Conflict per [error-taxonomy.md:32, 294](error-taxonomy.md); pre-M9 drafts of this section used a `CannotCancelAfterShipped` variant that never materialised in the locked taxonomy); [`CancelOrderEndpoint.cs:33-89`](../../services/Ordering/Ordering.API/Endpoints/Orders/CancelOrder/CancelOrderEndpoint.cs) dual-modes buyer-vs-admin and surfaces 409 Conflict on FSM rejection; functional tests `WhenBuyerCancelsOwnCreatedOrder_ReturnsNoContent`, `WhenAnotherBuyerTriesToCancel_ReturnsNotFound`, `WhenOrderShipped_ReturnsConflict`, and `WhenSameIdempotencyKeyUsedByDifferentBuyer_HandlerStillRuns` (all in `test/Ordering.FunctionalTests/ApiEndpoints/Orders/CancelOrderTests.cs`) pin the rule.
6. **Delivery confirmation in v1** — **RESOLVED: admin-only `MarkOrderDeliveredCommand`** (no auto-timer, no carrier-webhook adapter in v1). Rationale: explicit admin trigger keeps the reference solution behavior-predictable; an auto-timer adds scheduler + retry + idempotency machinery without teaching value for v1, and a real carrier-webhook adapter requires an external-carrier sandbox account we deliberately don't depend on. v2 may replace the surface with a webhook adapter that calls the same domain method. *Citation:* [`MarkOrderDeliveredEndpoint.cs:36`](../../services/Ordering/Ordering.API/Endpoints/Orders/MarkOrderDelivered/MarkOrderDeliveredEndpoint.cs) (`AuthPolicies.OrderingAdmin`) + functional tests `WhenAuthenticatedAsBuyer_ReturnsForbidden` (buyers blocked) and `WhenOrderShipped_ReturnsNoContentAndStatusDelivered` (admin happy path) in `test/Ordering.FunctionalTests/ApiEndpoints/Orders/MarkOrderDeliveredTests.cs` pin the admin-only surface.

---

## Appendix C — Error types

Ordering's error class set is the authoritative table in **[error-taxonomy.md § 1 + § 3.3](error-taxonomy.md)** (look for the `OrderingErrors` rows + the C# sketch in § 3). Single source of truth; do not duplicate here.

Key Ordering-specific semantics (the rest lives in error-taxonomy.md):

- `CannotCancelInStatus(status)` is the only user-visible error produced during the checkout saga flow (mapped to 409 Conflict per [error-taxonomy.md:32, 294](error-taxonomy.md)); all other Ordering errors are bug-class because saga-issued commands should have already satisfied preconditions.
- Ordering's inbox-consumed saga commands route failures to `ordering.order-commands.DLT` per [kafka-dlq-strategy.md](kafka-dlq-strategy.md).
