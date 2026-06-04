# ADR-0001: Centralized Saga Orchestration

## Status

Accepted — revised 2026-05-30 to reflect the post-Weather-cleanup eShop topology (the original `AlertSubscriptionPurchaseSaga` / `AlertSubscriptionExtensionSaga` narrative was replaced with the Checkout saga; see [master-design § 2 — Solution Structure Recap](../eshop-master-design.md) for the cleanup record).

## Context

DotNetAtlas is a distributed eShop reference solution with multiple services that must coordinate multi-step business processes:

- **Basket** — captures a user's basket state in Redis; emits `BasketCheckoutInitiatedEvent` when the user checks out.
- **Ordering** — owns the `Order` aggregate and its lifecycle (Created → StockReserved → PaymentCompleted → Confirmed → Shipped → Delivered, with Cancelled / Failed off-ramps).
- **Inventory** — event-sourced reservation lifecycle: `ReserveStock` → `ConfirmReservation` (or `ReleaseReservation`) with a 15-min TTL.
- **Payments** — executes payment commands (authorize, capture, void, refund) against an external gateway.
- **Invoicing** — converges on `OrderConfirmedEvent` + `PaymentCapturedEvent` to issue an invoice; mirror on `OrderCancelledEvent` + `PaymentRefundedEvent` for credit notes.
- **Notifications** — sends email notifications from `SendEmailNotificationCommand` (v1; renamed in ADR-0031) (drained from Ordering / Invoicing).

The flagship business workflow — *"check out a basket"* — spans Basket → Ordering → Inventory → Payments → (Ordering confirm) and may need compensation (refund + stock release + order cancel) at any failure point after capture. This distributed transaction requires reliable orchestration with timeout handling and explicit compensation paths.

The system currently has two sagas, both deployed inside the `saga/SagaOrchestrators/` worker:

1. **CheckoutSaga** ([`saga/SagaOrchestrators/Checkout/CheckoutSaga/`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/)) — Basket → Ordering → Inventory → Payments → Confirmation. 11 states including compensation branches. See [ADR-0004](0004-checkout-saga-topology.md).
2. **PaymentProcessingSaga** ([`saga/SagaOrchestrators/Payments/PaymentProcessingSaga/`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/)) — sub-saga that handles Authorize → Capture with retry, plus void/refund compensation paths. Invoked by the Checkout saga via `RequestPaymentCommand` on `payments.payment-commands` (renamed from `PaymentRequestedEvent` per [ADR-0023](0023-payments-event-vs-command-classification.md)).

> **Historical note:** earlier revisions of this ADR also referenced `AlertSubscriptionPurchaseSaga` and `AlertSubscriptionExtensionSaga` under `saga/SagaOrchestrators/Orders/`. Those sagas were removed in the pre-dispatch Weather cleanup along with the `services/Order/` project and the `order.alert-subscriptions` Kafka topic. The cleanup is recorded in [`docs/eshop-master-design.md § 2`](../eshop-master-design.md).

We needed to decide where this orchestration logic lives.

## Decision Drivers (ranked)

1. **Clear ownership of orchestration logic** — workflow coordination must have a single owner to avoid split-brain scenarios where two services each believe they own the next step.
2. **Avoid circular dependencies between services** — the Checkout flow touches five BCs; embedding orchestration in any one of them would force that service to consume the other four's events, creating tight coupling.
3. **Services remain autonomous command responders** — each BC should own its domain logic without needing to know about cross-BC workflows.
4. **Centralized observability for distributed transactions** — tracing a checkout flow across five BCs is easier when the orchestrator is a single observable process emitting per-transition activities.
5. **Independent scaling** — saga processing load differs from API or downstream-consumer load; running sagas in a dedicated worker isolates their resource footprint.

## Considered Options

### Option 1: Centralized Saga Service (chosen)

A dedicated `saga/SagaOrchestrators` worker hosts all MassTransit state machines. Services communicate via Kafka events; the saga worker consumes events from all participating BCs and publishes commands/events back through transactional outbox.

Sagas are organized by initiating domain or coordination scope (`Checkout/`, `Payments/`) but deployed as a single worker process. Each saga uses the consumer-adapter pattern: Kafka consumers translate Avro events into internal MassTransit saga events via `IPublishEndpoint`.

