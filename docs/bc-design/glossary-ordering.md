# Ubiquitous Language — Ordering Bounded Context

> **Parent BC design:** [ordering.md](./ordering.md)
> **Scope:** terms and concepts owned by or consumed at the boundary of the Ordering BC. Definitions here supersede any colloquial interpretation elsewhere in the solution.
> **Consolidated into:** [eshop-ubiquitous-language.md](../eshop-ubiquitous-language.md) during Stage 5 synthesis.

Entries are alphabetized within each group. When a term has a different meaning in another BC, the divergence is called out explicitly — that divergence is a boundary marker.

---

## A. Core domain terms

### Address
An immutable value object with `Street1`, optional `Street2`, `City`, optional `State`, `PostalCode`, and ISO 3166-1 alpha-2 `CountryCode`. An Order has one **ShippingAddress** and one **BillingAddress**, frozen at creation. Addresses in Ordering are structural — validity is format-only, not a real postal-service lookup. See [ordering.md § 4.2](./ordering.md).

### BillingAddress
The postal address the buyer designates for payment / invoice purposes. Captured at order creation from the BFF-collected form. Immutable on the Order once created (I-3). Distinct from ShippingAddress.

### Buyer
The authenticated end-user who placed the order. Ordering knows the Buyer by their `BuyerId` only (JWT `sub` claim; see ADR-0005). Ordering does **not** own a Customer / Account aggregate — customer profile data does not live in v1 (see Appendix C of master-design). Divergence: in a mature eShop "Buyer", "Customer", and "User" would be distinguished; in v1 they collapse to a single id.

### BuyerId
`Guid` identifier of the Buyer, taken verbatim from the JWT `sub` claim by the API layer and persisted on every Order. Immutable (I-4). The Order aggregate does **not** resolve this to a name or email — BFF / Notifications enrich from JWT claims or external identity.

### Cancellation
The act of terminating an Order's lifecycle before fulfillment. Represented as the transition `Status → Cancelled` via the aggregate's `Cancel(reason, utcNow)` method, producing `OrderCancelledDomainEvent` → `OrderCancelledEvent`. Cancellation is legal from `Created`, `StockReserved`, `PaymentCompleted`, or `Confirmed`; illegal from `Shipped`, `Delivered`, or any terminal state (I-11, I-12). Cancellation itself is a terminal status — it is *not* reversible.

### CancellationInfo
A value object capturing the Reason, the `AtStatus` (status just before cancellation), and the `CancelledAtUtc` timestamp. Attached to `Order.Cancellation` after a successful `Cancel` call. See [ordering.md § 4.5](./ordering.md).

### Carrier
The shipping company that physically delivers an order (e.g., "FedEx", "DHL", "UPS"). Stored today as a free-form string on `ShipmentInfo.Carrier`; planned migration to a SmartEnum is in [roadmap.md § 2.3 Ordering](../roadmap.md). Not owned by Ordering — Ordering only records the chosen carrier's name and the Carrier-assigned tracking number.

### Compensation
The reversal of previously-applied effects when a downstream step of the Checkout saga fails. Ordering does **not** orchestrate compensation itself; compensation is emergent from the published `OrderCancelledEvent` / `OrderFailedEvent`: Inventory consumes the event and releases the reservation; Payments consumes the event and issues a refund if payment had already completed. Divergence: "compensation" in the saga BC means the state-machine's compensation branch; in Ordering it means the downstream effect of a cancelled/failed order.

### Confirmed (status)
The `OrderStatus` that marks the successful end of the Checkout saga — stock has been reserved AND payment has completed. `Confirm()` is the transition method. The Order is now ready for fulfillment (shipping). `OrderConfirmedEvent` is emitted here; Notifications sends a confirmation email; BFF invalidates the buyer's order-history cache.

