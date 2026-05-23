# Rate Limiting — YARP Configuration

> Per-endpoint rate limits applied at the YARP reverse proxy (before requests reach backend services). Protects upstream services from overload and malicious/accidental abuse.
>
> Related: [eshop-general-plan.md](../eshop-general-plan.md) § YARP positioning, [bff.md](bff.md), [eshop-master-design.md](../eshop-master-design.md) § 11.3 observability.

---

## 1. Positioning

YARP sits between the user (internet) and the backend services (BFF, Catalog, Basket, Ordering, Inventory). Per the general plan, YARP is either:

- A Docker Compose `yarp` service container (current default).
- Or `.AddYarp()` in an Aspire app host (optional).

Rate limiting is a **YARP concern** — backend services do NOT duplicate limits. Each service still enforces its own domain-level invariants (e.g., max 50 basket items, max 10 address lines per order), but network-level throttling is YARP's job. This keeps the throttling policy centralized and out of business code.

### What YARP does vs what services do

| Concern | YARP | Service |
|---------|------|---------|
| Per-IP request rate | ✓ | — |
| Per-user request rate | ✓ | — |
| Admin / elevated-role quotas | ✓ | — |
| 429 response generation | ✓ | — |
| Domain invariants (e.g., max items) | — | ✓ |
| Authorization (who can do what) | partial (JWT validation) | ✓ (row-level, business rules) |

---

## 2. Client identification strategy

| Client type | Identifier | Source |
|-------------|-----------|--------|
| Anonymous public | IP | `X-Forwarded-For` (trust only if the hop directly in front of YARP is the controlled load balancer; otherwise use the socket remote address) |
| Authenticated user | JWT `sub` (UserId) | `Authorization: Bearer {token}` — validate and extract `sub` claim at YARP |
| Admin | JWT `sub` + role claim | As above + `realm_access.roles` must include `admin` |

### IP trust chain

YARP MUST only honor `X-Forwarded-For` when the immediately upstream peer is a known load balancer IP (CIDR allowlist in YARP config). Otherwise an attacker can spoof headers to bypass IP-based limits. For local dev via Docker Compose, the single-hop network makes this a non-issue; the check matters in production.

### JWT validation at the edge

YARP validates the JWT signature and expiry before applying any per-user policy. If validation fails:

- **401 Unauthorized** for routes that require auth.
- For mixed public/auth routes (e.g., BFF home page), YARP falls back to IP-based limits and lets the backend handle the business-level auth check.

---

## 3. Per-endpoint limit table

| Endpoint family | Limit | Per | Notes |
|-----------------|-------|-----|-------|
| `GET /api/bff/home-page` | 100 req/min | IP | Public, aggressively cached — burst-tolerant |
| `GET /api/bff/product-page/{id}` | 100 req/min | IP | Public |
| `GET /api/catalog/**` | 200 req/min | IP | Read-only, higher limit |
| `GET /api/catalog/products?q=...` (search) | 60 req/min | IP | More expensive query — lower limit |
| `GET /api/bff/basket` | 60 req/min | UserId | Authenticated |
| `POST /api/basket/items` (add) | 30 req/min | UserId | Write |
| `DELETE /api/basket/items/{id}` | 30 req/min | UserId | Write |
| `POST /api/basket/checkout` | 5 req/min | UserId | Rare but expensive — kicks off saga |
| `GET /api/bff/order-summary/{id}` | 60 req/min | UserId | Authenticated; owner or admin |
| `GET /api/ordering/orders` | 60 req/min | UserId | Buyer order history |
| `POST /api/ordering/orders/{id}/cancel` | 10 req/min | UserId | Rare write, compensation-triggering |
| `POST /api/catalog/products` (admin write) | 30 req/min | Admin UserId | Higher trust → higher limit per-admin |
| `PUT /api/catalog/products/{id}` (admin write) | 30 req/min | Admin UserId | |
| `POST /api/inventory/stock-adjustments` | 30 req/min | Admin UserId | |
| `POST /api/ordering/orders/{id}/mark-shipped` | 60 req/min | Admin UserId | Bulk-ship operation expected |
| `POST /api/ordering/orders/{id}/mark-delivered` | 60 req/min | Admin UserId | |
| `/api/auth/login` | 10 req/min | IP | Brute-force protection; Keycloak also limits |
| `/api/auth/register` | 5 req/min | IP | Abuse protection |