### Option 2: Sagas Embedded in the Initiating Service

The CheckoutSaga lives inside one of the participating services (e.g., the Ordering service hosts it because the order is the first persisted artifact). PaymentProcessingSaga lives inside the Payments service.

### Option 3: Choreography (No Orchestrator)

Remove explicit orchestration entirely. Each service reacts to events and publishes its own events. The Checkout workflow emerges implicitly from the chain of event handlers across BCs.

## Evaluation Matrix

| Driver (ranked) | Centralized Saga | Embedded in Services | Choreography |
|---|---|---|---|
| 1. Clear orchestration ownership | ✅ Single owner | ⚠️ Split across services — Ordering would gain orchestration duty for a 5-BC flow it doesn't naturally own | ❌ No owner — workflow logic scattered |
| 2. No circular dependencies | ✅ Saga is the hub; BCs reply via events | ❌ Ordering would consume Basket/Inventory/Payments/Notifications events | ✅ No direct deps, but every service must know the chain |
| 3. Services stay autonomous | ✅ Services are pure command responders | ❌ The host service gains orchestration responsibility | ✅ Fully autonomous |
| 4. Centralized observability | ✅ Single process to trace; per-transition activities under one ActivitySource | ⚠️ Distributed across services — must stitch traces by `CorrelationId` | ❌ Must correlate across every participating service |
| 5. Independent scaling | ✅ Saga worker scales separately | ❌ Tied to host service's scaling envelope | ✅ Each service scales independently |

## Decision

We will use a **centralized saga service** (`saga/SagaOrchestrators/`) hosting all MassTransit state machines, with Kafka as the event backbone and **PostgreSQL** for saga state persistence (schema `saga`, optimistic concurrency via `RowVersion`).

## Rationale

**Centralized orchestration wins on the highest-priority drivers.** The Checkout saga coordinates five BCs — none is a natural orchestration owner. Embedding the saga in Ordering would force it to consume `BasketCheckoutInitiatedEvent`, `StockReservedEvent`, `StockReservationFailedEvent`, `PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`, inverting the producer-consumer direction this ADR explicitly avoids. Choreography would scatter the workflow logic across BCs, making it nearly impossible to reason about the end-to-end flow or implement the reliable compensation path required for a payment-then-stock-release sequence after a confirmation failure.

**PaymentProcessingSaga is intentionally layered as a sub-saga.** The Checkout saga delegates the authorize → capture sequence by publishing `RequestPaymentCommand` (renamed from `PaymentRequestedEvent` per [ADR-0023](0023-payments-event-vs-command-classification.md)) on `payments.payment-commands`. PaymentProcessingSaga consumes the command and runs its own state machine (auth retry, capture retry, void on capture-failure compensation). When it reaches its terminal `PaymentCompleted` or `PaymentFailed` state, it emits the matching event on `payments.transactions` and the Checkout saga consumes that to drive its own FSM forward. This saga-within-saga pattern:

- Keeps payment-orchestration logic (retries, void compensation) in one place that any future payment-using workflow can reuse without re-implementing.
- Keeps the Checkout saga focused on its business flow (basket → order → stock → payment → confirm) rather than payment-gateway minutiae.
- Cleanly separates two timeout horizons: the Checkout saga's outer 90 s `PaymentTimeout` and the PaymentProcessingSaga's per-step `AuthorizationMinutes` / `CaptureMinutes`.

This layering adds indirection (two saga instances per checkout, both observable via the same `CorrelationId`) but is justified by the separation of concerns. If payment processing were inlined into the Checkout saga, retry logic and void/refund compensation would have to live in the Checkout state machine and would not be reusable.

## Consequences

### Positive

- Workflow logic for distributed transactions lives in one place — easy to understand, test, and modify in isolation from the BCs.
- BCs remain simple command responders with no knowledge of multi-step flows.
- Saga state is persisted to PostgreSQL with optimistic concurrency — durable and recoverable from worker restart.
- Each saga has configurable timeouts at every state, preventing indefinite hangs; the cumulative happy-path budget stays well under Inventory's 15-min reservation TTL (see [ADR-0004 § Implementation Notes — TTL ↔ saga-timeout coupling invariant](0004-checkout-saga-topology.md)).
- Compensation flows (release stock, refund, cancel order) are explicit in the state machine, not scattered across BCs.
- Single deployment unit (`saga/SagaOrchestrators/`) for all orchestration simplifies operational monitoring and health-check coverage.

