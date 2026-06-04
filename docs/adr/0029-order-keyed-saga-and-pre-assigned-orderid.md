# ADR-0029: Order-Keyed Saga & Pre-Assigned OrderId

## Status

Accepted (2026-06-03) — supersedes the correlation-key and naming decisions in [ADR-0004](0004-checkout-saga-topology.md); the step-ordering (reserve-before-pay) and centralized-placement decisions of ADR-0004 are **unchanged**.

## Context

The checkout saga is the order-processing process: one `BasketCheckoutInitiatedEvent` drives `CreateOrder → ReserveStock → Pay → Confirm` across Ordering, Inventory, and Payments. The dominant MassTransit idiom keys an order-processing saga **on the order's own id**: `Event(() => OrderSubmitted, x => x.CorrelateById(m => m.Message.OrderId))`, with `SagaState.CorrelationId == OrderId` (e.g. the canonical [Milan Jovanović MassTransit saga walkthrough](https://www.milanjovanovic.tech/blog/implementing-the-saga-pattern-with-masstransit)).

That requires the `OrderId` to exist at saga start. A **pre-assigned** (client/edge-generated) GUIDv7 id makes this trivial — GUIDv7 ids are explicitly designed for this allocate-early, time-sortable, index-friendly use. With the `OrderId` allocated at checkout initiation and carried through every event, every saga correlation is a primary-key lookup on `OrderId`; the "how do Inventory result events correlate back to the saga?" question raised in `checkout-saga.md § 8.1` disappears, because Inventory's events already carry `OrderId`.

This is a non-production reference solution; breaking changes are free (root `CLAUDE.md`).

## Decision Drivers (ranked)

1. **Conceptual honesty** — the saga is the order-processing process manager; its identity should be the order's identity.
2. **Eliminate the correlation-mapping problem** — keying on `OrderId` deletes the `§ 8.1` Option A/B/C mapping entirely; events already carry `OrderId`.
3. **Idiom alignment + fewer identifiers** — match the MassTransit norm; one identifier per order flow.
4. **Name honesty** — the saga's scope is the *checkout/purchase* (basket → paid, confirmed order); its name should describe that process and not overclaim the full order lifecycle (ship/deliver live outside it).
5. **Centralized placement is non-negotiable** — preserve [ADR-0001](0001-centralized-saga-orchestration.md): the saga stays a centralized orchestrator and is **not** embedded into Ordering.

## Considered Options

### Option 1: Ordering mints the `OrderId` at order creation

Ordering generates the `OrderId` when it creates the Order aggregate and returns it on `OrderCreatedEvent`. The saga cannot be keyed on `OrderId` at start (the id does not exist yet), so it must carry a separate start-time key and map Inventory `OrderId` events back to it (the `§ 8.1` mapping). Rejected: carries the mapping complexity and the conceptual split for no benefit once the `OrderId` can be pre-assigned.

### Option 2: Order-keyed saga with a pre-assigned `OrderId` (chosen)

Allocate the `OrderId` (GUIDv7) at checkout initiation; carry it through `BasketCheckoutInitiatedEvent` as a pass-through payload (the same role Basket already plays for addresses per [ADR-0005](0005-customer-data-in-ordering.md)); the saga's `CorrelationId == OrderId`; every event correlates via `CorrelateById(m => m.OrderId)`. The saga **keeps its name** (`CheckoutSaga`) — see the Decision. Ordering accepts the client-assigned `OrderId` in `CreateOrderCommand` instead of minting its own.

### Option 3: Embed the saga in Ordering

Rejected for the same reasons as ADR-0004 Option 2: Ordering would consume Inventory + Payments events, inverting the dependency direction ADR-0001 forbids. "Order-keyed" is about the **identity**, not the **host**; the saga stays centralized.

## Evaluation Matrix

| Driver (ranked) | Opt 1: Ordering-minted OrderId | Opt 2: order-keyed (chosen) | Opt 3: embed in Ordering |
|---|---|---|---|
| 1. Conceptual honesty | Saga not identified by the order at start | Saga id **is** the order id | Honest, but wrong host |
| 2. Eliminate § 8.1 mapping | Mapping required | Mapping deleted | Deleted, but new event coupling |
| 3. Idiom + fewer ids | Two ids, non-idiomatic | One id, MassTransit-idiomatic | One id |
| 4. Name honesty | `CheckoutSaga` (accurate) | `CheckoutSaga` retained — rename to `OrderProcessingSaga` rejected as overclaiming | n/a |
| 5. Centralized placement | Preserved | Preserved | **Violated** (ADR-0001) |

## Decision

Adopt **Option 2**. Concretely:

