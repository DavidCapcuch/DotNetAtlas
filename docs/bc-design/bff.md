# BFF Aggregation — eShop Reference Solution

> **Status:** DRAFT (Stage 2 Agent 7) — **reconciled** to the dispatch spec ([implementation-prompts/bff.md](../implementation-prompts/bff.md)) and ADRs **[0012](../adr/0012-api-versioning.md)** (routes under `/api/v1/bff/`), **[0013](../adr/0013-idempotency-key-http.md)** (idempotent `POST /checkout`), **[0016](../adr/0016-redis-topology.md)** (`redis-cache` backplane), **[0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)** (pre-assigned `OrderId` as the durable business key). When this file and the dispatch spec disagree, the dispatch spec + ADRs win.
> **Target section in master design:** [eshop-master-design.md § 9](../eshop-master-design.md)
> **Companion file:** [use-cases.md](./use-cases.md) (commands/queries of the four upstream services)
> **Stage 1 inputs:** [catalog.md](./catalog.md), [basket.md](./basket.md), [ordering.md](./ordering.md), [inventory.md](./inventory.md)

This document specifies the **Backend-for-Frontend** (BFF) service: the public-facing aggregation HTTP API consumed by the eShop web/mobile clients. The BFF lives in `src/EShop.BFF/` and composes responses from the four internal services (Catalog, Basket, Ordering, Inventory). The BFF has no own database and no own domain — it is a composition + caching + resilience layer, plus **one** state-changing seam: the idempotent `POST /api/v1/bff/checkout` that triggers the Checkout saga (ADR-0013 / ADR-0029).

**Design lineage:** BFF positioning and relationship to YARP are already fixed in [eshop-general-plan.md](../eshop-general-plan.md) (YARP handles routing/SSL; BFF handles response aggregation). This document specifies the five endpoints (four read-composition GETs + the idempotent `POST /api/v1/bff/checkout`), the HTTP client contracts, the resilience pipeline, the caching strategy, and the Kafka invalidation consumer. All BFF routes live under `/api/v1/bff/...` ([ADR-0012](../adr/0012-api-versioning.md)).

---

## 1. Project Structure

