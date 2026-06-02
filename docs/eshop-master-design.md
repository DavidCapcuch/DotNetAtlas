# eShop Reference Solution — Master Architecture Design

> **Status:** Accepted (2026-04-18) — all sections authored; implementation-ready.
> **Supersedes:** high-level parts of [eshop-general-plan.md](eshop-general-plan.md) related to bounded-context internals, events, and integration contracts.
> **Companion ADRs:** [0001](adr/0001-centralized-saga-orchestration.md), [0002](adr/0002-pricing-in-catalog.md), [0003](adr/0003-basket-as-technical-bc.md), [0004](adr/0004-checkout-saga-topology.md), [0005](adr/0005-customer-data-in-ordering.md), [0006](adr/0006-event-sourcing-for-inventory.md).
> **Detailed design chapters:** [docs/bc-design/](bc-design/) (per-BC domain design, event catalog, saga, use cases, BFF).

---

## How to read this document

This master design is the **navigation + conventions + cross-cutting** hub. Implementation agents should read it end-to-end, then jump to the detailed chapter(s) for their service:

| If you are implementing… | Start with this master (§3, §4, §10, §11), then read |
|---|---|
| **Catalog** service | [bc-design/catalog.md](bc-design/catalog.md), [bc-design/events-catalog.md](bc-design/events-catalog.md) (§ 5.1), [bc-design/use-cases.md](bc-design/use-cases.md) (§ Catalog) |
| **Basket** service | [bc-design/basket.md](bc-design/basket.md), [bc-design/events-catalog.md](bc-design/events-catalog.md) (§ 5.2), [bc-design/use-cases.md](bc-design/use-cases.md) (§ Basket) |
| **Ordering** service | [bc-design/ordering.md](bc-design/ordering.md) (incl. Appendix A migration), [bc-design/events-catalog.md](bc-design/events-catalog.md) (§ 5.3, § 5.5), [bc-design/use-cases.md](bc-design/use-cases.md) (§ Ordering) |
| **Inventory** service | [bc-design/inventory.md](bc-design/inventory.md), [bc-design/events-catalog.md](bc-design/events-catalog.md) (§ 5.4, § 5.6), [bc-design/use-cases.md](bc-design/use-cases.md) (§ Inventory) |
| **Checkout saga** | [bc-design/checkout-saga.md](bc-design/checkout-saga.md), [ADR-0004](adr/0004-checkout-saga-topology.md) |
| **EShop.BFF** | [bc-design/bff.md](bc-design/bff.md), [bc-design/use-cases.md](bc-design/use-cases.md) (§ Service HTTP surfaces) |

---

## 1. Introduction

### 1.1 Goals

Implementation-ready architecture specification for the eShop reference solution. Parallel implementation agents, one per service, must be able to implement their service using this document (plus the linked chapters and ADRs) alone, without asking follow-up questions about:

- aggregate shape, invariants, factory methods, and state transitions
- internal domain events + external summary events with full Avro schemas
- Kafka topic names, partition keys, consumer groups
- use cases (commands, queries) with request/response contracts
- checkout saga state machine, compensation paths, timeouts
- BFF aggregation endpoints and caching/resilience strategy

### 1.2 Non-Goals

- `.cs` implementation files (produced by downstream agents)
- Actual `.avsc` files (fully specified in [bc-design/events-catalog.md](bc-design/events-catalog.md); materialized by implementation agents)
- EF Core migrations (per CLAUDE.md: always user-generated deterministically)
- CI/CD pipeline changes
- Deployment manifests (Kubernetes, Aspire)
- Performance/load-testing strategy
- Planned bounded contexts beyond current scope (Accounts, Shipping, Returns, Reviews, Promotions — see [roadmap.md § 2.2](roadmap.md))

### 1.3 Relationship to Prior Artifacts

| Artifact | Role |
|----------|------|
| [eshop-general-plan.md](eshop-general-plan.md) | High-level scope, solution structure, YARP positioning — authoritative for everything not superseded here |
| [ADR-0001](adr/0001-centralized-saga-orchestration.md) | Centralized saga service placement |
| [ADR-0002](adr/0002-pricing-in-catalog.md) | Pricing inside Catalog (v1 simplification) |
| [ADR-0003](adr/0003-basket-as-technical-bc.md) | Basket as technical/session BC |
| [ADR-0004](adr/0004-checkout-saga-topology.md) | Checkout saga step ordering and compensation |
| [ADR-0005](adr/0005-customer-data-in-ordering.md) | Customer data flow (no Accounts BC in v1) |
| [ADR-0006](adr/0006-event-sourcing-for-inventory.md) | Event sourcing for Inventory (trade-off analysis) |

---

## 2. Solution Structure Recap

See [eshop-general-plan.md § Solution Structure](eshop-general-plan.md) for the full folder layout. Summary:

```
src/EShop.BFF/              # Backend-for-Frontend (new)
services/
├── Catalog/                # new — 4 layers: Api, Application, Domain, Infrastructure
├── Basket/                 # new — Redis-backed aggregate + SQL outbox side-car
├── Ordering/               # new — greenfield Order aggregate (former services/Order/ deleted)
├── Inventory/              # new — Event Sourced (single ES example)
├── Payments/                # existing — reused unchanged (Payments)
└── Notifications/          # existing — reused unchanged
saga/SagaOrchestrators/
├── Checkout/               # NEW — multi-step checkout saga
├── Payments/                # existing PaymentProcessingSaga — sub-saga reused
└── (Orders/ previously held AlertSubscription sagas — removed pre-dispatch with the Weather cleanup)
platform/                   # unchanged
docker-compose.yaml         # new topics + one outbox-relay container per service schema
```

---

## 3. Event Discipline — Internal vs External Events

### 3.1 Principle

Cross-service Kafka topics carry **enriched summary events** (external events) that are contractually stable. Raw domain events stay inside the owning bounded context as in-process `IDomainEventHandler<T>` dispatches.

