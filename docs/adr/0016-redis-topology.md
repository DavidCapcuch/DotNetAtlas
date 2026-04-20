# ADR-0016: Redis Topology — Split `redis-basket` and `redis-cache`

## Status

Accepted (2026-04-19)

## Context

The reference solution uses Redis for two different purposes:

1. **Basket primary store** — the Basket BC holds its aggregate in Redis keyed `basket:{userId}`. Redis is the **authoritative** store; losing the data means users lose their baskets.
2. **BFF FusionCache L2 backplane** — the BFF caches composed responses (product detail, order summary) in a distributed cache with invalidation pub/sub. Redis is **volatile**; losing the data means the next request is slower, not wrong.

The initial design used a single Redis container for both roles. Reviewer feedback flagged this as a shared-failure-domain issue: a memory-pressure eviction or a `FLUSHALL` on the cache side would evict baskets too. Different durability contracts, different access patterns, different memory pressure — but one physical Redis.

Splitting them costs one additional container in `docker-compose.yaml` and a second connection string. The question is whether the pedagogical win is worth the small infrastructure overhead.

## Decision Drivers (ranked)

1. **Durability contract clarity** — basket loss is a customer incident; cache loss is a latency blip. These cannot share a physical instance in production, and the reference should teach why.
2. **Failure-domain isolation** — a `FLUSHALL` attack on the cache must not evict baskets.
3. **Independent operation** — basket Redis needs AOF `everysec`; cache Redis needs volatile-lru eviction. Mixing policies on one instance is a config compromise.
4. **Testability** — integration tests should be able to wipe the cache without wiping baskets.
5. **Low friction** — one extra container + one extra connection string is a small cost.

## Considered Options

### Option 1: Two distinct Redis containers — `redis-basket` (AOF) and `redis-cache` (volatile)

Two containers, two connection strings, two eviction / persistence policies. Each has a clear name reflecting its role.

### Option 2: Single Redis container with logical-database separation (DB 0 for basket, DB 1 for cache)

One container; application code selects different DB indexes. Keeps infra identical to today.

### Option 3: Single Redis container with key-prefix separation (`basket:{id}` vs `cache:{key}`)

One container; applications agree on key prefixes.

### Option 4: Basket in Postgres; Redis only for BFF cache

Move basket out of Redis entirely — no need for basket-specific Redis policies.

## Evaluation Matrix

| Driver (ranked) | Option 1: Two containers | Option 2: Logical DBs | Option 3: Key prefixes | Option 4: Basket → Postgres |
|---|---|---|---|---|
| 1. Durability contract clarity | Two containers = two contracts, obvious | One instance = one contract, docs lie | Same as Option 2 | Postgres's contract is different again |
| 2. Failure-domain isolation | Full isolation | Shared | Shared | Full isolation (different technology) |
| 3. Independent operation | Different config files per container | Single config must satisfy both — compromise | Same as Option 2 | Postgres handles Basket differently than current design intends |
| 4. Testability | `docker compose restart redis-cache` wipes cache only | `FLUSHDB` on one DB; still same container | Pattern-match `DEL cache:*` — error-prone | Different reset approach |
| 5. Low friction | +1 container, +1 connection string | Zero new infra | Zero | Largest rework — Basket BC design changes |

## Decision

We will use **Option 1: two distinct Redis containers** — `redis-basket` with AOF `everysec` persistence, and `redis-cache` with no persistence and `allkeys-lru` eviction. Each has its own connection string, its own container, its own port, its own name.

## Rationale

The durability contract is the load-bearing reason. Basket is an authoritative store: data loss = customer incident. Cache is volatile by definition: data loss = next request pays the miss. Putting them in one Redis instance means any config choice (eviction policy, persistence fsync mode, maxmemory limit) compromises one of the two contracts. In production, no mature team would collapse these; the reference should show the canonical split.

Option 2 (logical DBs) is superficially appealing but silently fails: Redis DB selection is a per-connection flag, not a per-key namespace. A bug can `SELECT 0` instead of `SELECT 1` and touch the wrong data. More importantly, the *instance* is one failure domain; a memory-pressure OOM takes both DBs down together.

Option 4 (basket → Postgres) is a fine alternative design and was considered in ADR-0003 (Basket as Technical BC). The reference chose Redis-backed basket deliberately to showcase the pattern. Changing that choice here would undo a pedagogical goal.

The small infrastructure cost (one extra container, ~40 MB memory) is the explicit price of the teaching value.

## Consequences

### Positive

