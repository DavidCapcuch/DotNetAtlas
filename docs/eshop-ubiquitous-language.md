# eShop Ubiquitous Language

> Consolidated glossary across all bounded contexts. Terms used in code, API surface, and documentation MUST come from this file. Conflicts across BCs are resolved in [§ 12 of eshop-master-design.md](eshop-master-design.md). Per-BC authoritative glossaries live in `docs/bc-design/glossary-{bc}.md`.

## Conventions

- **Basket** — retained throughout the solution. `Cart` is a US-English synonym; documentation may mention it but code and API use `Basket`.
- **Buyer** vs **Customer** vs **User** — the same person. Use:
  - `User` in the authentication/identity context (JWT claims, Keycloak)
  - `Buyer` inside the Ordering and Invoicing bounded contexts (the person who placed an Order / is invoiced)
  - `Customer` in marketing/BFF-facing copy only
- **Snapshot** — an immutable point-in-time copy of another BC's data, isolated from later changes (frozen). Used in Basket (`ProductSnapshot`) and Ordering (`ProductSnapshot`, address snapshots).
- All timestamps are `DateTimeOffset` at the domain boundary and `timestamptz` in Postgres. UTC is the canonical zone; offsets may be preserved for display.

---

## Catalog

> Full: [glossary-catalog.md](bc-design/glossary-catalog.md)

| Term | Definition |
|------|------------|
| Product | The sellable-thing aggregate — Sku, name, description, price, category, images. Authority for product identity. |
| Category | Hierarchical classification aggregate — tree structure, max depth 5. Products reference one category by id. |
| Sku | Stock Keeping Unit — business-level product code, 1–32 chars, normalized uppercase. Unique across Catalog. |
| ProductStatus | SmartEnum: `Draft → Active → Discontinued` (with `Reactivate` back-edge to `Active`, admin-only). |
| IsSellable | Derived flag on the `product_search_view` projection: `Status == Active AND available_stock > 0`. Updated by consuming Inventory's `StockLevelChangedEvent`. |
| ProductSearchView | Denormalized read-side projection (CQRS) built in-process by per-event `*ProjectionDomainEventHandler` classes within the same DB transaction as the write-model save. |
| Reactivation | Explicit admin action to move a product back from `Discontinued` to `Active`. Requires the `AuthPolicies.CatalogAdmin` claim. |

---

## Basket

> Full: [glossary-basket.md](bc-design/glossary-basket.md)

| Term | Definition |
|------|------------|
| Basket | Ephemeral shopping session keyed by `UserId`. Redis-primary aggregate with 30-day sliding TTL. |
| BasketItem | Line within a Basket — `ProductId`, `Quantity`, `ProductSnapshot`. Mutable until checkout. |
| ProductSnapshot (Basket) | Frozen product data captured at add-time: `Sku`, `Name`, `Price`, `CapturedAtUtc`. Price is the basket's authoritative price for the item, immune to later Catalog changes ("frozen-pricing contract"). |
| Frozen-pricing contract | Basket never auto-updates snapshots when Catalog price changes. Stale prices are intentional — refreshed only by explicit user action or BFF diff-render. |
| Checkout | Terminal action: publishes `BasketCheckoutInitiatedEvent`, then deletes the Basket from Redis. Single-step, irreversible. |
| Version | Monotonic counter on the aggregate for optimistic concurrency via Redis CAS (Lua). |
| ACL (Anti-Corruption Layer) | The `ProductCatalogHttpAdapter` in `Basket.Infrastructure` translating Catalog DTOs into `ProductSnapshot` VOs. |

---

## Ordering

> Full: [glossary-ordering.md](bc-design/glossary-ordering.md)

