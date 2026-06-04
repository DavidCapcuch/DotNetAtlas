# Event Catalog & Kafka Topology

> **Role:** **§ 2 (Master Event Catalog) is the per-event contract SSOT** for the eShop — producer, consumer(s), consumer group(s), correlation key, trigger, and Avro schema path for every cross-service event/command. Per [ADR-0033](../adr/0033-kafka-topic-contract-doc-ssot.md) this is one of **two** canonical anchors; the other is [kafka-topology.md](../kafka-topology.md) (per-**topic** partitions / retention / class — compatibility mode is *derived* from class per [ADR-0007](../adr/0007-avro-compatibility-modes.md)). The two anchors join on the topic name. Everything else (master design § 6, per-BC topic restatements, the DLT runbook) **points here**; it does not restate.
> **Scope:** every Avro schema (paths only — the `.avsc` is the source of truth, § 5), every outbox/inbox registration (§ 6–7), and the internal HTTP surfaces that matter for messaging (§ 8).
> **SSOT decision:** [ADR-0033](../adr/0033-kafka-topic-contract-doc-ssot.md). **Companion ADRs:** [0001](../adr/0001-centralized-saga-orchestration.md), [0004](../adr/0004-checkout-saga-topology.md), [0006](../adr/0006-event-sourcing-for-inventory.md), [0007](../adr/0007-avro-compatibility-modes.md).

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
- Command streams (imperative intent): `{...}.{...}-commands`, e.g. `payments.payment-commands`, `inventory.reservation-commands`.

**Partitioning key rule:** the stable business identity that preserves the right invariant.
- Per-aggregate ordering → aggregate id. Examples: `catalog.products` keyed by `ProductId`, `ordering.orders` keyed by `OrderId`.
- Saga correlation → `OrderId` (the saga is keyed on `OrderId` per [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md); the dedicated `CorrelationId` was retired — [ADR-0030](../adr/0030-retire-dedicated-correlationid.md)). Example: `inventory.reservations` keyed by `OrderId` so all reservation events for one saga run co-partition.

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
| D-1 | **One `ordering.order-commands` topic** for saga→Ordering instead of per-command topics | Matches `payments.payment-commands` precedent; saga already fan-outs by command type via MassTransit consumer type-routing. |
| D-2 | **One `inventory.reservation-commands` topic** for saga→Inventory | Same rationale as D-1. Keeps saga surface uniform. |
| D-3 | **One `ReserveStockCommand` per order line item** (Option A in Agent-5 prompt) | Matches Inventory's per-`ProductId` stream model (ADR-0006); gives natural per-item failure granularity; saga fan-in by `OrderId` correlates N responses. |
| D-4 | **No dedicated `checkout.commands` topic** | Saga publishes imperative intent to `ordering.order-commands`, `inventory.reservation-commands`, and `payments.payment-commands`. A checkout-specific topic would duplicate infrastructure for no new semantics. |
| D-5 | **Notifications is command-driven, NOT event-subscribed** (decision reversed from earlier intent) | Producer BCs emit `NotifyUserCommand` on `notifications.notify-commands` (channel-agnostic, producer-assigned `NotificationId`; v2 — [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)); Notifications resolves the recipient's channels from its own preferences, fans out per-channel, and emits `NotificationDeliveryStatusChangedEvent` on `notifications.notify-events`. Editorial control (whether to notify) stays in the producing BC; channel selection stays local to Notifications. Notifications does NOT subscribe to `ordering.orders` / `inventory.reservations` / `payments.transactions` / `invoicing.invoices`. Full rationale: [notifications.md § 2](notifications.md). |
| D-6 | **Outbox-relay is a separate container per service schema** | One relay container per service schema in `docker-compose.yaml` (`outbox-relay-saga`, `outbox-relay-basket`, `outbox-relay-catalog`, `outbox-relay-inventory`, `outbox-relay-invoicing`, `outbox-relay-notifications`, `outbox-relay-ordering`, `outbox-relay-payments`). Each service gets its own relay with its own `OutboxRelay__SchemaName` binding. See § 6. |
| D-7 | **Weather-remnant fully decommissioned pre-dispatch** | The `services/Order/` project, `AlertSubscription*Saga` sagas, and Kafka topic `order.alert-subscriptions` were deleted. Ordering is greenfield; no legacy topic coexistence remains. |
| D-8 | **Basket sessions topic retains events for 30 days**; all other event-log topics use `compact + delete` (infinite retention) to preserve audit trail | Basket is ephemeral by definition. Order/Inventory events feed compliance audit; deleting them defeats the audit-trail purpose of § 2 of `inventory.md`. |
| D-9 | **Command topics retain 7 days** | Commands are transient intent; after 7 days a replay is not operationally useful and keeping them consumes broker disk needlessly. |
| D-10 | **Notifications v2 uses `notifications.notify-commands` / `notify-events`** | v2 ([ADR-0031](../adr/0031-notify-user-command-and-notification-id.md), [ADR-0032](../adr/0032-notifications-dispatch-and-channels.md), [notifications.md](notifications.md)) renames the v1 `notifications.email-commands` / `email-events` topics to the channel-agnostic `notifications.notify-commands` / `notifications.notify-events` (+ `.DLT`), registered in the `kafka-create-topic` block of `docker-compose.yaml`. |

---

## 2. Master Event Catalog

Sorted by topic then event name. All rows reflect Stage 1 BC designs plus the command schemas introduced in § 5.5 / § 5.6. Existing Payments events (which the Checkout saga reuses unmodified) are included for completeness with a link back to the existing `.avsc`.