- Failure-domain isolation: a `FLUSHALL` on the cache does not touch baskets.
- Policy independence: basket gets AOF durability; cache gets aggressive eviction.
- Ops can tune each instance independently without worrying about the other.
- Docs can say "basket Redis never evicts under normal load" and "cache Redis will evict under memory pressure" — both statements are now precisely true.
- Mirrors production topology — readers taking this to production change container → managed Redis (ElastiCache, MemoryStore) 1:1.
- Integration tests can `docker compose restart redis-cache` to simulate cache loss without affecting basket state.

### Negative

- +1 container in `docker-compose.yaml`. Negligible operational cost.
- +1 connection string per service that uses Redis (Basket + BFF). Each service now has `Redis:Basket` / `Redis:Cache` config keys.
- Developers must remember which Redis is which. Mitigation: names are self-explanatory; arch test flags cross-use.

### Risks

- **Connection-string mix-up in code** — a developer configures BFF's FusionCache to hit `redis-basket`. Mitigation: arch test asserts `ICacheProvider` only points to `redis-cache` and `IBasketRepository` only points to `redis-basket`.
- **Increased memory overhead** — two Redis instances = two Redis processes. Negligible at reference scale.
- **Backplane coordination** — if a future service needs its own cache backplane, does it get its own Redis or share `redis-cache`? Mitigation: default is "share `redis-cache`"; a dedicated instance is an opt-in decision.

## Implementation Notes

### `docker-compose.yaml` additions

```yaml
services:
  redis-basket:
    image: redis:7.4-alpine
    command: ["redis-server", "--appendonly", "yes", "--appendfsync", "everysec", "--maxmemory-policy", "noeviction"]
    ports: ["6379:6379"]
    volumes: ["redis-basket-data:/data"]
    profiles: ["core", "full"]

  redis-cache:
    image: redis:7.4-alpine
    command: ["redis-server", "--appendonly", "no", "--maxmemory", "512mb", "--maxmemory-policy", "allkeys-lru"]
    ports: ["6380:6379"]
    profiles: ["core", "full"]
    # no volume — volatile by design

volumes:
  redis-basket-data:
```

Rationale for config:

- `redis-basket`: AOF `everysec` trades ≤ 1s of loss on crash for durability. `noeviction` ensures Redis never drops a basket silently — under memory pressure it returns errors instead, which the Basket service can alert on.
- `redis-cache`: no persistence (by design — cache should be empty on restart). 512 MB limit with `allkeys-lru` eviction — the cache auto-manages its own size.

### Per-service config

`appsettings.json` entries:

```json
{
  "Redis": {
    "Basket": "redis-basket:6379",
    "Cache": "redis-cache:6379"
  }
}
```

Basket's `PersistenceDependencyInjection` registers `IConnectionMultiplexer` keyed to `Redis:Basket`. BFF's FusionCache L2 backplane points to `Redis:Cache`. Other services (Catalog, Ordering, Inventory, Payments, Invoicing) use `Redis:Cache` for cached HTTP client responses and similar ephemeral state.

### Health checks

Both instances expose `/healthz` via a service-side check — Basket's readiness fails if `redis-basket` is unreachable; BFF's degrades if `redis-cache` is down (still serves uncached responses).

### Architecture tests

- `Basket.Infrastructure` may only reference `Redis:Basket` connection string (assert via reflection on DI registration).
- `EShop.BFF.Infrastructure` may only reference `Redis:Cache`.
- No service may reference both Redis instances for the same purpose.

### Observability

- OTel metrics: `redis.connections.active` tagged `instance=basket|cache`.
- Dashboard panels per instance: used memory, evicted keys, hit ratio (cache instance only).
- Alert: `redis-basket` `used_memory > 80% of maxmemory` (NOT expected under `noeviction` policy — indicates incident).
- Alert: `redis-cache` eviction rate is informational, not an alert trigger.

### Production migration

- Both become managed Redis (ElastiCache, Azure Cache for Redis, GCP Memorystore).
- `redis-basket` goes to a Sentinel setup or managed HA tier; `redis-cache` can be a cheaper non-HA cluster.
- ADR-0009's "Taking this to production" checklist references this decision.

## Related Decisions

- [ADR-0003: Basket as Technical BC](0003-basket-as-technical-bc.md) — establishes Basket's Redis primacy
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — single-AZ, non-Sentinel Redis is part of the v1 profile
- [ADR-0011: PII Handling & GDPR](0011-pii-handling-gdpr.md) — basket data does NOT carry PII beyond UserId; no encryption required at the Redis layer for v1
