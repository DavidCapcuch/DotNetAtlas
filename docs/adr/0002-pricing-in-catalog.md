# ADR-0002: Pricing inside Catalog (v1)

## Status

Accepted (2026-04-18)

## Context

The DotNetAtlas eShop reference solution introduces four new Bounded Contexts — **Catalog**, **Basket**, **Ordering**, and **Inventory** — plus a new Checkout saga. Every one of these BCs touches **product price**:

- **Catalog** is the product-information authority and, per the [eshop-general-plan.md § Bounded Contexts table](../eshop-general-plan.md), explicitly owns "pricing"
- **Basket** snapshots price into a `ProductSnapshot { Sku, Name, Price, CapturedAtUtc }` value object at add-time and freezes it until explicit user-initiated refresh (see [`basket.md § 3.2 ProductSnapshot`](../bc-design/basket.md))
- **Ordering** captures per-line `UnitPrice : Money` as a frozen field on each `OrderItem` via `Order.CreateFromBasket(BasketSnapshot, ...)` and carries it in `OrderCreatedEvent` (see [`ordering.md § OrderItem`](../bc-design/ordering.md))
- **Inventory** does not touch price but consumes `catalog.products` events for stock initialization

Price is therefore on the critical path for every major user flow — product browsing, adding to basket, price-drift refresh, checkout, and order creation. Decisions about where price lives have knock-on effects on every BC that consumes it and on the shape of the external Kafka contracts between them.

In real-world eShops, pricing is rarely a flat attribute on a product. Prices routinely diverge along multiple axes:

- **Customer segmentation** — B2B contracts with negotiated tier pricing vs anonymous B2C list pricing
- **Time-limited campaigns** — promotions, flash sales, seasonal discounts, coupon codes
- **Region and tax jurisdiction** — VAT/GST rules, regional surcharges, country-specific list prices
- **Currency** — multi-currency per product with conversion rules and rounding policies
- **Channel** — web vs app vs partner-API pricing that differs by distribution agreement

Mature systems extract this into a dedicated **Pricing / Promotions Bounded Context** that owns `Price` aggregates keyed by product + {segment, region, channel, validity window} and publishes `PriceCalculated` events for downstream consumption. A Pricing BC is a well-known DDD pattern, and the literature (Vernon, Evans, Khononov) all flag it as a canonical example of a supporting-to-core subdomain split.

For v1 of the reference solution, we must decide whether to introduce a Pricing BC now — teaching the pattern at the cost of conceptual surface — or keep pricing inside Catalog as a flat `Money` value object on the `Product` aggregate (the current shape described in [`catalog.md § Product`](../bc-design/catalog.md)) and defer the split until a real business driver appears. The ADR must also explain, for learners, *why* the v1 choice is a simplification and under what pressures they would reach the opposite decision in a production project.

## Decision Drivers (ranked)

1. **Minimize BC count in v1** — the reference solution already introduces 4 new BCs plus a new Checkout saga on top of the then-existing Weather/Alerts/Order/Payments estate. Adding Pricing nearly doubles the cross-BC coordination surface without teaching a fundamentally new DDD or integration pattern that is not already demonstrated elsewhere in the solution.
2. **Avoid teaching the wrong pattern** — learners must see *when* Pricing is a genuine BC and when it is not. A premature split in a simple, flat-priced catalog obscures the decision criteria and risks being cargo-culted into future projects where it is equally unjustified.
3. **Keep critical paths short** — every `AddItemToBasketCommand` and every `Order.CreateFromBasket` is a price touchpoint. An additional synchronous hop to a Pricing service (or an eventually-consistent price book) adds latency, new failure modes, and a snapshot-drift window between Pricing's view and Catalog's view of the same product.
4. **Keep the option to split later** — Catalog's price is already encapsulated in a `Money` VO and surfaced to the outside world via `ProductPriceChangedEvent` (Avro, topic `catalog.products`). Extracting Pricing later is a targeted refactor behind a stable contract, not a rewrite.

## Considered Options

### Option 1: Price flat inside Catalog (chosen)

Price lives on the `Product` aggregate as a `Money` value object — `decimal Amount` (strictly > 0) plus ISO 4217 `Currency`. One price per product, one currency per product, no segmentation, no time-bound promotions.

