# ADR-0034: Inventory Stock-Availability Read Path — Inventory-Owned Read-Through Cache

## Status

Accepted (2026-06-05)

## Context

The BFF's `/api/v1/bff/basket` and `/api/v1/bff/home-page` endpoints display per-product availability for a *batch* of products (every line in a basket; every featured product). The BFF also caches its **composed** responses (`bff.md § 3`). The open question is how the BFF obtains availability for N products without an N+1 fan-out, and — given this reference solution targets a **high-RPS, large-scale eShop** (the reason it is microservices at all, `ADR-0009`) — where, if anywhere, a cache for the hot stock-read path should live.

Three facts constrain the answer:

1. **`StockLevelChangedEvent` is deliberately coarse.** Its schema `doc` states it is emitted only when availability *crosses a threshold* (0↔positive) and is **"not emitted on every stock change."** A product moving `available: 100 → 5` (both positive) emits nothing. The event can therefore maintain a correct `inStock` *boolean* but **cannot** maintain a correct `availableQty` *integer*.
2. **The BFF's cache is volatile.** `redis-cache` is `allkeys-lru` with no persistence (`ADR-0016`). Anything the BFF "materializes" there is lost on eviction/cold-start with no authoritative source to rebuild from inside the BFF.
3. **Inventory is event-sourced (`ADR-0006`).** The reservation *decision* (`ReserveStockCommand`) rehydrates the `StockItem` aggregate from the event store under optimistic concurrency; it never reads the `current_stock_levels` projection. Availability **display** and availability **decision** are already separate code paths.

## Decision Drivers (ranked)

1. **Read-model completeness** — whatever serves the BFF must be able to return a truthful `availableQty`, not just a stale boolean.
2. **Oversell safety** — no caching choice may let a stale read cause a double-sell.
3. **Service-boundary encapsulation** — Inventory must stay independently deployable; its internal storage/caching must not become an implicit cross-service contract.
4. **Scalability at high RPS** — the hot stock-read path must not contend with the write path on Postgres connections under load.
5. **BFF statelessness** — the BFF owns no domain and no durable store (no `DbSet<>`, arch-test-enforced).

## Considered Options

### Option 1: BFF materializes availability from `inventory.stock-events`

The BFF already consumes `stock-events` for cache invalidation; have it instead *store* the availability numbers from the event payload and serve them without calling Inventory.

### Option 2: Shared cache — Inventory populates `redis-cache`, the BFF reads Inventory's keys directly

One availability cache, written by Inventory, read by the BFF on a key both agree on; on miss the BFF calls Inventory.

### Option 3: Inventory-owned read-through cache behind the API + BFF composed cache (chosen)

`POST /api/v1/inventory/stock-items/bulk` is the contract. *Inside* Inventory, a read-through cache fronts the `current_stock_levels` projection for the single + bulk display queries. The BFF calls the API and never sees the cache; the BFF's own composed-response cache (`bff.md § 3`) remains a separate, higher layer.

### Option 4: No cache — Postgres-only single + bulk reads

Build `bulk`; serve both display queries straight from the `current_stock_levels` projection with no cache.

## Evaluation Matrix

| Driver (ranked) | Opt 1: BFF materializes | Opt 2: Shared cache | Opt 3: Inventory read-through (chosen) | Opt 4: Postgres-only |
|---|---|---|---|---|
| 1. Read-model completeness | **Fails** — event is threshold-coarse, `availableQty` goes stale | OK | OK (reads the projection) | OK |
| 2. Oversell safety | Safe (display-only) but wrong numbers | Safe (display-only) | Safe — command side is ES, never the cache | Safe |
| 3. Boundary encapsulation | BFF grows availability logic it shouldn't own | **Violated** — cache key becomes an unversioned cross-service contract | Preserved — cache is hidden behind the HTTP API | Preserved |
| 4. Scalability at high RPS | Good (no hop) but incorrect | Good but coupled | **Good** — offloads Postgres for *every* caller | Read path contends with writes at scale |
| 5. BFF statelessness | **Violated** — needs durable state to survive eviction | Borderline — BFF reads foreign state | Preserved — BFF stays a thin composer | Preserved |

## Decision

Adopt **Option 3**. Concretely:

1. **Build `POST /api/v1/inventory/stock-items/bulk`** (`GetStockLevelsBulkQuery`, `use-cases.md § 4.4.2`) — the batch contract the BFF depends on. The BFF MUST NOT work around its absence with N single-item calls.
2. **Inventory owns a read-through cache** (FusionCache over `redis-cache`, `Redis:Cache` per `ADR-0016`) fronting the `current_stock_levels` projection for **both** `GET /stock-items/{productId}` and `POST /stock-items/bulk`. The cache is an Inventory implementation detail behind its HTTP API; no other service reads its keys.
3. **Freshness = invalidate-on-projection-update + short fail-safe TTL.** The existing `IDomainEventHandler<T>` that upserts `current_stock_levels` (§ 9.1) also evicts the affected cache key in the same handler flow as the upsert; the next read rebuilds (FusionCache stampede protection collapses the rebuild). The eviction is **best-effort**: `redis-cache` is volatile / non-critical (`ADR-0016`), so a transient eviction failure is logged and swallowed — it does NOT fail the stock-mutating transaction. A short fail-safe TTL bounds staleness if an invalidation is ever missed or lost.
4. **`inventory.stock-events` is an invalidation signal only** for the BFF — it invalidates the BFF's *composed* cache; it is never the source the BFF materializes availability from (driver 1).
5. **Oversell safety is structural, not conventional.** The command side (`ReserveStockCommand`) reads the event-sourced aggregate, so the display cache — at any staleness — cannot cause an oversell. No code rule is needed to "remember not to reserve from the cache"; the CQRS/ES split already guarantees it.
6. **The BFF→Inventory hop is accepted.** It is paid only on a BFF *composed-cache miss* (cold path), and it is the price of the service boundary — Inventory can retune or remove its cache without the BFF knowing.

## Rationale

Option 1 fails the top driver outright: a deliberately threshold-coarse event cannot back a precise `availableQty`, and "materializing" into a volatile LRU cache would need durable BFF state — exactly the stateless-BFF principle this solution enforces with an arch test. Option 2 trades a versioned HTTP contract (`ADR-0012`) for an *unversioned* shared-cache-key contract: Inventory could no longer change its cache shape or invalidation without breaking the BFF, and the two services could no longer deploy independently — the distributed-monolith smell. Option 4 is correct but leaves Postgres serving the hot read path under the write path's connection pressure at the scale this reference targets.

Option 3 keeps each cache owned by exactly one service and reachable only through that service's API. The Inventory cache protects Postgres for **every** reader (BFF, Catalog availability flags, direct clients), not just the BFF; the BFF composed cache eliminates the upstream fan-out on its own hot path. The two layers compose without coupling. Crucially, the event-sourced write path makes the whole arrangement oversell-safe *by construction* — the cache lives entirely on the query side, and the reservation decision is computed against the authoritative stream. This ADR is, in effect, a worked demonstration of why `ADR-0006` chose ES for Inventory.

## Consequences

### Positive

- BFF batch availability is a single round trip; no N+1.
- Postgres read path is offloaded at high RPS for all of Inventory's callers.
- Inventory's cache is free to evolve (shape, TTL, technology) behind its HTTP API; the BFF is insulated.
- Oversell-safety requires no discipline — the ES command path cannot read the cache.
- Demonstrates layered caching with single-owner boundaries — a teachable pattern.

### Negative

- A BFF composed-cache miss pays an extra in-cluster hop (BFF→Inventory) versus a hypothetical direct cache read (~1–2 ms; cold path only).
- Inventory gains a cache-invalidation surface on its projection-update handler.

### Risks

- **Missed invalidation → stale display.** Mitigation: the short fail-safe TTL bounds it; and it is display-only (driver 2 unaffected).
- **Cache/projection divergence on partial failure.** Mitigation: evict (not write-through) in the same handler flow that upserts the projection. The eviction is best-effort — it does NOT fail the write (the write path must stay available when the volatile cache is down, `ADR-0016`) — so the **short fail-safe TTL is the divergence backstop**: any missed or failed eviction self-heals within the TTL, and divergence is display-only (driver 2 unaffected: the reservation decision never reads the cache).
- **Over-caching a cheap read.** The single-row PK read is already cheap; the cache earns its keep only at the targeted RPS. Accepted deliberately given the scale premise (`ADR-0009`); revisit if load tests show the projection read is not hot.

## Implementation Notes

- Cache key namespace: `inventory:stock:{productId}` on `redis-cache`; bulk composes per-id hits + a single `WHERE ProductId = ANY(@missing)` for the misses.
- The reservation decision path (`ReserveStockCommandHandler`) is unchanged and MUST remain cache-free.
- Reuse the FusionCache + `redis-cache` wiring pattern from the BFF / `PersistenceDependencyInjection`; do not introduce a second cache library.
- `GetStockLevelsBulkQuery` is `AllowAnonymous` (BFF home/product overlays) and partial-tolerant (`MissingProductIds`).

## Related Decisions

- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md) — the ES write path is what makes the display cache oversell-safe.
- [ADR-0016: Redis Topology](0016-redis-topology.md) — `redis-cache` (volatile, `allkeys-lru`) is the cache instance; `redis-basket` is never used here.
- [ADR-0012: API Versioning](0012-api-versioning.md) — the versioned HTTP API is the cross-service contract that a shared cache would have bypassed.
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — the high-RPS scale premise that justifies the cache.