| Term | Definition |
|------|------------|
| Order | The commercial commitment aggregate — from `Created` through `Delivered`. Authority for order lifecycle state. |
| OrderItem | Immutable value object — `ProductId`, `Quantity`, `UnitPrice`, `LineTotal`, `ProductSnapshot(Sku, Name)`. Frozen at `StockReserved`. |
| OrderStatus | SmartEnum with 8 values: `Created → StockReserved → PaymentCompleted → Confirmed → Shipped → Delivered`; `Cancelled` / `Failed` off-ramps. Transitions gated by `CanTransitionTo`. |
| Buyer | The authenticated user who placed the Order. `BuyerId == JWT sub` claim. |
| ShippingAddress / BillingAddress | Address value objects snapshotted at order creation from `CheckoutBasketCommand`. Immutable. |
| CancellationInfo | VO recording `CancelledAtUtc`, `Reason`, `CancelledBy` (Buyer / Admin / System). Populated on `Cancelled` transition. |
| FailureInfo | VO recording saga-driven failure — `ErrorCode`, `ErrorMessage`, `AtStatus`, `FailedAtUtc`. |

---

## Inventory

> Full: [glossary-inventory.md](bc-design/glossary-inventory.md)

| Term | Definition |
|------|------------|
| StockItem | The aggregate in Inventory — one per `ProductId`; state rehydrated by folding the event stream. |
| ES event | Event-sourcing event persisted in `inventory.stock_events` — append-only, keyed by `(StreamId, Version)`. Rehydration folds these into aggregate state. |
| OnHand | Physical stock quantity — what is in the warehouse. |
| Reserved | Quantity held by in-flight reservations awaiting confirmation. |
| Available | Derived: `OnHand - Reserved`. Must stay ≥ 0 (enforced by the aggregate). |
| Reservation | A hold on stock for a specific `OrderId`. 15-minute default TTL. Lifecycle: `Active → Confirmed` OR `Active → Released(Compensation/Expiry/Cancellation)`. |
| ReservationExpiryWorker | Background worker that publishes `ReservationReleasedEvent(reason=Expiry)` for reservations past TTL. |
| Threshold-crossing | The rule that `StockLevelChangedEvent` is emitted only when `Available` crosses `0 ↔ positive`, not on every arithmetic change. Prevents bus spam. |
| Projection | Read-side denormalized view built from the event stream: `inventory.current_stock_levels`, `inventory.reservation_audit`. Upserted in the same transaction as event append. |

---

## Payments

> Full: [glossary-payments.md](bc-design/glossary-payments.md)

| Term | Definition |
|------|------------|
| PaymentTransaction | Aggregate root — one per saga-scoped payment lifecycle. |
| PaymentStatus | SmartEnum with 7 values: `Requested → Authorized → Captured → Completed`; off-ramps `Failed`, `Voided`, `Refunded`. |
| Authorize | First gateway call — reserves funds without moving money. Reversible via Void. |
| Capture | Second gateway call — actually moves money. Reversal requires Refund. |
| Void | Pre-capture cancellation. Releases authorization hold; cheaper than Refund. |
| Refund | Post-capture reversal. New gateway call referencing the original `GatewayTransactionId`. |
| PaymentMethodId | Gateway-issued token (not PAN). PCI-scope boundary — Payments stores only tokens. |
| GatewayTransactionId | External payment processor's transaction reference. Immutable once set. |
| PaymentProcessingSaga | Standalone saga orchestrator — sub-saga of Checkout saga; the only caller of Payments commands. |

---

## Invoicing

> Full: [glossary-invoicing.md](bc-design/glossary-invoicing.md)

| Term | Definition |
|------|------------|
| Invoice | Fiscal-record aggregate — legally binding record that a purchase occurred. Issued after both `OrderConfirmedEvent` and `PaymentCapturedEvent` arrive. |
| CreditNote | Negative-amount counterpart to an Invoice — issued on cancel-after-capture. Separate aggregate with own numbering. |
| InvoiceNumber | Gap-free sequential identifier formatted `INV-YYYY-NNNNNN`. Allocated via Postgres allocator table with `SELECT FOR UPDATE`. |
| CreditNoteNumber | Format `CN-YYYY-NNNNNN`. Separate sequence from invoices. |
| Gap-free sequencing | Numbers must have no missing values (EU VAT requirement). Rollback of issuing transaction must release the number. |
| Enrichment projection | The `pending_invoices` / `pending_credit_notes` tables buffering partial state until both upstream halves arrive. |
| Convergent enrichment | Pattern: two independent events must both arrive before a downstream action. Invoicing uses this instead of a saga. |
| PdfBlobRef | VO: `BlobUri`, `ContentHash` (SHA-256), `SizeBytes`. Points to write-once Azurite/Azure Blob object. |
| Presigned URL | Time-bounded (10-minute) S3-compatible URL granting GET access to the PDF without credentials. Served through nginx-cdn. |
| QuestPDF | MIT-licensed fluent-DSL PDF library used by `InvoiceDocument` template. |