| Event | Topic | Namespace | Producer | Consumer(s) | Consumer Group(s) | Correlation Key | Triggered By | Schema File Path |
|-------|-------|-----------|----------|-------------|-------------------|-----------------|--------------|------------------|
| `BasketCheckoutInitiatedEvent` | `basket.sessions` | `Basket.Sessions` | Basket | Checkout saga, BFF (cache invalidate) | `saga-checkout`, `bff-group` | `OrderId` | `BasketCheckedOutDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc` |
| `CategoryCreatedEvent` | `catalog.categories` | `Catalog.Categories` | Catalog | BFF (cache invalidate), Search indexer¹ | `bff-group` | `CategoryId` | `CategoryCreatedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Categories/CategoryCreatedEvent.avsc` |
| `ProductCreatedEvent` | `catalog.products` | `Catalog.Products` | Catalog | Inventory (init stream), BFF (cache invalidate) | `inventory-group`, `bff-group` | `ProductId` | `ProductCreatedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductCreatedEvent.avsc` |
| `ProductDiscontinuedEvent` | `catalog.products` | `Catalog.Products` | Catalog | Basket (flag stale snapshots; on-demand in v1)², BFF (cache invalidate) | `bff-group` | `ProductId` | `ProductDiscontinuedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductDiscontinuedEvent.avsc` |
| `ProductPriceChangedEvent` | `catalog.products` | `Catalog.Products` | Catalog | BFF (cache invalidate); Basket consumes on-demand only in v1 | `bff-group` | `ProductId` | `ProductPriceChangedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductPriceChangedEvent.avsc` |
| `CheckoutCompletedEvent` | `checkout.sagas` | `Checkout.Sagas` | Checkout saga | — (no v1 consumer by design³) | — | `OrderId` | `AwaitingConfirmation → Confirmed` (see [checkout-saga.md § 9](checkout-saga.md)) | `platform/Platform.SchemaRegistry.Contracts/Avro/Checkout/Sagas/CheckoutCompletedEvent.avsc` |
| `CheckoutFailedEvent` | `checkout.sagas` | `Checkout.Sagas` | Checkout saga | — (no v1 consumer by design³) | — | `OrderId` | `AwaitingOrderCreation / CompensatingStockReservations → Failed / Compensated` (see [checkout-saga.md § 9](checkout-saga.md)) | `platform/Platform.SchemaRegistry.Contracts/Avro/Checkout/Sagas/CheckoutFailedEvent.avsc` |
| `CheckoutStuckEvent` | `checkout.sagas` | `Checkout.Sagas` | Checkout saga | — (ops alert sink, no v1 consumer by design³) | — | `OrderId` | `Compensating* → CompensationStuck` (see [checkout-saga.md § 9](checkout-saga.md)) | `platform/Platform.SchemaRegistry.Contracts/Avro/Checkout/Sagas/CheckoutStuckEvent.avsc` |
| `CapturePaymentCommand` | `payments.payment-commands` | `Payments.Transactions` | PaymentProcessingSaga | Payments | `payments-group` | `PaymentTransactionId` | saga transition | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/CapturePaymentCommand.avsc` (existing) |
| `AuthorizePaymentCommand` | `payments.payment-commands` | `Payments.Transactions` | PaymentProcessingSaga | Payments | `payments-group` | `PaymentTransactionId` | saga transition | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AuthorizePaymentCommand.avsc` (existing) |
| `RequestRefundCommand` | `payments.payment-commands` | `Payments.Transactions` | *(deferred — no v1 producer; future customer/admin refund flow per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md))* | Payments | `payments-group` | `OrderId` | Deferred customer/admin-initiated refund (returns / post-purchase cancellation is future work) | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/RequestRefundCommand.avsc` (existing) |
| `ApproveCaptureCommand` | `payments.payment-commands` | `Payments.Transactions` | Checkout saga | PaymentProcessingSaga | `saga-payment-processing` | `OrderId` | Checkout saga confirmed stock + order → capture (the pivot); fields `OrderId`, `UserId`, `RequestedAtUtc`. New per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md) | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/ApproveCaptureCommand.avsc` |
| `AbortCaptureCommand` | `payments.payment-commands` | `Payments.Transactions` | Checkout saga | PaymentProcessingSaga | `saga-payment-processing` | `OrderId` | Checkout saga confirmation failed → pre-capture void; fields `OrderId`, `UserId`, `Reason`, `RequestedAtUtc`. New per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md) | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AbortCaptureCommand.avsc` |
| `VoidPaymentCommand` | `payments.payment-commands` | `Payments.Transactions` | PaymentProcessingSaga | Payments | `payments-group` | `PaymentTransactionId` | saga pre-capture compensation (on `AbortCaptureCommand` / capture-approval-wait timeout) | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/VoidPaymentCommand.avsc` (existing) |
| `PaymentAuthorizationFailedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga | `saga-payment-processing` | `OrderId` | Payments auth failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizationFailedEvent.avsc` (existing) |
| `PaymentAuthorizedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga, **Checkout saga** (drives order + reservation confirmation) | `saga-payment-processing`, `saga-checkout` | `OrderId` | Payments auth success | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizedEvent.avsc` (existing) |
| `PaymentCaptureFailedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga | `saga-payment-processing` | `OrderId` | Payments capture failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCaptureFailedEvent.avsc` (existing) |
| `PaymentCapturedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga, Invoicing (invoice-issuance projection) | `saga-payment-processing`, `invoicing-group` | `OrderId` | Payments capture success | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCapturedEvent.avsc` (existing) |
| `PaymentCompletedEvent` | `payments.transactions` | `Payments.Transactions` | **Payments** | **Checkout saga** | `saga-checkout` | `OrderId` | `PaymentCompletedDomainEvent` (co-raised with capture; Payments outbox per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCompletedEvent.avsc` (existing) |
| `PaymentFailedEvent` | `payments.transactions` | `Payments.Transactions` | **Payments** | **Checkout saga** | `saga-checkout` | `OrderId` | `PaymentFailedDomainEvent` (auth or capture decline; Payments outbox per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentFailedEvent.avsc` (existing) |
| `PaymentRefundedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | Invoicing (credit-note trigger) — deferred customer/admin refund flow; not consumed by the Checkout saga (see [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)). Notifications via command-driven path only (see D-5) | `invoicing-group` | `OrderId` | Payments refund success | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentRefundedEvent.avsc` (existing) |
| `RequestPaymentCommand` | `payments.payment-commands` | `Payments.Transactions` | Checkout saga | PaymentProcessingSaga | `saga-payment-processing` | `OrderId` | Checkout saga step (after stock reserved) | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/RequestPaymentCommand.avsc` (existing — renamed from `PaymentRequestedEvent` per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)) |
| `PaymentVoidedEvent` | `payments.transactions` | `Payments.Transactions` | Payments | PaymentProcessingSaga | `saga-payment-processing` | `OrderId` | Payments void success | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentVoidedEvent.avsc` (existing) |
| `StockLevelChangedEvent` | `inventory.stock-events` | `Inventory.Stock` | Inventory | Catalog (IsSellable projection), BFF (cache invalidate) | `catalog-group`, `bff-group` | `ProductId` | Availability crosses 0↔positive | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Stock/StockLevelChangedEvent.avsc` |
| `ReservationConfirmedEvent` | `inventory.reservations` | `Inventory.Reservations` | Inventory | Checkout saga (informational) — Notifications via command-driven path only (see D-5) | `saga-checkout` | `OrderId` | `ReservationConfirmedDomainEvent` (ES internal) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReservationConfirmedEvent.avsc` |
| `ReservationReleasedEvent` | `inventory.reservations` | `Inventory.Reservations` | Inventory | Checkout saga (compensation confirmation) | `saga-checkout` | `OrderId` | `ReservationReleasedDomainEvent` (ES internal) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReservationReleasedEvent.avsc` |
| `StockReservationFailedEvent` | `inventory.reservations` | `Inventory.Reservations` | Inventory | Checkout saga (triggers compensation) | `saga-checkout` | `OrderId` | `ReserveStockCommand` failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/StockReservationFailedEvent.avsc` |
| `StockReservedEvent` | `inventory.reservations` | `Inventory.Reservations` | Inventory | Checkout saga | `saga-checkout` | `OrderId` | `StockReservedDomainEvent` (ES internal) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/StockReservedEvent.avsc` |
| `ConfirmReservationCommand` | `inventory.reservation-commands` | `Inventory.Reservations` | Checkout saga | Inventory | `inventory-group` | `OrderId` | saga step (after payment) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ConfirmReservationCommand.avsc` |
| `ReleaseReservationCommand` | `inventory.reservation-commands` | `Inventory.Reservations` | Checkout saga (compensation) / Ops | Inventory | `inventory-group` | `OrderId` | saga compensation / admin cancel | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReleaseReservationCommand.avsc` |
| `ReserveStockCommand` | `inventory.reservation-commands` | `Inventory.Reservations` | Checkout saga | Inventory | `inventory-group` | `OrderId` | saga step (after order created) | `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReserveStockCommand.avsc` |
| `NotifyUserCommand` (v2) | `notifications.notify-commands` | `Notifications` | Producing BCs (Invoicing — `InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler`) | Notifications | `notifications-group` | `RecipientUserId` | producer-driven; producer-assigned `NotificationId` ([ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)). v2 replaces v1 `SendEmailNotificationCommand` | `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/NotifyUserCommand.avsc` |
| `NotificationDeliveryStatusChangedEvent` (v2) | `notifications.notify-events` | `Notifications` | Notifications | Invoicing (`NotificationDeliveryStatusChangedEventKafkaHandler` filters `Channel==email && Status==Dispatched`, correlates on `NotificationId`, drives `Issued → Delivered`). Open to other BCs needing delivery feedback. | `invoicing-group` | `RecipientUserId` | per-channel dispatch outcome, durable channels only ([ADR-0032](../adr/0032-notifications-dispatch-and-channels.md)). v2 replaces v1 `EmailNotificationSentEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/NotificationDeliveryStatusChangedEvent.avsc` |
| `OrderCancelledEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Inventory (release if still reserved), Invoicing (credit-note projection), BFF (cache invalidate), Checkout saga (compensation confirmation) — Notifications via command-driven path only (see D-5) | `inventory-group`, `invoicing-group`, `saga-checkout`, `bff-group` | `OrderId` | `OrderCancelledDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCancelledEvent.avsc` |
| `OrderConfirmedEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Invoicing (invoice-issuance projection), BFF (cache invalidate), Checkout saga (terminal success) — Notifications via command-driven path only (see D-5) | `invoicing-group`, `bff-group`, `saga-checkout` | `OrderId` | `OrderConfirmedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderConfirmedEvent.avsc` |
| `OrderCreatedEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | Checkout saga (drives next step: reserve stock), BFF (cache invalidate) | `saga-checkout`, `bff-group` | `OrderId` | `OrderCreatedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCreatedEvent.avsc` |
| `OrderDeliveredEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | BFF (cache invalidate) — Notifications via command-driven path only (see D-5) | `bff-group` | `OrderId` | `OrderDeliveredDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderDeliveredEvent.avsc` |
| `OrderFailedEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | BFF (cache invalidate), Checkout saga (terminal failure) — Notifications via command-driven path only (see D-5) | `bff-group`, `saga-checkout` | `OrderId` | `OrderFailedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderFailedEvent.avsc` |
| `OrderShippedEvent` | `ordering.orders` | `Ordering.Orders` | Ordering | BFF (cache invalidate) — Notifications via command-driven path only (see D-5) | `bff-group` | `OrderId` | `OrderShippedDomainEvent` | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderShippedEvent.avsc` |
| `CancelOrderCommand` | `ordering.order-commands` | `Ordering.Orders` | Checkout saga (compensation) | Ordering | `ordering-group` | `OrderId` | saga compensation | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/CancelOrderCommand.avsc` |
| `ConfirmOrderCommand` | `ordering.order-commands` | `Ordering.Orders` | Checkout saga | Ordering | `ordering-group` | `OrderId` | saga step (after all greens) | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/ConfirmOrderCommand.avsc` |
| `CreateOrderCommand` | `ordering.order-commands` | `Ordering.Orders` | Checkout saga | Ordering | `ordering-group` | `OrderId` | saga step (after basket checkout) | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/CreateOrderCommand.avsc` |
| `MarkOrderFailedCommand` | `ordering.order-commands` | `Ordering.Orders` | Checkout saga | Ordering | `ordering-group` | `OrderId` | saga terminal-failure | `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/MarkOrderFailedCommand.avsc` |

> **Footnotes:**
> 1. **Search indexer** — planned scope, see [roadmap.md § 2.3 Catalog / Search](../roadmap.md). No code exists; the consumer would land under its own `{service}-group` when implemented per § 3.1.
> 2. **Basket consumes `ProductDiscontinuedEvent` on-demand** — i.e., Basket reads it lazily via the Catalog HTTP ACL at basket-view time, not via a Kafka subscription. A Kafka-driven flag-stale-snapshots flow is in [roadmap.md § 2.3 Basket](../roadmap.md); it would consume under `basket-group` per § 3.1.
> 3. **`checkout.sagas` ships with no v1 consumer by design** — the topic carries saga-terminal events emitted by the Checkout saga's transactional outbox (producer + Avro schemas are LIVE; see [`saga/SagaOrchestrators/Checkout/CheckoutSaga/CheckoutSagaOrchestrator.cs`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/CheckoutSagaOrchestrator.cs) `PublishToOutbox` sites). Rationale per [checkout-saga.md § 9.1](checkout-saga.md): kept as a forensic record + future-consumer seam (analytics dashboard, ops alerting on `CheckoutStuckEvent`); adding a consumer later is non-breaking. Topic retention is 30 days ([docker-compose.yaml](../../docker-compose.yaml) `kafka-create-topic` `retention.ms=2592000000`).

---

## 3. Kafka Topics

> Per-topic **topology** (partitions, retention, class, compatibility) is canonical in [kafka-topology.md](../kafka-topology.md). The **topic → event/command** inverse mapping is **not** restated here: § 2 is sorted by topic, so it already *is* the per-topic grouping (read down the Topic column). The standalone inverse table was removed per [ADR-0033](../adr/0033-kafka-topic-contract-doc-ssot.md) — it duplicated § 2.

### 3.1 Consumer groups

**Rule: one Kafka consumer group per service.** Naming: `{service-name}-group`. Per-topic offsets are tracked independently inside that single group (Kafka commits offsets per `(group, topic, partition)`), so a second group inside the same service is operational overhead — extra rebalance scope, extra dashboard row, extra on-call mapping — without recovering any isolation that the per-topic offset partitioning doesn't already give. Multiple groups per service is therefore a documented anti-pattern.

The motivating concern: two groups consuming the SAME topic inside one service would observe events independently and could process them out-of-order relative to each other, splitting the service's view of "what has been handled." Per-topic offset independence inside one group eliminates this without losing replay-of-one-topic semantics (Kafka's offset-reset works per `(group, topic, partition)`).