### OrderId (the saga correlation key)
The Order's `Id` is pre-assigned at checkout ([ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)) and **is** the Checkout saga's correlation key (the saga's MassTransit `CorrelationId == OrderId`). It is passed into `Order.CreateFromBasket`, immutable on the Order (I-5), and carried on every external event Ordering publishes — the saga routes events to its state machine by `OrderId`.

### Delivered (status)
The terminal happy-path `OrderStatus`. Set by `MarkDelivered(utcNow)`. In v1 this transition is triggered by admin / Dev tooling (there is no carrier-webhook adapter yet). `OrderDeliveredEvent` is emitted — it carries `OrderId` only (delivery is a post-saga milestone).

### Failed (status)
A terminal `OrderStatus` indicating the order could not be fulfilled due to a system or business failure (stock unavailable, payment declined, payment timeout, activation failure after payment). The transition method is `Fail(errorCode, errorMessage, utcNow)`, typically called by the Checkout saga in response to an upstream failure. `OrderFailedEvent` is emitted, carrying `ErrorCode` + `ErrorMessage` + `AtStatus` so downstream can understand which compensation window applies.

### FailureInfo
A value object attached to `Order.Failure` after a successful `Fail` call. Carries `ErrorCode`, `ErrorMessage`, `AtStatus`, and `FailedAtUtc`. See [ordering.md § 4.6](./ordering.md).

### Fulfillment
The post-confirmation lifecycle segment: `Confirmed → Shipped → Delivered`. Distinct from the Checkout saga (which covers `Created → … → Confirmed`). Fulfillment is driven from outside the saga — in v1 via admin tooling; in a mature system via warehouse systems and carrier webhooks.

---

## O. Order-centric terms

### Order
The aggregate root of this BC. A single commitment by a Buyer to purchase a set of items at a frozen price, shipped to a fixed address, tracked through the FSM described in [§ 5.1](./ordering.md). See [ordering.md § 3.1](./ordering.md). Divergence: in Basket BC the analogous concept is **Basket** (mutable, ephemeral, pre-commitment); in Catalog BC there is no concept of "order", only of "product".

### OrderId
`Guid` primary identifier of an Order, generated in the `CreateFromBasket` factory via `Guid.CreateVersion7()`. Used as the Kafka partitioning key on `ordering.orders` so that all lifecycle events for a single order land in the same partition and are consumed in order. Immutable.

### OrderItem
A value object inside the `Order` aggregate representing a single product line: `ProductId`, `ProductSnapshot`, `Quantity`, `UnitPrice`, and computed `LineTotal`. OrderItems are **value objects, not entities** (Vernon rule 2) — they have no identity beyond their position in the Items collection and cannot be individually addressed for updates after creation. Divergence: Basket has a **BasketItem** that *is* mutable (quantity can change); Ordering's `OrderItem` is frozen.

### OrderStatus
The SmartEnum that encodes the Order's lifecycle position. Eight values: `Created`, `StockReserved`, `PaymentCompleted`, `Confirmed`, `Shipped`, `Delivered`, `Cancelled`, `Failed`. `CanTransitionTo(target)` is the single guard consulted by every transition method. See [ordering.md § 5.1](./ordering.md) for the full transition table.

---

## P. External references (owned by other BCs, named here)

### PaymentMethodId
`Guid` reference to a payment method in the Payments BC (e.g., a saved credit card, PayPal, SEPA direct debit). Ordering stores the id only; resolution to card details belongs to Payments. Captured at order creation and immutable.

### PaymentTransactionId
`Guid` reference to a transaction in the Payments BC, set on the Order by `MarkPaymentCompleted(paymentTransactionId, utcNow)` after the Checkout saga observes Payments's `PaymentCompletedEvent`. Ordering treats this as an opaque pointer — the Payments BC owns the transaction's state.

### ProductId
`Guid` reference to a product in the Catalog BC. Ordering stores this on every `OrderItem` so that downstream consumers (e.g., Inventory's stock reservation, sales analytics) can look up product-level data. Ordering never joins to a `Product` entity itself.

### ProductSnapshot
A value object inside `OrderItem` holding an order-time frozen copy of product fields needed by Ordering: `Sku`, `Name`. **Duplicated per BC** — Basket's own `ProductSnapshot` additionally carries `Price` and `CapturedAtUtc` for its frozen-pricing contract; Ordering needs neither — `OrderItem` holds `UnitPrice` itself, and items freeze at order creation, so `Order.CreatedAtUtc` is the capture instant. That BC-specific shape is what fails [ADR-0036](../adr/0036-shared-kernel-value-objects.md)'s shared-kernel promotion criterion.

---

## S. Lifecycle / saga terms

### ShippingAddress
The postal address to which the Order will be delivered. Distinct from BillingAddress. Captured at order creation; immutable thereafter (I-3). If the buyer needs a different address, they cancel and re-order (v1).

### ShipmentInfo
A value object attached to `Order.Shipment` after `MarkShipped`. Carries `Carrier`, `TrackingNumber`, and `ShippedAtUtc`. See [ordering.md § 4.7](./ordering.md).

### Shipped (status)
The `OrderStatus` that follows `Confirmed` when the parcel is handed to a carrier with a tracking number. Transition method: `MarkShipped(carrier, trackingNumber, utcNow)`. Once Shipped, cancellation is no longer possible (I-12). `OrderShippedEvent` is emitted; Notifications sends the tracking-number email.

### StockReservation
An external (Inventory BC) concept: a soft hold on stock for a specific Order for a bounded time. Ordering never models a StockReservation entity — it holds only a `StockReservationId` reference after the Checkout saga confirms the reservation via `MarkStockReserved(reservationId, utcNow)`. Divergence: Inventory owns the full StockReservation lifecycle (Created, Confirmed, Released, Expired); Ordering sees only "we have a reservation id" or "we don't".

### StockReservationId
`Guid` reference to an Inventory-side reservation. Set once by `MarkStockReserved`; Ordering exposes it so that consumers of `OrderCancelledEvent` / `OrderFailedEvent` can release it.

### StockReserved (status)
The `OrderStatus` reached when Inventory has confirmed a soft hold on stock for the Order. The next valid saga transition is `PaymentCompleted` (per ADR-0004: stock BEFORE payment). Cancellation from StockReserved triggers Inventory compensation (release reservation). Transition method: `MarkStockReserved(reservationId, utcNow)`.

---

## T. Technical / derived terms

### Total
A value object of type `Money` on the `Order` aggregate, representing the full price owed by the Buyer. Invariant: `Total.Amount == Σ Items.LineTotal.Amount` and all items share `Total.Currency` (I-6, I-9). Immutable after creation. Does not include shipping costs or taxes — v1 ignores both (see master-design Appendix C for deferred scope).

### Currency
An ISO 4217 three-letter code. Modeled as the `CurrencyCode` enum on `Money`. One Order uses exactly one currency across all line items (I-9) — mixed-currency orders are out of scope in v1.

### LineTotal
`Money` value, computed as `OrderItem.UnitPrice × OrderItem.Quantity` and stored (rather than recomputed) so EF Core projections and analytic queries do not re-multiply. Immutable.

### UnitPrice
`Money` value on `OrderItem`. The per-unit price frozen at order-creation time. Must be strictly positive (I-8). Unrelated to the Catalog's *current* price — Ordering deliberately does not reprice on re-read.

### CanTransitionTo
The guard method on `OrderStatus` that returns `true` iff moving from the current status to the argument status is legal in the FSM. Used by every transition method on `Order` — either by throwing `DataIntegrityException` when `!Status.CanTransitionTo(...)` (for saga-driven transitions that can only fail due to bugs) or via `Result.Fail` (for user-triggered transitions like `Cancel`).

---

## Cross-BC disambiguation

| Term | In Ordering | In other BC |
|------|-------------|-------------|
| **Item** | `OrderItem` — immutable value object, quantity + frozen price + product snapshot | Basket: `BasketItem` — mutable, quantity editable until checkout |
| **Order** | The generic commercial order aggregate (§3.1) | No other BC uses "Order" — Basket uses `Basket`; Invoicing uses `Invoice` |
| **Snapshot** | `ProductSnapshot` — sku + name only | Basket's snapshot also carries `Price` and `CapturedAtUtc`; Inventory has its own event-sourced "state at point in time" concept which is also called a snapshot |
| **Status** | `OrderStatus` (8-value FSM, §5.1) | Basket has no status (it's ephemeral); Inventory tracks reservation *state* as a sequence of events |
| **Buyer** | The authenticated user who placed the Order; a `Guid` only | Catalog / Basket may use "User" or "Customer" colloquially — in v1 they all collapse to the JWT `sub` claim |
| **Reservation** | `StockReservationId` — a reference only | Inventory: owns the full StockReservation lifecycle as an event-sourced concept |
| **Compensation** | The emergent downstream reversal triggered by `OrderCancelledEvent` / `OrderFailedEvent` | Checkout saga: the saga state-machine's compensation branch |

---

*End of Ordering glossary — 33 terms defined.*
