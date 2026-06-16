# ADR-0004: Checkout Saga Topology

## Status

Accepted (2026-04-18) — revised 2026-05-31 to reflect the post-Weather-cleanup eShop topology (the original `AlertSubscriptionPurchaseSaga` / `AlertSubscriptionExtensionSaga` references were dropped in [ADR-0001](0001-centralized-saga-orchestration.md)'s 2026-05-30 revision; only `PaymentProcessingSaga` remained as the sibling state machine). The substantive decisions (Option A reserve-first, Option 1 centralized placement) are unchanged.

## Context

The eShop reference solution introduces a multi-step checkout flow that spans five bounded contexts: **Basket → Ordering → Inventory → Payments → Notifications**. A `BasketCheckoutInitiatedEvent` produced by Basket must be converted into either a **Confirmed Order** (happy path) or a **Compensated Order** (any failure path), with stock and money movement coordinated across services.

Two structural decisions shape this saga:

1. **Step ordering** — should stock be reserved *before* payment is processed, or should payment be taken first?
2. **Placement** — should the orchestrator live in the centralized saga service (per ADR-0001), or be embedded in one of the participating services?

ADR-0001 accepted centralized saga orchestration as the default topology for distributed transactions in the system (home of `PaymentProcessingSaga`, and — once this ADR ships — the Checkout saga). This ADR **extends** that decision specifically for the Checkout saga and commits to a step-ordering choice.

Real-world precedent for step ordering varies:

- **Amazon, Shopify, eBay** — stock is reserved at checkout entry; the customer sees a "you have X minutes to complete checkout" experience and is guaranteed availability through payment.
- **Some European retailers** — payment is taken first, then stock is allocated; stock-outs after payment are handled via refund. This trades UX for inventory-holding cost.

The Checkout saga also has no natural "owner service" — Basket, Ordering, Inventory, and Payments all participate equally — which sharpens the placement question compared to the existing sagas, where the initiating domain was clearer.

## Decision Drivers (ranked)

1. **UX expectation of guaranteed availability** — a user who sees "in stock" at checkout and proceeds to payment expects the item to still be available after paying. Anything else is a customer-visible failure.
2. **Compensation simplicity** — compensation paths should be conceptually and operationally simple. A "release stock" compensation (no money moved) is strictly cheaper than a "refund plus release" compensation.
3. **Avoid payment-then-oversell** — we must never charge a customer for something we cannot ship. Taking money and then discovering out-of-stock is a strict anti-pattern.
4. **Reservation TTL bounds stock-held-without-payment** — Inventory's default 15-minute reservation TTL (see `docs/bc-design/inventory.md`) puts an upper bound on the cost of holding stock during a payment wait.
5. **Consistent with existing saga placement philosophy** — ADR-0001's centralization rationale (ownership, no circular deps, independent scaling, observability) should continue to apply unless Checkout is fundamentally different.

## Considered Options

### Step Ordering

#### Option A: Reserve stock BEFORE payment (chosen)

Flow: `CreateOrder → ReserveStock → RequestPayment → ConfirmOrder → ConfirmReservations`.

Pros:

- Guarantees availability at the moment of purchase intent.
- Matches industry-standard consumer-commerce UX.
- Compensation on payment failure is a simple "release stock + cancel order" with no money movement.
- Aligned with Inventory's reservation semantics — the 15-min TTL bounds worst-case hold time.

Cons:

- Stock is tied up during the payment wait window.
- Abandoned payments temporarily hold inventory (bounded by TTL).

#### Option B: Payment BEFORE stock reservation

Flow: `CreateOrder → RequestPayment → ReserveStock → ConfirmOrder`.

Pros:

- Stock is only committed once payment is known to have succeeded — no "reservation-holding" cost during the payment window.

Cons:

- A payment-success-then-stock-out produces a strictly worse UX ("sorry, sold out — we'll refund you").
- A refund is heavier than a release (gateway call, customer-visible, potentially taxable).
- Violates driver #3 — taking money and discovering out-of-stock is the exact failure mode we must avoid.

#### Option C: Reserve and charge in parallel

Flow: `CreateOrder → (ReserveStock ∥ RequestPayment) → ConfirmOrder`.

Pros:

- Nominal latency is the minimum of the two steps.

Cons:

- Requires a two-phase commit across Inventory and Payments to handle "one succeeded, one failed".
- Contradicts the saga pattern's one-step-at-a-time premise.
- Compensation when both succeed but confirmation fails is the union of both single-step compensations (worst of both worlds).

### Placement

#### Option 1: Centralized in `saga/SagaOrchestrators/Checkout/` (chosen)

Follows ADR-0001. Folder shape mirrors the existing [`saga/SagaOrchestrators/Payments/PaymentProcessingSaga/`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/) — the sibling state machine in the same worker. Reuses the existing MassTransit state-machine + EF Core (PostgreSQL) + Kafka consumer-adapter pattern; co-deploys with the existing sagas in the `saga` worker.

#### Option 2: Embedded in the Ordering service

Ordering hosts the state machine. Basket triggers it via HTTP or an event handler inside Ordering.

Pros:

- One fewer deployment unit.
- Keeps all order-lifecycle code in one place at a glance.

Cons:

- Ordering would need to consume Inventory and Payments events (`StockReservedEvent`, `StockReservationFailedEvent`, `PaymentCompletedEvent`, `PaymentRefundedEvent`), inverting the dependency direction established by ADR-0001.
- Violates the "services as command responders, saga as hub" philosophy from ADR-0001.
- Duplicates saga infrastructure already present in the centralized worker (persistence, outbox wiring, consumer-adapter pattern, health checks).
- Creates a long-running state machine inside a service whose primary responsibility is synchronous command handling — operational characteristics (scaling, restart behaviour, memory footprint) diverge from the rest of Ordering.

## Evaluation Matrix

### Step ordering

| Driver (ranked) | A: Reserve first | B: Pay first | C: Parallel |
|---|---|---|---|
| 1. Guaranteed availability UX | Availability guaranteed through payment | Stock-out after payment is possible | Requires 2PC to guarantee |
| 2. Compensation simplicity | Release only (no money moved) | Refund + nothing else | Worst of both — refund and release |
| 3. Avoid payment-then-oversell | Cannot happen by construction | Primary failure mode | Possible if reservation fails after capture |
| 4. TTL bounds hold time | 15-min Inventory TTL bounds it | Not applicable | Not applicable |
| 5. Saga-pattern consistency | One step at a time | One step at a time | Violates one-step-at-a-time |

### Placement

| Driver (ranked) | 1: Centralized | 2: Embedded in Ordering |
|---|---|---|
| 1. Clear orchestration ownership | Single owner, hub-and-spoke | Ordering gains orchestration duty |
| 2. No circular dependencies | Saga is the hub | Ordering consumes Inventory + Payments events |
| 3. Services stay autonomous command responders | Preserved | Violated for Ordering |
| 4. Centralized observability | Single process to trace | Distributed across Ordering + saga infra |
| 5. Reuses existing saga infrastructure | Full reuse (MassTransit, Postgres, Kafka outbox) | Requires duplicating parts of it |

## Decision

We will use **Option A (reserve stock before payment)** and **Option 1 (centralized in `saga/SagaOrchestrators/Checkout/`)**.

The saga is placed at [`saga/SagaOrchestrators/Checkout/CheckoutSaga/`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/), following the folder convention already established by the sibling [`Payments/PaymentProcessingSaga/`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/). The step order is:

1. `CreateOrder` — Ordering creates the order in `Created` status.
2. `ReserveStock` — Inventory reserves stock for each distinct ProductId (fan-out).
3. `RequestPayment` — delegates to `PaymentProcessingSaga` via `RequestPaymentCommand`.
4. `ConfirmOrder` — Ordering transitions the order to `Confirmed`.
5. `ConfirmReservations` — Inventory converts reservations into allocations.

Compensation paths are explicit and vary by failure point: failures before capture flow through `CompensatingStockReservations → Compensated`; failures after capture flow through `CompensatingPayment → CompensatingStockReservations → Compensated` (refund first, then release stock).

## Rationale

**Reserving stock before payment is the industry-standard pattern in consumer commerce** — Amazon, eBay, and most large B2C marketplaces follow it. It guarantees availability at the moment of purchase intent, which matters more to customer UX than the temporary holding cost of unsold inventory. The alternative ("charge first, then reserve") produces a failure mode where a customer sees "payment successful" and then immediately "sorry, out of stock" — a strictly worse UX and a refund operation for every stock-out. Reserve-first aligns with existing industry mental models, which also simplifies hiring and onboarding: engineers joining the team recognize the pattern without documentation.

**The compensation topology also favours reserve-first.** A payment failure *after* reservation simply releases the stock with no money movement; a stock failure *after* payment requires a refund operation, which is heavier (gateway call, customer-visible, potentially taxable). Reserve-first minimizes the refund path. In concrete terms, the Checkout saga's compensation matrix has a clean split: failures before or during payment produce `Compensated` with *zero* money movement (release reservations, cancel order, emit `CheckoutFailedEvent`), while failures *after* capture — only reachable in `AwaitingConfirmation` — require the heavier two-phase path (`refund → release stock → cancel order`). Because confirmation failures are rare (typical cause: Ordering service crash post-payment), the expensive refund path is the exception, not the rule.

**The parallel "charge and reserve together" option was rejected** because it introduces a consistency problem: if the charge succeeds but the reservation fails (or vice-versa), a non-trivial two-phase commit protocol between Payments and Inventory would be required, and that contradicts the saga pattern's one-step-at-a-time premise. Parallel would also force duplication of compensation logic to handle the "both succeed but confirm fails" case, which is the union of both single-step compensations.

**The existing `PaymentProcessingSaga` is preserved unchanged as a sub-saga.** The Checkout saga delegates payment orchestration by publishing `RequestPaymentCommand`, which triggers `PaymentProcessingSaga` to handle authorize → capture → complete (with void/refund compensation and retry logic). This is the same saga-within-saga pattern ADR-0001 already established for `PaymentProcessingSaga`, and it means the Checkout saga inherits Payments's orchestration maturity — retry policies, idempotent capture semantics, refund gateway integration — for free. No changes to Payments or `PaymentProcessingSaga` are required to add Checkout.

**Centralized placement extends ADR-0001's rationale to a saga with no natural owner service.** The Checkout saga cannot live inside a single participating service — Basket, Ordering, Inventory, and Payments all participate equally, and none is a natural host. Embedding it in Ordering would force Ordering to consume Inventory and Payments events (`StockReservedEvent`, `StockReservationFailedEvent`, `PaymentCompletedEvent`, `PaymentRefundedEvent`), inverting the dependency direction ADR-0001 explicitly avoided. The centralized placement also wins on the same drivers ADR-0001 articulated: orchestration ownership, avoidance of cross-service circular dependencies, independent scaling of saga processing load, and centralized observability of the distributed transaction. Inventory's 15-minute reservation TTL caps the worst-case "stock held but payment never completed" window at a bounded, auto-released quantity — the Inventory `ReservationExpiryWorker` cleans up even if the saga itself goes stuck.

## Consequences

### Positive

- **Industry-standard UX**: availability is guaranteed from the moment of checkout intent through payment, matching consumer expectations set by Amazon, Shopify, and eBay.
- **Clear compensation semantics** across three distinct outcomes:
  - `Confirmed` — happy path; order confirmed, reservations confirmed, payment captured.
  - `Failed` — no money moved; reached when the failure occurred before or during stock reservation.
  - `Compensated` — money moved and was refunded; reached only on confirmation-stage failures.
  - `CompensationStuck` — compensation itself failed; requires ops intervention.
- **Payment-failure compensation is cheap** — release the stock, cancel the order, emit `CheckoutFailedEvent`. No refund needed because payment was never captured.
- **Reuses existing saga infrastructure**: MassTransit state machines, EF Core optimistic-concurrency persistence on the shared `saga` PostgreSQL schema, Kafka consumer-adapter pattern, transactional outbox — all of which are already exercised by the sibling `PaymentProcessingSaga`.
- **The `PaymentProcessingSaga` sub-saga** keeps refund/capture/retry logic in one place. Today the Checkout saga is its only caller, but the seam stays open: any future payment-using workflow (subscription billing, refund-as-a-service) would compose against the same sub-saga without re-implementing capture/void/refund orchestration. No changes to Payments are required.
- **Same deployment unit and operational mental model** as existing sagas — no new infrastructure, dashboards, or health checks to learn. Existing `SagaHealthCheck`, stuck-saga alerts, and OpenTelemetry tracing extend to Checkout with trivial additions.
- **Onboarding cost is low** because the reserve-first pattern is already the mental model new engineers bring with them from other e-commerce domains.

### Negative

- **Stock is held during the payment window** (p99 ≤ ~90s, plus reservation-TTL slack up to 15 min per Inventory's default policy). During flash sales, this visible "stock held but not shipped" pool can push unlucky customers to out-of-stock errors even though the buyer eventually abandons payment. Mitigated in part by Inventory's `ReservationExpiryWorker`, which auto-releases expired reservations.
- **The Checkout saga has 11 states** including compensation branches (`Initial`, `AwaitingOrderCreation`, `AwaitingStockReservation`, `AwaitingPayment`, `AwaitingConfirmation`, `Confirmed`, `CompensatingStockReservations`, `CompensatingPayment`, `Compensated`, `Failed`, `CompensationStuck`). Testing discipline is required to cover every path, especially the compensation branches; the design document mandates MassTransit `SagaTestHarness` unit tests per transition plus integration tests using Testcontainers.
- **Multi-item baskets require a fan-out pattern** internal to the saga: one `ReserveStockCommand` per distinct ProductId, tracked via a counter-based completion check. This is novel relative to the existing sagas (which have no fan-out) and increases saga-state complexity, including edge cases for duplicate command delivery after saga crash-restart.
- **The Checkout saga sits above `PaymentProcessingSaga`**, adding a second layer of state to debug when an end-to-end flow misbehaves. Two saga instances exist per purchase (Checkout and Payment); operators must correlate via the shared `OrderId` (each saga's `CorrelationId`).

### Risks

- **Stuck compensation.** If compensation itself fails (refund gateway down, Inventory unavailable beyond the compensation timeout), the saga enters the `CompensationStuck` terminal state. There is no automatic recovery — manual operator action is required. Mitigated by a dedicated `saga.checkout.stuck` counter and alert wired into Grafana (see `docs/bc-design/checkout-saga.md § 11`), and by a generous `CompensationTimeout` default of 300 seconds.
- **Reservation TTL shorter than payment completion.** If average payment completion time exceeds 15 minutes (rare, but possible during 3-D Secure challenges), Inventory's TTL expires mid-flow and `ReservationExpiryWorker` releases stock out from under the saga. Mitigation: the saga's `StockReservationTimeout` (60 seconds default) fails fast well before the reservation TTL elapses; the cumulative happy-path timeout stack (3.5 min) leaves 11+ minutes of TTL slack. Tune timeouts if production data disagrees.
- **Payment capture-then-outer-timeout race.** The Checkout saga's `PaymentTimeout` (90s) can fire while `PaymentProcessingSaga` is still running, producing a late "capture-then-compensate" scenario where Payments eventually captures payment for an already-finalized outer saga. Mitigation: set outer timeouts with headroom over inner-saga timeout sums, and defer a stale-payment reconciliation worker to a future iteration (see checkout-saga design § 14 open question 5).
- **Concurrent reservations on the last-in-stock item.** Two sagas for the same last-in-stock ProductId both enter `AwaitingStockReservation` simultaneously; Inventory's per-stream optimistic concurrency serializes the `ReserveStockCommand`s — one saga receives `StockReservedEvent`, the other receives `StockReservationFailedEvent` and compensates cleanly. Works correctly by construction; documented as a test case.
- **Separate `CheckoutFailedEvent` schema.** The design introduces a saga-level terminal event on a new `checkout.sagas` topic (covered in `docs/bc-design/checkout-saga.md § 9`). If Stage 2's event catalog decides Ordering's existing events are sufficient, this ADR's saga-level emission can be collapsed — a simplification, not a breaking change.

## Implementation Notes

- **Saga class**: `CheckoutSagaOrchestrator : MassTransitStateMachine<CheckoutSagaState>`, folder `saga/SagaOrchestrators/Checkout/CheckoutSaga/`.
- **State persistence**: `saga.checkout_saga_states` (PostgreSQL, optimistic concurrency via `RowVersion`), EF Core mapping, same `saga` schema as existing sagas. Mutable per-saga fields (basket snapshot, reservation tracking) are stored as `jsonb` columns for atomic read-modify-write under a single `RowVersion` check.
- **CorrelationId**: the saga's MassTransit key equals the pre-assigned `BasketCheckoutInitiatedEvent.OrderId` (UUID v7, [ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md)), immutable after first set. The same `OrderId` is the Order aggregate's id and travels on every downstream event, so the whole Basket → Saga → Ordering → Inventory → Payments flow correlates on it.
- **Consumer group**: `saga-checkout` (one group per saga, per ADR-0001's pattern).
- **Topics consumed**: `basket.sessions`, `ordering.orders`, `inventory.reservations`, `payments.transactions`. Kafka consumers use the consumer-adapter pattern: Avro external events are transformed into internal MassTransit saga events via `IPublishEndpoint`, mirroring the sibling `PaymentProcessingSaga`.
- **Topics published to**: `ordering.order-commands` (`CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand`), `inventory.reservation-commands` (`ReserveStockCommand`, `ConfirmReservationCommand`, `ReleaseReservationCommand`), `payments.payment-commands` (`AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RequestRefundCommand`), plus saga-level terminal events on `checkout.sagas` (`CheckoutCompletedEvent`, `CheckoutFailedEvent`, `CheckoutStuckEvent`). All publications go through the transactional outbox to ensure saga state and message emission are atomically consistent.
- **Payment orchestration** is delegated to the existing `PaymentProcessingSaga` via `RequestPaymentCommand` — no changes to Payments are required. The sub-saga reports back with `PaymentCompletedEvent` / `PaymentFailedEvent` / `PaymentRefundedEvent`.
- **Multi-item fan-out** is internal to `AwaitingStockReservation`: one `ReserveStockCommand` per distinct ProductId (summing quantities for duplicated lines), tracked via a `ReservationIdsJson` dictionary with `ExpectedReservations` / `PendingReservations` counters. Completion of the state requires `PendingReservations == 0` and no failures; any failure triggers fan-in release of reservations acquired so far.
- **Timeouts** (configurable under `Saga:CheckoutTimeouts` in `appsettings.json`, mirroring the existing `PaymentProcessingTimeouts` sections): `OrderCreationSeconds: 30`, `StockReservationSeconds: 60`, `PaymentSeconds: 90`, `OrderConfirmationSeconds: 30`, `CompensationSeconds: 300`. Token IDs persisted on the saga state; scheduled via the MassTransit message scheduler.
- **Timeout budget vs reservation TTL**: the cumulative happy-path timeout stack (30 + 60 + 90 + 30 = 210s ≈ 3.5 min) is comfortably below Inventory's default 15-min reservation TTL, leaving ~11 min of slack and another 5 min for compensation. If a reservation TTL does expire mid-saga, the `ReservationReleasedSagaEvent(ReleaseReason=Expiry)` is treated as a stock-timeout and triggers compensation.
- **Hard cross-BC invariant — TTL ↔ saga-timeout coupling** (per [saga-stuck-runbook.md § 6 line 297](../bc-design/saga-stuck-runbook.md)): the reservation-TTL race documented in [runbook § 4.3](../bc-design/saga-stuck-runbook.md) is "the most common `CompensationStuck` scenario" precisely when the inequality below is too tight or violated. The invariant is:

  ```
  OrderCreationSeconds + StockReservationSeconds + PaymentSeconds + OrderConfirmationSeconds + (2 × CompensationSeconds) < InventoryReservationTtlSeconds
  ```

  At default values: `30 + 60 + 90 + 30 + 2×300 = 810s` vs `900s` TTL → 90s margin. Tuning `CompensationSeconds` upward (e.g., 300 → 600) or shortening Inventory's TTL silently inverts the inequality and turns the race from "rare benign" into "default". **An architecture test in `saga/SagaOrchestrators.UnitTests/` MUST encode this invariant** (see [`docs/bc-design/checkout-saga.md`](../bc-design/checkout-saga.md)) so that any future timeout retuning fails the build instead of paging on-call.
- **Architecture tests**: architecture-test fixtures must assert that `CheckoutSagaOrchestrator` lives under `saga/SagaOrchestrators/Checkout/`, does NOT depend on any service assembly directly, and that all state transitions terminate in one of the four terminal states (`Confirmed`, `Failed`, `Compensated`, `CompensationStuck`). Additionally the timeout invariant test described above MUST be present.
- **Observability**: OpenTelemetry activity source `SagaOrchestrators.Checkout`; OTEL meter with counters for `saga.checkout.started`, `saga.checkout.confirmed`, `saga.checkout.failed`, `saga.checkout.compensated`, `saga.checkout.stuck`, plus histograms for per-phase latency. The `SagaHealthCheck` includes a stuck-saga extension specific to Checkout's `CompensationStuck` terminal.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — parent decision; this ADR extends it to the Checkout saga and preserves the same placement philosophy, folder convention, persistence story, and sub-saga layering.
- [ADR-0003: Basket as Technical BC](0003-basket-as-technical-bc.md) — defines `BasketCheckoutInitiatedEvent`, the trigger event for this saga.
- [ADR-0005: Customer Data in Ordering](0005-customer-data-in-ordering.md) — the Checkout saga does NOT carry customer profile data; it flows through Basket → Order per ADR-0005's placement rule.
- [kafka-topology.md](../kafka-topology.md) — per-topic retention/partition/class for every topic the saga consumes or publishes to (`basket.sessions`, `ordering.*`, `inventory.*`, `payments.*`, `checkout.sagas`); per-event producer/consumer/key in [events-catalog.md § 2](../bc-design/events-catalog.md).