### Negative

- Extra deployment unit (the saga worker service) to operate and monitor — adds container count, health-check endpoint, and observability dashboard.
- The saga worker becomes a coupling point — it must be updated when cross-BC workflows change, even if no BC code changes.
- The saga-within-saga pattern (Checkout → PaymentProcessingSaga) adds debugging complexity when an end-to-end flow misbehaves — operators must correlate both saga states via the shared `CorrelationId`.
- All sagas share a single PostgreSQL database for state; high saga volume could create contention on the `saga` schema's tables (mitigated by row-level optimistic concurrency and the worker's `Saga:ConcurrencyLimit`).

### Risks

- **Saga worker as bottleneck**: if saga throughput becomes a concern, the worker can be scaled horizontally since MassTransit supports concurrent saga processing with configurable per-saga concurrency limits.
- **Schema evolution**: adding new sagas or modifying existing ones requires deploying the saga worker. Mitigation: sagas are isolated in their own folders with no shared state between them.
- **PaymentProcessingSaga reuse assumption**: with WeatherAlert sagas removed, the Checkout saga is currently the only caller of PaymentProcessingSaga, so the extra layering carries no immediate reuse benefit. The pattern is retained because (a) the cost of removing it is non-trivial (collapse the two state machines and re-test compensation paths) and (b) any future payment-using workflow (e.g., subscription billing, refund-as-a-service) would re-introduce the same separation.
- **Cross-saga refund path drift**: master-design § 5.5 documents PaymentProcessingSaga as *"the only caller of Payments commands"*, but Checkout saga's `OrderConfirmationFailed` handler currently publishes `RequestRefundCommand` directly. That divergence is documented as a known deferral; either wire the refund flow back through PaymentProcessingSaga, or update master-design § 5.5 to acknowledge Checkout-owned refunds — both are valid and the decision is deferred to a future ADR.

## Implementation Notes

- Sagas use MassTransit `MassTransitStateMachine<TState>` with EF Core persistence to PostgreSQL (`saga` schema).
- Kafka consumers use the consumer-adapter pattern: Avro events / commands → internal saga events via `IPublishEndpoint`. Adapter classes live under each saga's `Consumers/` folder.
- Consumer groups are **per-saga** (not per-message): `saga-checkout` (Checkout saga across `basket.sessions`, `ordering.orders`, `inventory.reservations`, `payments.transactions`); `saga-payment-processing` (PaymentProcessingSaga across `payments.transactions` and `payments.payment-commands`).
- Outbox pattern ensures saga state changes and published messages are transactionally consistent — see [Platform.ReliableMessaging.Outbox.EFCore](../../platform/Platform.ReliableMessaging.Outbox.EFCore/).
- Saga folders: [`saga/SagaOrchestrators/Checkout/CheckoutSaga/`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/), [`saga/SagaOrchestrators/Payments/PaymentProcessingSaga/`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/).
- Health check: `SagaHealthCheck` reports degraded / unhealthy on stuck-saga counts; `CompensationStuck` (Checkout saga abnormal terminal) increments a dedicated counter wired to ops alerting.

## Related Decisions

- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — extends this ADR's centralization rationale to the specific Checkout saga step ordering (stock before payment) and placement.
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — FORWARD_TRANSITIVE for event topics, FULL_TRANSITIVE for command topics; both relied on by the saga's contract surface.
- [ADR-0023: Payments Event-vs-Command Classification](0023-payments-event-vs-command-classification.md) — clarifies the `RequestPaymentCommand` naming (formerly `PaymentRequestedEvent`) on `payments.payment-commands` referenced in this ADR's sub-saga description.
- Transactional Outbox pattern (platform library) — ensures reliable message delivery from saga state changes.
- Avro serialization with Confluent Schema Registry — contract format for all cross-BC events and saga-issued commands.
