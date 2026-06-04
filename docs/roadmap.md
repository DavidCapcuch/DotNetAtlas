# DotNetAtlas Roadmap

> **Status:** Consolidated record of planned post-current-scope work. This is the **single source of truth** for "deferred" / "future" items across the codebase. BC design docs, ADRs, and master-design must NOT re-narrate planned work inline — cross-reference this document instead.
>
> **Scope of this doc.** This is a *reference solution* per [ADR-0009 — Reference Solution Target Profile](adr/0009-reference-solution-target-profile.md); roadmap items here are illustrative of how the architecture would extend, not a commercial product backlog. Items are intentionally lightweight (no dates, no owners, no priorities) — when a real implementation push starts for an item, that item is promoted to a GitHub issue and the row here links to it.

---

## 1. Currently shipped scope

The following 7 bounded contexts are in scope today:

- **Catalog** — product + category master data
- **Basket** — session-scoped cart (Redis), with HTTP ACL to Catalog
- **Ordering** — Order aggregate lifecycle (Created → StockReserved → PaymentCompleted → Confirmed → Shipped → Delivered, with Cancelled / Failed off-ramps)
- **Inventory** — event-sourced reservation + stock-level (per-`ProductId` streams)
- **Payments** — gateway-facing Authorize / Capture / Void / Refund commands and lifecycle events
- **Invoicing** — invoice issuance + credit notes (10-year retention, EU VAT compliance)
- **Notifications** — channel-agnostic `NotifyUserCommand` → per-channel fan-out (email / fake SMS / SignalR bell). **v2 is the agreed design, in progress (#312–#317)**; v1 shipped the single-channel email path. See [notifications.md](bc-design/notifications.md) + [ADR-0031](adr/0031-notify-user-command-and-notification-id.md)/[ADR-0032](adr/0032-notifications-dispatch-and-channels.md).

Plus the **CheckoutSaga** + **PaymentProcessingSaga** in `saga/SagaOrchestrators/`.

---

## 2. Tracked planned items

### 2.1 Items tracked via GitHub issues (already)

| # | Issue | Scope |
|---|---|---|
| 1 | [#289](https://github.com/DavidCapcuch/DotNetAtlas/issues/289) | BFF inbox-dedup + topic-set reconciliation across events-catalog § 7.7 ↔ bff.md § 2.2 ↔ master-design § 9.2 |
| 2 | [#290](https://github.com/DavidCapcuch/DotNetAtlas/issues/290) | Cross-saga refund path: master-design § 5.5 vs Checkout-saga reality (resolves ADR-0001 deferred decision) |

---

### 2.2 Planned bounded contexts

These BCs are explicitly out of current scope. The architectural seams that allow each to be added without rewriting existing BCs are documented inline at the seam.

| BC | Seam / signal that triggers extraction |
|---|---|
| **Accounts** (user profiles, address book, saved preferences) | Today Ordering snapshots `BuyerEmail` / `BuyerName` / `ShippingAddress` / `BillingAddress` from the JWT-derived claims at checkout per [ADR-0005](adr/0005-customer-data-in-ordering.md). The VOs and the snapshot fields are designed to remain valid if an Accounts BC is later introduced — Order keeps the snapshot as a commercial-commitment record, Accounts owns the source-of-truth. |
| **Shipping / Fulfillment** (carrier integration, tracking, delivery webhooks) | Today Ordering accepts admin-driven `MarkOrderShipped(carrier, trackingNumber)` + `MarkOrderDelivered` commands. The `Carrier` field is a free-form string today; would become a SmartEnum when the carrier set is bounded. |
| **Returns / RMA** (post-delivery return + refund orchestration) | Today the FSM blocks cancellation after `Shipped` (Ordering invariant I-12). The Returns BC would orchestrate its own flow over Inventory + Payments without reusing the Checkout saga. |
| **Reviews / Ratings** | No seam needed — pure additive BC; subscribes to `ordering.orders` for purchase-verification ("verified buyer" badge). |
| **Recommendations** | Subscribes to `catalog.products` + `ordering.orders`; pure read-side. |
| **Promotions / Discounts / Coupons** | Triggers extraction of pricing into its own BC — see next row. |
| **Pricing BC extraction** | Per [ADR-0002](adr/0002-pricing-in-catalog.md), the trigger is any one of: customer-segmented pricing, time-bound promotions, multi-currency-per-product, region/tax-jurisdiction rules. Catalog's `ProductPriceChangedEvent` is the extraction seam. When extracted, a placeholder ADR documents the move. |

---

### 2.3 Per-BC planned features

#### Catalog
- **Search indexer consumer.** Would land under `search-group` per [events-catalog § 3.1](bc-design/events-catalog.md), subscribing to `catalog.products` + `catalog.categories`.
- **Product dimensions for shipping estimation.** `Dimensions` VO already exists (`Length`/`Width`/`Height`/`Unit`); shipping-estimator consumer is the missing piece.
- **`ProductImageAdded` read-view projection.** `ProductImageAddedDomainEvent` fires today, but the `ProductImageAddedProjectionDomainEventHandler` that updates `ImagesJson` on `product_search_view` is not implemented in v1 (the `RemoveProductImage` counterpart projection is). See [use-cases.md § 1.1.6](bc-design/use-cases.md).
- **Category breadcrumb projection seeding.** `CategoryCreatedProjectionDomainEventHandler` is a no-op placeholder today; future breadcrumb seeding per [catalog.md § 9](bc-design/catalog.md).
- **`CategoryReparentedEvent` external publication.** Reparenting raises `CategoryReparentedDomainEvent` (in-process descendant-path cascade) but publishes no external event in v1; reserved for later. See [use-cases.md § 1.1.9](bc-design/use-cases.md).

#### Basket
- **Cart abandonment re-engagement.** Hook Redis keyspace events → `NotifyUserCommand`.
- **Saved-for-later collection.** A parallel collection to the basket.

#### Ordering
- **Carrier SmartEnum.** `Carrier` is a free-form string today; bounded carrier set → SmartEnum migration.
- **Aggregate sales analytics.** Catalog consumer of `OrderConfirmedEvent` to power "top-selling products" surfaces.
- **Real-time order-status updates** (WebSocket / SSE). The current BFF `/api/bff/order-summary/{orderId}` endpoint has 30 s soft TTL + 5 min fail-safe; real-time push replaces the polling model.
- **Order-history pagination — keyset.** Today offset/limit (`Skip`/`Take`) per [ADR-0021 (read-side spec-less)](adr/0021-read-side-no-specifications.md); migration to keyset when per-buyer history grows large enough that offset's `O(skip)` matters.

#### Inventory
- **Stream snapshots** (performance). Designed in [inventory.md § 8.2](bc-design/inventory.md); the alert metric (`inventory.aggregate.rehydration.duration` p99 > 1s) is in v1 and is the trigger to implement the mechanism.
- **Configurable per-SKU low-stock thresholds.** `StockLevelChangedEvent` schema is ready; only the 0↔positive crossover fires today.
- **Multi-warehouse support.** Today one logical warehouse per `ProductId`; adding `LocationId` to the stream is the seam.
- **GDPR crypto-shredding** on the event store. Inventory streams carry user GUIDs only today; crypto-shredding is the path if PII ever enters an event. Tied to the broader PII work below.
- **Projection-drift validation job.** Nightly replay-sample comparison to detect silent projection bugs.

#### Payments
- **Chargebacks / disputes.** `PaymentDisputedEvent` would be added when a real gateway integration carries chargeback signals.
- **Partial refunds.** Today `RequestRefundCommand` refunds the full captured amount.
- **Reconciliation jobs.** Nightly comparison of gateway ledger to local `payments.transactions`.
- **Stale-payment reconciliation worker** (capture-then-compensate race). When the Checkout-saga `PaymentTimeout` fires while `PaymentProcessingSaga` is still running, Payments may eventually capture for an already-finalized outer saga. The worker reconciles by issuing a void or refund.

#### Invoicing
- **Tax-authority webhook delivery channel.** The `DeliveryChannel.TaxAuthorityWebhook` value exists but no delivery behavior is wired (v1 delivers on `Email` only); webhook to a stub SII / XRechnung endpoint.
- **Postal-mail delivery channel.** A new `DeliveryChannel` value (`PostalMail`) + PDF + envelope queue.
- **Archival process.** The `Archived` terminal state + `Invoice.Archive()` / `CreditNote.Archive()` transitions (`Delivered → Archived`) exist; the missing piece is the background job that drives them and moves PDFs to cold storage after N years.
- **Credit-note partial refund + adjustment reasons.** `CreditNoteReason` SmartEnum has `PartialRefund` + `Adjustment` slots beyond `OrderCancelled`.
- **`ResendInvoiceCommandHandler` production handler.** Today a stub (logging-only no-op); the design is an `invoice_delivery_log` insert (keyed `(InvoiceId, Channel)`, `Attempt` column) + an outbox `NotifyUserCommand` carrying a fresh producer-assigned `NotificationId` ([ADR-0031](adr/0031-notify-user-command-and-notification-id.md)).
- **`ResendInvoice` scope-based gating** (v2+). The endpoint gates on the `Admin` realm role (`AuthPolicies.InvoicingAdmin`) today; per [ADR-0010 § Implementation Notes](adr/0010-service-to-service-auth.md) the policy gains a `RequireClaim("scope", "invoicing.admin.*")` when scope-based gating lands.
- **Buffer-projection alarm** (credit-note flow). Today the `pending_invoices` buffer can grow indefinitely if `PaymentRefundedEvent` is never received after `OrderCancelledEvent`; a configurable timeout alarm fires when buffer rows age past a threshold.
- **Void-path cleanup job.** Today when a buyer cancels pre-capture, no invoice is issued (no `PaymentCapturedEvent`), so no credit note is needed; the `pending_invoices` row is left dangling. A cleanup job sweeps these.

#### Notifications
Notifications v2 ([notifications.md](bc-design/notifications.md), [ADR-0031](adr/0031-notify-user-command-and-notification-id.md) / [ADR-0032](adr/0032-notifications-dispatch-and-channels.md)) — the agreed design, in progress (#312–#317) — supersedes the v1 channel-scoped command with a channel-agnostic `NotifyUserCommand` fanned out per-channel via Hangfire. The sibling-command-per-channel plan is **superseded** by that fan-out:
- **Real SMS / push providers.** SMS already ships as a fan-out channel (fake log handler today); a real provider is a new `IChannelDispatcher` + a `template_channels` row — no sibling command/topic. Push is the same pattern (a new `ChannelType` + adapter).
- **Durable in-app inbox.** The bell ships as an **ephemeral SignalR live push** (no persistence). A durable feed (history, unseen-count badge, mark-read, HTTP poll, SSE replay) is the deferred evolution — see [notifications.md § 13](bc-design/notifications.md).
- **Preference HTTP + marketing consent.** Preferences are seeded (no API), transactional-only; a read/mutate HTTP surface and a marketing-consent system-of-record are deferred seams.

#### BFF
- **Service-to-service token-exchange fallback** (`IdentityServerTokenExchange`). Today the BFF forwards user-token-only; if no user token is present and an upstream requires auth, the upstream returns 401 and the BFF surfaces 401.
- **Language / region forwarding.** `Accept-Language` propagation through HTTP pipeline.
- **Personalized home page.** Requires auth + a "featured per buyer" service surface.
- **`IPaymentsClient` (Payments-as-BFF-query source).** Today `PaymentStatus` is derived from order fields (`Completed` / `Pending` / `Failed`); future moves to read `payments.transactions` projection or a Payments HTTP endpoint.
- **Real-time updates (WebSocket / SSE).** Tied to the Ordering item above.

#### Catalog / Search
- **Search indexer consumer** for `catalog.products` + `catalog.categories` events. Would land under `search-group` per [events-catalog § 3.1](bc-design/events-catalog.md).

---

### 2.4 Cross-cutting planned work

| Area | Item | Cross-reference |
|---|---|---|
| **PII / GDPR** | Crypto-shredding mechanism (the contract is defined; the mechanism is deferred) | [ADR-0011](adr/0011-pii-handling-gdpr.md) — current scope implements the allowlist + PII-column inventory + `*_enc` naming convention. Crypto-shredding is the next iteration. |
| **DLT operations** | Replay-admin operator CLI | [kafka-dlt-strategy.md § 7 F-4](bc-design/kafka-dlt-strategy.md) |
| **DLT operations** | Grafana `Kafka Consumer Health` dashboard JSON | [kafka-dlt-strategy.md § 7 F-6](bc-design/kafka-dlt-strategy.md) |
| **Saga observability** | Saga-terminal events (`CheckoutCompletedEvent` / `CheckoutFailedEvent` / `CheckoutStuckEvent`) — schemas + topic exist; cataloguing in [events-catalog.md § 2](bc-design/events-catalog.md) + consumer wiring | (To be filed as GitHub issue.) || **Performance** | High-volume tracing / span sampling configuration | Not in current scope per [ADR-0009](adr/0009-reference-solution-target-profile.md) |
| **Storage** | SSE-CMK encryption for PDF invoices (Azure Storage customer-managed keys) | [ADR-0011](adr/0011-pii-handling-gdpr.md) + [ADR-0017](adr/0017-blob-storage-cdn.md) |
| **Storage** | `product-images` blob container | [ADR-0017](adr/0017-blob-storage-cdn.md) |
| **Storage** | `payment-receipts` blob container (gateway-returned receipts; 7 yr retention) | [ADR-0017](adr/0017-blob-storage-cdn.md) |
| **PDF generation** | Multi-page `invoicing.pdf.page_count` span attribute | [ADR-0019](adr/0019-pdf-generation-questpdf.md) |

---

## 3. Open ADR placeholders

These are anchors for future ADRs that will be authored when the trigger condition above the corresponding row materializes:

- **ADR-XXXX — Extract Pricing into its own BC** (trigger: customer-segmented pricing, time-bound promotions, multi-currency-per-product, or region/tax-jurisdiction rules).
- **ADR-XXXX — Accounts bounded context** (trigger: address book, saved preferences, or marketing opt-in surface that forces a dedicated aggregate).
- **ADR-XXXX — Returns / RMA flow** (trigger: any feature beyond admin `MarkOrderDelivered` that needs post-delivery state).
- **ADR-XXXX — Cross-saga refund ownership** (trigger: [#290](https://github.com/DavidCapcuch/DotNetAtlas/issues/290) decision; either restore PaymentProcessingSaga-owned refund flow or acknowledge Checkout-owned refunds in master-design § 5.5).
- **ADR-XXXX — Kafka Topic config standardization** (trigger: the spawned chip's plan completes).
- **ADR-XXXX — PII crypto-shredding mechanism** (supersedes the deferred portion of [ADR-0011](adr/0011-pii-handling-gdpr.md)).

---

## 4. Out of scope, indefinitely

Per [ADR-0009 — Reference Solution Target Profile](adr/0009-reference-solution-target-profile.md), the following are explicitly out of scope for this reference solution and have no roadmap entry:

- **Highly-available production topology** — multi-region active-active, geographic failover.
- **Production-grade cluster sizing** — node count, replica factor > 1, dedicated Kafka brokers, dedicated PostgreSQL primary/replica.
- **Real production secrets management** beyond `appsettings.json` + env-var binding (no Azure Key Vault / AWS Secrets Manager in the demo path).
- **Real card-network integration** (PCI scope). The Payments gateway is intentionally a stub adapter.

A "production variant" of this reference would layer these in without rewriting the domain — that path is the natural next step for an adopter, not a planned milestone for the reference itself.

---

## 5. How to use this document

- **When working on a BC design doc**, if you need to mention a deferral, link here (`see [roadmap.md](../roadmap.md) § 2.3 Inventory`) rather than narrating the deferral inline.
- **When a real implementation push starts** for a planned item, promote that item to a GitHub issue and update the row here with the issue link.
- **When a planned item ships**, delete the row.
- **When you discover a new candidate**, add it to the right § 2.x table with a one-line description and the architectural seam / signal that would trigger it.