- **Write path:** pricing changes flow through `Product.UpdatePrice(Money newPrice, DateTimeOffset utcNow)` which raises `ProductPriceChangedDomainEvent` only when the price actually changes (same-value update is a no-op)
- **Publication:** an outbox publisher translates the internal event into the external Avro `ProductPriceChangedEvent` on topic `catalog.products`
- **Read path (downstream):** Basket and Ordering never call a pricing service — they snapshot `Product.Price` at add-time (Basket) and at `CreateFromBasket` time (Ordering)
- **Read path (Catalog itself):** `product_search_view` carries `PriceAmount` / `PriceCurrency` columns updated by the projection handler in the same transaction as the aggregate save

### Option 2: Dedicated Pricing BC from day 1

A separate service owning `Price` aggregates keyed by `ProductId + {segment, region, channel}` with validity windows.

- Catalog publishes `ProductCreatedEvent`; Pricing consumes and seeds a default price book entry per product
- Basket and Ordering query Pricing (synchronously, or via a locally-projected read model) for the effective price per user/segment/time
- Catalog no longer knows the customer-facing price — it only knows product identity, taxonomy, and descriptive content
- Requires a 5th service: `Pricing.Api`, `Pricing.Application`, `Pricing.Domain`, `Pricing.Infrastructure`, plus its own PostgreSQL schema, outbox, outbox relay worker, Avro namespace, and topic (`pricing.prices`)

### Option 3: Hybrid — Catalog owns list price, Pricing computes effective price

A compromise between Options 1 and 2.

- Catalog keeps a "list price" on `Product` (same shape as Option 1)
- A Pricing service layers promotions, discounts, and segment rules on top, computing the effective price on demand from the Catalog list price plus its own rule set
- Basket and Ordering snapshot the *effective* price returned by Pricing, not the list price from Catalog
- Both services exist from day 1 and the read path has two hops: Catalog (list price) → Pricing (effective price) → Basket/Ordering

## Evaluation Matrix

| Driver (ranked)                              | Option 1: Flat in Catalog | Option 2: Dedicated Pricing BC | Option 3: Hybrid |
|----------------------------------------------|---------------------------|--------------------------------|------------------|
| 1. Minimize BC count in v1                   | Pricing folded into Catalog — no new BC | Adds a 5th new BC plus its own storage/outbox/relay | Adds a 5th new BC while still keeping Catalog |
| 2. Avoid teaching the wrong pattern          | Teaches "only split when segmentation is real"; ADR names the trigger | Risks cargo-culting a split that isn't justified by the v1 domain | Worst of both — learners see complexity without a clear motivating requirement |
| 3. Keep critical paths short                 | Price is local to Catalog; Basket snapshots via one ACL hop | Extra hop from Basket to Pricing on every add + snapshot drift window | Two hops on every effective-price read; Basket must reason about both sources |
| 4. Keep the option to split later            | `Money` VO + `ProductPriceChangedEvent` are stable seams ready for extraction | Already split — no migration needed, but the split may never be justified | Split already paid for; extraction in reverse (merge) is unusual and awkward |

## Decision

We will use **Option 1: Price flat inside Catalog** for v1.

## Rationale

**Option 1 best serves every ranked driver.** It folds pricing into an existing BC (driver 1), presents learners with a simple baseline and a clearly-named trigger for splitting (driver 2), keeps `AddItem` and checkout flows to a single ACL hop against Catalog (driver 3), and leaves a stable `Money` VO plus external `ProductPriceChangedEvent` contract behind which a future Pricing BC can be extracted (driver 4).

Option 2 inverts drivers 1–3 without a compensating domain justification in v1. The reference solution has no B2B segments, no promotional rules, no multi-currency users, and no region/tax variation — the Pricing BC would be a ceremonial shell with a trivial one-price-per-product "rule set". Building it anyway would force learners to carry extra conceptual load to understand a pattern whose motivating requirements have been deliberately engineered out of v1.

Option 3 pays both costs without either benefit. Two services exist, two Kafka topics exist, two Avro namespaces exist, and yet in v1 the Pricing service is computing "effective price = list price" — a pure identity function dressed up as a domain. Learners see the shape of a hybrid solution without seeing the forces that justify it, which is worse for pedagogy than either pure option. It also locks in a read-path double-hop that is hard to reverse.