**Sole exception — sagas.** Each MassTransitStateMachine in `saga/SagaOrchestrators/` is its own logical service (per [ADR-0001](../adr/0001-centralized-saga-orchestration.md)) and gets its own group, even though they share the saga-worker deployment. The two saga groups today are `saga-checkout` and `saga-payment-processing`. They share `payments.transactions` but subscribe to disjoint Avro event types on it (no observable interleaving risk).

**Group membership is read off § 2 — not re-listed here.** Each group's topic span is exactly the set of § 2 rows carrying that group in the *Consumer Group(s)* column. The per-group span bullets that used to live here were deleted per [ADR-0033](../adr/0033-kafka-topic-contract-doc-ssot.md): restating the spans was the § 2 ↔ § 3.1 transpose drift that issue #299 spent three audit passes reconciling. The groups are the two saga groups (`saga-checkout`, `saga-payment-processing`) plus one per consuming service (`inventory-group`, `invoicing-group`, `bff-group`, `catalog-group`, `notifications-group`, `payments-group`, `ordering-group`); `bff-group` registers **no inbox** (idempotent cache invalidation — see § 7.7).

---

## 4. Topic Creation & Retention

The live topics — names, partitions, `retention.ms` — are created by the `kafka-create-topic` block in [docker-compose.yaml](../../docker-compose.yaml) (the runtime source of truth) and documented per-topic in [kafka-topology.md](../kafka-topology.md) (the per-topic physical SSOT, including the class → retention / compatibility rule and the meaning of each `retention.ms` value). This section previously carried a copy-paste compose delta plus a retention-rationale list; both were removed per [ADR-0033](../adr/0033-kafka-topic-contract-doc-ssot.md) — they duplicated the runtime and the topology anchor.

