# Kafka topology

> **Role:** the **per-topic physical SSOT** (partitions, retention, class) for the eShop, kept in lockstep with the `kafka-create-topic` block in [docker-compose.yaml](../docker-compose.yaml) (the runtime source of truth). Per [ADR-0033](adr/0033-kafka-topic-contract-doc-ssot.md) this is one of **two** canonical anchors; the per-**event** contract — producer, consumer(s), consumer group, correlation key, trigger, schema path — lives in [events-catalog.md § 2](bc-design/events-catalog.md). The two join on the topic name. **Compatibility mode is not a separate fact: it is *derived* from the class** (see the Topic-classes table below + [ADR-0007](adr/0007-avro-compatibility-modes.md)) and is never tabulated per topic.

Reference for the Kafka topics defined in `docker-compose.yaml` (kafka-create-topic init block). Each topic belongs to one of five **classes**; the class fixes retention and Schema-Registry compatibility expectations. Add a new topic only after deciding which class it belongs to.

## Topic classes

| Class | Retention (ms) | Compat (per ADR-0007) | Rationale |
|-------|---------------:|-----------------------|-----------|
| **Event log** | `-1` (infinite) | `FORWARD_TRANSITIVE` | Downstream BCs **replay-rebuild** projections from these topics. Truncation would silently break new consumers. FORWARD_TRANSITIVE lets producers add fields without breaking historical consumers, transitively across all versions. |
| **Saga state** | `2592000000` (30d) | `FORWARD_TRANSITIVE` | Run-history of in-flight + recently terminal sagas; 30d is long enough for offline forensics and short enough to bound storage. Same producer-additive evolution shape as event-log. |
| **Command** | `604800000` (7d) | `FULL_TRANSITIVE` | Transient intent. Consumer and producer can deploy in either order, so compat must work in both directions. A command not consumed within a week is a runbook event, not a replay candidate. |
| **Audit** | `315360000000` (10y) | `FORWARD_TRANSITIVE` | Regulatory retention. Used for invoicing/financial trails that must survive long after the operational system retains them elsewhere. |
| **Health probe** | default (~7d) | n/a | Single-partition liveness signal. Not a business topic. |

Defaults (no explicit `retention.ms` config): Kafka broker default (7d) applies. Treated as **command** class in this taxonomy.

## Per-topic inventory

| Topic | Partitions | Retention | Class | Notes |
|-------|------------|-----------|-------|-------|
| `catalog.products` | 3 | -1 | event-log | Source of truth for product master data; Inventory rebuilds from this on bootstrap. |
| `catalog.categories` | 3 | -1 | event-log | Category master data; same replay-rebuild contract. |
| `ordering.orders` | 3 | -1 | event-log | Order summary events (`OrderConfirmedEvent`, `OrderCancelledEvent`); Invoicing projects from these. |
| `inventory.stock-events` | 3 | -1 | event-log | `StockLevelChangedEvent` threshold-crossing events; Catalog consumes. Event-sourced per ADR-0006. |
| `inventory.reservations` | **6** | -1 | event-log | Reservation events; co-partitioned by `OrderId` (6 partitions for cross-order parallelism). Consumed by Checkout saga. |
| `payments.transactions` | 3 | -1 | event-log | `PaymentAuthorized/Captured/Refunded/Voided` events; Invoicing + saga consume. |
| `basket.sessions` | 3 | 2592000000 | saga-state | `BasketCheckoutInitiatedEvent` and per-session lifecycle; 30d forensic window. |
| `checkout.sagas` | 3 | 2592000000 | saga-state | `CheckoutCompleted/Failed/Stuck` terminal events; 30d forensic window per ADR-0004. |
| `ordering.order-commands` | 3 | 604800000 | command | Saga → Ordering commands (`CreateOrder`, `ConfirmOrder`, `CancelOrder`, `MarkOrderFailed`). |
| `inventory.reservation-commands` | 3 | 604800000 | command | Saga → Inventory commands (`ReserveStock`, `ConfirmReservation`, `ReleaseReservation`). |
| `payments.payment-commands` | 3 | 604800000 | command | Saga → Payments commands (`AuthorizePayment`, `CapturePayment`, `VoidPayment`, `RequestRefund`). |
| `notifications.notify-commands` | 3 | 604800000 | command | Inbound `NotifyUserCommand` (channel-agnostic; [events-catalog.md § 2](bc-design/events-catalog.md), [ADR-0031](adr/0031-notify-user-command-and-notification-id.md)). |
| `notifications.notify-events` | 3 | -1 | event-log | `NotificationDeliveryStatusChangedEvent` delivery confirmations ([events-catalog.md § 2](bc-design/events-catalog.md), [ADR-0031](adr/0031-notify-user-command-and-notification-id.md)). |
| `invoicing.invoices` | 3 | 315360000000 | audit | Invoice issuance events; 10-year regulatory retention. |
| `healthchecks` | **1** | default (~7d) | health-probe | Liveness signal only; single partition is correct. |

> **DLT topics are not listed above.** They are *derived* (`{source-topic}.{consumer-bc}.DLT`, 14-day retention), governed by [kafka-dlt-strategy.md](bc-design/kafka-dlt-strategy.md), and pre-created in the same `kafka-create-topic` block in [docker-compose.yaml](../docker-compose.yaml). The table above covers business + health topics only.

## Conventions

- **Replication factor** is `1` everywhere because the reference compose is a single-broker dev setup. Production raises this to `3` with `min.insync.replicas=2`.
- **`min.insync.replicas=1`** is set explicitly on every topic to make the single-broker config legible; production override per environment.
- **Partition count** defaults to `3` (matches saga + outbox-relay parallelism). `inventory.reservations` raises to `6` to scale per-`OrderId` consumer parallelism; `healthchecks` drops to `1` because order doesn't matter.
- **Compatibility mode** is governed by Schema Registry per ADR-0007 and is not set on the broker; it's bootstrapped by `schema-registry-init` in `docker-compose.yaml`, which walks `platform/Platform.SchemaRegistry.Contracts/Avro/**` and dispatches by filename suffix (`*Event.avsc` → `FORWARD_TRANSITIVE`, `*Command.avsc` → `FULL_TRANSITIVE`). Any `.avsc` lacking either suffix fails the bootstrap loudly — convention is machine-enforced.

## Adding a new topic

1. Decide the class. If unsure between event-log and command, default to **command** unless downstream BCs need to replay-rebuild from it.
2. Add the `kafka-topics --create …` line in `docker-compose.yaml` (kafka-create-topic init block) following the class's partition/retention defaults.
3. Update the table in this file.
4. Register the Avro schema under `platform/Platform.SchemaRegistry.Contracts/Avro/<Owner>/<Aggregate>/` with the `Event` or `Command` filename suffix. The dynamic `schema-registry-init` script picks up the new schema automatically; no list to update.

## Related decisions

- [ADR-0033: SSOT for Kafka topic & event-contract docs](adr/0033-kafka-topic-contract-doc-ssot.md) — why this file is the per-topic anchor and `events-catalog.md § 2` the per-event anchor, with everything else pointing here.
- [ADR-0004: Checkout Saga Topology](adr/0004-checkout-saga-topology.md) — names the consumer-group + topic pairing the Checkout saga relies on.
- [ADR-0006: Event Sourcing for Inventory](adr/0006-event-sourcing-for-inventory.md) — defines the `inventory.stock-events` + `inventory.reservations` event-log contract.
- [ADR-0007: Avro Schema Compatibility Modes](adr/0007-avro-compatibility-modes.md) — fixes the per-class compatibility expectation.