**This is a conscious simplification, not a general rule.** Real-world eShops outgrow flat Catalog pricing quickly, and the ADR must make that explicit so learners do not generalize "price lives in Catalog" to every system they build. The companion BC design ([`catalog.md § Pattern Showcase: CQRS Read Projection`](../bc-design/catalog.md)) keeps the Catalog pattern focused on its real teaching target — a denormalized read view built from internal domain events — without muddying the lesson with pricing-rule machinery that v1 has no user for.

**The extraction path is preserved, not accidental.** `Money` is already a value object with `Amount > 0` and ISO 4217 currency validation, and it is shared-kernel for Catalog/Basket/Ordering (see [`basket.md § 3.4 Money`](../bc-design/basket.md)). The external `ProductPriceChangedEvent` carries `OldPriceAmount`, `NewPriceAmount`, and `Currency` on topic `catalog.products` — the exact shape a future Pricing BC would need to seed its own store from historical Catalog events. Basket's ACL already copies price into a `ProductSnapshot` rather than holding a live reference to Catalog, so swapping the ACL's source from Catalog to Pricing is a one-class change in `ProductCatalogHttpAdapter`. This is the cheapest possible "open for extension" stance consistent with keeping v1 simple.

**Learners see *when* the split becomes justified.** The Negative Consequences and the placeholder future ADR below name the triggers — segmented pricing, time-bound promotions, multi-currency-per-product, or cross-region tax rules. When one of those appears in a real project, the team knows it is time to extract, and the existing contracts tell them where to cut. The reverse — a system that ships with Pricing split from day 1 because "that's what the book says" — has no comparable teaching moment; the extraction decision has been made without ever showing the forces that motivate it.

**Alignment with the centralized saga decision.** ADR-0001 already commits to a centralized saga hosting the Checkout flow. That saga consumes `BasketCheckoutInitiatedEvent` and orchestrates Ordering → Inventory → Payment. Keeping price inside Catalog means the saga never touches pricing concerns: the price snapshots travel inside the Basket event, and compensation/retry logic does not need to reason about price volatility across steps. Adding a Pricing BC would have introduced either a pre-saga price-resolution step or an additional saga participant — either way, additional coupling that v1 gains nothing from.

## Consequences

### Positive

- Simpler reference implementation — one fewer service to build, one fewer schema registry namespace, one fewer outbox relay worker to deploy and operate
- No cross-service synchronous call to fetch a price; `AddItemToBasketCommand` stays at one ACL hop to Catalog and is not exposed to a Pricing-service outage as an additional failure mode
- Clean teaching of Catalog's CQRS read-projection pattern — the `product_search_view` stays focused on search, filter, and display concerns, not on resolving a pricing-rules engine
- `ProductPriceChangedEvent` is sufficient for Basket's price-drift UX: the BFF (or Basket itself at refresh time) compares the frozen `ProductSnapshot.Price` against the current `Product.Price` and surfaces changes to the user without Basket needing to consume Kafka from Catalog
- Ordering captures `UnitPrice` on `OrderItem` as a per-line snapshot and is entirely insulated from how pricing is computed upstream — that snapshot boundary survives any future Pricing BC extraction unchanged
- The Checkout saga (ADR-0001) carries price snapshots end-to-end without needing to touch a pricing service at any step — compensation and retries don't have to reason about "what price was this at the time?"

### Negative

- B2B/B2C segmentation cannot be modeled in v1 — every user sees the same price for a given product, regardless of tenant or role
- No time-bound promotions, coupon codes, or campaign discounts in v1 (Basket explicitly defers coupons per [`basket.md § 15 Open Questions / Deferred`](../bc-design/basket.md))
- Each product is single-currency for its entire lifetime; a product priced in both USD and EUR would need two `Product` rows in v1, with separate SKUs and no shared identity
- No region- or tax-jurisdiction-specific pricing — tax handling, if later added, would need its own dedicated design
- When segmented pricing is later required, the team must extract the Pricing BC — a deliberate refactor with a migration path (Catalog stops owning price; Pricing owns price; Basket ACL retargets from `GET /api/products/{id}` to a Pricing endpoint). The contracts are stable, but the cutover is real work and touches three services

### Risks