### Rationale for key numbers

- **Checkout at 5/min per user** — a real buyer cannot possibly exceed this legitimately; bots trying to race-cart valuable stock are stopped cold.
- **Catalog search at 60/min per IP** — supports typical human typing in a search box (debounced) plus dev tooling, without enabling scraping.
- **Admin writes at 30/min** — admins legitimately do bulk operations; most bulk flows go through a dedicated batch endpoint, not per-item POSTs.

---

## 4. Algorithm

**Token bucket** with sliding window. Preferred over fixed window to tolerate bursts without cascading at minute boundaries.

- **Bucket size** = `1.5 × rate` (burst headroom).
- **Refill rate** = `rate / 60` tokens per second.
- **On request:** try remove 1 token; if empty → `429 Too Many Requests`.

### Why token bucket over fixed window

- A fixed window lets a client send `2 × rate` requests by timing the request burst across the minute boundary (end of minute N + start of minute N+1). Token bucket caps the true peak at the bucket size.
- Burst headroom (1.5×) tolerates legitimate parallelism without flapping 429s. Real users rarely press buttons in perfectly even cadence.

### Why not leaky bucket

Token bucket and leaky bucket behave identically on steady-state throughput. Token bucket exposes the burst capacity explicitly in configuration (`PermitLimit`), which is easier to tune and reason about.

---

## 5. 429 response format

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 30
Content-Type: application/problem+json

{
  "type": "https://httpstatuses.io/429",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded for /api/basket/items. Try again after 30 seconds.",
  "retryAfter": 30
}
```

- `Retry-After` (RFC 7231 §7.1.3) is mandatory and in seconds. Client SDKs should back off automatically.
- The response body follows `application/problem+json` (RFC 7807) to align with the rest of the API's error shape.
- The `detail` field is intentionally vague about the exact bucket state so the limit can't be reverse-engineered by attackers.

### Server-set response headers (optional, debug)

Behind a feature flag `EnableRateLimitHeaders`, YARP also emits:

- `X-RateLimit-Limit` — max tokens in the bucket.
- `X-RateLimit-Remaining` — tokens left after this request.
- `X-RateLimit-Reset` — seconds until the bucket fully refills.

These are useful in staging / integration tests and should be disabled in production to avoid exposing the rate-limit math to callers.

---

## 6. YARP configuration sketch

```json
// yarp/config.json (sketch — implementation agent writes the real file)
{
  "ReverseProxy": {
    "Routes": {
      "basket-checkout": {
        "ClusterId": "basket",
        "Match": { "Path": "/api/basket/checkout" },
        "RateLimiterPolicy": "basket-checkout-strict"
      },
      "catalog-search": {
        "ClusterId": "catalog",
        "Match": { "Path": "/api/catalog/products", "QueryParameters": [ { "Name": "q", "Mode": "Exists" } ] },
        "RateLimiterPolicy": "catalog-search"
      },
      "bff-home": {
        "ClusterId": "bff",
        "Match": { "Path": "/api/bff/home-page" },
        "RateLimiterPolicy": "public-ip"
      }
    },
    "Clusters": { /* cluster endpoints per service, omitted */ }
  },
  "RateLimiting": {
    "Policies": {
      "basket-checkout-strict": {
        "Algorithm": "TokenBucket",
        "PermitLimit": 8,              /* 1.5 × 5 req/min, rounded */
        "TokensPerPeriod": 5,
        "ReplenishmentPeriod": "00:01:00",
        "PartitionKey": "jwt-sub"
      },
      "catalog-search": {
        "Algorithm": "TokenBucket",
        "PermitLimit": 90,             /* 1.5 × 60 */
        "TokensPerPeriod": 60,
        "ReplenishmentPeriod": "00:01:00",
        "PartitionKey": "ip"
      },
      "public-ip": {
        "Algorithm": "TokenBucket",
        "PermitLimit": 150,            /* 1.5 × 100 */
        "TokensPerPeriod": 100,
        "ReplenishmentPeriod": "00:01:00",
        "PartitionKey": "ip"
      }
    }
  }
}
```

(Note: exact JSON shape depends on the `Microsoft.AspNetCore.RateLimiting` integration YARP uses — implementation agent adjusts to live YARP version.)

---

## 7. Monitoring

### Metrics

| Metric | Type | Labels | Purpose |
|--------|------|--------|---------|
| `yarp.rate_limit.rejected_total` | counter | `endpoint`, `reason` | Count of 429 responses |
| `yarp.rate_limit.allowed_total` | counter | `endpoint` | Count of successful passes |
| `yarp.rate_limit.queue_depth` | gauge | `partition_key` | Current bucket depletion (debug only) |
| `yarp.request.duration` | histogram | `endpoint`, `status` | End-to-end request latency at proxy |

### Grafana dashboard

Panel set (lives on the eShop ops dashboard):

- Per-endpoint 429 rate (stacked area).
- Top 10 offending clients (IP or UserId, last 1h) — feeds abuse investigation.
- Allowed vs rejected ratio per endpoint (single-stat).
- P95 request latency by endpoint — to catch the case where rate-limit decisions themselves become slow.

### Alerts

| Alert | Condition | Severity | Action |
|-------|-----------|---------|--------|
| `HighRateLimitRejection` | `rate(yarp.rate_limit.rejected_total[5m]) > 10` per endpoint | Warn | Slack `#ops-checkout` — investigate abuse or misconfigured client |
| `RateLimitStorm` | `rate(yarp.rate_limit.rejected_total[5m]) > 100` total | Page | On-call investigates possible DDoS or runaway client |
| `RateLimiterLatency` | `p95 yarp.request.duration{endpoint="rate-limit-path"} > 100ms` | Warn | Proxy itself is slow — memory pressure? |

