# BFF Aggregation — eShop Reference Solution

> **Status:** DRAFT (Stage 2 Agent 7)
> **Target section in master design:** [eshop-master-design.md § 9](../eshop-master-design.md)
> **Companion file:** [use-cases.md](./use-cases.md) (commands/queries of the four upstream services)
> **Stage 1 inputs:** [catalog.md](./catalog.md), [basket.md](./basket.md), [ordering.md](./ordering.md), [inventory.md](./inventory.md)

This document specifies the **Backend-for-Frontend** (BFF) service: the public-facing aggregation HTTP API consumed by the eShop web/mobile clients. The BFF lives in `src/EShop.BFF/` and composes responses from the four internal services (Catalog, Basket, Ordering, Inventory). The BFF has no own database and no own domain — it is a pure composition + caching + resilience layer.

**Design lineage:** BFF positioning and relationship to YARP are already fixed in [eshop-general-plan.md](../eshop-general-plan.md) (YARP handles routing/SSL; BFF handles response aggregation). This document specifies the four endpoints, the HTTP client contracts, the resilience pipeline, the caching strategy, and the Kafka invalidation consumer.

---

## 1. Project Structure

```
src/EShop.BFF/
├── EShop.BFF.Api/
│   ├── Endpoints/
│   │   ├── ProductPageEndpoint.cs
│   │   ├── BasketEndpoint.cs
│   │   ├── OrderSummaryEndpoint.cs
│   │   └── HomePageEndpoint.cs
│   ├── Responses/
│   │   ├── ProductPageResponse.cs
│   │   ├── BasketPageResponse.cs
│   │   ├── OrderSummaryResponse.cs
│   │   └── HomePageResponse.cs
│   ├── Common/
│   │   ├── BffGroup.cs                  # FastEndpoints group with shared policies
│   │   └── ResponseExtensions.cs        # HasStaleData header helpers
│   └── Program.cs
└── EShop.BFF.Infrastructure/
    ├── Clients/
    │   ├── Catalog/
    │   │   ├── ICatalogClient.cs
    │   │   ├── CatalogHttpClient.cs
    │   │   └── CatalogDtos.cs
    │   ├── Basket/
    │   │   ├── IBasketClient.cs
    │   │   ├── BasketHttpClient.cs
    │   │   └── BasketDtos.cs
    │   ├── Ordering/
    │   │   ├── IOrderingClient.cs
    │   │   ├── OrderingHttpClient.cs
    │   │   └── OrderingDtos.cs
    │   └── Inventory/
    │       ├── IInventoryClient.cs
    │       ├── InventoryHttpClient.cs
    │       └── InventoryDtos.cs
    ├── Caching/
    │   └── BffFusionCacheDependencyInjection.cs
    ├── Resilience/
    │   ├── ResiliencePipelines.cs       # Polly pipelines (timeout, retry, CB)
    │   └── AuthForwardingHandler.cs     # DelegatingHandler for JWT pass-through
    ├── Messaging/
    │   ├── CacheInvalidationConsumerGroup.cs  # KafkaFlow consumer group config
    │   └── Handlers/
    │       ├── ProductEventCacheInvalidator.cs
    │       ├── CategoryEventCacheInvalidator.cs
    │       ├── StockEventCacheInvalidator.cs
    │       ├── OrderEventCacheInvalidator.cs
    │       └── BasketEventCacheInvalidator.cs
    └── Common/
        └── BffInfrastructureDependencyInjection.cs
```

**Architecture test expectations:**

- `EShop.BFF.Api` depends on `EShop.BFF.Infrastructure` and `Platform.*` only — no direct references to `Catalog.*`, `Basket.*`, `Ordering.*`, or `Inventory.*` assemblies.
- `EShop.BFF.Infrastructure` depends on `Platform.*` and on `Platform.SchemaRegistry.Contracts` (for Avro records that drive cache invalidation) — no references to other service projects.
- All upstream contracts are re-declared as BFF-internal DTOs (`CatalogProductDto`, `BasketDto`, etc.). Upstream service types never cross the BFF's process boundary — same Anti-Corruption discipline Basket uses toward Catalog.

---

## 2. Cross-Cutting Concerns

### 2.1 HTTP client resilience

Every typed client uses a Polly-based resilience pipeline (registered via `AddResilienceHandler` on the typed-client builder). Defaults:

| Pipeline stage | Configuration |
|----------------|---------------|
| **Timeout (per attempt)** | 2 seconds for single-item calls; 10 seconds for batch calls (`GetProductsByIds`, `GetStockLevelsBulk`). Overrides via `HttpClientOptions.TimeoutSeconds` per client. |
| **Retry** | Exponential backoff: 100 ms × 2^attempt + ±50 ms jitter. Max 2 attempts (so worst-case 3 total calls). Retries only on `HttpRequestException`, `TaskCanceledException` (timeout), and HTTP 5xx/408/429. 4xx (except 408/429) are NOT retried. |
| **Circuit breaker** | Open after 5 consecutive failures within a 10-second sampling window. Break duration: 30 seconds. Half-open probe: 1 request. Configured per client (a Catalog outage must not break calls to Inventory). |
| **Total request timeout (outer)** | 15 seconds — hard wall across all attempts including retries. Prevents retry storms from amplifying an upstream latency spike. |

Each client resolves the resilience pipeline by name (`"catalog"`, `"basket"`, `"ordering"`, `"inventory"`) so the client-specific state (CB counters, etc.) is isolated.

### 2.2 Cache invalidation Kafka consumer

**Consumer group:** `bff-cache-invalidator`.

**Subscribed topics and per-topic handler → tag mapping:**