- **Learners assume "price always lives in Catalog"** — mitigate by making this ADR explicitly a v1 simplification and by documenting the split triggers in the future-ADR placeholder below. The `catalog.md` section also links back to this ADR so that any reader of the BC design sees the decision framing, not just the chosen shape
- **Scope creep during v1** — product teams frequently request "just one promotion" or "just one segment"; granting either inside Catalog silently builds the wrong model. Mitigate by deferring all such requests to v2+ and pointing at this ADR. If a real business need for segmentation appears mid-v1, that is the signal to open the Pricing extraction ADR, not to bolt rules onto `Product`
- **Catalog becomes the de-facto home for non-price commercial rules** (e.g., "bundle pricing", "buy-2-get-1") because "it's where price already is" — mitigate by rejecting such rules from Catalog's `Product` aggregate and treating their arrival as the signal to extract Pricing
- **Stale snapshots in Basket** — because Catalog owns price and Basket freezes it at add-time, a price reduction in Catalog does not flow into existing baskets until the user triggers refresh. This is a deliberate correctness-over-freshness trade (see [`basket.md § 6.3`](../bc-design/basket.md)), but it does mean the UX must surface "price changed since you added this" clearly. The BFF does this by comparing the snapshot to the current Catalog value

## Implementation Notes

### Domain shape

- Price is a `Money` VO: `decimal Amount` (strictly > 0) + ISO 4217 `Currency` string — the exact shape already used in [`basket.md § 3.4 Money`](../bc-design/basket.md) and shared via `platform/Platform.SharedKernel/ValueObjects/Money.cs`
- `Product.UpdatePrice(Money newPrice, DateTimeOffset utcNow) : Result` is the **only** mutation path for price; no direct field setter, no bulk import bypass
- `Discontinued` products cannot be repriced — `UpdatePrice` returns `Result.Fail(ProductErrors.CannotRepriceDiscontinued())`
- A price update that matches the current `Price` (same amount and currency) is a no-op — no `ProductPriceChangedDomainEvent` is raised, no outbox row is written, no projection refresh runs
- `Currency` is immutable after product creation — v1 does **not** support currency swap on a product; changing currency requires creating a new product with a distinct SKU

### Events

- Internal `ProductPriceChangedDomainEvent` carries `ProductId`, `OldPrice`, `NewPrice`, `OccurredOnUtc` — dispatched in-process to the projection handler
- External Avro `ProductPriceChangedEvent` on topic `catalog.products` carries `ProductId`, `Sku`, `OldPriceAmount`, `NewPriceAmount`, `Currency`, `ChangedAtUtc` — see [`catalog.md § ProductPriceChangedEvent`](../bc-design/catalog.md) for the full schema
- Kafka message key for `ProductPriceChangedEvent` is `ProductId` as string — enables per-product ordering within a partition, which is required for "latest price wins" consumer semantics downstream
- Publication follows the platform outbox pattern — the outbox row is written in the same DbContext transaction as the aggregate save, and `Catalog.OutboxRelay` relays to Kafka with schema-registry validation

### Consumers

- Basket's ACL (`IProductCatalogQueryPort`) continues to return `ProductSnapshot { Sku, Name, Price, CapturedAtUtc }` — no shape change needed if Pricing is later extracted; only the adapter's HTTP target changes
- Basket may optionally consume `catalog.products` to eagerly flag stale snapshots — v1 defers this in favor of on-demand refresh at checkout (see [`basket.md § 6.3`](../bc-design/basket.md))
- Ordering's `OrderItem.UnitPrice` remains a frozen per-line snapshot passed in via `Order.CreateFromBasket(BasketSnapshot, ...)`; this boundary survives any future Pricing extraction unchanged
- BFF invalidates product-detail caches on `ProductPriceChangedEvent` receipt
- The Checkout saga never re-reads price — it operates on the snapshot embedded in `BasketCheckoutInitiatedEvent` for the full duration of the flow

### Read view

- The projection handler updates `PriceAmount` / `LastUpdatedAtUtc` on `product_search_view` in the same `SaveChangesAsync` transaction as the aggregate — read view never drifts
- `PriceAmount` has a btree index to support price-range queries (`WHERE PriceAmount BETWEEN 500 AND 2000`)
- `PriceCurrency` (`char(3)`) is stored alongside amount on each row — queries filter on both

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — establishes the centralized saga model that the Checkout flow (Basket → Ordering) plugs into, carrying price snapshots end-to-end without reaching back to Catalog
- Future: **ADR-XXXX — Extract Pricing into its own BC** (placeholder for v2+, triggered by any one of: customer-segmented pricing, time-bound promotions, multi-currency-per-product, or region/tax-jurisdiction rules)