---

## 5. Avro Schemas

**Schema bodies are NOT reproduced here — the `.avsc` files are the single source of truth.** Each subsection below names the schema and points at its path under `platform/Platform.SchemaRegistry.Contracts/Avro/**`. To inspect or modify a schema, edit the `.avsc` directly; the C# binding is regenerated via `platform/Platform.SchemaRegistry.Contracts/generate-avro.ps1` (see [conventions.md § 3](conventions.md)).

Where a schema has non-trivial cross-cutting semantics (Summary Event flag, shared enum re-declaration, FORWARD_TRANSITIVE notes that aren't visible from the JSON alone), the prose callout sits next to the path reference.

### 5.1 Catalog External Events

#### 5.1.1 `ProductCreatedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductCreatedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.1.2 `ProductPriceChangedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductPriceChangedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.1.3 `ProductDiscontinuedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/ProductDiscontinuedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.1.4 `CategoryCreatedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Categories/CategoryCreatedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

### 5.2 Basket External Events

#### 5.2.1 `BasketCheckoutInitiatedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

### 5.3 Ordering External Events

> **Note on shared enum `OrderStatusAtTransition`:** `ordering.md` § 7.2.3 declares the enum inline inside `OrderCancelledEvent` and references it by FQN inside `OrderFailedEvent`. Avro supports cross-file enum reference only when both files are submitted to the same Schema Registry subject-group at compile time — which our build **does not** guarantee. To keep schemas self-contained and compilable independently, this catalog **re-declares** the enum inline in each file (with identical symbols). Implementation agents: use the schemas below verbatim, do NOT attempt cross-file `"type": "Ordering.Orders.OrderStatusAtTransition"` reference.

#### 5.3.1 `OrderCreatedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCreatedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.3.2 `OrderConfirmedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderConfirmedEvent.avsc`

> **Summary Event** per [ADR-0020](../adr/0020-summary-events.md) — carries the order's full state at the confirmation transition (`Items`, `TotalAmount`, `Currency`, `BillingAddress`) so Invoicing's issuance handler (10-year retention) can rebuild state without an HTTP round-trip to Ordering. The four enrichment fields are nullable / defaulted for FORWARD_TRANSITIVE compatibility per [ADR-0007](../adr/0007-avro-compatibility-modes.md); production producers always populate them.

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.3.3 `OrderCancelledEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderCancelledEvent.avsc`

> **Summary Event** per [ADR-0020](../adr/0020-summary-events.md) — carries the order's state at the cancellation transition (`Items`, `TotalAmount`, `Currency`, `BillingAddress`) alongside the original `Reason` / `AtStatus` delta payload, so Invoicing's credit-note handler (10-year retention) can rebuild state without an HTTP round-trip to Ordering. The four enrichment fields are nullable / defaulted for FORWARD_TRANSITIVE compatibility per [ADR-0007](../adr/0007-avro-compatibility-modes.md); production producers always populate them. Consumers that read only the `Reason` / `AtStatus` delta (Inventory, BFF, checkout saga) are unaffected by the enrichment fields; Invoicing's credit-note handler is the one consumer that reads the added state.

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.3.4 `OrderShippedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderShippedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.3.5 `OrderDeliveredEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderDeliveredEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.3.6 `OrderFailedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/OrderFailedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

### 5.4 Inventory External Events

#### 5.4.1 `StockLevelChangedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Stock/StockLevelChangedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.4.2 `StockReservedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/StockReservedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.4.3 `StockReservationFailedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/StockReservationFailedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.4.4 `ReservationConfirmedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReservationConfirmedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.4.5 `ReservationReleasedEvent.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReservationReleasedEvent.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

### 5.5 Saga-to-Ordering Commands

These four commands are published by the Checkout saga to `ordering.order-commands`. The Ordering service consumes them through its inbox (§ 7) and invokes the corresponding aggregate methods (see `ordering.md § 9.1`).

#### 5.5.1 `CreateOrderCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/CreateOrderCommand.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.5.2 `ConfirmOrderCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/ConfirmOrderCommand.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.5.3 `CancelOrderCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/CancelOrderCommand.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.5.4 `MarkOrderFailedCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/MarkOrderFailedCommand.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

### 5.6 Saga-to-Inventory Commands

Three commands published by the Checkout saga to `inventory.reservation-commands`, consumed by the Inventory service's inbox.

**Decision recall (D-3):** one `ReserveStockCommand` per order line item. The saga fan-outs N commands (one per `ProductId`), and fan-ins on `OrderId` across N `StockReservedEvent` / `StockReservationFailedEvent` responses. Rationale: Inventory's event-sourced model keys streams on `ProductId` — one command = one stream append. A single `ReserveStockItemsCommand` with N items would require Inventory to span N streams in one transaction, which contradicts the per-stream consistency boundary from `inventory.md § 3.2`.

#### 5.6.1 `ReserveStockCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReserveStockCommand.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.6.2 `ConfirmReservationCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ConfirmReservationCommand.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

#### 5.6.3 `ReleaseReservationCommand.avsc`

**Path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Inventory/Reservations/ReleaseReservationCommand.avsc`

<!-- Schema body: see .avsc path above (single source of truth). -->

### 5.7 Existing Reused Events (reference only)

The following schemas **already exist** in the repository and are **not** re-authored by this document. They are listed here so implementation agents know which existing files to reference in saga wiring, and which consumers to register in `§ 7`.

| Schema file | Purpose in eShop |
|-------------|------------------|
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/RequestPaymentCommand.avsc` | Produced by Checkout saga → PaymentProcessingSaga on `payments.payment-commands` (renamed from `PaymentRequestedEvent` per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AuthorizePaymentCommand.avsc` | PaymentProcessingSaga → Payments (unchanged for Checkout) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/CapturePaymentCommand.avsc` | PaymentProcessingSaga → Payments (unchanged for Checkout) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/VoidPaymentCommand.avsc` | PaymentProcessingSaga pre-capture compensation (on `AbortCaptureCommand` / capture-approval-wait timeout) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/RequestRefundCommand.avsc` | Deferred customer/admin refund flow — **no v1 producer** (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) → Payments |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/ApproveCaptureCommand.avsc` | Checkout saga → PaymentProcessingSaga (capture-approval handshake; new per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AbortCaptureCommand.avsc` | Checkout saga → PaymentProcessingSaga (pre-capture void on confirmation failure; new per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizedEvent.avsc` | Payments → PaymentProcessingSaga + **Checkout saga** (drives order + reservation confirmation, per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizationFailedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCapturedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCaptureFailedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCompletedEvent.avsc` | **Payments** → Checkout saga (post-capture terminal; Payments-owned per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentFailedEvent.avsc` | **Payments** → Checkout saga (auth/capture-decline terminal; Payments-owned per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentRefundedEvent.avsc` | Payments → Invoicing (credit-note trigger) — deferred refund flow, **not** consumed by the Checkout saga (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)). Notifications via command-driven path only (D-5). |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentVoidedEvent.avsc` | Payments → PaymentProcessingSaga (unchanged) |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/NotifyUserCommand.avsc` | Producing services → Notifications (v2 channel-agnostic command; [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)). Replaces v1 `Notifications/Email/SendEmailNotificationCommand.avsc`. |

**Direct-consumption events the Checkout saga subscribes to from this list:** `PaymentAuthorizedEvent`, `PaymentCompletedEvent`, `PaymentFailedEvent` (no longer `PaymentRefundedEvent` — refund left checkout compensation per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)).

---

### 5.8 Invoicing External Events

Current scope ships all 4 LOCKED Invoicing Avro schemas. `IssueInvoiceCommandHandler` issues with `DeliveryChannel.Email`, so issuance raises `InvoiceDeliveryRequestedDomainEvent` (fanned out as a `NotifyUserCommand` to Notifications with a producer-assigned `NotificationId`, persisted on the invoice); when Notifications reports back via `NotificationDeliveryStatusChangedEvent` (`Channel==email && Status==Dispatched`, correlated on `NotificationId`), Invoicing transitions `Issued → Delivered` and raises `InvoiceDeliveredDomainEvent`, whose outbox publisher emits `InvoiceDeliveredEvent`. (v2 — [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md) / [ADR-0032](../adr/0032-notifications-dispatch-and-channels.md).)

| File | Producer → Consumers |
|---|---|
| `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceIssuedEvent.avsc` | Invoicing → **no v1 consumer** by design (the v1 BFF defines no invoice endpoint/cache; a BFF invoice view is planned-not-v1 and would consume this topic if added). Buyer-delivery email flows via Invoicing's outbox publisher emitting `NotifyUserCommand` (NOT a Notifications subscription to this topic) — see D-5. |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceCancelledEvent.avsc` | Invoicing → **no v1 consumer** (BFF invoice cache is planned-not-v1). Buyer email deferred (would route via `NotifyUserCommand` per D-5). |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/CreditNoteIssuedEvent.avsc` | Invoicing → **no v1 consumer** (BFF invoice cache is planned-not-v1). Buyer email deferred (would route via `NotifyUserCommand` per D-5). |
| `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceDeliveredEvent.avsc` | Invoicing → **no v1 consumer** (BFF "my invoices" cache is planned-not-v1). Emitted after Invoicing consumes `NotificationDeliveryStatusChangedEvent` and transitions `Issued → Delivered`. |

All four target the `invoicing.invoices` topic — topology (partitions / retention / class) in [kafka-topology.md](../kafka-topology.md); partition key `BuyerId` per § 2. Compatibility is *derived* from class (event-log → `FORWARD_TRANSITIVE`), set per-subject by the `schema-registry-init` companion service ([ADR-0007](../adr/0007-avro-compatibility-modes.md)).

---

## 6. Outbox Relay Registration

### 6.1 Summary

Each service has:
- its own PostgreSQL schema (`catalog`, `basket`, `ordering`, `inventory`, …),
- its own outbox table in that schema (`{schema}.outbox_messages`),
- its own DbContext that implements `ITransactionalOutbox<T>` via `Platform.ReliableMessaging.Outbox.EFCore`,
- its own outbox-relay container in `docker-compose.yaml` pointed at that schema.

The full per-schema relay fleet currently in `docker-compose.yaml` is listed in § 6.2.

The **worker** implementation is `platform/Platform.OutboxRelay.WorkerService/` — a single code project parameterized entirely by environment variables (`OutboxRelay__SchemaName`, `OutboxRelay__TableName`, `ConnectionStrings__Outbox`, `OTEL_SERVICE_NAME`). One Dockerfile, N container instances.

**Recommended pattern: one relay container per service schema** (**not** embedded as `IHostedService` inside each service's `Program.cs`). Rationale:

1. **Existing repo evidence.** The outbox-relay containers are all separate services in docker-compose (one per service schema). Going embedded would break this consistency.
2. **Resource isolation.** Relay polls DB every 2 seconds (`OutboxRelay__PollingIntervalMs=2000`). Embedding it in the service's main process couples DB-poll traffic to the service's request-handling threads and memory budget.
3. **Operational independence.** A relay restart does not restart the service (and vice-versa). For high-throughput services like Ordering this matters.
4. **Symmetry with benchmarks.** `platform/Platform.OutboxRelay.Benchmark/` treats the relay as a stand-alone component; embedding would invalidate that benchmark's representativeness.

**Non-recommendation (rejected):** registering the relay as `IHostedService` inside each service — examined per the Agent-5 brief. Rejected because: (a) no existing service in the repo does this; (b) it requires every service to pull `Platform.OutboxRelay.WorkerService` as a library reference, which inflates the API's deployed image; (c) it couples service uptime to relay uptime.

### 6.2 Docker-compose relay containers

One relay container per service schema in `docker-compose.yaml` (outside the `kafka-create-topic` block). All share the same image/Dockerfile and differ only by `ConnectionStrings__Outbox`, `OutboxRelay__SchemaName`, `KafkaProducer__ClientId`, `OTEL_SERVICE_NAME`, and the published health port.

| Container | DB | Schema | Port (health) | OTEL_SERVICE_NAME |
|-----------|----|--------|---------------|-------------------|
| `outbox-relay-saga` | `Saga` | `saga` | 8089 | `SagaOutboxRelay` |
| `outbox-relay-basket` | `Basket` | `basket` | 8090 | `BasketOutboxRelay` |
| `outbox-relay-catalog` | `Catalog` | `catalog` | 8091 | `CatalogOutboxRelay` |
| `outbox-relay-inventory` | `Inventory` | `inventory` | 8092 | `InventoryOutboxRelay` |
| `outbox-relay-invoicing` | `Invoicing` | `invoicing` | 8093 | `InvoicingOutboxRelay` |
| `outbox-relay-notifications` | `Notifications` | `notifications` | 8094 | `NotificationsOutboxRelay` |
| `outbox-relay-ordering` | `Ordering` | `ordering` | 8095 | `OrderingOutboxRelay` |
| `outbox-relay-payments` | `Payments` | `payments` | 8096 | `PaymentsOutboxRelay` |

### 6.3 Per-service Program.cs registration

Inside each service's `Program.cs`, register the outbox (NOT the relay worker) via `services.AddOutbox(...)` in the service's messaging DI. This configures:

- `outbox.ConfigureMessageOrigin(ApplicationInfo.AppName)` — the `message.origin` header stamp.
- `outbox.ConfigureAvroSerializerConfig(...)` — schema registry serialization.
- `outbox.ConfigureSchemaRegistryConfig(...)` — schema registry URL.

The relay process separately reads from the same `{schema}.outbox_messages` table and produces to Kafka. No in-process work is done inside the service API pod for relay publishing.

Each service's DbContext (`CatalogDbContext`, `BasketDbContext`, `OrderingDbContext`, `InventoryDbContext`) implements `ITransactionalOutbox<T>` via `services.AddInbox<T>()` + `services.AddOutbox(...)`. Basket is a special case: it has no aggregate data in SQL, but its DbContext still owns the `basket.outbox_messages` table (see `basket.md § 5.5`).

### 6.4 Connection strings

Per `docker-compose.yaml` pattern each relay points to its own DB. Implementation devops wave must:
1. Ensure separate Postgres databases (`Catalog`, `Basket`, `Inventory`, `Ordering`) exist (the container `postgresdb` already runs the server; the databases are created by migration bootstrap).
2. Add one connection-string line per service into appsettings under `ConnectionStrings__Outbox` (env var), matching the per-relay blocks in `docker-compose.yaml`.
3. Verify `OutboxRelay__SchemaName` matches the EF Core schema exactly — schema names are case-sensitive in Postgres when quoted.

---

## 7. Inbox Registration per Service

Each service registers the message types it will dedupe in `services.AddInbox(...)` in its `MessagingDependencyInjection`. Only external events/commands that enter the service via Kafka need inbox entries — internal domain events do not. The inbox tables live at `{schema}.inbox_messages` per existing `Platform.ReliableMessaging.Inbox.EFCore` conventions.

### 7.1 Catalog Service

**Topics consumed:** `inventory.stock-events`

**Message types to dedupe:**
- `Inventory.Stock.StockLevelChangedEvent` — handled by [`StockLevelChangedEventKafkaHandler`](../../services/Catalog/Catalog.Infrastructure/Messaging/Kafka/StockEvents/StockLevelChangedEventKafkaHandler.cs), which dispatches to `Catalog.Application.Products.UpdateProductSellability.StockLevelChangedEventProjectionHandler` to flip `ProductSearchViewRow.IsSellable` based on `Available > 0`. See [catalog.md § Sellability projection](catalog.md).

**Registration:**
```csharp
services.AddInbox<CatalogDbContext>();
services.AddInbox(typeof(StockLevelChangedEvent));
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

Inbox dedupe-key is the Kafka message-id header; Idempotency contract per-command is also documented in `ordering.md § 11` (all idempotent on `OrderId` — `CreateOrderCommand` on the pre-assigned PK per [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md), the others on the existing aggregate id).

### 7.4 Inventory Service

**Topics consumed:** `catalog.products`, `inventory.reservation-commands`, `ordering.orders`

**Message types to dedupe:**
- From `catalog.products`: `Catalog.Products.ProductCreatedEvent` (triggers `InitializeStockItemCommand`).
- From `inventory.reservation-commands`:
  - `Inventory.Reservations.ReserveStockCommand`
  - `Inventory.Reservations.ConfirmReservationCommand`
  - `Inventory.Reservations.ReleaseReservationCommand`
- From `ordering.orders`: `Ordering.Orders.OrderCancelledEvent` — releases the reservation when an order is cancelled before shipment (`OrderCancelledEventKafkaHandler`).

Inventory does **not** consume `ProductPriceChangedEvent` or `ProductDiscontinuedEvent` in v1 — those are non-stock-lifecycle events that do not affect the event-sourced stream.

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
// consumer for ordering.orders (release-on-cancel):
.AddInbox(typeof(OrderCancelledEvent))
```

### 7.5 Checkout Saga (in `saga/SagaOrchestrators/Checkout/`)

Sagas use MassTransit's own dedup semantics (EntityFrameworkRepository + `ConcurrencyMode.Optimistic`, per `SagaDependencyInjection.cs` lines 83–119). The saga's state instance is its inbox — each incoming message locks the saga row by `CorrelationId` (the MassTransit state-machine key, whose value is the `OrderId` per [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)), and duplicate messages are swallowed by the MassTransit pipeline. No separate `services.AddInbox(typeof(...))` registration is needed.

**Topics consumed by Checkout saga:**
- `basket.sessions` → `BasketCheckoutInitiatedEvent` (saga start)
- `ordering.orders` → `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`, `OrderFailedEvent` (saga progress / terminal)
- `inventory.reservations` → `StockReservedEvent`, `StockReservationFailedEvent`, `ReservationConfirmedEvent`, `ReservationReleasedEvent` (saga progress / compensation confirmation)
- `payments.transactions` → `PaymentAuthorizedEvent` (drives confirmation), `PaymentCompletedEvent` (post-capture terminal), `PaymentFailedEvent` (auth/capture-decline fast-fail) — all Payments-owned per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md); no longer consumes `PaymentRefundedEvent`

Consumer-registration pattern inside `CheckoutSagaDependencyInjection` mirrors the `PaymentProcessingSaga` pattern in `SagaDependencyInjection.cs § AddSagaKafkaRider`. Stage 2 Agent 6 owns the saga-state-machine file; this document lists which consumer adapters it must register.

### 7.6 Notifications Service

**Topics consumed:** `notifications.notify-commands` only — the inbound `NotifyUserCommand` queue (v2; was `notifications.email-commands` / `SendEmailNotificationCommand`). Notifications does NOT subscribe to per-BC topics. Command-driven (D-5 + [notifications.md § 2](notifications.md)): producer BCs own the editorial decision ("should we notify this user?") and assign the `NotificationId` ([ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)); Notifications resolves channels from owned preferences and fans out per-channel via Hangfire ([ADR-0032](../adr/0032-notifications-dispatch-and-channels.md)).

**Topics published:** `notifications.notify-events` — `NotificationDeliveryStatusChangedEvent` per durable-channel dispatch outcome (email, SMS), key = `RecipientUserId`, infinite retention. Carries `NotificationId` so producers correlate back (e.g. Invoicing's `NotificationDeliveryStatusChangedEventKafkaHandler` filters `Channel==email && Status==Dispatched` to transition `Issued → Delivered`).

**Message types to dedupe** (inbox key = the `message.id` header, NOT a payload field — [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)):
- `Notifications.NotifyUserCommand` — the sole inbound contract.

**Registration:**
```csharp
// DI registration of the inbox store:
services.AddInbox<NotificationsDbContext>();
// KafkaFlow consumer middleware (inside AddKafka → AddConsumer → AddMiddlewares):
.AddInbox(typeof(NotifyUserCommand))
```

**Channels & templates (v2):** three channels — Email (MailKit→Mailpit), fake SMS (quiet-hours-aware), Bell (SignalR live push) — fanned out via per-channel Hangfire jobs ([ADR-0032](../adr/0032-notifications-dispatch-and-channels.md)). Two seeded templates: `invoicing.invoice-delivered` `[Email]` and `order.shipped` `[Email, Bell, Sms]`. Channel resolution = `enabled_channels ∩ template_channels`; preferences are seeded (no HTTP). See [notifications.md](notifications.md).

### 7.7 BFF

**Topics consumed:** `catalog.products`, `catalog.categories`, `inventory.stock-events`, `ordering.orders`, `basket.sessions` (all for FusionCache invalidation).

The BFF is a **read-through cache front-end** to the other services' HTTP APIs. It consumes events not for business logic but to invalidate cached read-models via `FusionCache.RemoveByTagAsync(...)`.

**The BFF registers NO inbox.** Cache invalidation is idempotent by construction — `RemoveByTagAsync` is a no-op when the tag is absent — so at-least-once Kafka redelivery is harmless, and an inbox would add a per-message DB write for zero behavioural change. (Contrast the service consumers in § 7.1–7.4, whose handlers mutate persisted state and therefore must dedupe.) The BFF also subscribes to *published-language event topics only* and never to saga-internal coordination streams — hence it does **not** consume `inventory.reservations` (an `OrderId`-keyed, saga-internal stream); its product-availability concern is served by the `ProductId`-keyed `inventory.stock-events` contract plus the short cache TTL.

The per-topic handler → FusionCache-tag mapping is canonical in [bff.md § 2.2](bff.md); the per-event consumer registry (which events `bff-group` consumes) is canonical in the master event catalog (§ 2). This section deliberately does **not** re-embed either table — the duplication between those tables was the source of the cross-section drift this catalog now avoids.

### 7.8 Invoicing Service

**Topics consumed:** `ordering.orders`, `payments.transactions`, `notifications.notify-events`

**Message types to dedupe:**
- From `ordering.orders`:
  - `Ordering.Orders.OrderConfirmedEvent` — `OrderConfirmedInvoiceProjectionKafkaHandler` (invoice-issuance projection; order half).
  - `Ordering.Orders.OrderCancelledEvent` — `OrderCancelledCreditNoteProjectionKafkaHandler` (credit-note projection; order half).
- From `payments.transactions`:
  - `Payments.Transactions.PaymentCapturedEvent` — `PaymentCapturedInvoiceProjectionKafkaHandler` (invoice-issuance projection; payment half — the invoice issues once both the order-confirmed and payment-captured halves have arrived).
  - `Payments.Transactions.PaymentRefundedEvent` — `PaymentRefundedCreditNoteProjectionKafkaHandler` (credit-note projection; payment half).
- From `notifications.notify-events`:
  - `Notifications.NotificationDeliveryStatusChangedEvent` — `NotificationDeliveryStatusChangedEventKafkaHandler` drives `Issued → Delivered` (filters `Channel==email && Status==Dispatched`, correlates on `NotificationId`).

**Registration:**
```csharp
services.AddInbox<InvoicingDbContext>();
// consumer for ordering.orders:
.AddInbox(typeof(OrderConfirmedEvent), typeof(OrderCancelledEvent))
// consumer for payments.transactions:
.AddInbox(typeof(PaymentCapturedEvent), typeof(PaymentRefundedEvent))
// consumer for notifications.notify-events:
.AddInbox(typeof(NotificationDeliveryStatusChangedEvent))
```

### 7.9 Payments Service

**Topics consumed:** `payments.payment-commands`

**Message types to dedupe** (saga-issued commands from PaymentProcessingSaga):
- `Payments.Transactions.AuthorizePaymentCommand`
- `Payments.Transactions.CapturePaymentCommand`
- `Payments.Transactions.VoidPaymentCommand`
- `Payments.Transactions.RequestRefundCommand` — consumer registered, but has no v1 producer (deferred refund flow per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)).

**Registration:**
```csharp
services.AddInbox<PaymentsDbContext>();
.AddInbox(
    typeof(AuthorizePaymentCommand),
    typeof(CapturePaymentCommand),
    typeof(VoidPaymentCommand),
    typeof(RequestRefundCommand))
```

### 7.10 Inbox registration summary table

| Service | Consumes topics | Inbox-registered message types |
|---------|-----------------|-------------------------------|
| Catalog | `inventory.stock-events` | `StockLevelChangedEvent` |
| Basket | — | — |
| Ordering | `ordering.order-commands` | `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand` |
| Inventory | `catalog.products`, `inventory.reservation-commands`, `ordering.orders` | `ProductCreatedEvent`, `ReserveStockCommand`, `ConfirmReservationCommand`, `ReleaseReservationCommand`, `OrderCancelledEvent` |
| Invoicing | `ordering.orders`, `payments.transactions`, `notifications.notify-events` | `OrderConfirmedEvent`, `OrderCancelledEvent`, `PaymentCapturedEvent`, `PaymentRefundedEvent`, `NotificationDeliveryStatusChangedEvent` |
| Payments | `payments.payment-commands` | `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RequestRefundCommand` |
| Checkout saga | `basket.sessions`, `ordering.orders`, `inventory.reservations`, `payments.transactions` | MassTransit handles (no `AddInbox(typeof(...))`) |
| PaymentProcessing saga | `payments.transactions`, `payments.payment-commands` | MassTransit handles (no `AddInbox(typeof(...))`) |
| Notifications | `notifications.notify-commands` (only) | `NotifyUserCommand` — per D-5 + [notifications.md § 2](notifications.md). No subscriptions to per-BC event topics. |
| BFF | `catalog.products`, `catalog.categories`, `inventory.stock-events`, `ordering.orders`, `basket.sessions` | — (no inbox — idempotent cache invalidation; see § 7.7) |

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
| POST | `/api/basket/me/checkout` | Trigger checkout saga. Body: `{ shippingAddress, billingAddress, paymentMethodId }`. | `202 Accepted` — `{ orderId }` (pre-assigned UUID v7 allocated server-side; becomes the saga key per [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)) |

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
- `POST /api/admin/inventory/stock-items/{productId}/receive` — Body: `{ quantity, source }`. Recorded as `StockReceivedDomainEvent`.
- `POST /api/admin/inventory/stock-items/{productId}/adjust` — Body: `{ delta, reason }`. Recorded as `StockAdjustedDomainEvent`.

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

## End of Event Catalog

§ 2 is the per-event contract SSOT (producer / consumers / group / correlation key / trigger / schema path). Per-topic physical topology lives in [kafka-topology.md](../kafka-topology.md); the runtime topic definitions live in [docker-compose.yaml](../../docker-compose.yaml); compatibility mode is derived from topic class per [ADR-0007](../adr/0007-avro-compatibility-modes.md). See [ADR-0033](../adr/0033-kafka-topic-contract-doc-ssot.md) for how the two anchors fit together and what was de-duplicated.
