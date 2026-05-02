# Event Catalog & Kafka Topology

> **Status:** Stage 2 Agent 5 authoritative output.
> **Scope:** Consolidates every cross-service event in the eShop reference solution — every Avro schema, every Kafka topic, every outbox/inbox registration, every internal HTTP surface that matters for messaging. Implementation agents use this document as the sole input for creating `.avsc` files, `docker-compose` topic entries, outbox-relay workers, inbox registrations, and consumer bindings.
> **Parent section:** `docs/eshop-master-design.md § 6`.
> **Companion ADRs:** [0001](../adr/0001-centralized-saga-orchestration.md), [0004](../adr/0004-checkout-saga-topology.md), [0006](../adr/0006-event-sourcing-for-inventory.md).
> **Hard ownership boundary:** this file. Nothing in `docs/eshop-master-design.md` or other BC-design files is touched by Stage 2 Agent 5.

---

## 1. Conventions

### 1.1 Internal vs external events

Per master design § 3.1:

| Kind | Name suffix | Transport | Avro schema | Naming namespace |
|------|-------------|-----------|-------------|------------------|
| Internal domain event | `{State}DomainEvent` | in-process `IDomainEventHandler<T>` | **none** | `{Service}.Domain.{Aggregate}.Events` (C# only) |
| External summary event | `{BusinessMoment}Event` | Kafka via transactional outbox | `.avsc` under `platform/Platform.SchemaRegistry.Contracts/Avro/{Domain}/{Aggregate}/` | `{Domain}.{Aggregate}` |
| Saga-issued command | `{Verb}{Target}Command` | Kafka (unless marked HTTP) | `.avsc` under `platform/Platform.SchemaRegistry.Contracts/Avro/{Domain}/{Aggregate}/` | `{Domain}.{Aggregate}` |

Every external event carries an `*AtUtc` / `OccurredOnUtc` field with `logicalType: timestamp-millis`. Every identifier carries `logicalType: uuid`. Every monetary amount uses `bytes + logicalType: decimal, precision: 19, scale: 4`. Every enum is declared inline (Avro convention for first-use) and referenced by fully-qualified name on subsequent uses.

### 1.2 Topic naming

Convention: `{domain}.{aggregate}[.{kind}]` — all lowercase, dot-delimited.

- Event logs (business moments): `catalog.products`, `ordering.orders`, `inventory.reservations`.
- Command streams (imperative intent): `{...}.{...}-commands`, e.g. `payments.commands`, `inventory.reservation-commands`.

**Partitioning key rule:** the stable business identity that preserves the right invariant.
- Per-aggregate ordering → aggregate id. Examples: `catalog.products` keyed by `ProductId`, `ordering.orders` keyed by `OrderId`.
- Saga correlation → `CorrelationId` or `OrderId`. Example: `inventory.reservations` keyed by `OrderId` so all reservation events for one saga run co-partition.

### 1.3 Avro style rules

- `"doc"` on every field (enforced by review; no silent fields).
- Nullable: `["null","{type}"]` with `default: null`.
- Decimal money always `precision: 19, scale: 4`.
- Timestamps always `long` + `timestamp-millis`.
- UUIDs always `string` + `logicalType: uuid`.
- Enums declared inline on first use, referenced by FQN thereafter (Avro permits this within a schema; for cross-schema enum sharing each schema declares its own — no enum reuse across files).
- Required fields have no default; optional fields have explicit `default`.

### 1.4 Decisions recorded in this document

| # | Decision | Rationale |
|---|----------|-----------|
| D-1 | **One `ordering.order-commands` topic** for saga→Ordering instead of per-command topics | Matches `payments.commands` precedent; saga already fan-outs by command type via MassTransit consumer type-routing. |
| D-2 | **One `inventory.reservation-commands` topic** for saga→Inventory | Same rationale as D-1. Keeps saga surface uniform. |
| D-3 | **One `ReserveStockCommand` per order line item** (Option A in Agent-5 prompt) | Matches Inventory's per-`ProductId` stream model (ADR-0006); gives natural per-item failure granularity; saga fan-in by `OrderId` correlates N responses. |
| D-4 | **No dedicated `checkout.commands` topic** | Saga publishes imperative intent to `ordering.order-commands`, `inventory.reservation-commands`, and `payments.commands`. A checkout-specific topic would duplicate infrastructure for no new semantics. |
| D-5 | **Notifications continues consuming business events directly** (not a `notification.commands` fan-out) | Matches existing Weather→Notifications pattern. `notification.commands` topic stays reserved for explicit SendEmail commands; Ordering emits `ordering.orders` and Notifications subscribes. |
| D-6 | **Outbox-relay is a separate container per service schema** | Follows the `outbox-relay`, `outbox-relay-saga`, `outbox-relay-ordering` precedent already in `docker-compose.yaml`. Each service gets its own relay with its own `OutboxRelay__SchemaName` binding. See § 6. |
| D-7 | **Weather-remnant fully decommissioned pre-dispatch** | The `services/Order/` project, `AlertSubscription*Saga` sagas, and Kafka topic `order.alert-subscriptions` were deleted. Ordering is greenfield; no legacy topic coexistence remains. |
| D-8 | **Basket sessions topic retains events for 30 days**; all other event-log topics use `compact + delete` (infinite retention) to preserve audit trail | Basket is ephemeral by definition. Order/Inventory events feed compliance audit; deleting them defeats the audit-trail purpose of § 2 of `inventory.md`. |
| D-9 | **Command topics retain 7 days** | Commands are transient intent; after 7 days a replay is not operationally useful and keeping them consumes broker disk needlessly. |
| D-10 | **`notification.commands` already exists and is reused** | It is registered in the current `docker-compose.yaml` line 243. No new topic needed for Notifications. |

---

## 2. Master Event Catalog

Sorted by topic then event name. All rows reflect Stage 1 BC designs plus the command schemas introduced in § 5.5 / § 5.6. Existing Payments events (which the Checkout saga reuses unmodified) are included for completeness with a link back to the existing `.avsc`.

| Event | Topic | Namespace | Producer | Consumer(s) | Consumer Group(s) | Correlation Key | Triggered By | Schema File Path |
|-------|-------|-----------|----------|-------------|-------------------|-----------------|--------------|------------------|
| `BasketCheckoutInitiatedEvent` | `basket.sessions` | `Basket.Sessions` | Basket | Checkout saga | `checkout-saga` | `BasketCorrelationId` | `BasketCheckedOutDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc` |
| `CategoryCreatedEvent` | `catalog.categories` | `Catalog.Categories` | Catalog | BFF (cache warm), Search indexer (v2) | `bff-category-tree`, (future) | `CategoryId` | `CategoryCreatedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Categories/CategoryCreatedEvent.avsc` |
| `ProductCreatedEvent` | `catalog.products` | `Catalog.Products` | Catalog | Inventory (init stream), BFF (cache warm) | `inventory-stock-init`, `bff-product-cache` | `ProductId` | `ProductCreatedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductCreatedEvent.avsc` |
| `ProductDiscontinuedEvent` | `catalog.products` | `Catalog.Products` | Catalog | Basket (flag stale snapshots; on-demand in v1), BFF (cache invalidate) | `basket-catalog-invalidation` (future), `bff-product-cache` | `ProductId` | `ProductDiscontinuedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductDiscontinuedEvent.avsc` |
| `ProductPriceChanged` | `catalog.products` | `Catalog.Products` | Catalog | BFF (cache invalidate); Basket consumes on-demand only in v1 | `bff-product-cache` | `ProductId` | `ProductPriceChangedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductPriceChanged.avsc` |
| `CapturePaymentCommand` | `payments.commands` | `Payments.Transactions` | PaymentProcessingSaga | Payments | `payments-payment-capture` | `CorrelationId` | saga transition | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/CapturePaymentCommand.avsc` (existing) |
| `AuthorizePaymentCommand` | `payments.commands` | `Payments.Transactions` | PaymentProcessingSaga | Payments | `payments-payment-authorize` | `CorrelationId` | saga transition | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AuthorizePaymentCommand.avsc` (existing) |
| `RequestRefundCommand` | `payments.commands` | `Payments.Transactions` | PaymentProcessingSaga (Checkout saga compensation path) | Payments | `payments-payment-refund` | `CorrelationId` | Checkout saga compensation after cancel-post-capture | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/RequestRefundCommand.avsc` (existing) |
| `VoidPaymentCommand` | `payments.commands` | `Payments.Transactions` | PaymentProcessingSaga | Payments | `payments-payment-void` | `CorrelationId` | saga compensation pre-capture | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/VoidPaymentCommand.avsc` (existing) |
| `PaymentAuthorizationFailedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga | `payment-saga-auth-failed` | `CorrelationId` | Payments auth failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizationFailedEvent.avsc` (existing) |
| `PaymentAuthorizedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga | `payment-saga-auth` | `CorrelationId` | Payments auth success | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizedEvent.avsc` (existing) |
| `PaymentCaptureFailedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga | `payment-saga-capture-failed` | `CorrelationId` | Payments capture failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCaptureFailedEvent.avsc` (existing) |
| `PaymentCapturedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga | `payment-saga-capture` | `CorrelationId` | Payments capture success | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCapturedEvent.avsc` (existing) |
| `PaymentCompletedEvent` | `payments.transactions` | `Payments.Transactions` | PaymentProcessingSaga | **Checkout saga** | `checkout-saga` | `CorrelationId` | Payment saga completion | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCompletedEvent.avsc` (existing) |
| `PaymentFailedEvent` | `payments.transactions` | `Payments.Transactions` | PaymentProcessingSaga | **Checkout saga** | `checkout-saga` | `CorrelationId` | Payment saga failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentFailedEvent.avsc` (existing) |
| `PaymentRefundedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | Checkout saga (cancel-post-capture path), Notifications | `checkout-saga`, `notifications` | `CorrelationId` | Payments refund success | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentRefundedEvent.avsc` (existing) |
| `PaymentRequestedEvent` | `payments.transactions` | `Payments.Transactions` | Checkout saga | PaymentProcessingSaga | `payment-saga` | `CorrelationId` | Checkout saga step | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentRequestedEvent.avsc` (existing) |
| `PaymentVoidedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga | `payment-saga-void` | `CorrelationId` | Payments void success | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentVoidedEvent.avsc` (existing) |
| `StockLevelChanged` | `inventory.stock-events` | `Inventory.Stock` | Inventory | Catalog (IsSellable projection) | `catalog-stock-level` | `ProductId` | Availability crosses 0↔positive | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Stock/StockLevelChanged.avsc` |
| `ReservationConfirmedEvent` | `inventory.reservations` | `Inventory.Reservations` | Inventory | Notifications (optional), Checkout saga (informational) | `notifications`, `checkout-saga` | `OrderId` | `ReservationConfirmedEvent` (ES internal) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReservationConfirmedEvent.avsc` |
| `ReservationReleasedEvent` | `inventory.reservations` | `Inventory.Reservations` | Inventory | Checkout saga (compensation confirmation) | `checkout-saga` | `OrderId` | `ReservationReleasedEvent` (ES internal) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReservationReleasedEvent.avsc` |
| `StockReservationFailedEvent` | `inventory.reservations` | `Inventory.Reservations` | Inventory | Checkout saga (triggers compensation) | `checkout-saga` | `OrderId` | `ReserveStockCommand` failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/StockReservationFailedEvent.avsc` |
| `StockReservedEvent` | `inventory.reservations` | `Inventory.Reservations` | Inventory | Checkout saga | `checkout-saga` | `OrderId` | `StockReservedEvent` (ES internal) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/StockReservedEvent.avsc` |
| `ConfirmReservationCommand` | `inventory.reservation-commands` | `Inventory.Reservations` | Checkout saga | Inventory | `inventory-reservation-commands` | `CorrelationId` | saga step (after payment) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ConfirmReservationCommand.avsc` |
| `ReleaseReservationCommand` | `inventory.reservation-commands` | `Inventory.Reservations` | Checkout saga (compensation) / Ops | Inventory | `inventory-reservation-commands` | `CorrelationId` | saga compensation / admin cancel | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReleaseReservationCommand.avsc` |
| `ReserveStockCommand` | `inventory.reservation-commands` | `Inventory.Reservations` | Checkout saga | Inventory | `inventory-reservation-commands` | `CorrelationId` | saga step (after order created) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReserveStockCommand.avsc` |
| `SendEmailNotificationCommand` | `notification.commands` | `Notifications.Email` | Notifications templates emitted by multiple services (existing pattern) | Notifications | `notifications-email` | `UserId` | various | `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/SendEmailNotificationCommand.avsc` (existing) |
| `OrderCancelledEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Notifications, Inventory (release if still reserved), Payments (refund if captured), BFF (cache invalidate), Checkout saga (compensation confirmation) | `notifications`, `inventory-stock-init` (reuses group? — see § 7), `checkout-saga`, `bff-order-cache`, `payments-refund-gateway` | `OrderId` | `OrderCancelledDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCancelledEvent.avsc` |
| `OrderConfirmedEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Notifications, BFF (cache invalidate), Checkout saga (terminal success) | `notifications`, `bff-order-cache`, `checkout-saga` | `OrderId` | `OrderConfirmedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderConfirmedEvent.avsc` |
| `OrderCreatedEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Checkout saga (drives next step: reserve stock), BFF (cache populate) | `checkout-saga`, `bff-order-cache` | `OrderId` | `OrderCreatedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCreatedEvent.avsc` |
| `OrderDeliveredEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Notifications | `notifications` | `OrderId` | `OrderDeliveredDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderDeliveredEvent.avsc` |
| `OrderFailedEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Notifications, BFF (cache invalidate), Checkout saga (terminal failure) | `notifications`, `bff-order-cache`, `checkout-saga` | `OrderId` | `OrderFailedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderFailedEvent.avsc` |
| `OrderShippedEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Notifications | `notifications` | `OrderId` | `OrderShippedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderShippedEvent.avsc` |
| `CancelOrderCommand` | `ordering.order-commands` | `Ordering.Orders` | Checkout saga (compensation) | Ordering | `ordering-order-commands` | `OrderId` | saga compensation | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/CancelOrderCommand.avsc` |
| `ConfirmOrderCommand` | `ordering.order-commands` | `Ordering.Orders` | Checkout saga | Ordering | `ordering-order-commands` | `OrderId` | saga step (after all greens) | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/ConfirmOrderCommand.avsc` |
| `CreateOrderCommand` | `ordering.order-commands` | `Ordering.Orders` | Checkout saga | Ordering | `ordering-order-commands` | `CorrelationId` | saga step (after basket checkout) | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/CreateOrderCommand.avsc` |
| `MarkOrderFailedCommand` | `ordering.order-commands` | `Ordering.Orders` | Checkout saga | Ordering | `ordering-order-commands` | `OrderId` | saga terminal-failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/MarkOrderFailedCommand.avsc` |

---

## 3. Kafka Topics

New topics introduced by this document. Existing topics (`notification.commands`, `healthchecks`) are **not** duplicated here. Topics `payments.transactions` + `payments.commands` are **renamed** from pre-eShop `payments.transactions` / `payments.commands` in Wave 0.

| Topic | Partitions | Retention | Key | Purpose | Events |
|-------|-----------|-----------|-----|---------|--------|
| `basket.sessions` | 3 | 30 days (delete) | `UserId` | Basket checkout hand-off to Checkout saga. Ephemeral by design (D-8). | `BasketCheckoutInitiatedEvent` |
| `catalog.categories` | 3 | Infinite (audit) | `CategoryId` | Category taxonomy events — BFF/search downstream | `CategoryCreatedEvent` |
| `catalog.products` | 3 | Infinite (audit) | `ProductId` | Product lifecycle events — Inventory/BFF/Basket downstream | `ProductCreatedEvent`, `ProductPriceChanged`, `ProductDiscontinuedEvent` |
| `inventory.reservation-commands` | 3 | 7 days (delete) | `CorrelationId` | Saga-issued imperative intent to Inventory | `ReserveStockCommand`, `ConfirmReservationCommand`, `ReleaseReservationCommand` |
| `inventory.reservations` | 6 | Infinite (audit) | `OrderId` | Reservation lifecycle — Checkout saga / Notifications downstream. 6 partitions because saga traffic is the heaviest on this topic (one command per order line). | `StockReservedEvent`, `StockReservationFailedEvent`, `ReservationConfirmedEvent`, `ReservationReleasedEvent` |
| `inventory.stock-events` | 3 | Infinite (audit) | `ProductId` | Stock-level threshold-crossing signals to Catalog | `StockLevelChanged` |
| `ordering.order-commands` | 3 | 7 days (delete) | `OrderId` (or `CorrelationId` for `CreateOrderCommand` which has no `OrderId` yet) | Saga-issued imperative intent to Ordering | `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand` |
| `ordering.orders` | 3 | Infinite (audit) | `OrderId` | Order lifecycle events — Checkout saga / Notifications / BFF downstream | `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderFailedEvent` |
| `payments.transactions` | 3 | Infinite (audit) | `CorrelationId` | Payment lifecycle events — Checkout saga / Notifications / Invoicing downstream. Renamed from `payments.transactions` in Wave 0. | `PaymentRequestedEvent`, `PaymentAuthorizedEvent`, `PaymentAuthorizationFailedEvent`, `PaymentCapturedEvent`, `PaymentCaptureFailedEvent`, `PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`, `PaymentVoidedEvent` |
| `payments.commands` | 3 | 7 days (delete) | `CorrelationId` | PaymentProcessingSaga → Payments imperative intent. Renamed from `payments.commands` in Wave 0. | `AuthorizePaymentCommand`, `CapturePaymentCommand`, `RequestRefundCommand`, `VoidPaymentCommand` |
| `invoicing.invoices` | 3 | **10 years (EU VAT)** | `BuyerId` | Invoice + credit note lifecycle. Retention reflects legal record-keeping norm (Czech Republic, Germany, France, Slovakia: 10-year). PII policy per ADR-0011 applies. | `InvoiceIssued`, `InvoiceDelivered`, `InvoiceCancelled`, `CreditNoteIssued` |

**Total new topics: 11.** The `notification.commands` topic already exists.

### 3.1 Why these partition counts

| Topic | Partition choice | Reasoning |
|-------|------------------|-----------|
| All 3-partition topics | 3 | Baseline v1 choice matching existing weather/payments topics; enough parallelism for demo-scale; easy to increase pre-GA |
| `inventory.reservations` = 6 | 6 | Order with N line items produces N `ReserveStockCommand` → N `StockReservedEvent`/`StockReservationFailedEvent` → N `ConfirmReservationCommand` → N `ReservationConfirmedEvent`. For even a modest 10 orders/sec with 5 items/order that's 200+ messages/sec on this topic. 2× headroom over the default. Keyed by `OrderId` so per-order ordering stays intact even with the higher fan-out. |

### 3.2 Why these retention choices

- **Event-log topics (infinite retention):** the master-design § 3 discipline and the ES-for-Inventory rationale (ADR-0006 § "Auditability as a first-class requirement") both depend on retained history. Compaction with no delete-retention preserves the latest per-key; but we want the full history, so we use `retention.ms=-1` (infinite).
- **Command topics (7 days):** commands are short-lived intent. A 7-day window covers saga retry exhaustion + forensic debugging. After that, a command that wasn't acted on is by definition stale.
- **Basket (30 days):** matches the Basket aggregate TTL (`basket.md § 5.3`). A checkout event older than its own basket window is inconsistent anyway.

### 3.3 Consumer groups

Consumer groups are named by **consumer intent**, not by producer. Rationale: a single service may consume one topic for multiple purposes (e.g., BFF consumes `catalog.products` both to warm cache AND to invalidate it), and each purpose needs its own offset. Naming scheme: `{service}-{intent}`.

Examples from § 2:
- `checkout-saga` — the Checkout saga state machine's consumer group. One offset across all topics it consumes.
- `notifications` — the Notifications service's consumer group (single, per service, since notifications render from events directly).
- `bff-product-cache`, `bff-order-cache`, `bff-category-tree` — distinct groups per cache domain inside the BFF.
- `inventory-stock-init` — the Inventory consumer that turns `ProductCreatedEvent` into an `InitializeStockItemCommand`.
- `catalog-stock-level` — the Catalog consumer that turns `StockLevelChanged` into an `IsSellable` projection update.

---

## 4. Docker-compose Delta

Append to `docker-compose.yaml` inside the `kafka-create-topic` command block, immediately **after** line 246 (`--topic healthchecks ...`) and **before** the closing `"` on line 247. Order: catalog → basket → ordering → inventory. Every line ends with `&& \` (or `&&` if inside a YAML `>` block — match the exact syntax already in use there, which uses `&&` with YAML multi-line folding).

```yaml
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic catalog.products --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic catalog.categories --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic basket.sessions --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=2592000000 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic ordering.orders --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic ordering.order-commands --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=604800000 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic inventory.stock-events --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic inventory.reservations --partitions 6 --replication-factor 1 --config min.insync.replicas=1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic inventory.reservation-commands --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=604800000 --if-not-exists
```

### 4.1 About `--config retention.ms`

- `2592000000` ms = 30 days (basket.sessions).
- `604800000` ms = 7 days (command topics).
- Topics without `--config retention.ms` inherit the broker default, which Confluent 7.5 ships at 7 days (`log.retention.hours=168`). For the infinite-retention event-log topics (`catalog.products`, `catalog.categories`, `ordering.orders`, `inventory.stock-events`, `inventory.reservations`) this is **too short**. They need an explicit long retention. Two options:
  - **Option (preferred for v1 reference):** add `--config retention.ms=-1` to each event-log topic. This sets infinite retention at topic-create time.
  - **Option (cluster-wide):** bump `log.retention.hours` in the broker env. Rejected — it affects every topic including legacy Weather ones.

**Revised delta (authoritative — use this block, not the one above):**

```yaml
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic catalog.products --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=-1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic catalog.categories --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=-1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic basket.sessions --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=2592000000 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic ordering.orders --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=-1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic ordering.order-commands --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=604800000 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic inventory.stock-events --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=-1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic inventory.reservations --partitions 6 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=-1 --if-not-exists &&
        /usr/bin/kafka-topics --bootstrap-server kafka:9092 --create --topic inventory.reservation-commands --partitions 3 --replication-factor 1 --config min.insync.replicas=1 --config retention.ms=604800000 --if-not-exists
      "
```

The final `"` closes the multi-line bash command. Implementation agents: when inserting, preserve the closing `"` at the right indentation so `kafka-create-topic.command` remains a valid YAML scalar.

---

## 5. Avro Schemas

Every schema listed below is the **complete** content of the `.avsc` file to be materialized. Implementation agents copy the JSON verbatim (no transformation) into the indicated file path. Schemas in § 5.1 – 5.4 were already specified in Stage 1 outputs and are reproduced verbatim. Schemas in § 5.5 – 5.6 are **new**, introduced by this document.

### 5.1 Catalog External Events

#### 5.1.1 `ProductCreatedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductCreatedEvent.avsc`

```json
{
    "type": "record",
    "name": "ProductCreatedEvent",
    "namespace": "Catalog.Products",
    "doc": "Event emitted when a new product is created in the Catalog. Enriched summary carrying all information downstream services need to initialize their own projections without calling back into Catalog.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the product aggregate."
        },
        {
            "name": "Sku",
            "type": "string",
            "doc": "Business key for the product (1-32 chars, alphanumeric + dashes, uppercase)."
        },
        {
            "name": "Name",
            "type": "string",
            "doc": "Product display name (max 200 chars)."
        },
        {
            "name": "Description",
            "type": "string",
            "doc": "Product description truncated to 1000 chars for transport. Consumers requiring full text must fetch via Catalog query API."
        },
        {
            "name": "CategoryId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Identifier of the category to which the product is assigned."
        },
        {
            "name": "CategoryPath",
            "type": "string",
            "doc": "Materialized category path (e.g., '/electronics/computers/laptops'). Enables prefix filtering downstream without a Catalog lookup."
        },
        {
            "name": "BrandName",
            "type": "string",
            "doc": "Brand name (max 100 chars)."
        },
        {
            "name": "PriceAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Product price amount."
        },
        {
            "name": "PriceCurrency",
            "type": "string",
            "doc": "ISO 4217 currency code (e.g., 'USD', 'EUR')."
        },
        {
            "name": "Status",
            "type": {
                "type": "enum",
                "name": "ProductStatus",
                "symbols": [
                    "Draft",
                    "Active",
                    "Discontinued"
                ]
            },
            "doc": "Product lifecycle status at the time of the event."
        },
        {
            "name": "CreatedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the product was created."
        }
    ]
}
```

#### 5.1.2 `ProductPriceChanged.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductPriceChanged.avsc`

```json
{
    "type": "record",
    "name": "ProductPriceChanged",
    "namespace": "Catalog.Products",
    "doc": "Event emitted when a product's price is changed. Carries both old and new price so downstream consumers can detect magnitude of change without a prior snapshot.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the product whose price changed."
        },
        {
            "name": "Sku",
            "type": "string",
            "doc": "Business key of the product (denormalized for consumer convenience)."
        },
        {
            "name": "OldPriceAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Price amount before the change."
        },
        {
            "name": "NewPriceAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Price amount after the change."
        },
        {
            "name": "Currency",
            "type": "string",
            "doc": "ISO 4217 currency code. Same for old and new (Catalog does not support currency swap on a product)."
        },
        {
            "name": "ChangedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the price change was recorded."
        }
    ]
}
```

#### 5.1.3 `ProductDiscontinuedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductDiscontinuedEvent.avsc`

```json
{
    "type": "record",
    "name": "ProductDiscontinuedEvent",
    "namespace": "Catalog.Products",
    "doc": "Event emitted when a product is moved to the Discontinued status. Downstream services should stop offering this product for new purchases.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the product that was discontinued."
        },
        {
            "name": "Sku",
            "type": "string",
            "doc": "Business key of the product (denormalized for consumer convenience)."
        },
        {
            "name": "Reason",
            "type": "string",
            "doc": "Free-text reason supplied by the operator (non-empty). Informational only."
        },
        {
            "name": "DiscontinuedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the product was discontinued."
        }
    ]
}
```

#### 5.1.4 `CategoryCreatedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Categories/CategoryCreatedEvent.avsc`

```json
{
    "type": "record",
    "name": "CategoryCreatedEvent",
    "namespace": "Catalog.Categories",
    "doc": "Event emitted when a new category node is created in the Catalog taxonomy.",
    "fields": [
        {
            "name": "CategoryId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the category."
        },
        {
            "name": "Name",
            "type": "string",
            "doc": "Category display name (max 100 chars)."
        },
        {
            "name": "ParentCategoryId",
            "type": [
                "null",
                {
                    "type": "string",
                    "logicalType": "uuid"
                }
            ],
            "default": null,
            "doc": "Optional identifier of the parent category. Null for root nodes."
        },
        {
            "name": "Path",
            "type": "string",
            "doc": "Materialized path of this category (e.g., '/electronics/computers'). Depth 1 to 5 segments."
        },
        {
            "name": "CreatedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the category was created."
        }
    ]
}
```

### 5.2 Basket External Events

#### 5.2.1 `BasketCheckoutInitiatedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc`

```json
{
    "type": "record",
    "name": "BasketCheckoutInitiatedEvent",
    "namespace": "Basket.Sessions",
    "doc": "Emitted when a user checks out their basket. Triggers the Checkout Saga. The basket is deleted from Redis after this event is written to the outbox.",
    "fields": [
        {
            "name": "BasketCorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Correlation identifier for the checkout flow. Becomes the CorrelationId of the downstream Checkout Saga state machine. Generated when CheckoutBasketCommand is invoked."
        },
        {
            "name": "UserId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Identifier of the user whose basket is being checked out. Also the Kafka message key for partitioning."
        },
        {
            "name": "Items",
            "type": {
                "type": "array",
                "items": {
                    "type": "record",
                    "name": "BasketCheckoutItem",
                    "namespace": "Basket.Sessions",
                    "doc": "One line of the basket at the moment of checkout. Prices reflect the snapshot captured at add-time (or at the last explicit refresh).",
                    "fields": [
                        {
                            "name": "ProductId",
                            "type": {
                                "type": "string",
                                "logicalType": "uuid"
                            },
                            "doc": "Catalog Product identifier. Consumers use this to reserve stock and to load authoritative product data."
                        },
                        {
                            "name": "Sku",
                            "type": "string",
                            "doc": "Catalog SKU at the time of checkout. Copied from the snapshot for consumer convenience."
                        },
                        {
                            "name": "Name",
                            "type": "string",
                            "doc": "Product name at the time of checkout. Copied from the snapshot for order history/display."
                        },
                        {
                            "name": "UnitPriceAmount",
                            "type": {
                                "type": "bytes",
                                "logicalType": "decimal",
                                "precision": 19,
                                "scale": 4
                            },
                            "doc": "Snapshot unit price amount. Decimal 19,4 matches the Catalog price precision."
                        },
                        {
                            "name": "UnitPriceCurrency",
                            "type": "string",
                            "doc": "ISO 4217 currency code of UnitPriceAmount. Uniform across all items (enforced by the Basket aggregate invariant)."
                        },
                        {
                            "name": "Quantity",
                            "type": "int",
                            "doc": "Number of units of this product in the basket. Always >= 1."
                        },
                        {
                            "name": "LineTotal",
                            "type": {
                                "type": "bytes",
                                "logicalType": "decimal",
                                "precision": 19,
                                "scale": 4
                            },
                            "doc": "UnitPriceAmount * Quantity, pre-computed for consumer convenience. Equals the domain value of the line at checkout."
                        }
                    ]
                }
            },
            "doc": "All line items of the basket at the moment of checkout. Never empty (empty-basket checkout is rejected at the aggregate)."
        },
        {
            "name": "TotalAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Sum of all LineTotal values. Pre-computed so consumers do not have to re-sum; they SHOULD re-verify."
        },
        {
            "name": "Currency",
            "type": "string",
            "doc": "ISO 4217 currency code for TotalAmount. Equals all item UnitPriceCurrency values."
        },
        {
            "name": "ShippingAddress",
            "type": {
                "type": "record",
                "name": "CheckoutAddress",
                "namespace": "Basket.Sessions",
                "doc": "Postal address supplied at checkout. Basket is a pass-through: it does not validate deeply (only non-empty + ISO country code) and does not persist addresses beyond this event. The Ordering service re-snapshots this into the Order aggregate.",
                "fields": [
                    { "name": "Street1", "type": "string", "doc": "Primary street line." },
                    { "name": "Street2", "type": ["null", "string"], "default": null, "doc": "Optional second street line (apartment, suite, etc.)." },
                    { "name": "City", "type": "string", "doc": "City name." },
                    { "name": "State", "type": ["null", "string"], "default": null, "doc": "Optional state/province/region. Null for countries without this concept." },
                    { "name": "PostalCode", "type": "string", "doc": "Postal or ZIP code." },
                    { "name": "CountryCode", "type": "string", "doc": "ISO 3166-1 alpha-2 country code (e.g., 'US', 'CZ')." }
                ]
            },
            "doc": "Shipping address collected by the BFF/client at checkout time and passed through the CheckoutBasketCommand. Basket does NOT own addresses; it carries this payload to the saga unchanged."
        },
        {
            "name": "BillingAddress",
            "type": "Basket.Sessions.CheckoutAddress",
            "doc": "Billing address. Same shape as ShippingAddress; may be identical to it."
        },
        {
            "name": "PaymentMethodId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Reference to a saved payment method in the Payments service. Collected at checkout by the BFF/client; passed through unchanged. Validation is delegated to Payments during payment."
        },
        {
            "name": "InitiatedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the CheckoutBasketCommand was handled and the domain event was raised."
        }
    ]
}
```

### 5.3 Ordering External Events

> **Note on shared enum `OrderStatusAtTransition`:** `ordering.md` § 7.2.3 declares the enum inline inside `OrderCancelledEvent` and references it by FQN inside `OrderFailedEvent`. Avro supports cross-file enum reference only when both files are submitted to the same Schema Registry subject-group at compile time — which our build **does not** guarantee. To keep schemas self-contained and compilable independently, this catalog **re-declares** the enum inline in each file (with identical symbols). Implementation agents: use the schemas below verbatim, do NOT attempt cross-file `"type": "Ordering.Orders.OrderStatusAtTransition"` reference.

#### 5.3.1 `OrderCreatedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCreatedEvent.avsc`

```json
{
    "type": "record",
    "name": "OrderCreatedEvent",
    "namespace": "Ordering.Orders",
    "doc": "Emitted when a new Order is created from a Basket checkout. Starts the Checkout saga instance.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order."
        },
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id."
        },
        {
            "name": "BuyerId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "User who placed the order (JWT sub)."
        },
        {
            "name": "Items",
            "type": {
                "type": "array",
                "items": {
                    "type": "record",
                    "name": "OrderItemCreated",
                    "namespace": "Ordering.Orders",
                    "fields": [
                        {
                            "name": "ProductId",
                            "type": {
                                "type": "string",
                                "logicalType": "uuid"
                            },
                            "doc": "Catalog product identifier for this line."
                        },
                        {
                            "name": "Sku",
                            "type": "string",
                            "doc": "Catalog SKU snapshot at order creation time."
                        },
                        {
                            "name": "Name",
                            "type": "string",
                            "doc": "Product display name snapshot at order creation time."
                        },
                        {
                            "name": "Quantity",
                            "type": "int",
                            "doc": "Quantity of this line (>= 1)."
                        },
                        {
                            "name": "UnitPriceAmount",
                            "type": {
                                "type": "bytes",
                                "logicalType": "decimal",
                                "precision": 19,
                                "scale": 4
                            },
                            "doc": "Per-unit price amount."
                        },
                        {
                            "name": "LineTotalAmount",
                            "type": {
                                "type": "bytes",
                                "logicalType": "decimal",
                                "precision": 19,
                                "scale": 4
                            },
                            "doc": "UnitPriceAmount * Quantity, pre-computed."
                        }
                    ]
                }
            },
            "doc": "Order line items with frozen product snapshots and prices."
        },
        {
            "name": "TotalAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Total order amount (sum of LineTotalAmount)."
        },
        {
            "name": "Currency",
            "type": "string",
            "doc": "ISO 4217 currency code shared by all items."
        },
        {
            "name": "PaymentMethodId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Payments-side payment method reference."
        },
        {
            "name": "CreatedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the order was created."
        }
    ]
}
```

#### 5.3.2 `OrderConfirmedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderConfirmedEvent.avsc`

> **Summary Event** per [ADR-0020](../adr/0020-summary-events.md) — carries the order's full state at the confirmation transition (`Items`, `TotalAmount`, `Currency`, `BillingAddress`) so Invoicing's M7 handler (10-year retention) can rebuild state without an HTTP round-trip to Ordering. The four enrichment fields are nullable / defaulted for FORWARD_TRANSITIVE compatibility per [ADR-0007](../adr/0007-avro-compatibility-modes.md); production producers always populate them.

```json
{
    "type": "record",
    "name": "OrderConfirmedEvent",
    "namespace": "Ordering.Orders",
    "doc": "Summary Event (per ADR-0020) emitted when the Order is confirmed (stock reserved AND payment captured). Carries the full aggregate snapshot — Items, TotalAmount, Currency, BillingAddress — so downstream consumers (notably Invoicing under 10-year retention) can rebuild state without an HTTP round-trip to Ordering. The four enrichment fields are nullable / defaulted for FORWARD_TRANSITIVE compatibility per ADR-0007; production producers always populate them.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order that was confirmed."
        },
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id. Consumers can correlate this event with the saga run."
        },
        {
            "name": "BuyerId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "User who placed the order."
        },
        {
            "name": "ConfirmedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the order was confirmed."
        },
        {
            "name": "Items",
            "type": {
                "type": "array",
                "items": {
                    "type": "record",
                    "name": "OrderItemConfirmed",
                    "namespace": "Ordering.Orders",
                    "doc": "One confirmed-order line. Mirrors the OrderItemCreated shape from OrderCreatedEvent.avsc (different name to avoid Avro record-name collision in namespace 'Ordering.Orders'). Frozen at confirmation per Order invariant I-2.",
                    "fields": [
                        { "name": "ProductId", "type": { "type": "string", "logicalType": "uuid" }, "doc": "Catalog product identifier for this line." },
                        { "name": "Sku", "type": "string", "doc": "Catalog SKU snapshot at order creation time." },
                        { "name": "Name", "type": "string", "doc": "Product display name snapshot at order creation time." },
                        { "name": "Quantity", "type": "int", "doc": "Quantity of this line (>= 1)." },
                        { "name": "UnitPriceAmount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 }, "doc": "Per-unit price amount." },
                        { "name": "LineTotalAmount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 }, "doc": "UnitPriceAmount * Quantity, pre-computed." }
                    ]
                }
            },
            "default": [],
            "doc": "Order line items with frozen product snapshots and prices. Empty default exists for FORWARD_TRANSITIVE compatibility with the v1 schema; production producers always populate at least one item per Order invariant I-7."
        },
        {
            "name": "TotalAmount",
            "type": [ "null", { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 } ],
            "default": null,
            "doc": "Total order amount (sum of OrderItemConfirmed.LineTotalAmount). Nullable union for FORWARD_TRANSITIVE compatibility with the v1 schema (Avro decimal defaults are encoding-fragile per ADR-0020); production producers always populate."
        },
        {
            "name": "Currency",
            "type": [ "null", "string" ],
            "default": null,
            "doc": "ISO 4217 currency code shared by all items. Nullable union covaries with TotalAmount; production producers always populate."
        },
        {
            "name": "BillingAddress",
            "type": [
                "null",
                {
                    "type": "record",
                    "name": "OrderBillingAddress",
                    "namespace": "Ordering.Orders",
                    "doc": "Snapshot of the buyer's billing address at confirmation time. Field shape is identical to Basket.Sessions.CheckoutAddress; defined locally because avrogen processes each .avsc file in isolation and a cross-file reference would emit a class collision (see ADR-0020).",
                    "fields": [
                        { "name": "Street1", "type": "string", "doc": "Primary street line." },
                        { "name": "Street2", "type": [ "null", "string" ], "default": null, "doc": "Optional second street line (apartment, suite, etc.)." },
                        { "name": "City", "type": "string", "doc": "City name." },
                        { "name": "State", "type": [ "null", "string" ], "default": null, "doc": "Optional state/province/region. Null for countries without this concept." },
                        { "name": "PostalCode", "type": "string", "doc": "Postal or ZIP code." },
                        { "name": "CountryCode", "type": "string", "doc": "ISO 3166-1 alpha-2 country code (e.g., 'US', 'CZ')." }
                    ]
                }
            ],
            "default": null,
            "doc": "Buyer's billing address snapshot. Consumed by Invoicing for invoice generation."
        }
    ]
}
```

#### 5.3.3 `OrderCancelledEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCancelledEvent.avsc`

> **Summary Event** per [ADR-0020](../adr/0020-summary-events.md) (Wave 1.6 promotion) — carries the order's state at the cancellation transition (`Items`, `TotalAmount`, `Currency`, `BillingAddress`) alongside the original `Reason` / `AtStatus` delta payload, so Invoicing's M8 credit-note handler (10-year retention) can rebuild state without an HTTP round-trip to Ordering. The four enrichment fields are nullable / defaulted for FORWARD_TRANSITIVE compatibility per [ADR-0007](../adr/0007-avro-compatibility-modes.md); production producers always populate them. Compensation consumers (Inventory, Payments, Notifications, BFF, checkout saga) keep reading only the `Reason` / `AtStatus` fields they already used.

```json
{
    "type": "record",
    "name": "OrderCancelledEvent",
    "namespace": "Ordering.Orders",
    "doc": "Summary Event (per ADR-0020) emitted when the Order is cancelled. Carries the aggregate snapshot at the cancellation transition — Items, TotalAmount, Currency, BillingAddress — so downstream consumers (notably Invoicing's credit-note path under 10-year retention) can rebuild state without an HTTP round-trip back to Ordering. The four enrichment fields are nullable / defaulted for FORWARD_TRANSITIVE compatibility per ADR-0007; production producers always populate them. Downstream compensation consumers (Inventory release, Payments refund, Notifications, BFF cache, checkout saga) continue to read only the Reason / AtStatus delta payload they already used.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order that was cancelled."
        },
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id. Used by the saga to correlate compensation flows."
        },
        {
            "name": "BuyerId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "User who placed the order."
        },
        {
            "name": "Reason",
            "type": "string",
            "doc": "Human- or system-assigned cancellation reason."
        },
        {
            "name": "AtStatus",
            "type": {
                "type": "enum",
                "name": "OrderStatusAtTransition",
                "namespace": "Ordering.Orders",
                "symbols": [
                    "Created",
                    "StockReserved",
                    "PaymentCompleted",
                    "Confirmed"
                ]
            },
            "doc": "OrderStatus just before cancellation. Informs consumers what compensation to perform (release stock, refund, etc.)."
        },
        {
            "name": "CancelledAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the order was cancelled."
        },
        {
            "name": "Items",
            "type": {
                "type": "array",
                "items": {
                    "type": "record",
                    "name": "OrderItemCancelled",
                    "namespace": "Ordering.Orders",
                    "doc": "One cancelled-order line. Mirrors the OrderItemConfirmed shape from OrderConfirmedEvent.avsc (different name to avoid an avrogen per-file class collision in namespace 'Ordering.Orders' — see ADR-0020 § Implementation Notes). Frozen at cancellation per Order invariant I-2.",
                    "fields": [
                        { "name": "ProductId", "type": { "type": "string", "logicalType": "uuid" }, "doc": "Catalog product identifier for this line." },
                        { "name": "Sku", "type": "string", "doc": "Catalog SKU snapshot at order creation time." },
                        { "name": "Name", "type": "string", "doc": "Product display name snapshot at order creation time." },
                        { "name": "Quantity", "type": "int", "doc": "Quantity of this line (>= 1)." },
                        { "name": "UnitPriceAmount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 }, "doc": "Per-unit price amount." },
                        { "name": "LineTotalAmount", "type": { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 }, "doc": "UnitPriceAmount * Quantity, pre-computed." }
                    ]
                }
            },
            "default": [],
            "doc": "Order line items with frozen product snapshots and prices. Empty default exists for FORWARD_TRANSITIVE compatibility with the v1 (pre-Wave-1.6) schema; production producers always populate at least one item per Order invariant I-7."
        },
        {
            "name": "TotalAmount",
            "type": [ "null", { "type": "bytes", "logicalType": "decimal", "precision": 19, "scale": 4 } ],
            "default": null,
            "doc": "Total order amount (sum of OrderItemCancelled.LineTotalAmount). Nullable union for FORWARD_TRANSITIVE compatibility with the v1 schema (Avro decimal defaults are encoding-fragile per ADR-0020); production producers always populate."
        },
        {
            "name": "Currency",
            "type": [ "null", "string" ],
            "default": null,
            "doc": "ISO 4217 currency code shared by all items. Nullable union covaries with TotalAmount; production producers always populate."
        },
        {
            "name": "BillingAddress",
            "type": [
                "null",
                {
                    "type": "record",
                    "name": "OrderCancellationBillingAddress",
                    "namespace": "Ordering.Orders",
                    "doc": "Snapshot of the buyer's billing address at cancellation time. Field shape is identical to Basket.Sessions.CheckoutAddress and Wave 1.5's Ordering.Orders.OrderBillingAddress; defined locally because avrogen processes each .avsc file in isolation and a cross-file reference would emit a class collision (see ADR-0020).",
                    "fields": [
                        { "name": "Street1", "type": "string", "doc": "Primary street line." },
                        { "name": "Street2", "type": [ "null", "string" ], "default": null, "doc": "Optional second street line (apartment, suite, etc.)." },
                        { "name": "City", "type": "string", "doc": "City name." },
                        { "name": "State", "type": [ "null", "string" ], "default": null, "doc": "Optional state/province/region. Null for countries without this concept." },
                        { "name": "PostalCode", "type": "string", "doc": "Postal or ZIP code." },
                        { "name": "CountryCode", "type": "string", "doc": "ISO 3166-1 alpha-2 country code (e.g., 'US', 'CZ')." }
                    ]
                }
            ],
            "default": null,
            "doc": "Buyer's billing address snapshot. Consumed by Invoicing for credit-note generation."
        }
    ]
}
```

#### 5.3.4 `OrderShippedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderShippedEvent.avsc`

```json
{
    "type": "record",
    "name": "OrderShippedEvent",
    "namespace": "Ordering.Orders",
    "doc": "Emitted when the Order is handed to a carrier with a tracking number.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order that was shipped."
        },
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id (carried post-saga for forensic continuity; saga itself is finalised by this point)."
        },
        {
            "name": "BuyerId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "User who placed the order."
        },
        {
            "name": "Carrier",
            "type": "string",
            "doc": "Shipping carrier name (e.g., 'FedEx', 'DHL', 'UPS')."
        },
        {
            "name": "TrackingNumber",
            "type": "string",
            "doc": "Carrier-assigned tracking number."
        },
        {
            "name": "ShippedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the order was shipped."
        }
    ]
}
```

#### 5.3.5 `OrderDeliveredEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderDeliveredEvent.avsc`

```json
{
    "type": "record",
    "name": "OrderDeliveredEvent",
    "namespace": "Ordering.Orders",
    "doc": "Emitted when the carrier confirms delivery. Terminal happy-path event for the order lifecycle.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order that was delivered."
        },
        {
            "name": "BuyerId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "User who placed the order."
        },
        {
            "name": "DeliveredAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the order was delivered."
        }
    ]
}
```

#### 5.3.6 `OrderFailedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderFailedEvent.avsc`

```json
{
    "type": "record",
    "name": "OrderFailedEvent",
    "namespace": "Ordering.Orders",
    "doc": "Emitted when the Order transitions to a terminal Failed state. Downstream consumers notify the buyer and reverse any applied compensations.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order that failed."
        },
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id."
        },
        {
            "name": "BuyerId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "User who placed the order."
        },
        {
            "name": "ErrorCode",
            "type": "string",
            "doc": "Machine-readable error code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED, PAYMENT_TIMEOUT, CONFIRMATION_TIMEOUT)."
        },
        {
            "name": "ErrorMessage",
            "type": "string",
            "doc": "Human-readable error message."
        },
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
            "doc": "OrderStatus just before failure."
        },
        {
            "name": "FailedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the order failed."
        }
    ]
}
```

### 5.4 Inventory External Events

#### 5.4.1 `StockLevelChanged.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Stock/StockLevelChanged.avsc`

```json
{
    "type": "record",
    "name": "StockLevelChanged",
    "namespace": "Inventory.Stock",
    "doc": "Emitted when a StockItem's availability crosses a business-meaningful threshold (e.g., zero to positive or positive to zero). Not emitted on every stock change.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
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
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp of the triggering event."
        }
    ]
}
```

#### 5.4.2 `StockReservedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/StockReservedEvent.avsc`

```json
{
    "type": "record",
    "name": "StockReservedEvent",
    "namespace": "Inventory.Reservations",
    "doc": "Emitted when units have been successfully reserved for an order. Consumed by the checkout saga as the positive outcome of ReserveStockCommand. The reservation is time-bounded by ExpiresAtUtc.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Product that was reserved. Shared key with Catalog."
        },
        {
            "name": "ReservationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique id of this reservation. Used by ConfirmReservationCommand and ReleaseReservationCommand."
        },
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Owning order (saga correlation id). Enables fan-in of multiple line-item reservations per order."
        },
        {
            "name": "Quantity",
            "type": "int",
            "doc": "Units reserved."
        },
        {
            "name": "ExpiresAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp after which the reservation is automatically released by the TTL worker unless confirmed or explicitly released."
        },
        {
            "name": "ReservedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the reservation was created."
        }
    ]
}
```

#### 5.4.3 `StockReservationFailedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/StockReservationFailedEvent.avsc`

```json
{
    "type": "record",
    "name": "StockReservationFailedEvent",
    "namespace": "Inventory.Reservations",
    "doc": "Emitted when a reservation request cannot be fulfilled because Available < RequestedQuantity. Consumed by the checkout saga to trigger compensation. No corresponding ES event (failed reservations do not mutate the aggregate).",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Product for which the reservation failed."
        },
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
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
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the reservation attempt was rejected."
        }
    ]
}
```

#### 5.4.4 `ReservationConfirmedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReservationConfirmedEvent.avsc`

```json
{
    "type": "record",
    "name": "ReservationConfirmedEvent",
    "namespace": "Inventory.Reservations",
    "doc": "Emitted when a reservation is confirmed (stock physically committed to the order). OnHand is decremented by Quantity as part of the same transaction.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Product whose reservation was confirmed."
        },
        {
            "name": "ReservationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Reservation that was confirmed."
        },
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Owning order."
        },
        {
            "name": "ConfirmedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp of confirmation."
        }
    ]
}
```

#### 5.4.5 `ReservationReleasedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReservationReleasedEvent.avsc`

```json
{
    "type": "record",
    "name": "ReservationReleasedEvent",
    "namespace": "Inventory.Reservations",
    "doc": "Emitted when a reservation is released without being shipped. ReleaseReason distinguishes deliberate compensation (saga rollback), automatic expiry (TTL worker), and explicit customer/operator cancellation.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Product whose reservation was released."
        },
        {
            "name": "ReservationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Reservation that was released."
        },
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Owning order."
        },
        {
            "name": "ReleaseReason",
            "type": {
                "type": "enum",
                "name": "ReleaseReason",
                "symbols": [
                    "Compensation",
                    "Expiry",
                    "Cancellation"
                ]
            },
            "doc": "Compensation = saga rollback. Expiry = TTL worker auto-release. Cancellation = customer or ops explicit action."
        },
        {
            "name": "ReleasedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp of release."
        }
    ]
}
```

### 5.5 Saga-to-Ordering Commands

These four commands are published by the Checkout saga to `ordering.order-commands`. The Ordering service consumes them through its inbox (§ 7) and invokes the corresponding aggregate methods (see `ordering.md § 9.1`).

#### 5.5.1 `CreateOrderCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/CreateOrderCommand.avsc`

```json
{
    "type": "record",
    "name": "CreateOrderCommand",
    "namespace": "Ordering.Orders",
    "doc": "Command issued by the Checkout saga to create a new Order from a BasketSnapshot. Ordering responds by persisting the Order aggregate and publishing OrderCreatedEvent. The saga correlates the resulting OrderCreatedEvent by CorrelationId.",
    "fields": [
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id. Becomes Order.CorrelationId. Primary saga correlation key."
        },
        {
            "name": "BuyerId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "JWT sub claim of the buyer. Captured by the saga at basket-checkout time."
        },
        {
            "name": "Items",
            "type": {
                "type": "array",
                "items": {
                    "type": "record",
                    "name": "CreateOrderItem",
                    "namespace": "Ordering.Orders",
                    "doc": "One line of the order at creation time. Prices are frozen at checkout.",
                    "fields": [
                        {
                            "name": "ProductId",
                            "type": {
                                "type": "string",
                                "logicalType": "uuid"
                            },
                            "doc": "Catalog product identifier."
                        },
                        {
                            "name": "Sku",
                            "type": "string",
                            "doc": "Catalog SKU snapshot."
                        },
                        {
                            "name": "Name",
                            "type": "string",
                            "doc": "Product display name snapshot."
                        },
                        {
                            "name": "UnitPriceAmount",
                            "type": {
                                "type": "bytes",
                                "logicalType": "decimal",
                                "precision": 19,
                                "scale": 4
                            },
                            "doc": "Per-unit price at checkout."
                        },
                        {
                            "name": "UnitPriceCurrency",
                            "type": "string",
                            "doc": "ISO 4217 currency code. Uniform across all items."
                        },
                        {
                            "name": "Quantity",
                            "type": "int",
                            "doc": "Quantity of units (>= 1)."
                        }
                    ]
                }
            },
            "doc": "Order line items. Non-empty; rejected by the Order aggregate factory if empty."
        },
        {
            "name": "ShippingAddress",
            "type": {
                "type": "record",
                "name": "OrderAddress",
                "namespace": "Ordering.Orders",
                "doc": "Postal address for shipping or billing.",
                "fields": [
                    {
                        "name": "Street1",
                        "type": "string",
                        "doc": "Primary street line (max 200 chars, required)."
                    },
                    {
                        "name": "Street2",
                        "type": [
                            "null",
                            "string"
                        ],
                        "default": null,
                        "doc": "Secondary street line (max 200 chars, optional)."
                    },
                    {
                        "name": "City",
                        "type": "string",
                        "doc": "City (max 100 chars, required)."
                    },
                    {
                        "name": "State",
                        "type": [
                            "null",
                            "string"
                        ],
                        "default": null,
                        "doc": "State or province (max 100 chars, optional for countries that do not use one)."
                    },
                    {
                        "name": "PostalCode",
                        "type": "string",
                        "doc": "Postal or ZIP code (max 20 chars, required)."
                    },
                    {
                        "name": "CountryCode",
                        "type": "string",
                        "doc": "ISO 3166-1 alpha-2 country code (exactly 2 uppercase letters, required)."
                    }
                ]
            },
            "doc": "Address to ship the order to."
        },
        {
            "name": "BillingAddress",
            "type": "Ordering.Orders.OrderAddress",
            "doc": "Address for billing. Same shape as ShippingAddress; reuses the OrderAddress record."
        },
        {
            "name": "PaymentMethodId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Payments-side payment method reference. Opaque to Ordering."
        },
        {
            "name": "RequestedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the saga issued the CreateOrderCommand."
        }
    ]
}
```

#### 5.5.2 `ConfirmOrderCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/ConfirmOrderCommand.avsc`

```json
{
    "type": "record",
    "name": "ConfirmOrderCommand",
    "namespace": "Ordering.Orders",
    "doc": "Command issued by the Checkout saga to confirm an Order after stock reservation and payment have both succeeded. Ordering transitions PaymentCompleted -> Confirmed and emits OrderConfirmedEvent.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order to confirm. Also the Kafka message key."
        },
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id."
        },
        {
            "name": "RequestedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the saga issued the command."
        }
    ]
}
```

#### 5.5.3 `CancelOrderCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/CancelOrderCommand.avsc`

```json
{
    "type": "record",
    "name": "CancelOrderCommand",
    "namespace": "Ordering.Orders",
    "doc": "Command issued by the Checkout saga (or ops tooling) to cancel an Order. Ordering transitions to Cancelled and emits OrderCancelledEvent, which downstream consumers use to release stock and refund if captured.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order to cancel. Also the Kafka message key."
        },
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id. Present for admin-driven cancellations too (null-sentinel omitted for v1)."
        },
        {
            "name": "Reason",
            "type": "string",
            "doc": "Human- or system-assigned cancellation reason (max 500 chars). Propagates to OrderCancelledEvent.Reason."
        },
        {
            "name": "RequestedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the cancellation was requested."
        }
    ]
}
```

#### 5.5.4 `MarkOrderFailedCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/MarkOrderFailedCommand.avsc`

```json
{
    "type": "record",
    "name": "MarkOrderFailedCommand",
    "namespace": "Ordering.Orders",
    "doc": "Command issued by the Checkout saga to mark an Order as terminally Failed when a saga step times out or cannot be completed (e.g., stock reservation failed, payment failed). Ordering transitions to Failed and emits OrderFailedEvent.",
    "fields": [
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the Order to fail. Also the Kafka message key."
        },
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id."
        },
        {
            "name": "ErrorCode",
            "type": "string",
            "doc": "Machine-readable error code (e.g., STOCK_UNAVAILABLE, PAYMENT_FAILED, PAYMENT_TIMEOUT, CONFIRMATION_TIMEOUT)."
        },
        {
            "name": "ErrorMessage",
            "type": "string",
            "doc": "Human-readable error message. Propagates to OrderFailedEvent.ErrorMessage."
        },
        {
            "name": "RequestedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the saga issued the command."
        }
    ]
}
```

### 5.6 Saga-to-Inventory Commands

Three commands published by the Checkout saga to `inventory.reservation-commands`, consumed by the Inventory service's inbox.

**Decision recall (D-3):** one `ReserveStockCommand` per order line item. The saga fan-outs N commands (one per `ProductId`), and fan-ins on `OrderId` across N `StockReservedEvent` / `StockReservationFailedEvent` responses. Rationale: Inventory's event-sourced model keys streams on `ProductId` — one command = one stream append. A single `ReserveStockItemsCommand` with N items would require Inventory to span N streams in one transaction, which contradicts the per-stream consistency boundary from `inventory.md § 3.2`.

#### 5.6.1 `ReserveStockCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReserveStockCommand.avsc`

```json
{
    "type": "record",
    "name": "ReserveStockCommand",
    "namespace": "Inventory.Reservations",
    "doc": "Command issued by the Checkout saga to reserve N units of one product for an order. The saga issues one command per line item; Inventory responds asynchronously with StockReservedEvent on success or StockReservationFailedEvent on failure.",
    "fields": [
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id. Also the Kafka message key."
        },
        {
            "name": "OrderId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Order for which stock is being reserved. Used by Inventory to tag the reservation."
        },
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Product to reserve. Keys the event-sourced stream in Inventory."
        },
        {
            "name": "ReservationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Saga-generated reservation id (GUIDv7). Becomes the ReservationId on StockReservedEvent / ReservationConfirmedEvent / ReservationReleasedEvent."
        },
        {
            "name": "Quantity",
            "type": "int",
            "doc": "Units to reserve (>= 1)."
        },
        {
            "name": "RequestedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the saga issued the command."
        }
    ]
}
```

#### 5.6.2 `ConfirmReservationCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ConfirmReservationCommand.avsc`

```json
{
    "type": "record",
    "name": "ConfirmReservationCommand",
    "namespace": "Inventory.Reservations",
    "doc": "Command issued by the Checkout saga to confirm a reservation after payment has been captured. Inventory physically decrements OnHand by Quantity and emits ReservationConfirmedEvent. Idempotent on ReservationId.",
    "fields": [
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id. Also the Kafka message key."
        },
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Product whose reservation to confirm. Keys the event-sourced stream."
        },
        {
            "name": "ReservationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Reservation to confirm. Must refer to an Active reservation; confirming a non-Active reservation is a bug (DataIntegrityException)."
        },
        {
            "name": "RequestedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the saga issued the command."
        }
    ]
}
```

#### 5.6.3 `ReleaseReservationCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReleaseReservationCommand.avsc`

```json
{
    "type": "record",
    "name": "ReleaseReservationCommand",
    "namespace": "Inventory.Reservations",
    "doc": "Command to release an Active reservation without confirming it (saga compensation, TTL expiry, or admin cancel). Inventory emits ReservationReleasedEvent with the specified ReleaseReason.",
    "fields": [
        {
            "name": "CorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Checkout saga correlation id (or internally generated for TTL expiry / admin flows). Also the Kafka message key."
        },
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Product whose reservation to release."
        },
        {
            "name": "ReservationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Reservation to release. Must refer to an Active reservation."
        },
        {
            "name": "ReleaseReason",
            "type": {
                "type": "enum",
                "name": "ReleaseReason",
                "symbols": [
                    "Compensation",
                    "Expiry",
                    "Cancellation"
                ]
            },
            "doc": "Compensation = saga rollback; Expiry = TTL worker auto-release; Cancellation = explicit customer/ops action. Propagates to ReservationReleasedEvent.ReleaseReason."
        },
        {
            "name": "RequestedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the saga issued the command."
        }
    ]
}
```

### 5.7 Existing Reused Events (reference only)

The following schemas **already exist** in the repository and are **not** re-authored by this document. They are listed here so implementation agents know which existing files to reference in saga wiring, and which consumers to register in `§ 7`.

| Schema file | Purpose in eShop |
|-------------|------------------|
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentRequestedEvent.avsc` | Produced by Checkout saga → PaymentProcessingSaga |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AuthorizePaymentCommand.avsc` | PaymentProcessingSaga → Payments (unchanged for Checkout) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/CapturePaymentCommand.avsc` | PaymentProcessingSaga → Payments (unchanged for Checkout) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/VoidPaymentCommand.avsc` | PaymentProcessingSaga compensation (saga-pre-capture path) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/RequestRefundCommand.avsc` | Checkout saga compensation (cancel-post-capture path) → Payments |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizationFailedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCapturedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCaptureFailedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCompletedEvent.avsc` | PaymentProcessingSaga → **Checkout saga** (main success signal) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentFailedEvent.avsc` | PaymentProcessingSaga → **Checkout saga** (failure signal) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentRefundedEvent.avsc` | Payments → Checkout saga (cancel-post-capture confirmation) + Notifications |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentVoidedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/SendEmailNotificationCommand.avsc` | Multiple services → Notifications (existing pattern; Checkout-related emails use the same template-commanded pattern) |

**Direct-consumption events the Checkout saga subscribes to from this list:** `PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`.

---

## 6. Outbox Relay Registration

### 6.1 Summary

Each new service (Catalog, Basket, Ordering, Inventory) has:
- its own PostgreSQL schema (`catalog`, `basket`, `ordering`, `inventory`),
- its own outbox table in that schema (`{schema}.OutboxMessages`),
- its own DbContext that implements `ITransactionalOutbox<T>` via `Platform.ReliableMessaging.Outbox.EFCore`,
- its own outbox-relay container in `docker-compose.yaml` pointed at that schema.

This matches the existing pattern exemplified by three containers already in `docker-compose.yaml`:
- `outbox-relay` (schema `weather`, DB `Weather`) — lines 306–337
- `outbox-relay-saga` (schema `saga`, DB `Weather`) — lines 339–371
- `outbox-relay-ordering` (schema `ordering`, DB `Ordering`) — lines 373–404

The **worker** implementation is `platform/Platform.OutboxRelay.WorkerService/` — a single code project parameterized entirely by environment variables (`OutboxRelay__SchemaName`, `OutboxRelay__TableName`, `ConnectionStrings__Outbox`, `OTEL_SERVICE_NAME`). One Dockerfile, N container instances.

**Recommended pattern: one relay container per service schema** (**not** embedded as `IHostedService` inside each service's `Program.cs`). Rationale:

1. **Existing repo evidence.** The three current outbox-relay containers are all separate services in docker-compose. Going embedded would break this consistency.
2. **Resource isolation.** Relay polls DB every 2 seconds (`OutboxRelay__PollingIntervalMs=2000`). Embedding it in the service's main process couples DB-poll traffic to the service's request-handling threads and memory budget.
3. **Operational independence.** A relay restart does not restart the service (and vice-versa). For high-throughput services like Ordering this matters.
4. **Symmetry with benchmarks.** `platform/Platform.OutboxRelay.Benchmark/` treats the relay as a stand-alone component; embedding would invalidate that benchmark's representativeness.

**Non-recommendation (rejected):** registering the relay as `IHostedService` inside each service — examined per the Agent-5 brief. Rejected because: (a) no existing service in the repo does this; (b) it requires every service to pull `Platform.OutboxRelay.WorkerService` as a library reference, which inflates the API's deployed image; (c) it couples service uptime to relay uptime.

### 6.2 Docker-compose additions (outbox relays)

Four new relay containers to append to `docker-compose.yaml` (outside the `kafka-create-topic` block, alongside the existing three relays). Each follows the exact env-var shape of `outbox-relay-ordering` (lines 373–404). Implementation agent produces these in the devops wave; spec here is authoritative for the env-var keys.

| Container | DB | Schema | Port (health) | OTEL_SERVICE_NAME |
|-----------|----|--------|---------------|-------------------|
| `outbox-relay-catalog` | `Catalog` | `catalog` | 8091 | `CatalogOutboxRelay` |
| `outbox-relay-basket` | `Basket` | `basket` | 8092 | `BasketOutboxRelay` |
| `outbox-relay-inventory` | `Inventory` | `inventory` | 8093 | `InventoryOutboxRelay` |
| `outbox-relay-ordering` | `Ordering` | `ordering` | 8090 | `OrderingOutboxRelay` (ALREADY EXISTS — keep) |

The existing `outbox-relay` (Weather) stays in place during the COEXIST period (D-7). Port 8088 and 8089 remain taken.

### 6.3 Per-service Program.cs registration

Inside each service's `Program.cs`, register the outbox (NOT the relay worker) via `services.AddOutbox(...)` — identical to the Weather pattern at `Weather.Infrastructure.Common.MessagingDependencyInjection.AddKafkaMessaging` lines 135–148. This configures:

- `outbox.ConfigureMessageOrigin(ApplicationInfo.AppName)` — the `message.origin` header stamp.
- `outbox.ConfigureAvroSerializerConfig(...)` — schema registry serialization.
- `outbox.ConfigureSchemaRegistryConfig(...)` — schema registry URL.

The relay process separately reads from the same `{schema}.OutboxMessages` table and produces to Kafka. No in-process work is done inside the service API pod for relay publishing.

Each service's DbContext (`CatalogDbContext`, `BasketDbContext`, `OrderingDbContext`, `InventoryDbContext`) implements `ITransactionalOutbox<T>` via `services.AddInbox<T>()` + `services.AddOutbox(...)`. Basket is a special case: it has no aggregate data in SQL, but its DbContext still owns the `basket.OutboxMessages` table (see `basket.md § 5.5`).

### 6.4 Connection strings

Per `docker-compose.yaml` pattern each relay points to its own DB. Implementation devops wave must:
1. Ensure separate Postgres databases (`Catalog`, `Basket`, `Inventory`, `Ordering`) exist (the container `postgresdb` already runs the server; the databases are created by migration bootstrap).
2. Add one connection-string line per service into appsettings under `ConnectionStrings__Outbox` (env var), matching the existing Ordering block at docker-compose lines 389.
3. Verify `OutboxRelay__SchemaName` matches the EF Core schema exactly — schema names are case-sensitive in Postgres when quoted.

---

## 7. Inbox Registration per Service

Each service registers the message types it will dedupe in `services.AddInbox(...)` in its `MessagingDependencyInjection` (following the Weather pattern at lines 124). Only external events/commands that enter the service via Kafka need inbox entries — internal domain events do not. The inbox tables live at `{schema}.InboxMessages` per existing `Platform.ReliableMessaging.Inbox.EFCore` conventions.

### 7.1 Catalog Service

**Topics consumed:** *(none in v1)*

Catalog is pure publisher-in-v1. It does not consume any cross-service events. No inbox registration needed. The inbox infrastructure is still provisioned (empty table) so future subscribers can be added without a schema migration.

**Registration:**
```csharp
services.AddInbox<CatalogDbContext>();
// no .AddInbox(typeof(...)) since zero consumed message types
```

### 7.2 Basket Service

**Topics consumed:** *(none in v1)*

Basket communicates with Catalog via synchronous HTTP (ACL — `basket.md § 9`). It does not consume Kafka events. No inbox registration needed.

**Registration:**
```csharp
services.AddInbox<BasketDbContext>();
// no .AddInbox(typeof(...))
```

### 7.3 Ordering Service

**Topics consumed:** `ordering.order-commands`

**Message types to dedupe:**
- `Ordering.Orders.CreateOrderCommand`
- `Ordering.Orders.ConfirmOrderCommand`
- `Ordering.Orders.CancelOrderCommand`
- `Ordering.Orders.MarkOrderFailedCommand`

**Registration:**
```csharp
services.AddInbox<OrderingDbContext>();
// inside AddKafka consumer pipeline for ordering.order-commands:
.AddInbox(
    typeof(CreateOrderCommand),
    typeof(ConfirmOrderCommand),
    typeof(CancelOrderCommand),
    typeof(MarkOrderFailedCommand))
```

Inbox dedupe-key is the Kafka message-id header; Idempotency contract per-command is also documented in `ordering.md § 11` (`CreateOrderCommand` idempotent on `CorrelationId`; others idempotent on `OrderId`).

### 7.4 Inventory Service

**Topics consumed:** `catalog.products`, `inventory.reservation-commands`

**Message types to dedupe:**
- From `catalog.products`: `Catalog.Products.ProductCreatedEvent` (triggers `InitializeStockItemCommand`).
- From `inventory.reservation-commands`:
  - `Inventory.Reservations.ReserveStockCommand`
  - `Inventory.Reservations.ConfirmReservationCommand`
  - `Inventory.Reservations.ReleaseReservationCommand`

Inventory does **not** consume `ProductPriceChanged` or `ProductDiscontinuedEvent` in v1 — those are non-stock-lifecycle events that do not affect the event-sourced stream.

**Registration:**
```csharp
services.AddInbox<InventoryDbContext>();
// consumer for catalog.products:
.AddInbox(typeof(ProductCreatedEvent))
// consumer for inventory.reservation-commands (separate consumer):
.AddInbox(
    typeof(ReserveStockCommand),
    typeof(ConfirmReservationCommand),
    typeof(ReleaseReservationCommand))
```

### 7.5 Checkout Saga (in `saga/SagaOrchestrators/Checkout/`)

Sagas use MassTransit's own dedup semantics (EntityFrameworkRepository + `ConcurrencyMode.Optimistic`, per `SagaDependencyInjection.cs` lines 83–119). The saga's state instance is its inbox — each incoming message locks the saga row by `CorrelationId`, and duplicate messages are swallowed by the MassTransit pipeline. No separate `services.AddInbox(typeof(...))` registration is needed.

**Topics consumed by Checkout saga:**
- `basket.sessions` → `BasketCheckoutInitiatedEvent` (saga start)
- `ordering.orders` → `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`, `OrderFailedEvent` (saga progress / terminal)
- `inventory.reservations` → `StockReservedEvent`, `StockReservationFailedEvent`, `ReservationConfirmedEvent`, `ReservationReleasedEvent` (saga progress / compensation confirmation)
- `payments.transactions` → `PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent` (saga progress / compensation confirmation)

Consumer-registration pattern inside `CheckoutSagaDependencyInjection` mirrors the `PaymentProcessingSaga` pattern in `SagaDependencyInjection.cs § AddSagaKafkaRider`. Stage 2 Agent 6 owns the saga-state-machine file; this document lists which consumer adapters it must register.

### 7.6 Notifications Service

**Topics consumed:** `ordering.orders` (new), plus existing `notification.commands` self-consumer for SendEmail dispatch.

**Message types to dedupe** (new for eShop):
- `Ordering.Orders.OrderConfirmedEvent`
- `Ordering.Orders.OrderShippedEvent`
- `Ordering.Orders.OrderDeliveredEvent`
- `Ordering.Orders.OrderCancelledEvent`
- `Ordering.Orders.OrderFailedEvent`
- (Optional) `Inventory.Reservations.ReservationConfirmedEvent` — "your order is being prepared" email. Decision: **defer** — requires the Notifications template library to know how to render reservation-confirmation messages, which is out of scope for v1 per `inventory.md § 6.4`.

Notifications **already** subscribes to existing Weather/Order events via its current inbox registration. The new registrations to add:

```csharp
services.AddInbox<NotificationsDbContext>();
// consumer for ordering.orders:
.AddInbox(
    typeof(OrderConfirmedEvent),
    typeof(OrderShippedEvent),
    typeof(OrderDeliveredEvent),
    typeof(OrderCancelledEvent),
    typeof(OrderFailedEvent))
```

Notifications renders email via the existing template mechanism; each order event maps to a template id like `ordering.order-confirmed`, `ordering.order-shipped`, etc. — the template catalog is Notifications' own concern and out of scope for this document.

### 7.7 BFF

**Topics consumed:** `catalog.products`, `catalog.categories`, `ordering.orders` (all for cache warm/invalidate).

The BFF is a **read-through cache front-end** to the other services' HTTP APIs. It consumes events not for business logic but for cache-invalidation telemetry. Per Stage 2 Agent 7 ownership, the exact shape of the BFF's consumer pipeline (whether it uses inbox dedup at all, or accepts at-least-once as idempotent cache-invalidate operations) is decided there.

Recommended default from this document: **yes, register inbox** so cache-invalidate messages are dedup'd per-key within one partition. Message types to dedupe:

```csharp
services.AddInbox<BffDbContext>();
.AddInbox(
    typeof(ProductCreatedEvent),
    typeof(ProductPriceChanged),
    typeof(ProductDiscontinuedEvent),
    typeof(CategoryCreatedEvent),
    typeof(OrderCreatedEvent),
    typeof(OrderConfirmedEvent),
    typeof(OrderCancelledEvent),
    typeof(OrderFailedEvent))
```

### 7.8 Inbox registration summary table

| Service | Consumes topics | Inbox-registered message types |
|---------|-----------------|-------------------------------|
| Catalog | — | — |
| Basket | — | — |
| Ordering | `ordering.order-commands` | `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand` |
| Inventory | `catalog.products`, `inventory.reservation-commands` | `ProductCreatedEvent`, `ReserveStockCommand`, `ConfirmReservationCommand`, `ReleaseReservationCommand` |
| Checkout saga | `basket.sessions`, `ordering.orders`, `inventory.reservations`, `payments.transactions` | MassTransit handles (no `AddInbox(typeof(...))`) |
| Notifications | `ordering.orders` (NEW) + existing | `OrderConfirmedEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderCancelledEvent`, `OrderFailedEvent` + existing |
| BFF | `catalog.products`, `catalog.categories`, `ordering.orders` | `ProductCreatedEvent`, `ProductPriceChanged`, `ProductDiscontinuedEvent`, `CategoryCreatedEvent`, `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`, `OrderFailedEvent` |

---

## 8. Internal Service HTTP API Surfaces (for BFF)

This section lists the **minimum** HTTP endpoints each service must expose so that the BFF (and operator tooling) can call them. Endpoint shapes are descriptive — the canonical command/query signatures live in each BC's design doc and will be finalised by Stage 2 Agent 7 (Use Case Catalog). This document lists **which** endpoints must exist, not their full request/response DTOs.

All endpoints below are **internal** (service-to-service), routed through YARP / in-cluster DNS. Authentication is a service-to-service JWT (not a user JWT) per `ordering.md § 11` and confirmed for the other BCs by this catalog.

### 8.1 Catalog Service

| Method | Path | Purpose | Response |
|--------|------|---------|----------|
| GET | `/api/catalog/products/{productId}` | Single product detail. Backs BFF product-detail page. | `CatalogProductResponse` — full product with category path, brand, price, images, status |
| GET | `/api/catalog/products?q=&categoryPrefix=&minPrice=&maxPrice=&currency=&status=&page=&size=` | Paged search. Backs BFF browse/search. | `Paged<CatalogProductResponse>` |
| GET | `/api/v1/catalog/products/by-ids?ids=...` | Fetch by IDs (max 100, comma-separated). Used by Basket ACL's `GetProductsByIdsAsync` (see `basket.md § 9.3`). | `IReadOnlyList<CatalogProductResponse>` — partial-tolerant |
| GET | `/api/catalog/categories/tree` | Full category tree for navigation UI. Backs BFF category menu. | `CategoryTreeNode` (recursive) |
| GET | `/api/catalog/categories/{categoryId}` | Single category metadata. | `CategoryResponse` |

**Admin/write endpoints** (not BFF-consumed, listed for completeness so implementation agents don't miss them):
- `POST /api/admin/catalog/products` — create product (admin only).
- `PATCH /api/admin/catalog/products/{id}/price` — update price.
- `PATCH /api/admin/catalog/products/{id}/discontinue` — discontinue with reason.
- `POST /api/admin/catalog/categories` — create category.
- `PATCH /api/admin/catalog/categories/{id}` — rename/reparent.

### 8.2 Basket Service

| Method | Path | Purpose | Response |
|--------|------|---------|----------|
| GET | `/api/basket/me` | Get current user's basket (UserId from JWT `sub` claim). Backs BFF "view cart" page. | `BasketDto` — items + total, or 404 if basket doesn't exist |
| POST | `/api/basket/me/items` | Add item. Body: `{ productId, quantity }`. Internal call goes through the ACL to Catalog. | `BasketDto` |
| DELETE | `/api/basket/me/items/{productId}` | Remove item. | `BasketDto` |
| PATCH | `/api/basket/me/items/{productId}` | Change quantity. Body: `{ quantity }`. | `BasketDto` |
| POST | `/api/basket/me/refresh-prices` | User-initiated price refresh (per `basket.md § 6.3`). | `BasketDto` (with refreshed snapshots) |
| DELETE | `/api/basket/me` | Clear basket. | `BasketDto` (empty) |
| POST | `/api/basket/me/checkout` | Trigger checkout saga. Body: `{ correlationId }` (BFF-generated GUIDv7). | `202 Accepted` — `{ correlationId }` echoed; saga started asynchronously |

### 8.3 Ordering Service

| Method | Path | Purpose | Response |
|--------|------|---------|----------|
| GET | `/api/orders/{orderId}` | Single order detail (row-level auth: buyer sees only their own, admin bypass). Backs BFF order-detail page. | `OrderResponse` |
| GET | `/api/orders?buyerId=&status=&page=&size=` | Paged order history. BuyerId enforced from JWT unless admin. Backs BFF order-history page. | `Paged<OrderSummaryResponse>` |
| POST | `/api/orders/{orderId}/cancel` | Buyer-initiated cancel. Body: `{ reason }`. Order must be pre-shipped. | `OrderResponse` (with Cancelled status) |

**Admin/Dev endpoints:**
- `POST /api/admin/orders/{orderId}/mark-shipped` — Body: `{ carrier, trackingNumber }`.
- `POST /api/admin/orders/{orderId}/mark-delivered`.

**Saga-to-Ordering transport is Kafka** (`ordering.order-commands`), not HTTP — per D-1. `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand` are **not** exposed as HTTP endpoints.

### 8.4 Inventory Service

| Method | Path | Purpose | Response |
|--------|------|---------|----------|
| GET | `/api/inventory/stock-levels/{productId}` | Single product stock level. Backs BFF product-detail page availability badge. | `StockLevelResponse` — `{ productId, onHand, reserved, available, lastUpdatedUtc }` |
| POST | `/api/inventory/stock-levels/batch` | Batch fetch (product ids in body). Backs BFF browse/search for availability overlays. | `IReadOnlyList<StockLevelResponse>` — partial-tolerant |
| GET | `/api/inventory/reservations/{reservationId}` | Single reservation detail (ops/audit). | `ReservationResponse` |
| GET | `/api/inventory/reservations?orderId=` | All reservations for an order (ops/saga debugging). | `IReadOnlyList<ReservationResponse>` |

**Admin/Ops endpoints:**
- `POST /api/admin/inventory/stock-items/{productId}/receive` — Body: `{ quantity, source }`. Recorded as `StockReceivedEvent`.
- `POST /api/admin/inventory/stock-items/{productId}/adjust` — Body: `{ delta, reason }`. Recorded as `StockAdjustedEvent`.

**Saga-to-Inventory transport is Kafka** (`inventory.reservation-commands`), not HTTP — per D-2. `ReserveStockCommand`, `ConfirmReservationCommand`, `ReleaseReservationCommand` are **not** exposed as HTTP endpoints.

### 8.5 BFF composition map (informational)

| BFF endpoint (indicative — Stage 2 Agent 7 finalises) | Calls internally |
|---------|------------------|
| `GET /bff/product/{id}` | `Catalog.GET /products/{id}` + `Inventory.GET /stock-levels/{id}` — composed into a single product-detail response |
| `GET /bff/browse?q=&category=&page=&size=` | `Catalog.GET /products?...` + `Inventory.POST /stock-levels/batch` (for availability overlays) |
| `GET /bff/my-basket` | `Basket.GET /me` (pass-through) |
| `POST /bff/my-basket/add` | `Basket.POST /me/items` (with prior Catalog product-existence check optional since Basket's ACL already does it) |
| `POST /bff/checkout` | `Basket.POST /me/checkout` (pass-through) |
| `GET /bff/my-orders` | `Ordering.GET /orders?buyerId={me}` (pass-through with JWT-forwarded buyer id) |
| `GET /bff/my-orders/{id}` | `Ordering.GET /orders/{id}` (pass-through) |

All BFF → internal calls use typed HttpClients (matching the `ProductCatalogHttpAdapter` pattern in `basket.md § 9.3`), with ~2-second timeouts, and JSON response contracts.

---

## End of Event Catalog & Kafka Topology

All success criteria confirmed against the Stage 2 Agent 5 prompt:

- [x] Master event catalog table with all required columns + every external event listed — § 2.
- [x] Every new Avro schema has COMPLETE `.avsc` content in a json code fence — § 5.1–5.6.
- [x] Every schema has namespace `{Domain}.{Aggregate}` matching existing patterns — verified.
- [x] Docker-compose delta has exact copy-paste-ready bash lines — § 4 (revised authoritative block).
- [x] Saga-to-service command schemas complete — § 5.5 (Ordering, 4 commands) + § 5.6 (Inventory, 3 commands).
- [x] Outbox and inbox registration strategy documented per service — § 6 and § 7.
- [x] HTTP endpoints per service listed for BFF consumption — § 8.
- [x] Decisions documented — § 1.4 (D-1 through D-10).
- [x] No code files written — only this markdown file.