---

## 8. Bypasses (controlled exceptions)

- **Internal traffic** (BFF → Catalog/Basket/etc.) bypasses YARP entirely — services are on a private Docker network; YARP only fronts external traffic. Service-to-service calls are authorized via service JWTs, not rate-limited.
- **Health-check endpoints** (`/api/healthz`, `/api/readiness` — see [`Platform.ServiceDefaults.WebApplicationExtensions`](../../platform/Platform.ServiceDefaults/WebApplicationExtensions.cs)) are not rate-limited.
- **Metrics scrape endpoints** (`/metrics`) are not rate-limited when called from the Prometheus CIDR range; rate-limited from anywhere else.
- **Admin "break glass" flag** — an operator can temporarily disable a policy via Aspire dashboard or a config-reload hot-patch (emergency-only; audited via a dedicated audit log entry that captures operator identity, timestamp, and reason).

### Audit of bypasses

Every invocation of the break-glass flag emits an `ops.rate_limit.bypass_enabled` event with:

- Operator `sub`.
- Policy name disabled.
- Expected duration (`ttl_minutes`).
- Reason text (free-form).

Event sinks to Seq + triggers a non-paging Slack notification so the wider team has visibility.

---

## 9. Testing

### Unit (YARP policy configuration)

- Assert each policy's `PermitLimit`, `TokensPerPeriod`, `ReplenishmentPeriod`, `PartitionKey` match the § 3 table.
- Assert every route declared in § 3 has a matching `RateLimiterPolicy` binding in config.

### Integration tests with synthetic load

- Issue `rate + 10%` requests in a tight loop against a test endpoint; assert 429s appear exactly at the expected token depletion point.
- Sleep `ReplenishmentPeriod`; assert tokens replenish and further requests succeed.
- Cross-partition isolation: user A exhausting their bucket MUST NOT cause user B to get 429.

### Load test (k6 or similar)

- Confirm burst behavior matches token-bucket math (send 2× rate in a 1-second burst; expect `PermitLimit` through immediately, then queue or 429).
- Measure proxy p95 latency at 3× sustained rate to validate the policy implementation's performance.

### Chaos test

- Verify service still recovers gracefully when YARP returns 429 for 5 minutes straight (client-side retry with exponential backoff). No cascading failures downstream; no orphaned sagas triggered by partial request completion.
- Inject YARP pod restart during sustained load; assert no "token reservoir" is lost in a way that would over-throttle after recovery.

### Staging shakedown

Before every production rollout of a policy change:

- Deploy new `yarp/config.json` to staging.
- Run the integration suite against staging.
- Synthetic buyer flow (login → browse → add to basket → checkout) MUST succeed end-to-end with headroom against limits.
- Only then promote to production.