1. **Pre-assign `OrderId`** (UUID v7) at checkout initiation. It is the Order's identity from birth; Basket carries it through `BasketCheckoutInitiatedEvent` as a pass-through field. Ordering persists the Order with the supplied id (client-assigned identity).
2. **Saga `CorrelationId == OrderId`.** All saga events correlate via `CorrelateById(m => m.OrderId)`; `CreateOrderCommand` carries the pre-assigned `OrderId`.
3. **Name retained — `CheckoutSaga`.** A rename to `OrderProcessingSaga` was **considered and rejected**: this saga's scope is the checkout/purchase (it ends at *paid + confirmed*; `Shipped` / `Delivered` are Order-aggregate transitions **outside** it), so `CheckoutSaga` is the more honest name and `OrderProcessingSaga` would overclaim the full order lifecycle. Re-keying on `OrderId` does **not** require a rename. The topic (`checkout.sagas`), consumer group (`saga-checkout`), folder, and class names are unchanged.
4. **`PaymentProcessingSaga` re-keys on `OrderId`.** One payment process per order; the saga's `CorrelationId == OrderId`; the one-payment-per-order uniqueness constraint is on `payment_transactions.order_id`. `PaymentTransactionId` remains the Payments aggregate's own primary key, distinct from the saga key.

## Rationale

The order is the spine of the whole flow; once the `OrderId` can exist at saga start (pre-assignment), keying the saga on it is both the honest model and the MassTransit idiom, and it deletes the `§ 8.1` correlation-mapping problem outright. Client-assigned GUIDv7 ids are a standard pattern precisely to enable this allocate-early correlation; they are time-sortable and index-friendly, so Ordering loses nothing by accepting the id rather than minting it. Centralized placement is retained unchanged — "order-keyed" constrains the saga's **identity**, not its **deployment** — so ADR-0001's hub-and-spoke autonomy guarantees still hold. Re-keying `PaymentProcessingSaga` on `OrderId` keeps the outer and inner sagas correlated on a single shared business key, consistent with the one-payment-per-order invariant.

The saga **keeps the name `CheckoutSaga`** because it models the checkout/purchase process specifically (basket → paid, confirmed order), not the full order lifecycle. Splitting the checkout flow into two sagas was also rejected: `CreateOrder → ReserveStock → Pay → Confirm` is one cohesive transaction under a single compensation envelope (a payment failure releases stock *and* cancels the order), so splitting it would force cross-saga compensation and a three-saga stack per purchase (Checkout + the split + the existing `PaymentProcessingSaga` sub-saga) — the cyclic-dependency / untraceable-flow cost that centralized orchestration exists to avoid. The genuinely separable second process is **post-purchase fulfillment** (`Confirmed → Shipped → Delivered`); if that ever becomes a cross-BC orchestration it would warrant a dedicated `OrderFulfillmentSaga` (also `OrderId`-keyed), but today those are Order-aggregate status transitions needing no saga. The one legitimate sub-saga — payment — already exists as `PaymentProcessingSaga`.

## Consequences

### Positive

- The `§ 8.1` Inventory-event correlation-mapping problem disappears; events correlate on `OrderId`, which they already carry.
- One identifier per order flow; the model matches the MassTransit idiom new engineers bring with them.

### Negative

- **Ordering accepts a client-assigned `OrderId`** rather than generating it — a real but standard change (GUIDv7 client-assigned identity).
- The saga is now conceptually unable to exist "before an order" — acceptable, because the `OrderId` is allocated at checkout initiation by definition.

(Churn is bounded to the re-key — Basket boundary + saga consumers/state + the `BasketCheckoutInitiatedEvent` schema + tests — because the saga is **not** renamed; the topic/group/folder/class churn is avoided.)

### Risks

- **Pre-assigned id collision** — negligible with UUID v7.
- **`PaymentProcessingSaga` future reuse** — keying it on `OrderId` couples it to orders; if a non-order payment workflow ever reuses it, it would need its own key. Documented as a known constraint; revisit if/when that workflow appears.

## Implementation Notes

- **Allocation point:** `OrderId` (UUID v7) generated in Basket's checkout command handler, carried on `BasketCheckoutInitiatedEvent`.
- **Ordering:** `CreateOrderCommand` carries `OrderId`; the Order aggregate factory accepts the supplied id; architecture/unit tests updated to assert client-assigned identity.
- **Saga:** `CheckoutSagaState.CorrelationId == OrderId`; every `Event(...).CorrelateById(m => m.OrderId)`; internal saga events carry `OrderId`; the consumer-adapters source the value from the inbound event's `OrderId`. No class/folder/topic/group rename; the saga state exposes only its MassTransit `CorrelationId` (no separate `OrderId` column).
- **PaymentProcessingSaga:** `CorrelateById(m => m.OrderId)`; migration moving the unique constraint to `order_id`; update `RequestPaymentCommand` correlation.
- **Tests:** the saga unit + integration suites (`CheckoutSaga*Tests`) re-key (no rename); `Basket`/`Ordering` boundary tests updated.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — placement unchanged; the saga stays centralized.
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — **superseded** on the correlation-key and naming choices; step-ordering and placement retained.
- [ADR-0005: Customer Data in Ordering](0005-customer-data-in-ordering.md) — Basket-as-pass-through precedent for carrying the pre-assigned `OrderId`.
- [ADR-0026: Checkout Payment Flow Capture Pivot](0026-checkout-payment-flow-capture-pivot.md) — unaffected; the capture-pivot flow re-keys on `OrderId` like every other transition.