---

## Shared / Cross-Context

| Term | Definition |
|------|------------|
| Money | Value object: decimal amount + ISO 4217 currency code. Shared-kernel in `Platform.SharedKernel.ValueObjects.Money`. |
| Address | Value object: `Street1`, `Street2?`, `City`, `State?`, `PostalCode`, `CountryCode` (ISO 3166-1 alpha-2). |
| Correlation key | `OrderId` (UUID v7) — the durable business key shared across a checkout workflow (one checkout → one order → one payment → one invoice); pre-assigned at checkout and also the saga's MassTransit `CorrelationId` ([ADR-0029](adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)). The earlier dedicated `CorrelationId` (its `X-Correlation-Id` header, span attribute, and DB columns) was retired ([ADR-0030](adr/0030-retire-dedicated-correlationid.md)); `traceId` (W3C / OpenTelemetry) covers cross-cutting telemetry. |
| External event | Enriched summary event published to Kafka with an Avro contract. Naming: `{BusinessMoment}Event` (e.g. `ProductCreatedEvent`, `OrderShippedEvent`, `StockLevelChangedEvent`). All event schemas use the `Event` suffix; the convention is enforced at infrastructure bootstrap (see [bc-design/conventions.md § 1.1](bc-design/conventions.md)). |
| Internal (domain) event | In-process event raised by an aggregate and dispatched via `IDomainEventHandler<T>`. Naming: `{State}DomainEvent`. Never published to Kafka directly. |
| Command (Kafka) | Imperative intent published on a `{domain}.{aggregate}-commands` topic — exactly one known consumer, specific response expected. Avro namespace `{Domain}.{Aggregate}`. |
| Outbox | Transactional outbox table (`{schema}.outbox_messages`) — domain write + outbox insert in one transaction; relay worker dequeues to Kafka. |
| Inbox | Transactional inbox table (`{schema}.inbox_messages`) — message-ID deduplication for idempotent consumers. |
| SmartEnum | `Ardalis.SmartEnum<T>` — type-safe state-machine-friendly alternative to string enums. Every SmartEnum exposes a `CanTransitionTo(target)` guard consulting a `_allowed` readonly dictionary. |
| Result pattern | `FluentResults.Result<T>` / `Result.Fail(IError)` — how handlers signal user/business-expected failures. Bugs throw `DataIntegrityException` and route to DLT. |
| DLT (Dead Letter Topic) | `{source-topic}.{consumer-bc}.DLT` (e.g. `payments.payment-commands.Payments.DLT`) — per-consumer-BC destination for messages that failed the handler. Each BC pins its own `DltTopicSuffix` (`.Payments.DLT`, `.Inventory.DLT`, etc.); when multiple BCs consume the same source topic they get separate DLT buckets. 14-day retention target; alert on cumulative count per topic. See [bc-design/kafka-dlt-strategy.md](bc-design/kafka-dlt-strategy.md). |
| Schema compatibility | *Derived* from the `.avsc` filename suffix — `*Event` subjects → FORWARD_TRANSITIVE, `*Command` subjects → FULL_TRANSITIVE — machine-enforced by `schema-registry-init`. Canonical: [ADR-0007](adr/0007-avro-compatibility-modes.md) + [kafka-topology.md](kafka-topology.md) (class → mode). |
| Idempotency-Key | HTTP header on state-changing POST/PATCH/DELETE. Dedups client retries. Server stores `(key, request-hash, response)` for 24h. Mechanism: FastEndpoints' built-in `.Idempotency()` chain method backed by ASP.NET Output Cache on `redis-cache` (see [ADR-0013](adr/0013-idempotency-key-http.md)). |