Source: [event-driven.io — Internal and external events](https://event-driven.io/en/internal_external_events/).

### 3.2 Naming & Avro Namespacing

| Kind | C# name suffix | Avro schema | Avro namespace | Transport |
|------|----------------|-------------|----------------|-----------|
| Internal domain event | `{State}DomainEvent` (e.g., `ProductPriceChangedDomainEvent`) | **none** | — | in-process dispatcher |
| External summary event | `{BusinessMoment}Event` (e.g., `ProductPriceChangedEvent`) | `.avsc` under `platform/Platform.SchemaRegistry.Contracts/Avro/{Domain}/{Aggregate}/` | `{Domain}.{Aggregate}` (e.g., `Catalog.Products`) | Kafka via transactional outbox |
| Saga-issued command | `{Verb}{Target}Command` (e.g., `ReserveStockCommand`) | `.avsc` under same path | `{Domain}.{Aggregate}` | Kafka via transactional outbox |

Kafka topic convention: `{domain}.{aggregate}[.{kind}]` — all lowercase, dot-delimited, hyphens for multi-word. Examples: `catalog.products`, `ordering.order-commands`, `inventory.reservation-commands`.

Avro style rules (enforced in review):
- Every field has `doc`.
- Nullable: `["null","{type}"]` union with `default: null`.
- Monetary amounts: `{"type":"bytes","logicalType":"decimal","precision":19,"scale":4}`.
- Timestamps: `{"type":"long","logicalType":"timestamp-millis"}`.
- UUIDs: `{"type":"string","logicalType":"uuid"}`.
- Enums declared inline on first use; cross-schema sharing forbidden (each schema self-contained).

### 3.3 Transformation Pattern

Every external event is produced by a domain-event handler in the Application layer that:

1. Receives the internal `*DomainEvent`.
2. Loads any missing state from the aggregate/DbContext at the event position (avoids stale-read race).
3. Constructs the Avro-compiled `ISpecificRecord` external event (enriched with business-relevant context).
4. Adds it to the transactional outbox: `_transactionalOutbox.AddOutboxMessage(topic, key, event)`.

The aggregate's `SaveChangesAsync()` persists domain state + outbox message in a single transaction. A per-service outbox-relay worker dequeues and publishes to Kafka.

**Template to replicate**: [Weather.Application/WeatherAlerts/../SubscriptionActivatedOutboxPublisherDomainEventHandler.cs](../src/Weather.Application/) and siblings. Every new external event follows this shape.

### 3.4 Anti-Patterns

- Publishing raw `*DomainEvent` directly to Kafka (no enrichment, no contract stability).
- Requiring downstream consumers to aggregate multiple events to reconstruct state.
- Breaking external event schema without a new version + deprecation plan.
- Using external events as commands (if exactly one known consumer must act, use an HTTP command or a command-topic instead — see `payments.payment-commands`, `inventory.reservation-commands`, `ordering.order-commands` pattern).

### 3.5 Is it an event, or a command?

Guidance from [event-driven.io — Internal and External Events](https://event-driven.io/en/internal_external_events/):

> *"If an event has one expected consumer performing specific logic with guaranteed feedback, it's probably a command, not an event."*

Decision test (run for every proposed cross-service message):

| Signal | Event | Command |
|---|---|---|
| Consumer count | zero-or-many | exactly one known |
| Caller expectation | fire-and-forget (reactive) | specific response expected |
| Naming | past-tense business moment (`OrderConfirmed`) | imperative verb (`ConfirmOrder`) |
| Topic | `{domain}.{aggregate}` | `{domain}.{aggregate}-commands` |
| Schema subject | …Event | …Command |

Canonical examples in this solution:

- `OrderConfirmedEvent` → **event**: Notifications, BFF cache invalidator, and CheckoutSaga all react independently.
- `ReserveStockCommand` → **command**: the CheckoutSaga needs the specific response (`StockReservedEvent` or `StockReservationFailedEvent`) to drive its state machine.

**Important — apply the spirit, not just the letter.** The decision-table row "consumer count: exactly one known → command" is a useful summary but lossy. The article quoted above requires both parts: *specific logic at the consumer* **AND** *guaranteed feedback to the producer*. A past-tense fact published fire-and-forget is an event even if exactly one consumer happens to react today. See [ADR-0023](adr/0023-payments-event-vs-command-classification.md) for the worked example.

**Payments classification (resolved per [ADR-0023](adr/0023-payments-event-vs-command-classification.md), 2026-05-30):** of the nine messages on the `payments.transactions` / `payments.payment-commands` topics, exactly one — what was `PaymentRequestedEvent` — has been renamed to `RequestPaymentCommand` and moved to `payments.payment-commands`. The Checkout saga publishes it and *blocks* on the matching `PaymentCompletedEvent` / `PaymentFailedEvent` reply (90 s timeout drives compensation) — the textbook "guaranteed feedback" pattern, so the command-shape is correct. The remaining seven Payments messages stay event-named: `PaymentAuthorizedEvent`, `PaymentAuthorizationFailedEvent`, `PaymentCaptureFailedEvent`, `PaymentVoidedEvent` (Payments-BC-produced facts), and `PaymentCompletedEvent`, `PaymentFailedEvent` (PaymentProcessingSaga-produced terminals) — their producers don't await any reply, so the 2-part test classifies them as events even with one consumer today. `PaymentCapturedEvent` and `PaymentRefundedEvent` already have ≥ 2 real consumers (Invoicing joins the saga in both cases) and need no action.

### 3.6 External event authoring checklist

Every `{BusinessMoment}Event` schema must satisfy (enforced in review):

- [ ] Name is a **past-tense business moment**, not a state delta. Prefer `OrderShipped` over `OrderStatusChanged`.
- [ ] Payload is **enriched** so a downstream consumer can act without another call. The article: *"Price calculation can be complex if we consider discounts, loyalty plans, taxes"* → include `TotalAmount` pre-computed, not raw line-by-line that forces consumer re-summation.
- [ ] **Computed values** are pre-calculated in the event (`LineTotal`, `Available`, `TotalAmount`).
- [ ] **Identity fields** are present for saga correlation + audit: `CorrelationId` where applicable, primary aggregate id, and `*AtUtc` timestamp.
- [ ] Payload is **NOT the full aggregate** — only the facts relevant to the moment. Avoid dumping a 30-field aggregate into every event.
- [ ] Schema is **FORWARD_TRANSITIVE** (event log) or **FULL_TRANSITIVE** (command topic) per [ADR-0007](adr/0007-avro-compatibility-modes.md).
- [ ] **Idempotency** via the Kafka message id: consumers dedupe via the inbox middleware ([kafka-dlt-strategy.md](bc-design/kafka-dlt-strategy.md)); you do not need a separate `EventId` field in the payload.
- [ ] When the outbox publisher **enriches**, it loads aggregate state **at the event position**, not at "now". This is trivial in an event-sourced BC (Inventory); in OLTP BCs (Catalog, Ordering, Basket) you must be careful to enrich from the aggregate *as it was immediately after the domain transition* — use the domain event's own fields as the source of truth, not a fresh repository fetch that could see a newer version.

### 3.7 Topic-naming rationale — internal / external suffixes

The article distinguishes **module-scope** vs **system-scope** channels:

> *"Internal (or private) is understandable in the module context, and external (or public) is understandable in the whole system context."*

**Current state (v1).** Every Kafka topic in the eShop solution is **system-scope / external** — our internal events never cross a process boundary (they are in-process `IDomainEventHandler<T>` dispatches per § 3.1). Consequently we default to unsuffixed names following the pattern `{domain}.{aggregate}[.{kind}]`:

- `catalog.products`, `ordering.orders`, `inventory.reservations` — business-moment event logs (external events, stable contracts)
- `ordering.order-commands`, `inventory.reservation-commands`, `payments.payment-commands` — imperative intent (commands, 7-day retention)
- `basket.sessions` — session hand-off

This matches the codebase convention (`payments.transactions`, `catalog.products`, etc.) and keeps Kafka's tree flat.

**Forward-looking rule — when you introduce the FIRST internal-scope topic, use the suffix.** If a BC later needs to publish events for **its own horizontal scaling, its own audit consumer, or any module-scoped use** where the contract is *not* guaranteed across the whole system, the topic MUST carry the `-internal` suffix. The semantics follow the article:

| Suffix | Scope | Compatibility expectation | Typical producers / consumers |
|---|---|---|---|
| *(no suffix, default)* | **system / external** | FORWARD_TRANSITIVE events or FULL_TRANSITIVE commands per [ADR-0007](adr/0007-avro-compatibility-modes.md); treated as API contract; breaking changes require a new subject | other BCs, sagas, BFF, external downstream |
| `-internal` | **module / private** | Owned by one team; schema changes allowed with coordination *inside* that team; other teams consume at their own risk | same BC's sibling processes (e.g., per-instance cache priming), the BC's own audit/archive consumer, ops tools |
| `-external` | **system, explicit** | Optional explicit variant of the default for teams that want grep-level clarity when the same BC has BOTH `-internal` and `-external` topics. Same rules as the default. | same as default |

**Naming shape when the suffix is used:** `{domain}.{aggregate}[.{kind}]-internal` — e.g., a hypothetical future `catalog.products-internal` for Catalog's own audit-archive consumer, or `inventory.stock-rebuild-internal` for an ops-triggered projection rebuild stream.

**Consumer semantics.** A consumer reading an `-internal` topic must be owned by the same team as the producer; cross-team consumption of `-internal` topics is forbidden without an ADR promoting the topic (or a sibling `-external` topic). Architecture tests should enforce that inbox registrations in one BC never list an `-internal` topic belonging to another BC.

The internal-vs-external distinction therefore lives in **three coordinated places**: the **event type** (`*DomainEvent` = in-process; `*Event` = external Kafka), the **topic name suffix** (absent or `-external` vs `-internal`), and the **schema-registry compatibility mode** (stricter for external).

### 3.8 Extended anti-patterns (from the article)

Additional anti-patterns beyond § 3.4:

- **Under-enrichment** — external event lacks the context a downstream consumer needs, forcing another RPC call. The article's cautionary tale: *"Client complain that from time to time we're charging them too much"* — the downstream consumer was racing its own projection against the original event. Fix: enrich with pre-computed totals at publish time.
- **Over-enrichment** — dumping the entire aggregate into every event. Breaks forward compatibility (more fields to protect across versions) and wastes bandwidth.
- **`*Changed` / `*Updated` suffixes** — symptomatic of missing ubiquitous language. When tempted to write `CustomerAddressChanged`, ask: *what business moment is this?* The answer might be `CustomerRelocated`, `CustomerBilledToNewAddress`, or `CustomerAddressCorrected` — each implies a different downstream reaction.
- **Cross-schema enum references in Avro** — not portable across Schema Registry subjects; inline each enum per schema (see events-catalog.md § 5 — `OrderStatusAtTransition` is inlined in both `OrderCancelledEvent.avsc` and `OrderFailedEvent.avsc`, not shared).
- **Assuming global ordering across topics** — Kafka guarantees per-partition ordering within a topic, NOT across topics or across partitions. Design sagas to tolerate out-of-order arrivals where possible (correlation-id-driven state machines do this correctly).

---

## 4. Context Map

> **Diagram:** [Context Map mermaid source](diagrams/context-map.md) — open interactively in drawio via the link in § 10.2.

### 4.1 Narrative

The eShop is a hub-and-spoke system with the **Checkout saga** as the cross-cutting orchestrator and the **BFF** as the consumer-facing composition layer. The four new bounded contexts (Catalog, Basket, Ordering, Inventory) are each pure command responders — they own their aggregates and publish external events when business moments occur, but none of them initiates multi-BC workflows. The Checkout saga reacts to `BasketCheckoutInitiatedEvent` and drives each participating BC via command topics, reassembling the response events into saga state.

Three integration patterns dominate:

- **Saga orchestration** (Checkout ↔ Ordering, Inventory, PaymentProcessingSaga). Bidirectional: saga publishes commands; BCs publish responses.
- **Anti-Corruption Layer** (Basket → Catalog). Basket's `ProductCatalogHttpAdapter` translates Catalog DTOs into an internal `ProductSnapshot` VO; Basket never references Catalog aggregates directly.
- **Published Language** (Catalog → Inventory via `ProductCreatedEvent`; Ordering → Notifications via order lifecycle events). Fire-and-forget; downstream consumers interpret the enriched external events as a stable contract.

The BFF layer is not a bounded context; it is an ACL-like composition gateway over internal HTTP APIs of all four BCs.

### 4.2 Integration Patterns

| From | To | Pattern | Protocol | Payload / Contract |
|------|-----|---------|----------|---------------------|
| User | BFF | Public API | HTTPS | REST JSON |
| BFF | Catalog | Customer/Supplier | HTTP (internal) | `GetProductByIdQuery`, `SearchProductsQuery`, `GetCategoryTreeQuery`, `GetProductsByIdsQuery` |
| BFF | Basket | Customer/Supplier | HTTP (internal) | `GetBasketByUserIdQuery`, `AddItemToBasketCommand`, etc. |
| BFF | Ordering | Customer/Supplier | HTTP (internal, read-only) | `GetOrderByIdQuery`, `GetOrdersByBuyerQuery` |
| BFF | Inventory | Customer/Supplier | HTTP (internal, read-only) | `GetStockLevelQuery`, `GetStockLevelsBulkQuery` |
| Basket | Catalog | Anti-Corruption Layer (snapshots) | HTTP | `IProductCatalogQueryPort` adapter → `ProductSnapshot` VO |
| Catalog | Inventory | Published Language (async) | Kafka (`catalog.products`) | `ProductCreatedEvent` → Inventory initializes stock stream |
| Basket | CheckoutSaga | Async trigger | Kafka (`basket.sessions`) | `BasketCheckoutInitiatedEvent` |
| CheckoutSaga | Ordering | Saga command | Kafka (`ordering.order-commands`) | `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand` |
| Ordering | CheckoutSaga | Saga event | Kafka (`ordering.orders`) | `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`, `OrderFailedEvent` |
| CheckoutSaga | Inventory | Saga command | Kafka (`inventory.reservation-commands`) | `ReserveStockCommand`, `ConfirmReservationCommand`, `ReleaseReservationCommand` |
| Inventory | CheckoutSaga | Saga event | Kafka (`inventory.reservations`) | `StockReservedEvent`, `StockReservationFailedEvent`, `ReservationReleasedEvent` |
| CheckoutSaga | PaymentProcessingSaga | Async saga sub-orchestration trigger | Kafka (`payments.payment-commands`) | `RequestPaymentCommand` (renamed from `PaymentRequestedEvent` per [ADR-0023](adr/0023-payments-event-vs-command-classification.md)) |
| PaymentProcessingSaga | CheckoutSaga | Saga event | Kafka (`payments.transactions`) | `PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent` |
| PaymentProcessingSaga | Payments | Saga command | Kafka (`payments.payment-commands`) | `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RequestRefundCommand` (existing) |
| Inventory | Catalog | Published Language (async) | Kafka (`inventory.stock-events`) | `StockLevelChangedEvent` (crosses 0↔positive) |
| Ordering | Notifications | Published Language (async) | Kafka (`ordering.orders`) | Order lifecycle events |

### 4.3 BC Classification

| BC | Subdomain | Teaching pattern | Storage |
|----|-----------|------------------|---------|
| Catalog | Core | CQRS read projection | PostgreSQL (write + denormalized read view) |
| Basket | Supporting (technical/session — [ADR-0003](adr/0003-basket-as-technical-bc.md)) | Redis-backed aggregate + ACL | Redis AOF primary; SQL side-car for outbox/inbox only |
| Ordering | Core | Rich status FSM (SmartEnum-guarded) | PostgreSQL |
| Inventory | Core | Event Sourcing with projections ([ADR-0006](adr/0006-event-sourcing-for-inventory.md)) | PostgreSQL event store + projection tables |
| Payments | Generic (reused) | Payment sub-saga | PostgreSQL |
| Notifications | Generic (reused) | Event-driven consumer | — |

---

## 5. Bounded Contexts — Overview

Detailed design per BC lives in [docs/bc-design/](bc-design/). Each chapter is self-contained with ubiquitous language, aggregates, value objects, SmartEnums, internal events, external events (with Avro schemas), pattern showcase, integration points, and infrastructure notes.

### 5.1 Catalog → [bc-design/catalog.md](bc-design/catalog.md)

**Aggregates:** `Product`, `Category` (hierarchical, max depth 5).
**Value objects:** `Sku`, `Money`, `ProductName`, `ProductDescription`, `Dimensions`, `CategoryPath`, `ImageReference`, `BrandName`.
**SmartEnums:** `ProductStatus` (Draft → Active → Discontinued, with `Reactivate(adminReactivation: true)` back-edge).
**Internal events (8):** `ProductCreated/PriceChanged/Described/Activated/Discontinued/Reactivated/DomainEvent`, `CategoryCreated/ReparentedDomainEvent`.
**External events (4):** `ProductCreatedEvent`, `ProductPriceChangedEvent`, `ProductDiscontinuedEvent` on `catalog.products`; `CategoryCreatedEvent` on `catalog.categories`.
**Pattern:** CQRS read projection — `ProductSearchView` denormalized table built by per-event `*ProjectionDomainEventHandler` classes (one per Catalog domain event) in the same transaction as the write-model save.

### 5.2 Basket → [bc-design/basket.md](bc-design/basket.md)

**Aggregate:** `Basket` (keyed by UserId).
**Value objects:** `BasketItem`, `ProductSnapshot`, `BasketTotal`, `Money`.
**SmartEnums:** none (documented explicitly).
**Internal events (7):** `BasketCreated`, `ItemAddedToBasket`, `ItemRemovedFromBasket`, `ItemQuantityChanged`, `BasketPricesRefreshed`, `BasketCleared`, `BasketCheckedOutDomainEvent`.
**External events (1):** `BasketCheckoutInitiatedEvent` on `basket.sessions`.
**Pattern:** Redis-backed aggregate (key `basket:{userId}`, 30-day sliding TTL, AOF persistence, MemoryPack serialization, per-user optimistic concurrency). SQL `basket` schema holds outbox/inbox only — no aggregate table.
**Integration:** ACL to Catalog via `IProductCatalogQueryPort` → `ProductCatalogHttpAdapter`.
**Lifecycle:** basket deleted on successful `BasketCheckoutInitiatedEvent` publication.

### 5.3 Ordering → [bc-design/ordering.md](bc-design/ordering.md)

**Aggregate:** `Order` (BuyerId, Items, ShippingAddress, BillingAddress, Status, PaymentTransactionId, CorrelationId, Total, timestamps).
**Value objects:** `OrderItem`, `Address` (ISO 3166-1 alpha-2 country code), `Money`, `ProductSnapshot`.
**SmartEnum:** `OrderStatus` (Created → StockReserved → PaymentCompleted → Confirmed → Shipped → Delivered; with Cancelled/Failed off-ramps per [ADR-0004](adr/0004-checkout-saga-topology.md)).
**Internal events (8):** `OrderCreated`, `OrderStockReserved`, `OrderPaymentCompleted`, `OrderConfirmed`, `OrderShipped`, `OrderDelivered`, `OrderCancelled`, `OrderFailedDomainEvent`.
**External events (6):** `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderCancelledEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderFailedEvent` on `ordering.orders`. `OrderStockReserved` and `OrderPaymentCompleted` stay internal — the saga drives those transitions directly.
**Pattern:** Rich SmartEnum-guarded status FSM; factory from `BasketSnapshot`; multi-event aggregate transitions.
**Migration:** greenfield. The former `services/Order/` (Weather-specific `AlertSubscriptionOrder`) was deleted pre-dispatch along with its sagas and Kafka topics.

### 5.4 Inventory → [bc-design/inventory.md](bc-design/inventory.md)

**Aggregate:** `StockItem` (keyed by ProductId; state is the fold over the event stream).
**ES events (6, persisted as write model):** `StockItemInitializedDomainEvent`, `StockReceivedDomainEvent`, `StockReservedDomainEvent`, `ReservationConfirmedDomainEvent`, `ReservationReleasedDomainEvent`, `StockAdjustedDomainEvent`.
**Value objects:** `Quantity`, `ReservationId`, `ReservationInfo`, `StockItemSnapshot`.
**Event store schema:** `inventory.stock_events (StreamId, Version, EventType, Payload, OccurredAtUtc, AppendedAtUtc, CorrelationId)` with PK `(StreamId, Version)`.
**Read projections:** `inventory.current_stock_levels` and `inventory.reservation_audit` built by in-process `IDomainEventHandler` upserts in the same transaction as event append.
**External events (5):** `StockLevelChangedEvent` on `inventory.stock-events`; `StockReservedEvent`, `StockReservationFailedEvent`, `ReservationConfirmedEvent`, `ReservationReleasedEvent` on `inventory.reservations`.
**Pattern:** Full Event Sourcing ([ADR-0006](adr/0006-event-sourcing-for-inventory.md)). Aggregate rehydrates from stream; commands append events; projections catch up asynchronously within same transaction. Reservation TTL = 15 min, background `ReservationExpiryWorker` publishes `ReservationReleasedEvent(reason: Expiry)`.

### 5.5 Payments → [bc-design/payments.md](bc-design/payments.md)

**Aggregate:** `PaymentTransaction` (one per saga-scoped payment lifecycle).
**Value objects:** `Money`, `PaymentMethodId`, `GatewayResponseCode`, `FailureInfo`.
**SmartEnum:** `PaymentStatus` (Requested → Authorized → Captured → Completed; off-ramps Failed / Voided / Refunded); `FailureReason` (GatewayDeclined / GatewayTimeout / InsufficientFunds / FraudSuspected / Cancelled / Unknown).
**Internal events (8):** `PaymentAuthorized/AuthorizationFailed`, `PaymentCaptured/CaptureFailed`, `PaymentCompleted`, `PaymentRefunded`, `PaymentVoided`, `PaymentFailedDomainEvent`.
**External events (8) on `payments.transactions`:** `PaymentAuthorizedEvent`, `PaymentAuthorizationFailedEvent`, `PaymentCapturedEvent`, `PaymentCaptureFailedEvent`, `PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`, `PaymentVoidedEvent`. (`PaymentRequestedEvent` was renamed to `RequestPaymentCommand` and moved to `payments.payment-commands` per [ADR-0023](adr/0023-payments-event-vs-command-classification.md).)
**External commands (5) on `payments.payment-commands`:** `RequestPaymentCommand` (Checkout-saga → PaymentProcessingSaga), `AuthorizePaymentCommand`, `CapturePaymentCommand`, `RequestRefundCommand`, `VoidPaymentCommand` (PaymentProcessingSaga → Payments).
**Pattern:** Saga sub-orchestration — `PaymentProcessingSaga` (under `saga/SagaOrchestrators/Payments/PaymentProcessingSaga/`) is the sole caller of Payments commands; Checkout saga delegates via `RequestPaymentCommand` and awaits guaranteed feedback (`PaymentCompletedEvent` / `PaymentFailedEvent`) to drive its FSM. PCI scope minimization: only gateway-issued tokens stored, no PAN/CVV.
**Integration:** `IPaymentGateway` port with stub adapter (`StubPaymentGateway`) for reference solution; swap to real gateway (Stripe/Adyen/Braintree) via DI in production.
**Folder:** `services/Payments/` (renamed from `services/Payments/` in Wave 0).

### 5.6 Invoicing → [bc-design/invoicing.md](bc-design/invoicing.md)

**Aggregates:** `Invoice` (4-state FSM: Draft → Issued → Delivered → Archived; Cancelled off-ramp) + `CreditNote` (3-state: Issued → Delivered → Archived).
**Value objects:** `InvoiceNumber` (format `INV-YYYY-NNNNNN`), `CreditNoteNumber` (format `CN-YYYY-NNNNNN`), `InvoiceLine`, `VatLine`, `VatRate`, `PdfBlobRef`, `CancellationInfo`.
**SmartEnums:** `InvoiceStatus`, `CreditNoteStatus`, `DeliveryChannel` (`Email`, `None`), `CreditNoteReason`.
**Internal events (7):** `InvoiceCreatedDomainEvent`, `InvoiceIssuedDomainEvent`, `InvoiceDeliveryRequestedDomainEvent`, `InvoiceDeliveredDomainEvent`, `InvoiceCancelledDomainEvent`, `CreditNoteCreatedDomainEvent`, `CreditNoteIssuedDomainEvent`.
**External events (4) on `invoicing.invoices`:** `InvoiceIssuedEvent`, `InvoiceDeliveredEvent`, `InvoiceCancelledEvent`, `CreditNoteIssuedEvent`. **10-year retention** (EU VAT norm).
**Pattern:** **Async multi-source convergent enrichment** — `pending_invoices` projection buffers `OrderConfirmedEvent` + `PaymentCapturedEvent`; when both halves arrive, `IssueInvoiceCommand` fires (aggregate created, number allocated via gap-free Postgres allocator, PDF generated via QuestPDF, uploaded to Azurite/Azure Blob, outbox row written). Credit notes mirror with `OrderCancelledEvent` + `PaymentRefundedEvent`. No saga — projection + idempotent command handler is simpler than multi-step orchestration.
**Patterns showcased:** document generation + write-once blob storage; legal retention; gap-free numeric sequencing (transactional allocator); idempotent external re-emission (delivery-attempt log); convergent enrichment without sagas.
**Infrastructure:** Azurite (local Azure Blob emulator) + nginx-cdn (local CDN emulation) for PDF delivery; Azure SAS URLs (10-minute TTL). Aspire AppHost: `AddAzureStorage("storage").RunAsEmulator()` swaps to real Azure Blob Storage in production.
**Folder:** `services/Invoicing/`.

---

## 6. Event Catalog — Overview

> **Detail:** [bc-design/events-catalog.md](bc-design/events-catalog.md) — complete master catalog (34 events across 9 topics), full Avro `.avsc` per new event, docker-compose delta, outbox-relay and inbox registration strategy.

### 6.1 New Kafka Topics (summary)

> **Canonical registries:**
> - [kafka-topology.md](kafka-topology.md) — topology (partitions, retention, class, key, rationale) per topic.
> - [bc-design/events-catalog.md § 3](bc-design/events-catalog.md) — inverse view (each topic → events flowing through it).
>
> This section is a per-BC quick reference; topology and event-mapping detail live in the canonical registries.

12 new topics across the new BCs:

- **Catalog:** `catalog.products`, `catalog.categories`
- **Basket:** `basket.sessions`
- **Ordering:** `ordering.orders`, `ordering.order-commands`
- **Inventory:** `inventory.stock-events`, `inventory.reservations`, `inventory.reservation-commands`
- **Payments:** `payments.transactions`, `payments.payment-commands`
- **Invoicing:** `invoicing.invoices`
- **Notifications:** `notifications.email-events` (new; outbound delivery confirmations)

**Reuses existing:** `notifications.email-commands`.

### 6.2 Master Event Table (high-level; full table in events-catalog.md § 2)

38 events total across Catalog / Basket / Ordering / Inventory / Payments / Invoicing. Full Avro schemas for all new events are specified in [events-catalog.md § 5](bc-design/events-catalog.md).

### 6.3 Docker-compose Delta

New `kafka-topics --create ...` lines appended to [docker-compose.yaml](../docker-compose.yaml). Exact copy-paste block in [events-catalog.md § 4](bc-design/events-catalog.md), including the 10-year retention flag on `invoicing.invoices`.

Also: one `outbox-relay-*` container per service schema (`outbox-relay-saga`, `outbox-relay-basket`, `outbox-relay-catalog`, `outbox-relay-inventory`, `outbox-relay-invoicing`, `outbox-relay-notifications`, `outbox-relay-ordering`, `outbox-relay-payments`). Each binds to its service's `OutboxRelay__SchemaName`.

New containers: `azurite` (local Azure Blob Storage emulator for invoice PDFs; first-party Aspire integration via `AddAzureStorage().RunAsEmulator()`) + `nginx-cdn` (local CDN emulation fronting Azurite) — see [ADR-0017](adr/0017-blob-storage-cdn.md).

---

## 7. Use Case Catalog — Overview

> **Detail:** [bc-design/use-cases.md](bc-design/use-cases.md) — per-service tables of commands and queries with full request/response shapes, validators, handlers, flow descriptions.

### 7.1 Commands/Queries Summary

| Service | Commands | Queries |
|---------|----------|---------|
| Catalog | 10 (CreateProduct, UpdatePrice, Describe, Discontinue, Reactivate, AddImage, RemoveImage, CreateCategory, ReparentCategory, DeleteCategory) | 4 (GetProductById, SearchProducts, GetCategoryTree, GetProductsByCategory) |
| Basket | 6 (AddItem, RemoveItem, ChangeQuantity, RefreshPrices, Clear, Checkout) | 1 (GetBasketByUserId) |
| Ordering | 8 total — 5 saga-driven (CreateOrder, ConfirmOrder, CancelOrder, MarkOrderFailed) + 3 HTTP admin (MarkOrderShipped, MarkOrderDelivered) | 2 (GetOrderById, GetOrdersByBuyer) |
| Inventory | 6 total — 1 event-driven (InitializeStockItem) + 3 saga-driven (ReserveStock, ConfirmReservation, ReleaseReservation) + 2 HTTP admin (ReceiveStock, AdjustStock) | 4 (GetStockLevel, GetStockLevelsBulk, GetReservationById, GetReservationsByOrder) |

### 7.2 Saga-Command Intake Pattern (per service)

Saga-issued commands reach Ordering and Inventory via Kafka:
1. Saga publishes Avro command to `{service}.{aggregate}-commands` topic.
2. Service has a KafkaFlow consumer for that topic with **Inbox middleware** for idempotent consumption.
3. Consumer deserializes Avro → constructs internal `ICommand` → dispatches via handler (MediatR-style via `Platform.CQRS`).
4. Handler runs normal flow (repository load → aggregate method → SaveChangesAsync → outbox message for response event).

Full mechanism documented in [use-cases.md § Ordering § 3.3](bc-design/use-cases.md) and § Inventory § 4.3.

---

## 8. Checkout Saga — Overview

> **Detail:** [bc-design/checkout-saga.md](bc-design/checkout-saga.md) + [ADR-0004](adr/0004-checkout-saga-topology.md).
> **Diagram:** [Checkout Saga state mermaid source](diagrams/checkout-saga-state.md) — open interactively in drawio via the link in § 10.2.

### 8.1 Topology

- **Placement:** `saga/SagaOrchestrators/Checkout/` (per ADR-0001 + ADR-0004).
- **Trigger:** `BasketCheckoutInitiatedEvent` → `BasketCheckoutInitiatedConsumer`.
- **Correlation:** `CorrelationId` = `BasketCheckoutInitiatedEvent.BasketCorrelationId` (UUID v7).
- **Persistence:** MassTransit EF Core repository → PostgreSQL `saga` schema → `checkout_saga_states` table with `RowVersion` optimistic concurrency.
- **Consumer group:** `saga-checkout`.

### 8.2 Happy-path states

1. `Initial` → 2. `AwaitingOrderCreation` → 3. `AwaitingStockReservation` → 4. `AwaitingPayment` → 5. `AwaitingConfirmation` → 6. `Confirmed` (terminal success)

### 8.3 Compensation states

7. `CompensatingStockReservations` — releases all active reservations
8. `CompensatingPayment` — requests refund (only when confirmation fails post-capture)
9. `Compensated` (terminal — money moved then refunded)
10. `Failed` (terminal — no money moved)
11. `CompensationStuck` (terminal abnormal — ops alert)

### 8.4 Step ordering (stock BEFORE payment)

Per [ADR-0004](adr/0004-checkout-saga-topology.md). Industry-standard UX: reservation at checkout-entry guarantees availability through payment wait. Inventory 15-min TTL bounds worst-case hold time.

### 8.5 Compensation matrix (summary; full matrix in checkout-saga.md § 6)

| Failure point | Compensation |
|---|---|
| Order creation fails/times out | Terminal `Failed`. No side effects. |
| Stock reservation partial/full failure | Release any made reservations + CancelOrder → `Failed`. |
| Payment fails | Release all reservations + CancelOrder → `Failed`. |
| Confirmation fails | Refund (via PaymentProcessingSaga) → release reservations → CancelOrder → `Compensated`. |

### 8.6 Timeouts (defaults, configurable via `SagaOptions.Checkout`)

| State | Timeout |
|-------|---------|
| `AwaitingOrderCreation` | 30 s |
| `AwaitingStockReservation` | 60 s |
| `AwaitingPayment` | 90 s |
| `AwaitingConfirmation` | 30 s |
| `CompensatingStockReservations` / `CompensatingPayment` | 300 s |

---

## 9. BFF Aggregation — Overview

> **Detail:** [bc-design/bff.md](bc-design/bff.md)

### 9.1 Endpoints

| Endpoint | Auth | Upstream calls | Caching |
|----------|------|----------------|---------|
| `GET /api/bff/product-page/{productId}` | Public | Catalog + Inventory (parallel) | Tag `product-{id}`, 5 min TTL, fail-safe 30 min |
| `GET /api/bff/basket` | Required (UserId from JWT) | Basket + Catalog (current prices) + Inventory (availability) | Tag `basket-bff-{userId}`, 15 s TTL, fail-safe 2 min |
| `GET /api/bff/order-summary/{orderId}` | Required (own orders or admin) | Ordering + Catalog (item details) + Payments (payment status) | Tag `order-{id}`, 30 s TTL, fail-safe 5 min |
| `GET /api/bff/home-page` | Public | Catalog (featured) + Inventory (stock highlights) | Tag `home-page`, 5 min TTL, fail-safe 30 min |

### 9.2 Cross-cutting

- **Resilience** (Polly): per-call timeout 2s (batch 10s), retry max 2 (exponential backoff), circuit breaker opens after 5 failures / 10 s, half-opens after 30 s.
- **Cache invalidation**: BFF subscribes via consumer group `bff-group` (one-group-per-service rule per [events-catalog.md § 3.1](bc-design/events-catalog.md)) to `catalog.products`, `catalog.categories`, `inventory.stock-events`, `ordering.orders`, `basket.sessions` (five topics). Translates events to `FusionCache.RemoveByTagAsync(...)`. The BFF registers **no inbox dedup** — `RemoveByTagAsync` is idempotent, so at-least-once redelivery is harmless; it subscribes to published-language event topics only (never saga-internal streams such as `inventory.reservations`). Per-topic handler→tag mapping and rationale are canonical in [bc-design/bff.md § 2.2](bc-design/bff.md).
- **Auth pass-through**: `DelegatingHandler` forwards JWT bearer to upstream services.
- **Observability**: OpenTelemetry traceparent auto-propagated via `HttpClient` instrumentation.

---

## 10. Visual Artifacts

Two tools, two purposes:

- **Miro** (collaborative whiteboard) hosts the **sticky-note** artifacts — Event Modeling and Example Mapping — where colour coding and free-form layout are the teaching medium.
- **Drawio** (via MCP) hosts the **technical** diagrams — Context Map, Saga State Machine, BC Map — where rigorous shape semantics (classes, states, swimlanes) matter. Mermaid sources are version-controlled under `docs/diagrams/` and opened interactively in drawio when needed.

Board: <https://miro.com/app/board/uXjVGhsSJ6k=/> ("DotNetAtlas eShop").

### 10.1 Miro — Sticky-note artifacts

| Artifact | Type | Description |
|---|---|---|
| [Event Modeling — Checkout Flow](https://miro.com/app/board/uXjVGhsSJ6k=/?moveToWidget=3458764668359842186) | 21 real sticky notes + 5 swim-lane labels | Chronological left-to-right walk of the checkout workflow. Swim lanes: Commands (blue) → Events (orange) → Views (green) → Policies (yellow) → Externals (pink). |
| [Example Mapping — Oversell prevention](https://miro.com/app/board/uXjVGhsSJ6k=/?moveToWidget=3458764668359842602) | Frame with 16 sticky notes | Showcase session: "The one where concurrent reservations race on the last units." Given/When/Verify/Then layout with Question (pink) and Answer (gray) on the right, matching the reference pattern. The other 9 Example Mapping sessions live as markdown under [bc-design/example-mapping/](bc-design/example-mapping/). |

Sticky-color legend (Miro native sticky colors):

- 🟡 **Yellow** — Stories / policies / business rules
- 🔵 **Blue** / Light blue — Commands / user actions
- 🟢 Light green — Read models / views / given-state
- 🟠 **Orange** — Events / outcomes
- 🌸 **Pink** — Questions / hot spots
- ⚪ Gray — Notes / answers

### 10.2 Drawio — Technical diagrams

Mermaid sources are in `docs/diagrams/` (git-tracked). To view or edit interactively, pass the mermaid content to `mcp__drawio__open_drawio_mermaid`.

| Diagram | Mermaid source | Live drawio editor |
|---|---|---|
| Context Map — BC integration patterns | [docs/diagrams/context-map.md](diagrams/context-map.md) | [Open in drawio](https://app.diagrams.net/?grid=0&pv=0&border=10&edit=_blank#create=%7B%22type%22%3A%22mermaid%22%2C%22compressed%22%3Atrue%2C%22data%22%3A%22pVbbctowEP0azyQPdFxTQngEAW1mkibFad%2BFvLZVjOXKci79%2Bq7km2wwIc2MB6SV95y9W6PRyHHniqsEnPHc8VzwY5HhPxGpgheFqzuq987Kc65dZ%2FYF1wuCPxzPI0kVFynuMqoUyDRHsJGBDBPxzGIqNcLtBgUao35%2B5iAvnMlC%2FzuT5WXveLFe46H%2BHZOtdDz8X82jSEJU0k2WWqGrkxdbtCaLjekSUN%2FxPLNCPFGkAQStVzmeVSgWhP0QqmgiIoSpV60p5MfGx1c2QDXkgxS%2FgVl2DSAuaL4Dpf0qFy3eBgKOFrm0chFN1gGek9vTiPcyAMlTbWSztFA508HwFVWFRl%2F7d6fhbtInSJWQr4jXrlvAlRZpRFFIZmj7aJAGJxNzL1kMuaqKxmSoK%2FLci5xGVGuBfOIMLt9OVAxsJwrla73pAp%2BuZKmfYfUH%2BrpHt1rtSoBZZZDn6OYgzFvefoUUc8JKP%2BuN545M6RS5rsc3fFvzlKZM13KzOvX6d6F4yJkJZY5Kvf1Z9puORCNHmHBnSr49Pj74%2BF815WGbdl%2FFrdRdYRTqxjlXyVs%2FS47FX7KVTfIuwqYJ3qXVlvpBMCojUPlTpa17EpOc0iyPhTrlZy21lLGmgoIpgtwKjrEfbUYLwFeC7W7hCRIS0zSCw0AP2O%2BMl%2BbRIKWwbpKblCvemtPpnZ5D9lEXsvTIhN%2B8q%2BONkzbkct%2Fudf0muGD70%2BlqxF0OI65D1yOxJGvKk485swE9e8DEetCbDSRAc7DdOSeTNo8hqMgO7O%2FxDDh0tk%2FVUNvAnwKHbQ1nz74ulH3SRZoXKhaS%2FwUrsZkqZLv%2FJbhtf4ifXjtM9STrEtbSo2brWaVjmP%2B%2F2dUJEfssAXUq3mF5VTgj3lahNh1aVulBVfoxzzJrv4SEP4GsebqD%2BoCIJTTPlxDqZXmhCXmSmOvaeE1W8%2Bk1XhjwGyp25R1uTMMpuF4jHD3zQMV45GUvKGQiEbJ80cMv0CBX1HyyWrrZ1XJ2verRecEV%2FRx%2BlE5Ic19pueYrcrXquzZ2J1u6%2FShXebG1ougtpotpj2obuDN3dj7VETZrOHuknsakLRxiDweT2%2BMgTXuQbqm0SRog74wE0ukPUn4XTdiPa1d3AQzWPw%3D%3D%22%7D) |
| Checkout Saga — State Machine | [docs/diagrams/checkout-saga-state.md](diagrams/checkout-saga-state.md) | [Open in drawio](https://app.diagrams.net/?grid=0&pv=0&border=10&edit=_blank#create=%7B%22type%22%3A%22mermaid%22%2C%22compressed%22%3Atrue%2C%22data%22%3A%22rVZhb5swEP01SN0kqihdp%2B1jmrbaPmSdmv2BEzmIFbCZbTLl3%2B%2FsgI1NSGGdhDA67t69u3c2pGmaLFaa6RKTu1WyXKz3mB1Eo%2BlxCwXQkjwtky%2BL5OsnY9KgkdYNZHvGkUJTC6CM%2FZFBIaFKj0uymEB33T98TO4f6Ymc755oXf0BphkvXuQO5VoiaCa48bQcHkAdUHdMvnNyJfidQQ2Bx2DiNFstssMrKpTHMJMPPMPPwH4GVlJQgORty2e6%2F2IVmlaO8r7AK6b%2BE04Vcu3yQFlaHVzgKPEr4GtR1cgVODcHHwfNrcjTjQtZC54zWYX9b90NnxLHNRiijlfQ%2Bs4lHtHzeaw5FtqbZ6J51rGw%2FZCp5C91YWQ%2Bz3MjsURQ1nzDhTXkDd99mIHr3l0Fh1yjfB8%2B9WGrm56y%2FTeTWvIPY%2FNq%2BQ50nYoaUz7DtVJc4OqHq8PqzsrQ04l53S3UZqJvj3Qc0ZUhakV3xrU5b7gd6kExXNgvg2TF3vRG5EGB0Qehd32Duj7RStgV41Becb0RZu%2FRQ9aeF%2FFcId91TCbx88KMZPxh9kglOJ7sOjxsA3q1xLR205ETeCPfzbGv6XjuTcDRmD4n3e57g7RQOs3C8%2Bp%2FU3cjNs7jxY4YlCj1eeYU%2FX3cZu0%2FwK16I3wDvIHSDukRlWZFV4rE3w2Tgxb4Uv4C%22%7D) |
| BC Map — Entities per bounded context | [docs/diagrams/bc-map-entities.md](diagrams/bc-map-entities.md) | [Open in drawio](https://app.diagrams.net/?grid=0&pv=0&border=10&edit=_blank#create=%7B%22type%22%3A%22mermaid%22%2C%22compressed%22%3Atrue%2C%22data%22%3A%22zVjtTtswFH2aSDCpqAtDbD%2FbUrZKYzAC%2B%2B8ml9Sra0eOw8jbz19p45A6DhPSpKp1zPG539c3TCaTaDoTWBCIzmdRPIVkwwr5O1%2FIrxukltEyjj5Poy%2Bf5HpJJRZDKZcFcPm9ZhXNIJOrlFEBL0LSTTRpSlBZXmGUc7STj4qo%2Beg%2Fyd87zrIqFWrzct7BuJ%2B5ha4yo%2BbXCmf%2BA8m2MtBScExzP%2FgH2kE4%2Bo7j1MJvGIXaj14gATnj9QjVBRJVadDWbrvVOXZ5dcSxjcwQz47Xb6S3EAcqeqWcXw8dFRufoKP2N9myQzmE%2BOCRk3CDZkQ8qEQPPnCFy4Kg%2BpZnsmT0KUxFvy395sxRuYWgMnksgYcH8hfwEjNq4FWPUi58JWBn09JopDaii3l00Y1DN8U4yPBnM%2FEo0uAYtkQEGD6%2BP1BUlBsm3DJrNr1Hf1ZItcF6IJbH83IvJsCw%2F6iPFaLi3ij2G23yPsBUDQyP4Lyqx8DbXVVL6u%2BpnUMbXBTSkbMs41Da082DXztMyJsOLhjnQJCQhekz7miOGS%2B%2BW9nIdIzVbKBTzd%2BLKBbjUuzthdV4NsDkRHAA8dFYYR7igYgolSxc9rEBj7JSILJg2QBuIQcnwes%2BoK%2BYEsHS7btF95Z%2BQzQ76n4XfA%2FysnmGIPjkiREzJj5xpnSX%2FQlezYXH42tk6aIIMbwFH2H8uPYzmKwufPlSYJmivd2zm6CjZj2dEctnOWOFeCbRfn%2FjiDBspFLjoS5GjYc1Ycjqs64FHLHc3Wy9OcSxrOVY5dgkOl86O%2B0x2PADYTRXXhNsHOMHs9MdLDXrBnV1PgxsHi53wNFM6h0KYdpP1yA9BvfMF5a3MC9ttvqkYxBhuVydzBbfT%2Ftd0WI5OzOSWk5qj00rVdgcnkBO%2Bmm3ne2vf48rnDvL5wkvWWx2WneByUJ7h5t8U1lgruY%2BaqvDv9h7ONOwOD1b88i6yHNZWbqmsMI%2BY3Q4a%2BdlU9Quu8Pl8WinYVqpKN12PRpK6LYZzef2dJfWld94Yh%2B%2BwxSmfVmiHOmg70ef1yVPmQAtlHer9ZDN%2Br8WJzUQwv6cGiGLn%2FeJjhZSggrOfkNqtOre%2Bo6AdgXv14Y%2BlyMDtez3kOFyspZ%2B1Y4QkG4oThGxYK%2BIVi7rpUlRI2NNKjhtmqi%2BDuLpdXIzwNiJ5YqqcJn%2BZ2gZRzRviJtgJqziqVI%2F%2Fgs%3D%22%7D) |

**Rendering locally:** any mermaid-capable markdown viewer (VS Code + Markdown Preview Mermaid, GitHub, GitLab) renders the source files directly. The drawio links above open an interactive editor with the diagram pre-loaded.

**Type keywords in use:**
- Context Map → `flowchart LR` with colored `classDef` styles
- Saga State Machine → `stateDiagram-v2` with notes
- BC Map → `classDiagram` with cross-BC dashed references

---

## 11. Cross-Cutting Concerns

### 11.1 Authentication & Authorization

- **Identity provider**: Keycloak (existing in [docker-compose.yaml](../docker-compose.yaml)). Sole source of `UserId`; no Accounts BC (see [ADR-0005](adr/0005-customer-data-in-ordering.md)).
- **Token propagation**: JWT bearer forwarded by BFF to upstream services via `DelegatingHandler`. Every internal service validates JWT independently.
- **Claims**: `sub` → `UserId` (Guid), `realm_access.roles` → role list. FastEndpoints `[FromClaim(ClaimTypes.NameIdentifier)]` binds UserId to request DTOs.
- **Authorization policies**: Public endpoints (browse catalog, home page). User endpoints (basket, orders by self). Admin endpoints (catalog write, stock ops, order ship/deliver).

### 11.2 Outbox / Inbox Registration (per service)

Each service registers:
- **Outbox**: `services.AddOutbox<TDbContext>()` + per-service `OutboxRelay` container in docker-compose. Pattern: [Platform.ReliableMessaging.Outbox.EFCore](../platform/Platform.ReliableMessaging.Outbox.EFCore/).
- **Inbox**: `services.AddInbox<TDbContext>(messageTypes...)` — KafkaFlow middleware that dedupes consumed messages by `MessageId`. Pattern: [Platform.KafkaFlow.Inbox.EFCore](../platform/Platform.KafkaFlow.Inbox.EFCore/).
- **Message types to register per service**: full list in [events-catalog.md § 7](bc-design/events-catalog.md).

Basket's outbox lives in a PostgreSQL `basket` schema (side-car to the Redis aggregate store). Catalog, Ordering, Inventory use their own schemas (`catalog`, `ordering`, `inventory`).

### 11.3 Observability

Reused unchanged from existing services (`Platform.ServiceDefaults`):
- **Distributed tracing**: OpenTelemetry → OTLP collector → Jaeger (Tempo in full profile).
- **Metrics**: OpenTelemetry → Prometheus → Grafana.
- **Logs**: structured logging → Seq (dev) or centralized store.
- **Kafka instrumentation**: `KafkaFlow.OpenTelemetry` auto-instruments consumers; outbox relay emits span for publish.
- **Saga spans**: each state transition emits an activity `SagaOrchestrators.Checkout.StateTransition.{From}.{To}`. Counters: `saga.checkout.initiated/confirmed/failed/compensated/stuck`.
- **Health checks**: `AspNetCore.HealthChecks.Kafka`, `.EntityFrameworkCore`, `.ApplicationStatus` per service. Standardized `/api/healthz` (liveness) and `/api/readiness` (readiness) — see [`Platform.ServiceDefaults.WebApplicationExtensions.MapPlatformHealthCheckEndpoints`](../platform/Platform.ServiceDefaults/WebApplicationExtensions.cs).

**Health probe contract.** `/api/healthz` (liveness) and `/api/readiness` (readiness) are the external probe contract — orchestrators (Kubernetes kubelet, ALB target groups, monitoring systems) hit them over HTTP from outside the container. In compose local-dev, the API images run with an alpine base override (`mcr.microsoft.com/dotnet/aspnet:10.0.0-alpine3.22`, selected via the `BASE_IMAGE` build-arg in [`docker-compose.yaml`](../docker-compose.yaml)) and a compose-level `HEALTHCHECK` against `/api/readiness` — gives `docker compose ps` a `(healthy)` signal and enables `depends_on: condition: service_healthy` chains. Production builds the chiseled image (`mcr.microsoft.com/dotnet/aspnet:10.0.0-noble-chiseled-extra`, no shell, no probe tooling); production probing is handled by the orchestrator's pod-level `httpGet:` probes, externally to the container, matching the chiseled image's design intent.

### 11.4 Testing Layers (per service)

| Layer | Framework | Purpose |
|-------|-----------|---------|
| UnitTests | xUnit | Domain logic, handlers in isolation (mock repositories) |
| IntegrationTests | xUnit + Testcontainers | Handlers + DB + messaging (real Postgres, real Kafka, real Redis where applicable). Uses [Platform.Test.Framework](../platform/Platform.Test.Framework/). |
| ArchitectureTests | xUnit + NetArchTest (or similar) | Enforce layering rules: Domain has no external refs, Application only references Domain+Platform.CQRS, Infrastructure depends on Application, Api on both. Also enforce "no cross-BC direct references". |
| FunctionalTests | xUnit + WebApplicationFactory + Testcontainers | Full HTTP stack end-to-end (FastEndpoints → handlers → DB/Kafka). |

Saga-specific: MassTransit `SagaTestHarness<CheckoutSagaState>` for state machine tests; integration tests use Testcontainers with real Kafka + Postgres + saga binary.

### 11.5 Package Version Policy

Per CLAUDE.md: centralized in `Directory.Packages.props` at root, `services/`, `saga/`, `platform/`, and `test/` levels. New BCs must **add package references to the correct level** — `services/Directory.Packages.props` for service-specific packages (FastEndpoints.*, KafkaFlow.*, Npgsql, etc.). Lock files committed; CI enforces `dotnet restore --locked-mode`.

### 11.6 Formatting

CI-enforced:
```bash
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
```

### 11.7 Async vs Sync — decision matrix

When implementing a new interaction, use this rule in order. If after the rule you're still unsure, **ask** before implementing.

| If the interaction is… | Choose | Why |
|---|---|---|
| BFF queries a service's state during a user-initiated request | **Sync HTTP** | The caller blocks on the response; latency budget matters; no durability needed |
| One BC queries another BC's state to enrich a request (e.g., Basket → Catalog product lookup) | **Sync HTTP via ACL adapter** | Single-request consistency; ACL translates the remote model into the caller's VO (`ProductSnapshot`) |
| A BC raises a business moment that other BCs may react to (e.g., `ProductCreatedEvent`, `OrderConfirmedEvent`) | **Async Kafka via transactional outbox** | Decoupled consumers; durable delivery; no back-pressure on the producer |
| A saga drives a multi-BC workflow | **Async Kafka commands + events** | ADR-0001: centralized saga owns orchestration; services are command responders |
| Fire-and-forget side effect (notifications, audit log) | **Async Kafka** | No reply needed; consumer lag is acceptable |
| Admin / ops one-shot with no rollback concern (e.g., "re-index search view") | **Sync HTTP** | Direct feedback to the operator |
| Strict consistency required across aggregates | **Rethink the aggregate boundary** — neither sync nor async fixes a bad boundary. Consult DDD architect before proceeding. |

**Anti-patterns (forbidden):**

- Sync HTTP across a saga-coordinated boundary (violates ADR-0001).
- Publishing an external Kafka event without the transactional outbox (race between DB commit and message send).
- Consuming another BC's internal `*DomainEvent` as if it were an external event (internal events don't cross process boundaries — they have no Avro schema).
- Two-way synchronous chain spanning ≥ 3 services (turn it into a saga).

**Idempotency rule:** every sync command accepts the `Idempotency-Key` header ([use-cases.md § Conventions](bc-design/use-cases.md)); every async consumer uses the inbox-middleware dedup ([kafka-dlt-strategy.md](bc-design/kafka-dlt-strategy.md)).

**When in doubt:** ask the user before committing to a transport. The cost of asking is small; the cost of a wrong transport choice accumulates across every subsequent interaction that mirrors it.

### 11.8 Build

```bash
dotnet build -m
dotnet restore --locked-mode
```

---

## 12. Consolidated Ubiquitous Language

> Detailed per-BC glossaries: [glossary-catalog.md](bc-design/glossary-catalog.md), [glossary-basket.md](bc-design/glossary-basket.md), [glossary-ordering.md](bc-design/glossary-ordering.md), [glossary-inventory.md](bc-design/glossary-inventory.md).
> Shared cross-context terms: [eshop-ubiquitous-language.md](eshop-ubiquitous-language.md).

### 12.1 Conventions

- **Basket** is the official term throughout code, APIs, and docs. `Cart` is US-English synonym for marketing/UX copy only.
- **Buyer** (inside Ordering BC) vs **Customer** (marketing/BFF copy) vs **User** (identity/auth context) — all refer to the same person.
- **UserId** is the stable Keycloak `sub` claim (Guid). No duplicate user registry exists in v1.

### 12.2 Core terms (cross-cutting)

| Term | Definition |
|------|------------|
| **Bounded Context (BC)** | A service with its own language and aggregate boundary. In v1 we have Catalog, Basket, Ordering, Inventory as new BCs; Payments and Notifications are existing BCs reused as-is. |
| **Aggregate Root** | The transactional consistency boundary inside a BC. Private parameterless constructor; public static factory method; state changes via domain methods that raise internal domain events. |
| **Internal Domain Event** | C# record inheriting `DomainEvent`; suffix `DomainEvent`; dispatched in-process via `IDomainEventHandler<T>`; no Avro schema; owned by the BC; changeable without external consultation. |
| **External Summary Event** | Enriched, coarse-grained event with Avro schema; published to Kafka via outbox; contractually stable (treated as API); suffix `Event`. |
| **Transactional Outbox** | `ITransactionalOutbox<TDbContext>.AddOutboxMessage(topic, key, event)` persists outbox message in same DB transaction as aggregate save. Outbox relay dequeues and publishes to Kafka. |
| **Inbox (idempotent consumer)** | KafkaFlow `InboxMiddleware` dedupes consumed messages by `MessageId`; stored in `InboxMessage` table per service. |
| **Correlation ID** | UUID (v7) shared across all events in a single business workflow (checkout flow: BasketCheckoutInitiatedEvent.BasketCorrelationId propagates through Order, Payment, Reservation events). |
| **Saga** | Centralized orchestrator (per ADR-0001) for multi-BC workflows. MassTransit state machine persisted to PostgreSQL `saga` schema; drives BCs via command topics; reassembles response events. |
| **ACL (Anti-Corruption Layer)** | Adapter pattern that translates external model into internal model. Basket's `IProductCatalogQueryPort` / `ProductCatalogHttpAdapter` is the canonical example. |
| **Money** | `(decimal Amount, string Currency)` VO. Currency is ISO 4217 (e.g., `USD`). Amount uses decimal(19,4) precision in Avro and DB. |
| **Address** | `(Street1, Street2?, City, State?, PostalCode, CountryCode)` VO. CountryCode is ISO 3166-1 alpha-2 (e.g., `US`, `CZ`). |
| **SmartEnum** | Ardalis.SmartEnum — type-safe enum that encapsulates business rules (e.g., `OrderStatus.CanTransitionTo(target)`, `SubscriptionTier.MaxSubscriptions`). |
| **Result pattern** | FluentResults — handlers return `Result` / `Result<T>` for user-actionable errors. `DataIntegrityException` thrown for corrupted state; caught by global exception middleware. |

### 12.3 Per-BC glossaries

See the six linked glossary files in [docs/bc-design/](bc-design/):
- [glossary-catalog.md](bc-design/glossary-catalog.md) — 14 terms (Product, SKU, Category, Category Path, Category Breadcrumb, Price, Brand, Product Status, Discontinued, Reactivation, Read View, Active, Dimensions, Image Reference).
- [glossary-basket.md](bc-design/glossary-basket.md) — 14 terms (Basket, BasketItem, ProductSnapshot, BasketTotal, Money, Frozen-pricing contract, Checkout, BasketCorrelationId, Version, Basket expiry, Catalog Unavailable, ACL, Redis-backed aggregate, Outbox side-car).
- [glossary-ordering.md](bc-design/glossary-ordering.md) — 33 terms.
- [glossary-inventory.md](bc-design/glossary-inventory.md) — 37 terms grouped by Aggregate/state, Reservations, Events/ES, Commands/write-path, External surface, Value objects.
- [glossary-payments.md](bc-design/glossary-payments.md) — 30 terms.
- [glossary-invoicing.md](bc-design/glossary-invoicing.md) — 32 terms.

---

## Appendix A — Service Scaffolding Checklist

For each new service (`services/Catalog`, `services/Basket`, `services/Ordering`, `services/Inventory`), the implementation agent must:

### B.1 Project structure

```
services/{Bc}/
├── {Bc}.Api/              # FastEndpoints, Program.cs, appsettings*.json
├── {Bc}.Application/      # Command/Query handlers, validators, DI entry (ApplicationDependencyInjection)
├── {Bc}.Domain/           # Aggregates, VOs, SmartEnums, internal domain events
├── {Bc}.Infrastructure/   # EF Core (or Redis) repositories, Kafka consumers, MessagingDependencyInjection
└── README.md
```

### B.2 Project references

Standard 4-layer project-reference shape used by every service in the solution:
- `Domain` → `Platform.SharedKernel`
- `Application` → `Domain` + `Platform.CQRS` + `Platform.ReliableMessaging.Outbox.EFCore` + `Platform.SchemaRegistry.Contracts`
- `Infrastructure` → `Application` + `Platform.ReliableMessaging.Inbox/Outbox.*` + `Platform.KafkaFlow.*` + `Platform.ServiceDefaults`
- `Api` → `Application` + `Infrastructure` + `Platform.ServiceDefaults`

### B.3 DI registration

Mirror existing Weather/Order patterns:
- `ApplicationDependencyInjection.AddApplication()` — `AddValidatorsFromAssembly`, `AddCqrsHandlersFromAssembly`, `AddDomainEventHandlersFromAssembly`, `AddDomainEventDispatcher`, `AddCqrsHandlerBehaviors` (decorator chain: Tracing → Logging → Metrics → Validation → Handler).
- `MessagingDependencyInjection.AddKafkaMessaging()` — `AddOutbox()` with Avro, `AddInbox<TDbContext>(messageTypes)`, KafkaFlow `.TopicEndpoint(...)` per consumed topic.
- `ApiDependencyInjection.AddApi()` — FastEndpoints, Swagger, ProblemDetails, CORS.

### B.4 Test projects

```
test/{Bc}.UnitTests/
test/{Bc}.IntegrationTests/
test/{Bc}.ArchitectureTests/
test/{Bc}.FunctionalTests/
```

Each references `Platform.Test.Framework` for shared fixtures (Testcontainers setup, WebApplicationFactory).

### B.5 Lock files + package versions

- Add package references to correct `Directory.Packages.props` (root for `Platform.*`, `services/` for service-specific, `test/` for test-only).
- Run `dotnet restore --locked-mode` locally; commit `packages.lock.json`.

### B.6 Docker-compose

Add to [docker-compose.yaml](../docker-compose.yaml):
- Per-service `{bc}-db` entry (if the service uses its own Postgres schema, likely not — all BCs share a single Postgres instance; schemas are per-BC).
- One `outbox-relay-{bc}` container per service schema (saga, basket, catalog, inventory, invoicing, notifications, ordering, payments).
- 8 new topics in `kafka-create-topic` command (full list: [events-catalog.md § 4](bc-design/events-catalog.md)).

---

## Appendix C — Out of Current Scope

Planned bounded contexts and per-BC features beyond the current scope are catalogued in [roadmap.md](roadmap.md). Highlights:

- **Customer Accounts / User Profiles** — current scope sources user data from JWT claims and snapshots it into Order at checkout. See [ADR-0005](adr/0005-customer-data-in-ordering.md) for the seam.
- **Shipping / Fulfillment** — post-order carrier integration, tracking, delivery confirmations (beyond manual `MarkOrderShipped` / `MarkOrderDelivered` admin commands).
- **Returns / RMA** — post-delivery return flow with refund orchestration.
- **Reviews / Ratings** — product feedback.
- **Recommendations** — "customers also bought" analytics.
- **Promotions / Discounts / Coupons** — current scope uses Catalog's flat price per [ADR-0002](adr/0002-pricing-in-catalog.md); dynamic pricing is the extraction trigger.
- **Pricing BC extraction** — Catalog's `ProductPriceChangedEvent` is the seam; ADR-0002 preserves the extraction path.

Per-BC features beyond current scope (low-stock thresholds, partial refunds, additional notification channels, etc.) and cross-cutting planned work (crypto-shredding, replay-admin CLI, etc.) are in [roadmap.md § 2.3](roadmap.md) and [§ 2.4](roadmap.md).

---

## Appendix D — ADR Index

| ADR | Title | Status |
|-----|-------|--------|
| [0001](adr/0001-centralized-saga-orchestration.md) | Centralized Saga Orchestration | Accepted |
| [0002](adr/0002-pricing-in-catalog.md) | Pricing inside Catalog (v1) | Accepted (2026-04-18) |
| [0003](adr/0003-basket-as-technical-bc.md) | Basket as Technical / Session BC | Accepted (2026-04-18) |
| [0004](adr/0004-checkout-saga-topology.md) | Checkout Saga Topology | Accepted (2026-04-18) |
| [0005](adr/0005-customer-data-in-ordering.md) | Customer Data in Ordering | Accepted (2026-04-18) |
| [0006](adr/0006-event-sourcing-for-inventory.md) | Event Sourcing for Inventory | Accepted (2026-04-18) |
| [0007](adr/0007-avro-compatibility-modes.md) | Avro Schema Compatibility Modes | Accepted (2026-04-18) |
| [0023](adr/0023-payments-event-vs-command-classification.md) | Payments Event-vs-Command Classification | Accepted (2026-05-30) |

(ADRs 0008–0022 listed at [adr/README.md](adr/README.md); only the directly-master-design-related ADRs appear here.)

---

## Implementation Checklist (success criteria)

- [x] Internal vs external event distinction documented with explicit naming convention & transformation template (§ 3)
- [x] Each of 4 BCs has: glossary, aggregates (properties + invariants + factory methods + state transitions), VOs, SmartEnums, internal domain events, external summary events, pattern-showcase mechanics ([bc-design/](bc-design/))
- [x] Every external event has full `.avsc` specification ([events-catalog.md § 5](bc-design/events-catalog.md))
- [x] Event catalog covers every external event with topic + producer + consumer(s) + consumer group + correlation key ([events-catalog.md § 2](bc-design/events-catalog.md))
- [x] Checkout saga state machine has ≥ 6 happy states, ≥ 3 terminal states, ≥ 4 compensation paths, timeouts per awaiting state ([checkout-saga.md](bc-design/checkout-saga.md))
- [x] Stock reserved BEFORE payment in saga flow ([ADR-0004](adr/0004-checkout-saga-topology.md))
- [x] Every command has request shape + response type + validation rules; every query has request + response DTO ([use-cases.md](bc-design/use-cases.md))
- [x] BFF documents 4 aggregation endpoints with service-call pattern, caching, fallback ([bff.md](bc-design/bff.md))
- [x] Inventory design uses Event Sourcing explicitly; event store table schema included ([inventory.md](bc-design/inventory.md), [ADR-0006](adr/0006-event-sourcing-for-inventory.md))
- [x] Basket design uses Redis with AOF (no SQL state table); post-checkout deletion documented ([basket.md](bc-design/basket.md), [ADR-0003](adr/0003-basket-as-technical-bc.md))
- [x] All new Kafka topics follow `{domain}.{aggregate}[.{kind}]` naming (§ 6.1)
- [x] ADRs 0002–0006 authored and cross-linked from master design ([Appendix D](#appendix-d--adr-index))
- [x] Consolidated glossary covers all BCs (§ 12 + linked per-BC glossaries)
- [x] Miro hosts sticky-note artifacts (Event Modeling + Example Mapping); drawio hosts technical diagrams (Context Map, Saga State, BC Map) with mermaid sources under `docs/diagrams/` (§ 10)
- [x] Every pattern claim cites existing codebase file paths (per chapter)

**Ready for implementation waves** (one agent per service in parallel, using the per-service chapter links above).

---

## Appendix E — Review Findings Addendum (2026-04-18)

After the Stage 2 design synthesis, two reviewer passes (`nw-ddd-architect-reviewer` + `nw-system-designer-reviewer`) surfaced findings that have been resolved as documented below. Implementation agents MUST read this appendix — it supersedes any contradicting statement earlier in the master design or in the linked chapters.

### E.1 Address sourcing (structural fix applied)

**Problem:** `CheckoutBasketCommand` did not carry shipping/billing addresses or payment method; `BasketCheckoutInitiatedEvent` schema likewise omitted them; the Checkout saga's state class expected them with the sourcing deferred.

**Resolution (applied):** The BFF/client collects addresses and payment method at checkout and includes them in `CheckoutBasketCommand`. Basket is a **pass-through courier** — it validates basic shape only (non-empty strings, ISO 3166-1 alpha-2 country code) and forwards the data into `BasketCheckoutInitiatedEvent`. The saga carries them in state; `CreateOrderCommand` carries them to Ordering; Ordering re-snapshots them onto the `Order` aggregate (authoritative record for fulfillment).

Files updated:
- [use-cases.md § 2.1.6 `CheckoutBasketCommand`](bc-design/use-cases.md) — request shape now includes `shippingAddress`, `billingAddress`, `paymentMethodId` plus validator rules
- [events-catalog.md § 5.2.1 `BasketCheckoutInitiatedEvent.avsc`](bc-design/events-catalog.md) — Avro schema now includes nested `CheckoutAddress` record, used for both ShippingAddress and BillingAddress, plus `PaymentMethodId`

This aligns with [ADR-0005](adr/0005-customer-data-in-ordering.md): no Accounts BC; customer data snapshotted per-order via the BFF.

### E.2 Saga terminal events — DECIDED to OMIT

**Problem:** [checkout-saga.md](bc-design/checkout-saga.md) mentions emitting `CheckoutCompletedEvent`, `CheckoutFailedEvent`, `CheckoutStuckEvent` as saga-terminal business moments, but these were not in the [events-catalog.md](bc-design/events-catalog.md) master table — leaving the decision ambiguous.

**Resolution:** **OMIT saga-terminal events in v1.** Rationale:

- No consumer demonstrably needs them today — Notifications reacts to `OrderConfirmedEvent` / `OrderCancelledEvent` / `OrderFailedEvent`; BFF cache invalidation uses the same order events; observability of saga lifecycle is better served by OpenTelemetry activities + metrics (§ 11.3 + saga observability subsection in [checkout-saga.md](bc-design/checkout-saga.md) § 11).
- Adding an event class solely for "saga finished" duplicates information already present in order-lifecycle events.
- If a future consumer emerges (e.g., saga-specific analytics dashboard), adding a `checkout.sagas` topic + Avro schema later is non-breaking.

Implementation agents: when you encounter references to these three events in the saga chapter, treat them as **replaced by OpenTelemetry spans** (`SagaOrchestrators.Checkout.Confirmed`, `...Failed`, `...Stuck`) and metric counters (`saga.checkout.confirmed`, `saga.checkout.failed`, `saga.checkout.stuck`). No Kafka topic, no Avro schema, no outbox message.

### E.3 `CreateOrderCommand` partition key — CorrelationId confirmed

**Problem:** The event catalog master table lists `CreateOrderCommand` keyed by `CorrelationId` (because OrderId doesn't exist yet), while the other three Ordering commands are keyed by `OrderId`. Reviewer flagged this as a potential per-order ordering-guarantee gap.

**Resolution:** **`CorrelationId` keying for `CreateOrderCommand` is intentional and correct.** Analysis:

- `CreateOrderCommand` is issued exactly ONCE per saga (no re-entry). Ordering consumers are idempotent via inbox dedup by `MessageId`. A second-copy delivery of the same `CorrelationId` is a no-op.
- The other three commands (`ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand`) arrive AFTER `OrderCreatedEvent` → saga state contains `OrderId` → saga uses `OrderId` as the partition key, guaranteeing per-order order.
- There is no scenario where `CreateOrderCommand` and a subsequent Ordering command compete for ordering within the same partition — they're separated by the `OrderCreatedEvent` → saga transition → subsequent-command-issuance sequence.

Conclusion: the current keying is the correct design. No change required. Document this reasoning inline if someone re-flags it in review later.

### E.4 ProductSnapshot boundary Basket → Ordering

**Clarification:** Basket's `ProductSnapshot` has `(Sku, Name, Price, CapturedAtUtc)`; Ordering's `ProductSnapshot` has `(Sku, Name)` only, with price living on `OrderItem.UnitPrice` as a separate field. This is **intentional**:

- Basket's snapshot must carry price to surface "price drift" to the user pre-checkout.
- Ordering's snapshot must carry display data (Sku, Name) for order history / receipts, but price is on `OrderItem.UnitPrice` because:
  1. `OrderItem.UnitPrice` is the **legally binding** price for the transaction — separating it from descriptive data makes this visible in the aggregate design.
  2. Display snapshot (name, sku) could eventually be refreshed for UI; binding price must not.

No change required; design-intent note added here for implementation agents to reference.

### E.5 `OrderDeliveredEvent` missing `CorrelationId`

**Clarification:** `OrderDeliveredEvent` omits `CorrelationId` because the checkout saga is already terminal (`Confirmed`) well before delivery. Consumers correlating post-saga lifecycle (fulfillment analytics, customer-facing order timeline) should use `OrderId`, not `CorrelationId`.

**Guidance for consumers:** `CorrelationId` is saga-scoped (Basket → Order creation → Payment → Confirmation). `OrderId` is lifecycle-scoped and present on every Ordering external event. Correlate lifecycle views by `OrderId`.

### E.6 BFF basket price-refresh flow

**Clarification:** The BFF's `/api/bff/basket` endpoint fetches (a) the Basket snapshot (snapshot prices as captured) + (b) current Catalog prices for each item, and surfaces the delta to the client as a "price drift" indicator. Basket itself does NOT auto-refresh — refresh is always client-initiated via `RefreshBasketPricesCommand`. This preserves the frozen-pricing contract until the user explicitly acknowledges a change.

### E.7 BFF stale-order cache window

**Clarification:** The BFF's `/api/bff/order-summary/{orderId}` endpoint has a 30 s soft TTL and 5 min fail-safe. During a status transition (e.g., `Confirmed` just emitted), the cache invalidator removes the tag on receipt of `OrderConfirmedEvent`. However, if the BFF enters fail-safe mode due to an Ordering outage immediately after the transition, a client could see a status up to 5 minutes stale. This is **acceptable UX** (bounded staleness vs hard failure). Document this in client-facing API contract. Real-time push-based updates are planned scope — see [roadmap.md § 2.3 Ordering](roadmap.md).

### E.8 Outbox-relay containers

**Clarification:** Per [events-catalog.md § 1.4 D-6](bc-design/events-catalog.md), `docker-compose.yaml` runs one outbox-relay container per service schema (`outbox-relay-saga`, `outbox-relay-basket`, `outbox-relay-catalog`, `outbox-relay-inventory`, `outbox-relay-invoicing`, `outbox-relay-notifications`, `outbox-relay-ordering`, `outbox-relay-payments`). Each relay binds to the corresponding service's PostgreSQL schema via `OutboxRelay__SchemaName`. The checkout saga has no relay container — sagas use MassTransit's built-in outbox.

### E.9 Kafka topic retention application

**Clarification:** [events-catalog.md § 4](bc-design/events-catalog.md) specifies the authoritative `kafka-topics --create ...` block with explicit `--config retention.ms=...` flags per topic (`-1` for audit-log topics, `604800000` for 7-day commands, `2592000000` for 30-day basket sessions). Implementation agents must copy this block verbatim into [docker-compose.yaml](../docker-compose.yaml) after line 246. Broker default is 7 days; the explicit flags are REQUIRED to preserve audit-trail semantics for event-sourced and compliance-sensitive topics.

### E.10 Consumer-group `inventory-stock-init` reuse — RETRACTED

**Original clarification (superseded):** Inventory's `OrderCancelledEvent` consumer reused the `inventory-stock-init` group from its `ProductCreatedEvent` subscription; E.10 originally directed implementation agents to split this into a distinct `inventory-order-cancelled` group.

**Retraction (2026-05-31):** Superseded by the one-group-per-service rule codified in [events-catalog.md § 3.1](bc-design/events-catalog.md). Kafka commits offsets per `(group, topic, partition)`, so per-topic offset independence is already preserved inside the single `inventory-group`. Splitting into a second group inside the same service adds rebalance scope and dashboard count without any isolation that the per-topic offset partitioning doesn't already give. Inbox dedup on `MessageId` covers replay-on-rename. The original concern (one consumer handling two unrelated message types under the same offset tracking) was based on a misreading of Kafka offset semantics; the offsets were never shared across topics.

Current Inventory wiring: all three Inventory Kafka consumers (`catalog.products` → stock-init, `ordering.orders` → release-on-cancel, `inventory.reservation-commands` → saga commands) use `GroupId = "inventory-group"` per `services/Inventory/Inventory.Api/appsettings.json`.

---

### Verdict

After applying the fixes in E.1 (structural) and documenting the decisions in E.2–E.10, the design is **APPROVED for implementation** by both reviewers' criteria:

- E.1, E.2 — resolved gaps (structural change or binding decision)
- E.3–E.9 — clarifications without structural change (original design was correct; rationale documented)
- E.10 — **retracted** on 2026-05-31, superseded by the one-group-per-service rule in [events-catalog.md § 3.1](bc-design/events-catalog.md)

---

## Appendix F — Iteration 2 Improvements (2026-04-18)

Iteration 2 added structural rigor, operational playbooks, collaborative-discovery artifacts, and refreshed Miro diagrams. All additions are documentation-only (no code touched). This appendix is the navigation hub for all new content.

### F.1 Example Mapping sessions

Formal Matt-Wynne-style Example Mapping cards — Story / Rules / Examples / Questions — for the most complex business rules per BC. Each example uses BDD Given / When / Verify / Then (per the reference image convention). These become the seed for executable acceptance-test specs (SpecFlow / Reqnroll) during implementation.

| BC | Sessions | File |
|----|----------|------|
| Catalog | 2 (Reparent category; Reactivate product) | [example-mapping/catalog.md](bc-design/example-mapping/catalog.md) |
| Basket | 2 (Price drift; 30-day expiry) | [example-mapping/basket.md](bc-design/example-mapping/basket.md) |
| Ordering | 3 (Status FSM; Cannot cancel after Shipped; Items locked after StockReserved) | [example-mapping/ordering.md](bc-design/example-mapping/ordering.md) |
| Inventory | 3 (Reservation TTL auto-release; Cannot oversell; Confirm idempotency) | [example-mapping/inventory.md](bc-design/example-mapping/inventory.md) |

**10 sessions total.** All "Questions" sections are intentionally empty — ground-truth BC chapters and ADRs resolved every design-level question; deferrals are tracked in [roadmap.md](roadmap.md) rather than as open questions.

### F.2 Error taxonomy (consolidated)

[bc-design/error-taxonomy.md](bc-design/error-taxonomy.md) — master table of every `*Error` class across the 4 new BCs + CheckoutSaga + `DataIntegrityException`. Columns: BC, Category (User / Business-expected / Bug / Infrastructure), HTTP mapping, saga compensation semantics, retry-ability, DLT behavior. Per-BC error-class sketches and HTTP-mapping registration guidance.

Cross-linked from each BC chapter's new `## Error types` subsection.

### F.3 Kafka DLT (dead-letter) strategy

[bc-design/kafka-dlt-strategy.md](bc-design/kafka-dlt-strategy.md) — aligns with the existing `Platform.KafkaFlow.DeadLetter` convention: per-consumer-BC `<source-topic>.<consumer-bc>.DLT` suffix (each BC pins its own `DltTopicSuffix` in appsettings — `.Payments.DLT`, `.Inventory.DLT`, etc.). Per-consumer table covers all 10 BC-consumed source topics; poison-message runbook; DLT replay procedure; observability signals.

Factual corrections made during authoring:
- The codebase has `Platform.KafkaFlow.DeadLetter` but **no** `Platform.KafkaFlow.Retry` — current policy is aggressive DLT on first throw (documented as intentional).

### F.4 Avro schema compatibility

[bc-design/avro-compatibility.md](bc-design/avro-compatibility.md) + [ADR-0007](adr/0007-avro-compatibility-modes.md). Per-topic category decision:

- **Event-log topics** → `FORWARD_TRANSITIVE` (infinite retention requires every historical version stay readable by current consumer code)
- **Command topics** → `FULL_TRANSITIVE` (independent producer/consumer deploy cadence requires bidirectional compatibility)
- **Subject naming**: Record Name Strategy (existing `UniversalAvroSerializer` convention)

Breaking-change process: add-with-default only; deprecated fields retained forever within a subject; major versions require new subject names.

### F.5 Architecture test invariants

[bc-design/architecture-tests.md](bc-design/architecture-tests.md) — NetArchTest-based rule catalog (matches existing `test/Weather.ArchitectureTests/`). Common rules + per-BC specifics:

- Layer dependency rules (Domain ↮ Infrastructure/Api; Application ↮ only Domain+Platform.CQRS+Outbox)
- Aggregate discipline: private ctor + public static factory + no public setters
- Internal (`*DomainEvent`) vs external (`*Event`) naming
- Cross-BC reference blocking (only by ID, never by aggregate type)
- Result pattern enforcement (user errors → `Result.Fail`; bug-class → `DataIntegrityException`)

### F.6 Idempotency-Key HTTP header

[use-cases.md](bc-design/use-cases.md) now requires `Idempotency-Key` header on all **mutating** HTTP commands (`AddItemToBasket`, `Checkout`, `CreateProduct`, `DiscontinueProduct`, `ShipOrder`, `AdjustStock`, etc.). Client-generated UUID; each service maintains a per-service idempotency-key dedupe table (inbox-like) with 24 h retention. Safe client retries without side-effect duplication.

### F.7 Operational playbooks

**Saga-stuck runbook** — [bc-design/saga-stuck-runbook.md](bc-design/saga-stuck-runbook.md): Grafana alert config, investigation SQL, 5 recovery procedures, post-mortem template. Fires when `CompensationStuck` counter increments.

**Rate limiting** — [bc-design/rate-limiting.md](bc-design/rate-limiting.md): YARP per-endpoint token-bucket config. Public endpoints 100 req/min/IP; auth'd 60 req/min/UserId; admin 30 req/min/UserId. 429 with `Retry-After`.

**Migration playbook** — N/A. Ordering is greenfield (`services/Order/` was deleted pre-dispatch with the Weather cleanup). No migration required.

### F.8 Visual artifacts — iteration 3 (Miro stickies + drawio technical diagrams)

**Final state** (see § 10 for details):

- **Miro** hosts two sticky-note artifacts — real Miro stickies (not flowchart-shape approximations anymore):
  - Event Modeling board (21 real stickies + 5 swim-lane labels) laid out as classic command/event/view/policy/external swim lanes.
  - Example Mapping showcase session inside a Miro frame: "The one where concurrent reservations race on the last units." Given/When/Verify/Then columns, Rule/Example/Outcome stickies, plus pink Question + gray Answer on the right — matching the reference picture layout.
- **Drawio** (via MCP) hosts three technical diagrams with mermaid sources under `docs/diagrams/`:
  - Context Map — `flowchart LR` with BC subgraphs and colored `classDef` styles.
  - Checkout Saga State Machine — `stateDiagram-v2` with terminal notes.
  - BC Map — `classDiagram` with cross-BC dashed reference arrows.

Iteration 1 and 2's fake-sticky Miro flowcharts have been removed from the board.

### F.9 Navigation summary

| Concern | File(s) |
|---------|---------|
| Collaborative discovery | [example-mapping/](bc-design/example-mapping/) |
| Error handling contracts | [error-taxonomy.md](bc-design/error-taxonomy.md) |
| Kafka reliability | [kafka-dlt-strategy.md](bc-design/kafka-dlt-strategy.md) |
| Schema evolution | [avro-compatibility.md](bc-design/avro-compatibility.md), [ADR-0007](adr/0007-avro-compatibility-modes.md) |
| Code-rule enforcement | [architecture-tests.md](bc-design/architecture-tests.md) |
| Client retry safety | `use-cases.md` Idempotency-Key convention |
| Saga incident response | [saga-stuck-runbook.md](bc-design/saga-stuck-runbook.md) |
| Edge-protection | [rate-limiting.md](bc-design/rate-limiting.md) |
| Ordering migration | [ordering.md § Appendix A](bc-design/ordering.md) |
| Visual artifacts (Miro stickies) | [§ 10.1](#101-miro--sticky-note-artifacts) |
| Visual artifacts (drawio technical) | [§ 10.2](#102-drawio--technical-diagrams) |