```
src/EShop.BFF/
├── EShop.BFF.Api/
│   ├── Endpoints/
│   │   ├── ProductPageEndpoint.cs
│   │   ├── BasketEndpoint.cs
│   │   ├── OrderSummaryEndpoint.cs
│   │   ├── HomePageEndpoint.cs
│   │   └── CheckoutEndpoint.cs           # POST /api/v1/bff/checkout — .Idempotency() (ADR-0013)
│   ├── Requests/
│   │   └── CheckoutRequest.cs            # shipping + billing address + paymentMethodId
│   ├── Responses/
│   │   ├── ProductPageResponse.cs
│   │   ├── BasketPageResponse.cs
│   │   ├── OrderSummaryResponse.cs
│   │   ├── HomePageResponse.cs
│   │   └── CheckoutResponse.cs           # { orderId } — pre-assigned OrderId (ADR-0029)
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

**Consumer group:** `bff-group` (one-group-per-service rule, [events-catalog.md § 3.1](events-catalog.md)).

**Subscribed topics and per-topic handler → tag mapping:**

| Topic | External event type | Handler | FusionCache invalidation action |
|-------|--------------------|---------|--------------------------------|
| `catalog.products` | `ProductCreatedEvent` | `ProductEventCacheInvalidator` | `RemoveByTagAsync("home-page")` (new product may be featured). |
| `catalog.products` | `ProductPriceChangedEvent` | `ProductEventCacheInvalidator` | `RemoveByTagAsync("product-{ProductId}")` + `RemoveByTagAsync("home-page")`. |
| `catalog.products` | `ProductDiscontinuedEvent` | `ProductEventCacheInvalidator` | `RemoveByTagAsync("product-{ProductId}")` + `RemoveByTagAsync("home-page")`. |
| `catalog.categories` | `CategoryCreatedEvent` | `CategoryEventCacheInvalidator` | `RemoveByTagAsync("home-page")` (category tree changed). |
| `inventory.stock-events` | `StockLevelChangedEvent` | `StockEventCacheInvalidator` | `RemoveByTagAsync("product-{ProductId}")` + `RemoveByTagAsync("home-page")`. |
| `ordering.orders` | `OrderCreatedEvent`, `OrderConfirmedEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderCancelledEvent`, `OrderFailedEvent` | `OrderEventCacheInvalidator` | `RemoveByTagAsync("order-{OrderId}")` + `RemoveByTagAsync("order-history-{BuyerId}")` — except `OrderCreatedEvent`, which invalidates `order-history-{BuyerId}` only (no `order-{OrderId}` entry exists for a brand-new order yet). |
| `basket.sessions` | `BasketCheckoutInitiatedEvent` | `BasketEventCacheInvalidator` | `RemoveByTagAsync("basket-bff-{UserId}")` (basket has been converted to an order — aggressively clear the BFF's basket cache). |

**Middleware pipeline** — same shape as the service inbox consumers:

1. Avro deserialization.
2. **No inbox middleware** is required on the BFF side because cache invalidation is idempotent by construction (`RemoveByTag` is a no-op when the tag is absent). Double-invalidation is cheap; missing invalidation would be a correctness bug, but at-least-once Kafka delivery covers that. An inbox would add a per-message DB write for zero behavioural change, so the BFF registers none. **Subscription principle:** the BFF subscribes to *published-language event topics only* and never to saga-internal coordination streams — which is why it does **not** consume `inventory.reservations` (an `OrderId`-keyed, saga-internal stream owned by the Checkout saga). The BFF's product-availability concern is served by the purpose-built `ProductId`-keyed `inventory.stock-events` contract plus the short product-page cache TTL; oversell safety lives in Inventory, not in the BFF's cached display. (This is the canonical record of that decision — no separate ADR.)
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

Registered before the resilience handler so retry copies of the request preserve the Authorization header. For public BFF endpoints (home page, product page) the handler has nothing to forward — upstream anonymous endpoints accept calls without `Authorization`. For authed upstreams the raw user JWT is **not** a standalone credential (no BC accepts the user-token audience); it is the **subject token** consumed by the buyer-scoped token exchange described next.

**Upstream auth — decided ([ADR-0010 § BFF token exchange](../adr/0010-service-to-service-auth.md#amendment-2026-06-06--bff-token-exchange-for-buyer-scoped-callees); [#323](https://github.com/DavidCapcuch/DotNetAtlas/issues/323)).** Every BC validates its own `aud`, so a plain-forwarded user JWT authenticates at *no* BC. Two outbound shapes cover every BFF call:

- **Non-buyer-scoped reads** (Catalog, Inventory) — a `client_credentials` **service token** via `IHttpClientBuilder.AddServiceAuth("<scope>")` (`catalog.read` / `inventory.read`). The callee audience rides the scope ([ADR-0010](../adr/0010-service-to-service-auth.md)) and the buyer is irrelevant to the data.
- **Buyer-scoped calls** (Basket `GET /basket` + `POST /checkout`; and the buyer-owned Ordering / Invoicing reads) — an **RFC 8693 token exchange**: the user JWT is exchanged for a token re-audienced to the callee via the `bff` client's matching scope (`basket.read` / `basket.write` / `ordering.read` / `invoicing.read`) **while preserving the buyer `sub`**. A plain `client_credentials` token is unusable here — its `sub` is the BFF service account, and each callee derives the resource owner from `sub` (Basket: `GetUserIdFromSubClaim`; Ordering / Invoicing: buyer-self enforcement), so it would resolve the wrong buyer.

The `bff` service client is already provisioned with all six scopes ([`service-scope-matrix.md`](../../src/keycloak/service-scope-matrix.md)). The exchange is Keycloak **Standard Token Exchange** (RFC 8693) — GA and default-on in the pinned **Keycloak 26.3.2** (feature `token-exchange-standard`), enabled by the single client attribute `standard.token.exchange.enabled=true` on the `bff` client; **not** the legacy `token-exchange` preview feature and **no** fine-grained per-callee permissions. v2 additionally requires the user's `subject_token` to carry `aud: bff` (stamped by the user-facing client's `audience-bff` mapper; the BFF validates the same audience inbound). The BFF's `TokenExchangeHandler` + the realm enablement landed in [#329](https://github.com/DavidCapcuch/DotNetAtlas/issues/329); see [ADR-0010 § Implementation — Standard Token Exchange v2](../adr/0010-service-to-service-auth.md#implementation--keycloak-standard-token-exchange-v2-landed-329). **Historical caveat (now retired by #329):** Basket's FunctionalTests mint a synthetic token fusing buyer `sub` + `basket-service` `aud` (`FakeTokenCreator` + the production-audience `FakeTokenSigner`) — a combination no real Keycloak flow produces — so their green status alone was never end-to-end proof; #329 adds the isolated Keycloak-Testcontainer test that proves the real exchange path.

### 2.4 Observability

- **OpenTelemetry tracing** is already wired via existing `Platform.*` observability packages. The typed `HttpClient` automatically propagates `traceparent` on every call, so the trace spans end-to-end: Client → BFF endpoint → upstream service handler → DB. The W3C **`traceId`** is the cross-service correlation identifier — there is no separate application-level correlation id to thread through BFF requests.
- **Custom span tags** per endpoint:
  - `bff.endpoint` — one of `product-page`, `basket`, `order-summary`, `home-page`.
  - `bff.cache.hit` — bool (did the BFF return from cache without upstream calls?).
  - `bff.stale` — bool (was the response served with `HasStaleData: true`?).
- **Metrics** (OpenTelemetry meter `EShop.BFF`):
  - `bff.cache.hits` — counter, tagged `{ endpoint }`.
  - `bff.cache.misses` — counter, tagged `{ endpoint }`.
  - `bff.partial_response` — counter, tagged `{ endpoint }` — incremented when any upstream call failed but the endpoint still returned 200 with partial data.
- **Upstream-call outcomes reuse standard instrumentation — no custom counter.** An earlier draft specified a `bff.upstream.calls{ client, outcome = success|timeout|5xx|circuit-open }` counter; it was dropped as redundant, since every outcome is already emitted by meters the `AddMeter("*")` wildcard collects:
  - **success / 5xx (and latency)** → the OTel `http.client.request.duration` histogram (tags `server.address`, `http.response.status_code`).
  - **timeout / retry / circuit-state** → the resilience pipeline's built-in Polly telemetry (meter `Polly`, instrument `resilience.polly.strategy.events`), tagged `pipeline.name = bff-<client>` (§ 2.1): `OnTimeout` fires per cancelled attempt; `OnCircuitOpened` / `OnCircuitHalfOpened` / `OnCircuitClosed` per state transition.

  **Accepted gap:** Polly reports circuit *transitions*, not a per-rejected-call count, so the volume of calls fail-fast-shed by an *already-open* breaker is not a first-class metric. Read it indirectly from the `OnCircuitOpened` window (+ `BreakDuration`, § 2.1) against `bff.partial_response` and inbound 5xx (`http.server.request.duration`). (Canonical record of this decision — no separate ADR.)
- **Structured logging** uses Serilog + enrichers: every upstream call logs `{ Client, Method, Path, DurationMs, StatusCode }`. Cache events log `{ Tag, Operation = hit|miss|invalidation }`.

### 2.5 YARP positioning

YARP (per `eshop-general-plan.md`) handles coarse routing concerns — SSL termination, rate limiting (public-facing), and path-based routing that selects BFF vs. admin APIs. YARP does NOT do response aggregation. The request flow for a consumer request is:

```
Client → YARP (TLS, rate limit, routing) → BFF /api/v1/bff/... → (internal services) → BFF → YARP → Client
```

**Consumer traffic is BFF-mediated.** YARP exposes only `/api/v1/bff/*` to consumers, plus the admin/ops routes that reach services directly (admin-role only). There is **no** public consumer route to a BC's own API — in particular **no `/api/v1/basket/*` for the SPA**. Because YARP is a *transparent* reverse proxy (it neither mints nor exchanges tokens), the only token that reaches Basket is the BFF's exchanged one (§ 2.3); the user-facing app client carries no BC scope, so a user JWT is never a Basket credential. YARP config is out of this document's scope (Stage 3 / platform-architect).

---

## 3. Endpoints

### 3.1 `GET /api/v1/bff/product-page/{productId}`

Public product-detail page — composes Catalog (product info) + Inventory (stock availability) with optional Basket (has the current user already added this?).

#### 3.1.1 Surface

- **HTTP route and method:** `GET /api/v1/bff/product-page/{productId}` ([ADR-0012](../adr/0012-api-versioning.md)).
- **Authentication/authorization:** **Optional auth.** Anonymous users get the public product page; authenticated users additionally receive `AlreadyInBasket` populated from the Basket service.
- **Request params:**
  - `productId` (route, Guid).
  - No query params today (language / region selection is planned scope — see [roadmap.md § 2.3 BFF](../roadmap.md)).
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
  - **Storage (canonical for every BFF cache, all endpoints):** FusionCache with a Redis L2 distributed cache **and** backplane pointed at **`redis-cache`** (connection string `Redis:Cache`) per [ADR-0016](../adr/0016-redis-topology.md) — the volatile, `allkeys-lru` instance. The BFF backplane MUST NOT point at `redis-basket` (the authoritative basket store, `noeviction`); an **architecture test asserts** `EShop.BFF.Infrastructure` resolves only `Redis:Cache`, never `Redis:Basket`. The same `redis-cache` instance also backs the `POST /api/v1/bff/checkout` idempotency store (§ 3.5, ADR-0013). The FusionCache instance is namespaced to the BFF so its policy and eviction stay independent of any service-level caches.
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
  - `catalog.products` topic: on `ProductPriceChangedEvent` or `ProductDiscontinuedEvent` with matching `ProductId` → `RemoveByTagAsync("product-{ProductId}")`.
  - `inventory.stock-events` topic: on `StockLevelChangedEvent` → `RemoveByTagAsync("product-{ProductId}")`. This is the BFF's sole availability-invalidation signal; reservation-level `Available` shifts (the saga-internal `inventory.reservations` stream) are **not** subscribed — they are absorbed by the short product-page TTL, per the published-language-only subscription principle in § 2.2.

### 3.2 `GET /api/v1/bff/basket`

Authenticated user's current basket enriched with *current* Catalog prices and *current* Inventory availability — so the UI can flag "price changed since you added" and "out of stock since you added" without the user needing to refresh.

#### 3.2.1 Surface

- **HTTP route and method:** `GET /api/v1/bff/basket` ([ADR-0012](../adr/0012-api-versioning.md)).
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
  - Cache is **per-user** and **invalidated synchronously on every BFF-mediated mutation**. Because basket mutations flow through the BFF (§ 3.6) — there is no direct consumer→Basket path (§ 2.5) — each successful `add` / `change-quantity` / `remove` / `clear` / `checkout` calls `RemoveByTagAsync("basket-bff-{userId}")` in the same request, and the `redis-cache` backplane ([ADR-0016](../adr/0016-redis-topology.md)) propagates the eviction across BFF instances. So a `GET /api/v1/bff/basket` issued immediately after a mutation reflects it — no stale-window workaround needed.
  - The 15-second TTL is a **backstop** for out-of-band changes (e.g. the basket cleared by the checkout consumer), not the primary freshness mechanism. This is the value the BFF adds by fronting the mutations rather than leaving them direct: it owns the read cache, so it can keep it coherent.
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
  - `basket.sessions` topic: on `BasketCheckoutInitiatedEvent` → `RemoveByTagAsync("basket-bff-{UserId}")` — defense-in-depth for the checkout transition. (Item mutations are invalidated synchronously by the BFF endpoints that forward them, § 3.6, not via Kafka.)
  - `catalog.products` topic: on `ProductPriceChangedEvent` → `RemoveByTagAsync("basket-bff-*")` is **too aggressive** (would invalidate every basket on every price change). Instead, `PriceDrifted` is computed freshly on each (non-cached) request. The 15-second TTL absorbs the in-window drift.
  - `inventory.stock-events`: similarly, per-user basket invalidation on stock events would fan out too broadly. Accepted as stale within TTL.

### 3.3 `GET /api/v1/bff/order-summary/{orderId}`

Authenticated user's detailed order view — composes Ordering (order record) + Catalog (current product snapshots, optional) + Payments (payment status, future). `orderId` is the pre-assigned, saga-correlating business key ([ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)) returned by `POST /api/v1/bff/checkout`.

#### 3.3.1 Surface

- **HTTP route and method:** `GET /api/v1/bff/order-summary/{orderId}` ([ADR-0012](../adr/0012-api-versioning.md)).
- **Authentication/authorization:** **Required.** User must own the order (`BuyerId == claim.sub`) unless `admin` role — enforced upstream by Ordering's `GetOrderByIdQuery` handler (BFF just forwards the JWT).
- **Request params:**
  - `orderId` (route, Guid).
- **Upstream service calls:**
  1. `OrderingClient.GetOrderByIdAsync(orderId, ct)` — first. If 404 → BFF 404 (no order means no summary).
  2. **Parallel enrichment** once order is loaded:
     - `CatalogClient.GetProductsByIdsAsync(orderItemProductIds, ct)` — fetch *current* product metadata for display enrichment (name changes, current images, current price — NOT to override order snapshot, but to show "product details today" alongside "price you paid").
     - `PaymentsClient.GetPaymentStatusAsync(order.PaymentTransactionId, ct)` — **planned scope only** (see [roadmap.md § 2.3 BFF — `IPaymentsClient`](../roadmap.md)); requires Payments to expose a BFF-facing query API. Today the BFF treats Payments as opaque: `PaymentStatus` is derived from the order's own `PaymentCompletedAtUtc` (null ⇒ "Pending", non-null ⇒ "Completed"; on `Failed` status, "Failed"). The endpoint shape is left forward-compatible.
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
  4. `PaymentStatus`: derived from order fields today (`Completed` / `Pending` / `Failed`); a future `IPaymentsClient` source is planned scope per [roadmap.md § 2.3 BFF](../roadmap.md).
- **Response shape** (`OrderSummaryResponse`):
  ```
  {
    "orderId": "Guid",
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
  | Payments (planned `IPaymentsClient`) timeout / 5xx | Fall back to deriving `PaymentStatus` from order fields. | Documented for forward-compat; see [roadmap.md § 2.3 BFF](../roadmap.md). |
  | Network unavailable | Serve from cache with `HasStaleData=true`. If no cache, 503. | — |
- **Cache invalidation hooks:**
  - `ordering.orders` topic: on `OrderCreatedEvent` → `RemoveByTagAsync("order-history-{BuyerId}")` **only** — a brand-new order has no `order-{OrderId}` summary cached yet, but it must appear in the buyer's order-history list.
  - `ordering.orders` topic: on `OrderConfirmedEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderCancelledEvent`, `OrderFailedEvent` → `RemoveByTagAsync("order-{OrderId}")` AND `RemoveByTagAsync("order-history-{BuyerId}")`.
  - `catalog.products` topic: on `ProductPriceChangedEvent`, `ProductDiscontinuedEvent` → too broad to invalidate every order containing the product. Stale enrichment accepted within TTL.

### 3.4 `GET /api/v1/bff/home-page`

Public landing page — featured products + full category tree + stock highlights.

#### 3.4.1 Surface

- **HTTP route and method:** `GET /api/v1/bff/home-page` ([ADR-0012](../adr/0012-api-versioning.md)).
- **Authentication/authorization:** **Public.** No JWT required.
- **Request params:** none today (no per-user personalization; optional `language` / `region` are planned scope — see [roadmap.md § 2.3 BFF](../roadmap.md)).
- **Upstream service calls** (parallel):
  1. `CatalogClient.SearchProductsAsync(new SearchProductsRequest { Status = "Active", PageNumber = 1, PageSize = 20 }, ct)` — "featured" in v1 is simply the first 20 active products in Catalog search's **default order** (currently price — Catalog exposes no sort knob yet, so the BFF renders whatever order it gets). A `CreatedAtUtc DESC` sort or a dedicated "featured" flag is planned scope (Appendix-C).
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
  | Catalog search timeout / 5xx | Serve stale cache (most important fail-safe; home page never empties). `HasStaleData=true`. If no cache available on first request, 503. | `X-BFF-Stale: true` on a stale serve. |
  | Catalog category tree timeout / 5xx | Return `CategoryTree = null` but keep `FeaturedProducts`. 200 OK with `HasStaleData=true`. | `X-BFF-Stale: true`; `X-BFF-PartialData: categories`. |
  | Inventory bulk timeout / 5xx | `InStock = null`, `AvailableQty = null` on every item; `StockHighlights = null`. `HasStaleData=true`. | `X-BFF-Stale: true`; `X-BFF-PartialData: inventory`. |
  | Inventory partial | Items with `MissingProductIds` get `AvailableQty = null`. | — (`HasStaleData=false` — overlay present). |
  | Network unavailable | Cache-only fallback with `HasStaleData=true`. If no cache, 503. | `X-BFF-Stale: true` on a stale serve. |
- **Cache invalidation hooks:**
  - `catalog.products` topic: on `ProductCreatedEvent`, `ProductPriceChangedEvent`, `ProductDiscontinuedEvent` → `RemoveByTagAsync("home-page")`.
  - `catalog.categories` topic: on `CategoryCreatedEvent` → `RemoveByTagAsync("home-page")`.
  - `inventory.stock-events` topic: on `StockLevelChangedEvent` → `RemoveByTagAsync("home-page")` — ideally only when the product is in the featured set. Current simplification: always invalidate on any stock event; accepts occasional over-invalidation to keep the handler simple. A "featured-products-now" set + filter is planned scope — see [roadmap.md § 2.3 BFF](../roadmap.md).

### 3.5 `POST /api/v1/bff/checkout`

The BFF's **only** state-changing endpoint and the system's **#1 idempotency target** ([ADR-0013](../adr/0013-idempotency-key-http.md) — a customer double-clicking "Pay now" must not place two orders). It triggers the Checkout saga by forwarding to Basket's checkout command; the BFF adds the idempotency seam, the buyer-scoped token exchange (§ 2.3), and returns the pre-assigned `OrderId`.

> **Checkout is the saga seam — the BFF *owns* its idempotency here.** The item-level mutations (§ 3.6) are thin forwarders the BFF runs only so it can keep its basket-read cache coherent; their idempotency stays in Basket. Checkout is different: it is the system's #1 idempotency target ([ADR-0013](../adr/0013-idempotency-key-http.md)) and the Checkout-saga trigger, so the BFF owns the idempotency seam at this hop rather than forwarding it to Basket.

#### 3.5.1 Surface

- **HTTP route and method:** `POST /api/v1/bff/checkout` ([ADR-0012](../adr/0012-api-versioning.md)).
- **Authentication/authorization:** **Required.** The buyer is the JWT `sub`. The BFF reaches Basket via an **RFC 8693 token exchange** (§ 2.3): the user JWT is exchanged for a token re-audienced to `basket-service` through the `bff` client's **`basket.write`** scope while preserving the buyer `sub` — so Basket's `ValidateAudience` passes *and* `GetUserIdFromSubClaim` still resolves the buyer. A plain `client_credentials` token would carry the BFF service account's `sub` and check out the wrong buyer's basket, so it is **not** used here ([ADR-0010](../adr/0010-service-to-service-auth.md); decided [#323](https://github.com/DavidCapcuch/DotNetAtlas/issues/323)). `GET /api/v1/bff/basket` uses `basket.read` identically.
- **Idempotency:** **Required `Idempotency-Key` header** (UUID v4/v7). FastEndpoints `.Idempotency()` backed by the **`redis-cache`** output-cache store ([ADR-0013](../adr/0013-idempotency-key-http.md)); `CacheDuration` 24h; `AdditionalCacheKey` = buyer `sub` claim (so two buyers reusing a key cannot see each other's response). Missing header → 400. Replay with same key + same body → original `202` response replayed (handler not re-invoked). Replay with same key + **different** body → 409.
- **Request shape** (`CheckoutRequest`) — the client collects addresses + payment method at checkout (Basket does not own them, [ADR-0005](../adr/0005-customer-data-in-ordering.md)):
  ```
  {
    "shippingAddress": { "street1": "string", "street2": "string?", "city": "string", "state": "string?", "postalCode": "string", "countryCode": "string (ISO 3166-1 alpha-2)" },
    "billingAddress": "{ same shape as shippingAddress }",
    "paymentMethodId": "Guid"
  }
  ```
  `UserId` is **never** taken from the body — it is the JWT `sub`.
- **Upstream call (single — not an aggregation):**
  - `BasketClient.CheckoutAsync(request, ct)` → Basket's `POST /api/v1/basket/checkout` (`CheckoutBasketCommand`, [use-cases.md § 2.1.6](use-cases.md)). Basket's handler **pre-assigns** the `OrderId` (`Guid.CreateVersion7()`, [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)), raises `BasketCheckedOutDomainEvent`, and writes `BasketCheckoutInitiatedEvent` to its outbox on topic `basket.sessions`. The Checkout saga (`saga-checkout`) consumes that event and runs `CreateOrder → ReserveStock → Pay → Confirm`, every step correlated on `OrderId`.
- **Response shape** (`CheckoutResponse`):
  ```
  {
    "orderId": "Guid (pre-assigned UUID v7 — the durable saga-correlating business key, ADR-0029)"
  }
  ```
  - **Status:** `202 Accepted` — checkout is now the saga's asynchronous responsibility. Clients poll `GET /api/v1/bff/order-summary/{orderId}` for progress.
- **Caching:** **None.** A mutation is never cached and never serves stale data; the only Redis interaction is the idempotency store above.
- **Failure modes:**

  | Failure | Behavior | Notes |
  |---------|----------|-------|
  | `Idempotency-Key` header missing | 400 (FastEndpoints problem-detail). | — |
  | Replay, same key + same body | Original `202 { orderId }` replayed from `redis-cache`; Basket NOT re-called. | The whole point — double-click safety. |
  | Replay, same key + different body | 409 Conflict. | Hash mismatch. |
  | Basket 404 (no basket) | Surface 404. | Nothing to check out. |
  | Basket 409 (empty basket) | Surface 409. | `BasketErrors.EmptyBasket`. |
  | Basket timeout / 5xx / circuit-open | 502/503 — **no** stale fallback (mutations never serve stale). Client retries with the **same** `Idempotency-Key`. | Resilience pipeline (§ 2.1) still applies; a retry that lands after the first call committed replays the cached `202`. |
- **Cache invalidation hooks:** the endpoint emits nothing itself. The downstream `BasketCheckoutInitiatedEvent` reaches the BFF's own `bff-group` basket invalidator (§ 2.2), which clears `basket-bff-{UserId}` — so the post-checkout basket view is wiped promptly.

### 3.6 Basket item mutations — `/api/v1/bff/basket/items*`

Consumer basket access is **BFF-mediated** — there is no direct consumer→Basket path (§ 2.5). So the BFF fronts the item mutations as **thin forwarders** to Basket (one-to-one, no aggregation, no response composition):

| BFF route | Forwards to (Basket) | Command |
|-----------|----------------------|---------|
| `POST /api/v1/bff/basket/items` | `POST /api/v1/basket/items` | `AddItemToBasketCommand` |
| `PUT /api/v1/bff/basket/items/{productId}/quantity` | `PUT /api/v1/basket/items/{productId}/quantity` | `ChangeItemQuantityCommand` |
| `DELETE /api/v1/bff/basket/items/{productId}` | `DELETE /api/v1/basket/items/{productId}` | `RemoveItemFromBasketCommand` |
| `DELETE /api/v1/bff/basket/items` | `DELETE /api/v1/basket/items` | `ClearBasketCommand` |

(The same forwarding shape applies to any further consumer basket operation, e.g. `POST /refresh-prices`.)

- **Authentication/authorization:** **Required.** Reached via the **RFC 8693 token exchange** (§ 2.3) on the `bff` client's **`basket.write`** scope — re-audiences the user JWT to `basket-service` while preserving the buyer `sub`, so Basket's `ValidateAudience` passes and `GetUserIdFromSubClaim` resolves the buyer. Identical to checkout's exchange (§ 3.5).
- **Why through the BFF, not direct:** these forward verbatim, but routing them through the BFF buys two things a direct path cannot — (1) **synchronous invalidation** of the `basket-bff-{userId}` read cache (§ 3.2), so there is no stale-window workaround; (2) a **single auth boundary** — the user JWT never carries a BC audience, so the app client provisions no BC scope ([ADR-0010](../adr/0010-service-to-service-auth.md) minimalism) and Basket's write surface stays off the public edge. This is the canonical record superseding the earlier "item mutations go direct" stance, reconciling this doc with the master-design integration map ([eshop-master-design.md § 4.2](../eshop-master-design.md), which already models `AddItemToBasketCommand` as a BFF→Basket call).
- **Idempotency:** `AddItem` carries Basket-side `.Idempotency()`; the BFF forwards the `Idempotency-Key` header unchanged. `change-quantity` (PUT) and `remove` / `clear` (DELETE) are idempotent by HTTP-method semantics. Only **checkout's** idempotency is BFF-owned (§ 3.5, [ADR-0013](../adr/0013-idempotency-key-http.md)).
- **Behavior:** forward → on Basket success, `RemoveByTagAsync("basket-bff-{userId}")` → return Basket's status (`204` / `404` / `409` / `422`) verbatim. The mutation itself is never cached.

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
| `GetProductByIdAsync` | `GET /api/v1/catalog/products/{productId}` | `GetProductByIdQuery` |
| `GetProductsByIdsAsync` | `GET /api/v1/catalog/products/by-ids?ids=id1,id2,...` — query string `ids` (comma-separated Guids, 1..100) | *(endpoint added in Catalog Stage 2 — required by the ACL in `basket.md` § 9.3 and the BFF here; must be added to Catalog's use-case catalog as `GetProductsByIdsQuery` with route `GET /api/v1/catalog/products/by-ids`)*. See § 5 below. |
| `SearchProductsAsync` | `GET /api/v1/catalog/products` (products-collection root + query params — **not** a `/search` sub-path) | `SearchProductsQuery` |
| `GetCategoryTreeAsync` | `GET /api/v1/catalog/categories/tree` | `GetCategoryTreeQuery` |

### 4.2 `IBasketClient`

```csharp
public interface IBasketClient
{
    Task<Result<BasketDto>> GetBasketAsync(CancellationToken ct);

    // Item mutations forwarded for the BFF basket endpoints (§ 3.6) — thin passthroughs.
    Task<Result> AddItemAsync(AddItemDto item, CancellationToken ct);
    Task<Result> ChangeItemQuantityAsync(Guid productId, int quantity, CancellationToken ct);
    Task<Result> RemoveItemAsync(Guid productId, CancellationToken ct);
    Task<Result> ClearAsync(CancellationToken ct);

    // Backs POST /api/v1/bff/checkout (§ 3.5). Returns the pre-assigned OrderId (ADR-0029).
    Task<Result<CheckoutResultDto>> CheckoutAsync(
        CheckoutRequestDto request, CancellationToken ct);
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

record AddItemDto(
    Guid ProductId,
    int Quantity);               // snapshot price is captured server-side by Basket at add-time

record CheckoutRequestDto(
    AddressDto ShippingAddress,
    AddressDto BillingAddress,
    Guid PaymentMethodId);       // UserId is the JWT sub, never in the body

record CheckoutResultDto(
    Guid OrderId);               // pre-assigned UUID v7 (ADR-0029)
```

**Upstream mapping:**

| Client method | Upstream HTTP route | Query / Command |
|---------------|---------------------|-----------------|
| `GetBasketAsync` | `GET /api/v1/basket` | `GetBasketByUserIdQuery` (buyer from the exchanged token's `sub`) |
| `AddItemAsync` | `POST /api/v1/basket/items` | `AddItemToBasketCommand` |
| `ChangeItemQuantityAsync` | `PUT /api/v1/basket/items/{productId}/quantity` | `ChangeItemQuantityCommand` |
| `RemoveItemAsync` | `DELETE /api/v1/basket/items/{productId}` | `RemoveItemFromBasketCommand` |
| `ClearAsync` | `DELETE /api/v1/basket/items` | `ClearBasketCommand` |
| `CheckoutAsync` | `POST /api/v1/basket/checkout` | `CheckoutBasketCommand` → pre-assigns `OrderId` (`Guid.CreateVersion7()`), emits `BasketCheckoutInitiatedEvent` ([use-cases.md § 2.1.6](use-cases.md), [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)) |

**Note:** Consumer basket access is **BFF-mediated** — there is no direct consumer→Basket path (§ 2.5). The BFF fronts the full basket surface: the enriched read (`GET /api/v1/bff/basket`, § 3.2), the item mutations (§ 3.6, thin forwarders), and checkout (`POST /api/v1/bff/checkout`, § 3.5 — the saga + idempotency seam). Forwarding the item mutations is **not** value-less passthrough: it lets the BFF invalidate its own basket-read cache synchronously *and* keeps the user JWT off Basket's audience, so the user-facing app client provisions **no** `basket.*` scope ([ADR-0010](../adr/0010-service-to-service-auth.md) "no provisioned-for-someday dead config"). This supersedes the earlier "item mutations go direct" stance and reconciles this doc with the master-design integration map ([eshop-master-design.md § 4.2](../eshop-master-design.md), which already models `AddItemToBasketCommand` as a BFF→Basket call).

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
    Guid BuyerId,
    string Status,
    IReadOnlyList<OrderItemDto> Items,
    decimal TotalAmount,
    string Currency,
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
    decimal UnitPriceAmount,
    decimal LineTotalAmount);

record OrderSummaryDto(
    Guid OrderId,
    string Status,
    decimal TotalAmount,
    string Currency,
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
| `GetOrderByIdAsync` | `GET /api/v1/ordering/orders/{orderId}` | `GetOrderByIdQuery` |
| `GetOrdersByBuyerAsync` | `GET /api/v1/ordering/orders?status=...&pageNumber=...&pageSize=...` | `GetOrdersByBuyerQuery` |

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
| `GetStockLevelAsync` | `GET /api/v1/inventory/stock-items/{productId}` | `GetStockLevelQuery` |
| `GetStockLevelsBulkAsync` | `POST /api/v1/inventory/stock-items/bulk` — body `{ productIds: [] }` | `GetStockLevelsBulkQuery` |

### 4.5 `IPaymentsClient` (planned scope — forward-compat stub)

Documented for the `OrderSummary` endpoint's planned evolution per [roadmap.md § 2.3 BFF](../roadmap.md). NOT registered today. Shape:

```csharp
// planned scope — see roadmap.md § 2.3 BFF
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

Today the BFF derives `OrderSummaryResponse.PaymentStatus` purely from Ordering fields.

---

## 5. Dependency on New Upstream Endpoints

Two batch upstream endpoints the BFF depends on. **Build status verified against the code** (the BFF dispatch must confirm before relying on either):

1. **Catalog: `GetProductsByIdsQuery`** — `GET /api/v1/catalog/products/by-ids?ids=id1,id2,...`, query param `ids: Guid[] (1..100, comma-separated)`, returns `{ products: CatalogProductDto[], missingProductIds: Guid[] }`. **✅ Built and shipping** (`Catalog.Api` `GetProductsByIdsEndpoint`, `Get("by-ids")` under the `/catalog/products` group). Consumed by:
   - Basket's ACL (`IProductCatalogQueryPort.GetManyAsync`) — also documented in `basket.md` § 9.2.
   - BFF's `ICatalogClient.GetProductsByIdsAsync`.

   *Partial-tolerant batch variant of `GetProductByIdQuery` reading from the same `product_search_view` via a single SQL `WHERE ProductId = ANY(@ids)`.*

2. **Inventory: `GetStockLevelsBulkQuery`** — `POST /api/v1/inventory/stock-items/bulk`, body `{ productIds: Guid[] }` (up to 200), partial-tolerant (unknown ids returned in `missingProductIds`). Spec'd (`use-cases.md` § 4.4.2); **committed design is [ADR-0034](../adr/0034-inventory-stock-availability-read-path.md)** (Inventory-owned read-through cache behind the API; the BFF never materializes availability from `stock-events`). **✅ Built and shipping** (`Inventory.Api` `GetStockLevelsBulkEndpoint`, `Post("stock-items/bulk")` under the `/inventory/stock-items` group, `AllowAnonymous`). Consumed by:
   - BFF's `IInventoryClient.GetStockLevelsBulkAsync` — the `/home-page` stock overlay (§ 3.4, built) and the `/basket` availability overlay (§ 3.2, later slice).

Future maintainers: when extending BFF endpoints, first check whether the upstream query exists **in code**; if not, build it (or flag the gap) rather than assuming the doc implies an implementation.

---

## 6. Failure-Mode Summary Table (cross-endpoint)

One table combining the four **read** endpoints' behavior under common failure scenarios, for quick reference. The `POST /api/v1/bff/checkout` mutation is **not** in this table — it never serves stale data and its failure / idempotent-replay matrix lives in [§ 3.5](#35-post-apiv1bffcheckout).

| Scenario | product-page | basket | order-summary | home-page |
|----------|--------------|--------|---------------|-----------|
| Catalog down | 503 (no stale cache), stale cache (`X-BFF-Stale: true`), or 200 partial | 200 with `CurrentPrice=null`, `HasStaleData=true` | 200 with `CurrentName=null`, `HasStaleData=true` | 503 (first load) or stale cache + `HasStaleData=true` |
| Inventory down | 200 with `InStock=null`, `HasStaleData=true` | 200 with `AvailableQty=null`, `HasStaleData=true` | n/a (no call) | 200 with null stock fields, `HasStaleData=true` |
| Ordering down | n/a (no call; Basket is independent) | n/a | 503 or stale cache | n/a |
| Basket down (auth) | 200 with `AlreadyInBasket=null` | 503 or stale cache | n/a | n/a (public endpoint) |
| Payments down (planned `IPaymentsClient`) | n/a | n/a | 200 with derived PaymentStatus | n/a |
| All upstreams down | 503 (if no cache); stale from cache else | 503 (Basket gating) or stale | 503 or stale | Stale cache or 503 |

---

## 7. Testing Layers

| Layer | Scope |
|-------|-------|
| Unit | Response composition logic (merging Catalog + Inventory + Basket with partial-success scenarios); resilience pipeline correctness (not the Polly internals). |
| Integration | Each typed client against WireMock simulating every failure mode above. |
| Integration | FusionCache hit/miss/tag-invalidation behavior against Testcontainers Redis (`redis-cache`). |
| Integration | Kafka cache-invalidation handlers against Testcontainers Kafka, verifying each topic → tag mapping. |
| Integration | `POST /api/v1/bff/checkout` idempotency: first call forwards to Basket and returns `202 { orderId }`; replay with same `Idempotency-Key` + same body returns the cached `202` without re-calling Basket; same key + different body → 409 (ADR-0013). |
| Architecture | No direct references to `Catalog.*`, `Basket.*`, `Ordering.*`, `Inventory.*` assemblies from either BFF project. No `DbSet<>` / no Kafka producer. BFF cache + idempotency store resolve only `Redis:Cache`, **never** `Redis:Basket` ([ADR-0016](../adr/0016-redis-topology.md)). |
| Functional | Full HTTP stack via `WebApplicationFactory` with Testcontainers for Redis + Kafka, using WireMock for upstream services; exercise all five endpoints end-to-end including stale-fail-over and checkout idempotent replay. |

---

## 8. Open Questions / Deferred

Planned scope is catalogued in [roadmap.md § 2.3 BFF](../roadmap.md):

- **Rate limiting at BFF level** — current scope relies on YARP's rate limiting. A BFF-level per-user cap would be relevant if upstream protection via YARP proves insufficient.
- **Per-endpoint response compression** — deferred; YARP and ASP.NET Core defaults should be enough.
- **Language / region support** — home page and product page may want `Accept-Language` forwarding.
- **Personalized home page** — requires auth.
- **gRPC between BFF and services** — today uses HTTP/JSON (already matching the service endpoints). gRPC would halve latency but adds contract-generation surface; deferred.
- **Payments BFF query endpoint** — once Payments exposes a read API, `OrderSummary` switches from derived `PaymentStatus` to authoritative.
- **GraphQL gateway** — an alternative to this per-endpoint BFF; explicitly NOT chosen (REST + manual aggregation is the teaching goal).

---

**End of BFF Aggregation.**