| Topic | External event type | Handler | FusionCache invalidation action |
|-------|--------------------|---------|--------------------------------|
| `catalog.products` | `ProductCreatedEvent` | `ProductEventCacheInvalidator` | `RemoveByTagAsync("home-page")` (new product may be featured). |
| `catalog.products` | `ProductPriceChanged` | `ProductEventCacheInvalidator` | `RemoveByTagAsync("product-{ProductId}")` + `RemoveByTagAsync("home-page")`. |
| `catalog.products` | `ProductDiscontinuedEvent` | `ProductEventCacheInvalidator` | `RemoveByTagAsync("product-{ProductId}")` + `RemoveByTagAsync("home-page")`. |
| `catalog.categories` | `CategoryCreatedEvent` | `CategoryEventCacheInvalidator` | `RemoveByTagAsync("home-page")` (category tree changed). |
| `inventory.stock-events` | `StockLevelChanged` | `StockEventCacheInvalidator` | `RemoveByTagAsync("product-{ProductId}")` + `RemoveByTagAsync("home-page")`. |
| `inventory.reservations` | `StockReservedEvent`, `ReservationConfirmedEvent`, `ReservationReleasedEvent` | `StockEventCacheInvalidator` | `RemoveByTagAsync("product-{ProductId}")` (Available changed). |
| `ordering.orders` | `OrderConfirmedEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderCancelledEvent`, `OrderFailedEvent` | `OrderEventCacheInvalidator` | `RemoveByTagAsync("order-{OrderId}")` + `RemoveByTagAsync("order-history-{BuyerId}")`. |
| `basket.sessions` | `BasketCheckoutInitiatedEvent` | `BasketEventCacheInvalidator` | `RemoveByTagAsync("basket-bff-{UserId}")` (basket has been converted to an order — aggressively clear the BFF's basket cache). |

**Middleware pipeline** — same shape as the service inbox consumers:

1. Avro deserialization.
2. **No inbox middleware** is required on the BFF side because cache invalidation is idempotent by construction (`RemoveByTag` is a no-op when the tag is absent). Double-invalidation is cheap; missing invalidation would be a correctness bug, but at-least-once Kafka delivery covers that.
3. Handler dispatch.
4. DLT on exception.

**Ordering guarantee note.** Because cache invalidation is an idempotent set-membership removal operation, out-of-order deliveries across partitions are harmless. Within a single partition (keyed by `ProductId` / `OrderId` / `UserId`), order is preserved by Kafka.

### 2.3 Authentication pass-through

The BFF presents a Keycloak-issued JWT from the end-user (same realm as the services). It forwards this JWT verbatim to upstream services through a shared `AuthForwardingHandler : DelegatingHandler`:

```text
class AuthForwardingHandler : DelegatingHandler {
    private readonly IHttpContextAccessor _ctx;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) {
        if (_ctx.HttpContext?.Request.Headers.TryGetValue("Authorization", out var auth) == true)
            req.Headers.TryAddWithoutValidation("Authorization", auth.ToString());
        return base.SendAsync(req, ct);
    }
}
```

Registered before the resilience handler so retry copies of the request preserve the Authorization header. For public BFF endpoints (home page, product page), the handler simply has nothing to forward — upstream anonymous endpoints accept calls without `Authorization`.

**Service-to-service fallback (v2):** not in v1. In v1 the BFF always forwards whatever the user sent; if no user token is present and the upstream requires auth, the upstream returns 401 and the BFF surfaces a 401. Future work could use `IdentityServerTokenExchange` (client credentials) as a fallback.

### 2.4 Observability

- **OpenTelemetry tracing** is already wired via existing `Platform.*` observability packages. The typed `HttpClient` automatically propagates `traceparent` on every call, so the trace spans end-to-end: Client → BFF endpoint → upstream service handler → DB.
- **Custom span tags** per endpoint:
  - `bff.endpoint` — one of `product-page`, `basket`, `order-summary`, `home-page`.
  - `bff.cache.hit` — bool (did the BFF return from cache without upstream calls?).
  - `bff.stale` — bool (was the response served with `HasStaleData: true`?).
- **Metrics** (OpenTelemetry meter `EShop.BFF`):
  - `bff.cache.hits` — counter, tagged `{ endpoint }`.
  - `bff.cache.misses` — counter.
  - `bff.upstream.calls` — counter, tagged `{ client = catalog|basket|ordering|inventory, outcome = success|timeout|5xx|circuit-open }`.
  - `bff.partial_response` — counter, tagged `{ endpoint }` — incremented when any upstream call failed but the endpoint still returned 200 with partial data.
- **Structured logging** uses Serilog + enrichers: every upstream call logs `{ Client, Method, Path, DurationMs, StatusCode }`. Cache events log `{ Tag, Operation = hit|miss|invalidation }`.

### 2.5 YARP positioning

YARP (per `eshop-general-plan.md`) handles coarse routing concerns — SSL termination, rate limiting (public-facing), and path-based routing that selects BFF vs. admin APIs. YARP does NOT do response aggregation. The request flow for a consumer request is:

```
Client → YARP (TLS, rate limit, routing) → BFF /api/bff/... → (internal services) → BFF → YARP → Client
```

Admin/ops tools bypass the BFF and call service endpoints directly through YARP admin routes. YARP config is out of this document's scope (Stage 3 / platform-architect).

---

## 3. Endpoints

### 3.1 `GET /api/bff/product-page/{productId}`

Public product-detail page — composes Catalog (product info) + Inventory (stock availability) with optional Basket (has the current user already added this?).

#### 3.1.1 Surface

- **HTTP route and method:** `GET /api/bff/product-page/{productId}`.
- **Authentication/authorization:** **Optional auth.** Anonymous users get the public product page; authenticated users additionally receive `AlreadyInBasket` populated from the Basket service.
- **Request params:**
  - `productId` (route, Guid).
  - No query params in v1 (no language / region selection — deferred to v2).
- **Upstream service calls** (parallel):
  - `CatalogClient.GetProductByIdAsync(productId, ct)`.
  - `InventoryClient.GetStockLevelAsync(productId, ct)`.
  - `BasketClient.GetBasketAsync(ct)` — **only if user is authenticated.** No-op for anonymous.
- **Response composition logic:**
  1. `Task.WhenAll` the two (or three) upstream calls with shared `CancellationToken`.
  2. If Catalog returns 404 → BFF returns 404 (no product = no page). Skip composition.
  3. If Catalog succeeds:
     - Build `Product` portion from Catalog response.
     - If Inventory returned success → populate `InStock` = `Available > 0`, `AvailableQty` = `Available`. If Inventory failed (timeout / 5xx / CB open) → fall back to stale cached value if any; else `InStock = null`, `AvailableQty = null`, `HasStaleData = true`.
     - If Basket call was made and succeeded → set `AlreadyInBasket` = item present in `Basket.Items` with `ProductId == productId`; `BasketQuantity` = quantity (or null).
- **Response shape** (`ProductPageResponse`):
  ```
  {
    "product": {
      "productId": "Guid",
      "sku": "string",
      "name": "string",
      "description": "string",
      "brandName": "string",
      "categoryBreadcrumb": "string (e.g. 'Electronics > Computers > Laptops')",
      "categoryPath": "string (e.g. '/electronics/computers/laptops')",
      "price": { "amount": "decimal", "currency": "string" },
      "dimensions": { "length": "decimal", "width": "decimal", "height": "decimal", "unit": "string" } | null,
      "images": [ { "url": "string", "altText": "string", "displayOrder": "int" } ],
      "status": "string"
    },
    "inStock": "bool | null",
    "availableQty": "int | null",
    "alreadyInBasket": "bool | null",
    "basketQuantity": "int | null",
    "hasStaleData": "bool",
    "generatedAtUtc": "DateTimeOffset"
  }
  ```
- **Caching strategy:**
  - FusionCache key: `"product-page:{productId}"`.
  - Tag: `"product-{productId}"`.
  - TTL (soft): 5 minutes.
  - FailSafeMaxDuration: 30 minutes (stale cache may be served up to 30 min past soft TTL when upstream unavailable).
  - JitterMaxDuration: 30 seconds (prevents thundering herd on cache expiry).
  - The *cached* portion is the anonymous `Product + InStock + AvailableQty` composite. The `AlreadyInBasket` enrichment is computed per-request without caching — basket is ephemeral.
  - Storage: FusionCache with Redis backplane already wired in `PersistenceDependencyInjection.AddCache` of the reference services; BFF reuses this pattern with its own named cache `"bff"` (so its policy and eviction are independent of the service-level caches).
- **Failure modes:**

  | Failure | Behavior | Headers |
  |---------|----------|---------|
  | Catalog timeout | Fail-safe: serve stale cached `ProductPageResponse` if any; else 503. `HasStaleData = true` on stale serve. | `X-BFF-Stale: true` when serving stale. |
  | Catalog 5xx | Same as timeout. | Same. |
  | Catalog 404 | Return 404. No partial response. | — |
  | Inventory timeout / 5xx | Return product with `InStock = null`, `AvailableQty = null`, `HasStaleData = true`. 200 OK. | `X-BFF-Stale: true`; `X-BFF-PartialData: inventory`. |
  | Inventory 404 | Same as timeout — indicates stock item not initialized yet; treat as "unknown availability". | `X-BFF-PartialData: inventory`. |
  | Basket timeout / 5xx / 4xx (auth path only) | Set `AlreadyInBasket = null` + continue. Never let basket failure break product page. | `X-BFF-PartialData: basket`. |
  | Catalog circuit open | Serve stale cache or 503. Do not issue call. | `X-BFF-Stale: true` (if stale). |
  | Network unavailable | Serve from cache unconditionally with `HasStaleData = true`. If no cache, 503. | `X-BFF-Stale: true`. |
- **Cache invalidation hooks (external Kafka events):**
  - `catalog.products` topic: on `ProductPriceChanged` or `ProductDiscontinuedEvent` with matching `ProductId` → `RemoveByTagAsync("product-{ProductId}")`.
  - `inventory.stock-events` topic: on `StockLevelChanged` → `RemoveByTagAsync("product-{ProductId}")`.
  - `inventory.reservations` topic: on `StockReservedEvent` / `ReservationConfirmedEvent` / `ReservationReleasedEvent` → `RemoveByTagAsync("product-{ProductId}")` (because `Available` shifted).

### 3.2 `GET /api/bff/basket`

Authenticated user's current basket enriched with *current* Catalog prices and *current* Inventory availability — so the UI can flag "price changed since you added" and "out of stock since you added" without the user needing to refresh.

#### 3.2.1 Surface

- **HTTP route and method:** `GET /api/bff/basket`.
- **Authentication/authorization:** **Required.** `UserId` from JWT `ClaimTypes.NameIdentifier`.
- **Request params:** none (user identity is the entire implicit filter).
- **Upstream service calls** (sequential + parallel):
  1. `BasketClient.GetBasketAsync(ct)` — always first; the basket shape determines everything downstream.
  2. If basket is empty (Items.Count == 0) → return `BasketPageResponse` with empty items and zero totals. Skip steps 3 and 4.
  3. **Parallel batch enrichment:** `Task.WhenAll`:
     - `CatalogClient.GetProductsByIdsAsync(productIds, ct)` — fetch authoritative current price for every product in basket.
     - `InventoryClient.GetStockLevelsBulkAsync(productIds, ct)` — fetch current Available qty.
- **Response composition logic:**
  1. For each basket item:
     - `SnapshotPrice` = basket's stored unit price (what user saw when they added).
     - `CurrentPrice` = from Catalog batch response, matched by ProductId. Null if Catalog omitted this product (discontinued or transient missing).
     - `PriceDrifted` = `CurrentPrice != SnapshotPrice` (by value) when both are known.
     - `AvailableQty` = from Inventory batch, matched by ProductId. Null if Inventory omitted.
     - `OutOfStock` = `AvailableQty != null && AvailableQty < basketItem.Quantity`.
  2. Compute `TotalSnapshot` = sum of `SnapshotPrice × Quantity`.
  3. Compute `TotalCurrent` = sum of (`CurrentPrice ?? SnapshotPrice`) × Quantity — defensive fallback to snapshot when current is unknown.
  4. Compute `HasStaleData` = any upstream call (Basket / Catalog batch / Inventory batch) failed; or any item has a null `CurrentPrice` or `AvailableQty`.
- **Response shape** (`BasketPageResponse`):
  ```
  {
    "userId": "Guid",
    "version": "int (from basket)",
    "items": [
      {
        "productId": "Guid",
        "sku": "string",
        "name": "string",
        "quantity": "int",
        "snapshotPrice": { "amount": "decimal", "currency": "string" },
        "currentPrice": { "amount": "decimal", "currency": "string" } | null,
        "priceDrifted": "bool",
        "lineTotalSnapshot": { "amount": "decimal", "currency": "string" },
        "lineTotalCurrent": { "amount": "decimal", "currency": "string" },
        "availableQty": "int | null",
        "outOfStock": "bool",
        "primaryImageUrl": "string | null"
      }
    ],
    "totalSnapshot": { "amount": "decimal", "currency": "string" },
    "totalCurrent": { "amount": "decimal", "currency": "string" },
    "hasPriceDrift": "bool (any item with priceDrifted == true)",
    "hasOutOfStock": "bool (any item with outOfStock == true)",
    "hasStaleData": "bool",
    "generatedAtUtc": "DateTimeOffset"
  }
  ```
- **Caching strategy:**
  - FusionCache key: `"basket-bff:{userId}"`.
  - Tag: `"basket-bff-{userId}"`.
  - TTL (soft): 15 seconds — baskets are ephemeral; users want near-real-time feedback after mutations.
  - FailSafeMaxDuration: 2 minutes.
  - Cache is **per-user** and **bypasses automatically on any basket mutation** via `RemoveByTagAsync` triggered by the `basket.sessions` cache invalidator (see § 2.2). Since the BFF does not run basket mutations through itself (clients call `/api/basket/*` directly), the invalidation has a ≤ seconds delay.
  - Tradeoff documented: the 15-second TTL means a user's successive `POST /api/basket/items` followed by `GET /api/bff/basket` may see the pre-mutation state for up to 15 seconds if the basket mutation happened out-of-band and the Kafka invalidation event has not arrived. In practice the client SHOULD call `GET /api/basket` (direct) immediately after a mutation for up-to-date state; the BFF-level view is for page loads, not post-mutation freshness. This is a deliberate choice favoring backend reuse over per-request overhead.
- **Failure modes:**

  | Failure | Behavior | Notes |
  |---------|----------|-------|
  | Basket timeout / 5xx | Fail-safe stale cache if any; else 503. `HasStaleData = true`. | First call — cannot proceed without basket. |
  | Basket 404 | Treat as empty basket: return `BasketPageResponse { Items: [], TotalSnapshot: 0, ..., HasStaleData: false }` 200 OK. | Basket is lazily created. |
  | Catalog batch timeout / 5xx | Proceed with `CurrentPrice = null` on all items (falling back to `SnapshotPrice` in `LineTotalCurrent`). `HasStaleData = true`. | `X-BFF-PartialData: catalog`. |
  | Inventory batch timeout / 5xx | Proceed with `AvailableQty = null`, `OutOfStock = false`. `HasStaleData = true`. | `X-BFF-PartialData: inventory`. |
  | Catalog returns partial (some productIds missing) | Those items get `CurrentPrice = null, PriceDrifted = false`. | Matches Catalog's partial-tolerant contract. |
  | Inventory returns partial (products in `MissingProductIds`) | Those items get `AvailableQty = null`. | Same. |
  | Network unavailable | Serve from cache with `HasStaleData = true`. If no cache, 503. | — |
- **Cache invalidation hooks:**
  - `basket.sessions` topic: on `BasketCheckoutInitiatedEvent` → `RemoveByTagAsync("basket-bff-{UserId}")`. (Other basket mutations don't emit Kafka events; the 15-second TTL is the freshness guarantee for those.)
  - `catalog.products` topic: on `ProductPriceChanged` → `RemoveByTagAsync("basket-bff-*")` is **too aggressive** (would invalidate every basket on every price change). Instead, `PriceDrifted` is computed freshly on each (non-cached) request. The 15-second TTL absorbs the in-window drift.
  - `inventory.stock-events`: similarly, per-user basket invalidation on stock events would fan out too broadly. Accepted as stale within TTL.

### 3.3 `GET /api/bff/order-summary/{orderId}`

Authenticated user's detailed order view — composes Ordering (order record) + Catalog (current product snapshots, optional) + Payments (payment status, future).

#### 3.3.1 Surface

- **HTTP route and method:** `GET /api/bff/order-summary/{orderId}`.
- **Authentication/authorization:** **Required.** User must own the order (`BuyerId == claim.sub`) unless `admin` role — enforced upstream by Ordering's `GetOrderByIdQuery` handler (BFF just forwards the JWT).
- **Request params:**
  - `orderId` (route, Guid).
- **Upstream service calls:**
  1. `OrderingClient.GetOrderByIdAsync(orderId, ct)` — first. If 404 → BFF 404 (no order means no summary).
  2. **Parallel enrichment** once order is loaded:
     - `CatalogClient.GetProductsByIdsAsync(orderItemProductIds, ct)` — fetch *current* product metadata for display enrichment (name changes, current images, current price — NOT to override order snapshot, but to show "product details today" alongside "price you paid").
     - `PaymentsClient.GetPaymentStatusAsync(order.PaymentTransactionId, ct)` — **v2 only** if Payments exposes a BFF-facing query API. For v1 the BFF treats Payments as opaque: `PaymentStatus` is derived from the order's own `PaymentCompletedAtUtc` (null ⇒ "Pending", non-null ⇒ "Completed"; on `Failed` status, "Failed"). Document the v2 path so the endpoint shape is future-compatible.
- **Response composition logic:**
  1. From Ordering response: populate all order-intrinsic fields (status, totals, addresses, timestamps).
  2. For each `OrderItem`, merge with Catalog response by ProductId. If Catalog has the product, attach `CurrentName`, `CurrentPrimaryImageUrl`, `CurrentPrice`. If Catalog returned `CurrentPrice != snapshot.UnitPrice`, expose both values.
  3. Build `Timeline` from the order's transition timestamps:
     - `Placed` = `CreatedAtUtc`.
     - `StockReserved` = `StockReservedAtUtc` (nullable).
     - `PaymentCompleted` = `PaymentCompletedAtUtc` (nullable).
     - `Confirmed` = `ConfirmedAtUtc`.
     - `Shipped` = `shipment.ShippedAtUtc`.
     - `Delivered` = `DeliveredAtUtc`.
     - `Cancelled` = `cancellation.CancelledAtUtc`.
     - `Failed` = `failure.FailedAtUtc`.
     Entries with null timestamps are omitted from the timeline array.
  4. `PaymentStatus`: v1 derived from order fields (`Completed` / `Pending` / `Failed`); v2 sources from Payments.
- **Response shape** (`OrderSummaryResponse`):
  ```
  {
    "orderId": "Guid",
    "correlationId": "Guid",
    "buyerId": "Guid",
    "status": "string (OrderStatus smart-enum name)",
    "items": [
      {
        "productId": "Guid",
        "snapshotSku": "string",
        "snapshotName": "string",
        "currentName": "string | null",
        "currentPrimaryImageUrl": "string | null",
        "quantity": "int",
        "snapshotUnitPrice": { "amount": "decimal", "currency": "string" },
        "currentUnitPrice": { "amount": "decimal", "currency": "string" } | null,
        "lineTotal": { "amount": "decimal", "currency": "string" }
      }
    ],
    "total": { "amount": "decimal", "currency": "string" },
    "shippingAddress": { "street1": "string", "street2": "string | null", "city": "string", "state": "string | null", "postalCode": "string", "countryCode": "string" },
    "billingAddress": "Address (same shape)",
    "paymentMethodId": "Guid",
    "paymentStatus": "string (Pending|Completed|Failed|Refunded)",
    "shipment": { "carrier": "string", "trackingNumber": "string", "shippedAtUtc": "DateTimeOffset" } | null,
    "cancellation": { "reason": "string", "atStatus": "string", "cancelledAtUtc": "DateTimeOffset" } | null,
    "failure": { "errorCode": "string", "errorMessage": "string", "atStatus": "string", "failedAtUtc": "DateTimeOffset" } | null,
    "timeline": [
      { "event": "string (Placed|StockReserved|PaymentCompleted|Confirmed|Shipped|Delivered|Cancelled|Failed)", "atUtc": "DateTimeOffset" }
    ],
    "hasStaleData": "bool",
    "generatedAtUtc": "DateTimeOffset"
  }
  ```
- **Caching strategy:**
  - FusionCache key: `"order-summary:{orderId}"`.
  - Tag: `"order-{orderId}"`.
  - Additional tag: `"order-history-{buyerId}"` (so invalidating a buyer's full history wipes related summaries in one tag call).
  - TTL (soft): 30 seconds — orders change through saga events and post-shipment human actions; 30 s is short enough to reflect most state changes quickly while still absorbing page refreshes.
  - FailSafeMaxDuration: 5 minutes.
  - **Security note:** The cache is keyed only on `orderId`. Authorization is enforced *upstream* (Ordering handler rejects non-owners with 404). The cached copy therefore represents the **authorized** response; serving it to a second caller with a different JWT is safe only because the second request would see a 404 from Ordering first (BFF does NOT return cached data without at least the upstream authorization having been re-checked). **Implementation requirement:** the BFF caches the upstream response ONLY after it has been successfully authorized upstream for the current caller. If this invariant is violated (e.g., by changing the handler order later), the cache becomes an auth bypass. Captured here so that future maintainers see the load-bearing assumption explicitly.
- **Failure modes:**

  | Failure | Behavior | Notes |
  |---------|----------|-------|
  | Ordering timeout / 5xx | Serve stale cache if any (with `HasStaleData=true`); else 503. | First and gating call. |
  | Ordering 404 / 403 | BFF returns 404 (same masking as the upstream handler). | Do not leak existence. |
  | Catalog batch timeout / 5xx / partial | Items lose `CurrentName`, `CurrentPrimaryImageUrl`, `CurrentPrice` (set to null). `HasStaleData=true`. Snapshot values remain. | `X-BFF-PartialData: catalog`. |
  | Payments (v2) timeout / 5xx | Fall back to v1 derivation (derive PaymentStatus from order fields). | Documented for forward-compat. |
  | Network unavailable | Serve from cache with `HasStaleData=true`. If no cache, 503. | — |
- **Cache invalidation hooks:**
  - `ordering.orders` topic: on `OrderConfirmedEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderCancelledEvent`, `OrderFailedEvent` → `RemoveByTagAsync("order-{OrderId}")` AND `RemoveByTagAsync("order-history-{BuyerId}")`.
  - `catalog.products` topic: on `ProductPriceChanged`, `ProductDiscontinuedEvent` → too broad to invalidate every order containing the product. Stale enrichment accepted within TTL.

### 3.4 `GET /api/bff/home-page`

Public landing page — featured products + full category tree + stock highlights.

#### 3.4.1 Surface

- **HTTP route and method:** `GET /api/bff/home-page`.
- **Authentication/authorization:** **Public.** No JWT required.
- **Request params:** none in v1 (no per-user personalization; v2 may accept optional `language` / `region`).
- **Upstream service calls** (parallel):
  1. `CatalogClient.SearchProductsAsync(new SearchProductsRequest { Status = "Active", PageNumber = 1, PageSize = 20 }, ct)` — "featured" semantics in v1 is simply "first 20 active products sorted by `CreatedAtUtc DESC`". A dedicated "featured" flag is Appendix-C scope.
  2. `CatalogClient.GetCategoryTreeAsync(rootCategoryId: null, ct)` — full tree.
  3. After step 1 completes, `InventoryClient.GetStockLevelsBulkAsync(featuredProductIds, ct)` — enriches stock highlights. This is the only sequential dependency.
- **Response composition logic:**
  1. Map Catalog search results to `FeaturedProduct` entries (ProductId, Sku, Name, CategoryBreadcrumb, Price, PrimaryImageUrl).
  2. Merge Inventory bulk result by ProductId → populate `InStock` / `AvailableQty` per item.
  3. Category tree passes through from Catalog.
  4. `StockHighlights` (optional v1): derive a short list of "running low" products (`AvailableQty <= 10 && AvailableQty > 0`) from the featured list. Null / omitted if Inventory call failed.
- **Response shape** (`HomePageResponse`):
  ```
  {
    "featuredProducts": [
      {
        "productId": "Guid",
        "sku": "string",
        "name": "string",
        "brandName": "string",
        "categoryBreadcrumb": "string",
        "price": { "amount": "decimal", "currency": "string" },
        "primaryImageUrl": "string | null",
        "inStock": "bool | null",
        "availableQty": "int | null"
      }
    ],
    "categoryTree": {
      "nodes": [
        {
          "categoryId": "Guid",
          "name": "string",
          "path": "string",
          "parentCategoryId": "Guid | null",
          "depth": "int",
          "productCount": "int"
        }
      ]
    },
    "stockHighlights": [
      {
        "productId": "Guid",
        "name": "string",
        "availableQty": "int"
      }
    ] | null,
    "hasStaleData": "bool",
    "generatedAtUtc": "DateTimeOffset"
  }
  ```
- **Caching strategy:**
  - FusionCache key: `"home-page:v1"` (single key — the home page is the same for every anonymous visitor in v1).
  - Tag: `"home-page"`.
  - TTL (soft): 5 minutes — home page tolerates longer staleness than product detail; cache hit rate is the primary goal.
  - FailSafeMaxDuration: 30 minutes.
  - EagerRefresh: enabled at 80% of TTL (4 minutes) — background refresh before TTL expiry prevents cache-miss latency spikes for the most-hit endpoint.
- **Failure modes:**

  | Failure | Behavior | Notes |
  |---------|----------|-------|
  | Catalog search timeout / 5xx | Serve stale cache (most important fail-safe; home page never empties). `HasStaleData=true`. If no cache available on first request, 503. | — |
  | Catalog category tree timeout / 5xx | Return `CategoryTree = null` but keep `FeaturedProducts`. 200 OK with `HasStaleData=true`. | `X-BFF-PartialData: categories`. |
  | Inventory bulk timeout / 5xx | `InStock = null`, `AvailableQty = null` on every item; `StockHighlights = null`. `HasStaleData=true`. | `X-BFF-PartialData: inventory`. |
  | Inventory partial | Items with `MissingProductIds` get `AvailableQty = null`. | — |
  | Network unavailable | Cache-only fallback with `HasStaleData=true`. If no cache, 503. | — |
- **Cache invalidation hooks:**
  - `catalog.products` topic: on `ProductCreatedEvent`, `ProductPriceChanged`, `ProductDiscontinuedEvent` → `RemoveByTagAsync("home-page")`.
  - `catalog.categories` topic: on `CategoryCreatedEvent` → `RemoveByTagAsync("home-page")`.
  - `inventory.stock-events` topic: on `StockLevelChanged` → `RemoveByTagAsync("home-page")` — only when the product is in the featured set. v1 simplification: always invalidate on any stock event; accepts occasional over-invalidation to keep the handler simple. v2 would maintain a "featured-products-now" set and filter.

---

## 4. Typed HTTP Client Interfaces

One interface per internal service. All methods return `Task<Result<T>>` so BFF endpoints can compose with `FluentResults` and distinguish transport failure from 404. All DTOs below are BFF-internal; they are NOT the upstream service's DTOs — the BFF re-declares them as part of its Anti-Corruption discipline.

### 4.1 `ICatalogClient`

```csharp
public interface ICatalogClient
{
    Task<Result<CatalogProductDto>> GetProductByIdAsync(
        Guid productId, CancellationToken ct);

    Task<Result<IReadOnlyList<CatalogProductDto>>> GetProductsByIdsAsync(
        IEnumerable<Guid> productIds, CancellationToken ct);

    Task<Result<PagedResult<CatalogProductSummaryDto>>> SearchProductsAsync(
        SearchProductsRequest request, CancellationToken ct);

    Task<Result<CategoryTreeDto>> GetCategoryTreeAsync(
        Guid? rootCategoryId, CancellationToken ct);
}
```

**DTO shapes (BFF-internal):**

```text
record CatalogProductDto(
    Guid ProductId,
    string Sku,
    string Name,
    string Description,
    Guid CategoryId,
    string CategoryPath,
    string CategoryBreadcrumb,
    string BrandName,
    MoneyDto Price,
    string Status,           // "Draft" | "Active" | "Discontinued"
    DimensionsDto? Dimensions,
    IReadOnlyList<ImageDto> Images,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastUpdatedAtUtc);

record CatalogProductSummaryDto(
    Guid ProductId,
    string Sku,
    string Name,
    string CategoryBreadcrumb,
    string BrandName,
    MoneyDto Price,
    string Status,
    string? PrimaryImageUrl);

record SearchProductsRequest(
    string? Text,
    string? CategoryPathPrefix,
    decimal? MinPrice,
    decimal? MaxPrice,
    string? Currency,
    string? Status,
    int PageNumber = 1,
    int PageSize = 20);

record PagedResult<T>(
    int Total, int PageNumber, int PageSize, IReadOnlyList<T> Items);

record CategoryTreeDto(IReadOnlyList<CategoryNodeDto> Nodes);

record CategoryNodeDto(
    Guid CategoryId,
    string Name,
    string Path,
    Guid? ParentCategoryId,
    int Depth,
    int ProductCount);

record ImageDto(string Url, string AltText, int DisplayOrder);
record DimensionsDto(decimal Length, decimal Width, decimal Height, string Unit);
record MoneyDto(decimal Amount, string Currency);
```

**Upstream mapping:**

| Client method | Upstream HTTP route | Upstream query / command |
|---------------|---------------------|--------------------------|
| `GetProductByIdAsync` | `GET /api/catalog/products/{productId}` | `GetProductByIdQuery` |
| `GetProductsByIdsAsync` | `GET /api/v1/catalog/products/by-ids?ids=id1,id2,...` — query string `ids` (comma-separated Guids, 1..100) | *(endpoint added in Catalog Stage 2 — required by the ACL in `basket.md` § 9.3 and the BFF here; must be added to Catalog's use-case catalog as `GetProductsByIdsQuery` with route `GET /api/v1/catalog/products/by-ids`)*. See § 5 below. |
| `SearchProductsAsync` | `GET /api/catalog/products/search` with query params | `SearchProductsQuery` |
| `GetCategoryTreeAsync` | `GET /api/catalog/categories` | `GetCategoryTreeQuery` |

### 4.2 `IBasketClient`

```csharp
public interface IBasketClient
{
    Task<Result<BasketDto>> GetBasketAsync(CancellationToken ct);
}
```

**DTO shapes:**

```text
record BasketDto(
    Guid UserId,
    int Version,
    IReadOnlyList<BasketItemDto> Items,
    MoneyDto Total,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastModifiedAtUtc);

record BasketItemDto(
    Guid ProductId,
    string Sku,
    string Name,
    MoneyDto SnapshotPrice,
    int Quantity,
    DateTimeOffset CapturedAtUtc,
    MoneyDto LineTotal);
```

**Upstream mapping:**

| Client method | Upstream HTTP route | Query |
|---------------|---------------------|-------|
| `GetBasketAsync` | `GET /api/basket` | `GetBasketByUserIdQuery` (user from forwarded JWT) |

**Note:** BFF does NOT expose basket *mutation* APIs. Clients call `POST/DELETE/PUT /api/basket/*` directly (through YARP). BFF-level basket mutations would duplicate Basket's own endpoints with no aggregation value; they are deliberately omitted.

### 4.3 `IOrderingClient`

```csharp
public interface IOrderingClient
{
    Task<Result<OrderDto>> GetOrderByIdAsync(
        Guid orderId, CancellationToken ct);

    Task<Result<PagedResult<OrderSummaryDto>>> GetOrdersByBuyerAsync(
        GetOrdersByBuyerRequest request, CancellationToken ct);
}
```

**DTO shapes:**

```text
record OrderDto(
    Guid OrderId,
    Guid CorrelationId,
    Guid BuyerId,
    string Status,
    IReadOnlyList<OrderItemDto> Items,
    MoneyDto Total,
    AddressDto ShippingAddress,
    AddressDto BillingAddress,
    Guid PaymentMethodId,
    Guid? PaymentTransactionId,
    Guid? StockReservationId,
    ShipmentDto? Shipment,
    CancellationDto? Cancellation,
    FailureDto? Failure,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StockReservedAtUtc,
    DateTimeOffset? PaymentCompletedAtUtc,
    DateTimeOffset? ConfirmedAtUtc,
    DateTimeOffset? DeliveredAtUtc);

record OrderItemDto(
    Guid ProductId,
    string Sku,
    string Name,
    int Quantity,
    MoneyDto UnitPrice,
    MoneyDto LineTotal);

record OrderSummaryDto(
    Guid OrderId,
    string Status,
    MoneyDto Total,
    int ItemCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastStatusChangeAtUtc);

record GetOrdersByBuyerRequest(
    Guid? BuyerId,           // admin override; ignored when caller is non-admin
    string? Status,
    int PageNumber = 1,
    int PageSize = 20);

record AddressDto(
    string Street1, string? Street2,
    string City, string? State,
    string PostalCode, string CountryCode);

record ShipmentDto(string Carrier, string TrackingNumber, DateTimeOffset ShippedAtUtc);
record CancellationDto(string Reason, string AtStatus, DateTimeOffset CancelledAtUtc);
record FailureDto(string ErrorCode, string ErrorMessage, string AtStatus, DateTimeOffset FailedAtUtc);
```

**Upstream mapping:**

| Client method | Upstream HTTP route | Query |
|---------------|---------------------|-------|
| `GetOrderByIdAsync` | `GET /api/ordering/orders/{orderId}` | `GetOrderByIdQuery` |
| `GetOrdersByBuyerAsync` | `GET /api/ordering/orders?status=...&pageNumber=...&pageSize=...` | `GetOrdersByBuyerQuery` |

### 4.4 `IInventoryClient`

```csharp
public interface IInventoryClient
{
    Task<Result<StockLevelDto>> GetStockLevelAsync(
        Guid productId, CancellationToken ct);

    Task<Result<StockLevelsBulkDto>> GetStockLevelsBulkAsync(
        IEnumerable<Guid> productIds, CancellationToken ct);
}
```

**DTO shapes:**

```text
record StockLevelDto(
    Guid ProductId,
    int OnHand,
    int Reserved,
    int Available,
    int Version,
    DateTimeOffset LastUpdatedUtc);

record StockLevelsBulkDto(
    IReadOnlyList<StockLevelDto> Items,
    IReadOnlyList<Guid> MissingProductIds);
```

**Upstream mapping:**

| Client method | Upstream HTTP route | Query |
|---------------|---------------------|-------|
| `GetStockLevelAsync` | `GET /api/inventory/stock-items/{productId}` | `GetStockLevelQuery` |
| `GetStockLevelsBulkAsync` | `POST /api/inventory/stock-items/bulk` — body `{ productIds: [] }` | `GetStockLevelsBulkQuery` |

### 4.5 `IPaymentsClient` (v2, forward-compat stub)

Documented for the `OrderSummary` endpoint's future evolution. NOT registered in v1. Shape:

```csharp
// v2 only
public interface IPaymentsClient
{
    Task<Result<PaymentStatusDto>> GetPaymentStatusAsync(
        Guid paymentTransactionId, CancellationToken ct);
}

record PaymentStatusDto(
    Guid PaymentTransactionId,
    string Status,           // "Pending" | "Authorized" | "Captured" | "Failed" | "Refunded"
    decimal Amount,
    string Currency,
    DateTimeOffset LastUpdatedUtc);
```

In v1 the BFF derives `OrderSummaryResponse.PaymentStatus` purely from Ordering fields.

---

## 5. Dependency on New Upstream Endpoints

Two upstream endpoints needed by the BFF are introduced by this document that were not explicitly enumerated in Stage 1 per-BC designs. They MUST be added to `use-cases.md` in the same stage (this document and `use-cases.md` are written in the same pass):

1. **Catalog: `GetProductsByIdsQuery`** — `GET /api/v1/catalog/products/by-ids?ids=id1,id2,...`, query param `ids: Guid[] (1..100, comma-separated)`, returns `{ products: CatalogProductDto[], missingProductIds: Guid[] }`. Required by:
   - Basket's ACL (`IProductCatalogQueryPort.GetManyAsync`) — already documented in `basket.md` § 9.2.
   - BFF's `ICatalogClient.GetProductsByIdsAsync`.

   *This endpoint is a partial-tolerant batch variant of `GetProductByIdQuery` reading from the same `product_search_view`. Validator: `ProductIds` NotEmpty, 1..200; ForEach NotEmpty. Handler returns `Result.Ok(new { Products, MissingProductIds })` with a single SQL `WHERE ProductId = ANY(@ids)` read.*

2. **Inventory: `GetStockLevelsBulkQuery`** — `POST /api/inventory/stock-items/bulk` — **already defined** in § 4.4.2 of `use-cases.md`.

Future maintainers: when extending BFF endpoints, first check whether the upstream query exists; if not, add it to `use-cases.md` alongside this file.

---

## 6. Failure-Mode Summary Table (cross-endpoint)

One table combining all four endpoints' behavior under common failure scenarios, for quick reference:

| Scenario | product-page | basket | order-summary | home-page |
|----------|--------------|--------|---------------|-----------|
| Catalog down | 503 (no stale cache), stale cache (`X-BFF-Stale: true`), or 200 partial | 200 with `CurrentPrice=null`, `HasStaleData=true` | 200 with `CurrentName=null`, `HasStaleData=true` | 503 (first load) or stale cache + `HasStaleData=true` |
| Inventory down | 200 with `InStock=null`, `HasStaleData=true` | 200 with `AvailableQty=null`, `HasStaleData=true` | n/a (no call) | 200 with null stock fields, `HasStaleData=true` |
| Ordering down | n/a (no call; Basket is independent) | n/a | 503 or stale cache | n/a |
| Basket down (auth) | 200 with `AlreadyInBasket=null` | 503 or stale cache | n/a | n/a (public endpoint) |
| Payments down (v2) | n/a | n/a | 200 with derived PaymentStatus | n/a |
| All upstreams down | 503 (if no cache); stale from cache else | 503 (Basket gating) or stale | 503 or stale | Stale cache or 503 |

---

## 7. Testing Layers

| Layer | Scope |
|-------|-------|
| Unit | Response composition logic (merging Catalog + Inventory + Basket with partial-success scenarios); resilience pipeline correctness (not the Polly internals). |
| Integration | Each typed client against WireMock simulating every failure mode above. |
| Integration | FusionCache hit/miss/tag-invalidation behavior against Testcontainers Redis. |
| Integration | Kafka cache-invalidation handlers against Testcontainers Kafka, verifying each topic → tag mapping. |
| Architecture | No direct references to `Catalog.*`, `Basket.*`, `Ordering.*`, `Inventory.*` assemblies from either BFF project. |
| Functional | Full HTTP stack via `WebApplicationFactory` with Testcontainers for Redis + Kafka, using WireMock for upstream services; exercise each of the four endpoints end-to-end including stale-fail-over. |

---

## 8. Open Questions / Deferred

- **Rate limiting at BFF level** — v1 relies on YARP's rate limiting. A BFF-level per-user cap would be relevant if upstream protection via YARP proves insufficient.
- **Per-endpoint response compression** — deferred; YARP and ASP.NET Core defaults should be enough.
- **Language / region support** — home page and product page may want `Accept-Language` forwarding. v2.
- **Personalized home page** — requires auth; v2.
- **gRPC between BFF and services** — v1 uses HTTP/JSON (already matching the service endpoints). gRPC would halve latency but adds contract-generation surface; deferred.
- **Payments BFF query endpoint** — once Payments exposes a read API, `OrderSummary` switches from derived `PaymentStatus` to authoritative.
- **GraphQL gateway** — an alternative to this per-endpoint BFF; explicitly NOT chosen for v1 (REST + manual aggregation is the teaching goal).

---

**End of BFF Aggregation.**
