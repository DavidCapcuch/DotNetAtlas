# ADR-0003: Basket as a Technical / Session Bounded Context

## Status

Accepted (2026-04-18)

## Context

The DotNetAtlas eShop reference solution must model a shopping basket: the place where a user accumulates items before checkout. On the surface, Basket's ubiquitous language (items, quantities, prices, totals) largely mirrors Ordering's. A classical DDD bounded context is usually justified by two things: a genuinely distinct language and independent ownership of non-trivial business rules. Basket has neither strongly — its rules (max 50 items, uniform currency, frozen snapshots) are either technical safeguards or session-scoped concerns rather than policies the business negotiates. In practice, Basket is closer to a **session/persistence concern**: it caches the user's intent, then translates that intent into an Order at checkout.

Two framings were on the table when scoping the solution:

- **Domain BC** — model Basket as a peer of Catalog / Ordering / Inventory: full aggregate with its own repository, its own event stream, a rich status model, and a persistent storage tier.
- **Technical / session BC** — model Basket as a session layer: an ephemeral aggregate living in Redis, with a minimal external footprint (single summary event on checkout) and an explicit lifecycle (cleared on checkout, auto-expired otherwise).

The teaching value of the two framings is very different. A reference solution that treats every BC as "a domain BC" quietly fails to teach learners **when** a BC is genuinely domain-driven versus infrastructure-driven. We wanted Basket to be a deliberate, honest example of the latter.

## Decision Drivers (ranked)

1. **Honest pedagogy** — learners should see that not every BC has domain-rich language. Labeling Basket as technical is more truthful than pretending it owns deep business rules.
2. **Keep domain-rich BCs focused** — Ordering has a rich status FSM, Inventory demonstrates event sourcing. Basket should not dilute those showcases by competing for the same pattern space.
3. **Ephemeral by design** — per the Basket BC design, a basket is deleted on checkout and auto-expires after 30 days. Short lifespan and per-user isolation favor cache-like storage over an OLTP table.
4. **One small external event contract** — a single `BasketCheckoutInitiatedEvent` on `basket.sessions` minimizes blast radius for other BCs and avoids a chatty cross-service surface.
5. **Keep the option to upgrade** — if loyalty, promotions, abandoned-cart rescues, saved baskets, or gift-cart features later demand deeper semantics, Basket can graduate to a richer BC without re-architecting its consumers.

## Considered Options

### Option 1: Basket as a standalone technical BC

A dedicated Basket service with:

- Redis as the primary store for the `Basket` aggregate, accessed via FusionCache + Redis backplane
- MemoryPack serialization for the aggregate and its value objects
- A single external event — `BasketCheckoutInitiatedEvent` — published to `basket.sessions` via the transactional outbox pattern
- An Anti-Corruption Layer to Catalog (`IProductCatalogQueryPort`) that translates Catalog DTOs into a narrow `ProductSnapshot` VO
- A side-car PostgreSQL `basket` schema holding **only** outbox/inbox tables (no aggregate table)
- Explicit documentation labeling Basket as a "session/persistence BC, not a domain BC"

### Option 2: Basket as a sub-context under Ordering

Fold Basket into the Ordering service as a sub-aggregate, sharing its DbContext and deployment unit. Pros: fewer services to operate, no cross-BC hop for the checkout handoff. Cons: ties an ephemeral, hot-path session store to a transactional OLTP service; mixes Redis-like access patterns into Ordering's relational schema; violates the "small, focused services" principle the reference solution teaches everywhere else.

### Option 3: Basket as client-side state only

The SPA/mobile client holds the basket locally and submits the full basket to Ordering at checkout. Pros: zero server state, zero server infra cost. Cons: multi-device continuity is impossible; there is no server-side abandoned-cart recovery; operations cannot observe in-flight baskets; any server-side feature (price-change indicators, inventory-aware warnings, cross-device sync) is precluded by construction.

## Evaluation Matrix

| Driver (ranked)                       | Option 1: Standalone Technical BC | Option 2: Sub-context under Ordering | Option 3: Client-side only |
|---------------------------------------|-----------------------------------|--------------------------------------|----------------------------|
| 1. Honest pedagogy                    | Teaches the technical-BC pattern explicitly | Hides the distinction inside Ordering | Teaches "no BC needed" but never surfaces server-side session patterns |
| 2. Keep domain-rich BCs focused       | Basket and Ordering each showcase their own pattern | Ordering accumulates unrelated session concerns | Ordering stays focused, but Basket patterns are never demonstrated |
| 3. Ephemeral by design                | Redis + TTL is the natural fit | Forces ephemeral data into an OLTP table | Ephemeral by definition, but not durable across devices |
| 4. One small external event contract  | Single summary event; minimal coupling | No external event, but Ordering inherits basket semantics | No server-side event at all; Ordering receives a fat checkout payload |
| 5. Keep option to upgrade             | Can graduate to a richer BC without breaking consumers | Extracting later means splitting a shared DbContext | Moving to server-side later requires designing the BC from scratch under pressure |

## Decision

We will use **Option 1: Basket as a standalone technical BC**, with Redis-only persistence for the aggregate and explicit labeling as a session/persistence BC in all design docs.

## Rationale

**A Redis-backed aggregate is the teaching showcase.** Option 1 lets the Basket BC demonstrate a pattern that no other BC in the solution shows: an aggregate that lives entirely in a distributed cache, round-trips via MemoryPack, uses optimistic concurrency via a `Version` envelope, and still participates in the transactional outbox pattern through a side-car schema. Keeping this pattern isolated in its own BC — rather than folded into Ordering — lets learners study it without the noise of an OLTP status FSM and event-sourced inventory concerns crowding the same service.

**Labeling Basket as "technical" avoids misleading learners.** If every BC in the reference solution wore the "domain BC" label uniformly, readers would leave believing DDD prescribes the same shape everywhere. The master design explicitly distinguishes BCs by their nature, and ADR-0003 is where that distinction is made concrete for Basket. The label is load-bearing: it justifies the absence of rich status enums, the absence of a `basket.baskets` table, and the single external event. Option 2 would couple unlike concerns — transactional order lifecycle with hot-path session cache — and quietly train learners that everything collapses into the nearest relational service. Option 3 forfeits the multi-device UX a realistic eShop requires and removes the teaching surface for server-side session patterns entirely.

**The decision does not preclude a future upgrade.** If requirements evolve — abandoned-cart marketing, gift carts, promotions that need a first-class basket lifecycle, saved-for-later collections — Basket can graduate to a richer BC. The external contract (`BasketCheckoutInitiatedEvent`) is already designed to be stable across that evolution: consumers do not observe the internal storage mechanism. Choosing Option 1 now costs nothing against that future; choosing Option 2 would have made the split more expensive when it arrives.

## Consequences

### Positive

- Clear teaching boundary — the Redis-backed aggregate is a first-class pattern demonstrated in isolation, distinct from Ordering's status FSM and Inventory's event sourcing
- One external event contract (`BasketCheckoutInitiatedEvent`) minimizes cross-BC coupling and keeps downstream consumers (Checkout Saga, Ordering) pointed at a single, stable integration point
- Basket can be scaled and versioned independently — Redis cluster topology, TTL policy, MemoryPack schema version — without touching Ordering's deployment
- Ephemeral lifecycle is natural: sliding 30-day TTL, deleted on checkout, no archive required
- The ACL to Catalog (`IProductCatalogQueryPort`) becomes a clean teaching example of Port-Adapter translation that other BCs can reference

### Negative

- The "is this a domain BC?" question must be addressed explicitly in the docs — this ADR is the primary mitigation
- Some DDD purists may prefer a richer Basket model; the reference solution prizes honest classification and clarity over maximalism
- The aggregate-in-Redis pattern requires learners to understand the outbox-without-an-aggregate-table insight (SQL holds only outbox/inbox, aggregate lives in Redis) — a valuable lesson, but an extra concept to absorb
- Basket still requires a deployed service and a `basket` schema for outbox/inbox, so the "just a cache" framing is slightly misleading under the hood

### Risks

- **Feature creep** — promotions, saved baskets, gift cards, or abandoned-cart rescues may demand a richer model. Mitigation: scope Basket v1 docs tightly; author a future ADR if/when these requirements actually land, rather than preemptively designing for them.
- **Data loss surprises** — Redis AOF (`--appendonly yes`) is the documented persistence mode, with a sub-second loss window on crash and `allkeys-lru` eviction under memory pressure. Operators must understand that wiping Redis loses in-flight baskets, which is acceptable per the Basket design but surprising if unexamined.
- **Outbox-then-delete ordering** — the SQL outbox commit is the source of truth for "did we publish?"; the Redis delete is a best-effort follow-up. If the service crashes between the two, the basket remains until the user re-checks out with the same correlation id. Handlers deduplicate on `(UserId, CorrelationId)`; the risk is bounded but must be covered by integration tests.

## Implementation Notes

- Redis key pattern: `basket:{userId}` where `{userId}` is the raw `Guid` in `D` format; value is a `(Version, Payload)` envelope so CAS checks do not need to deserialize the full aggregate
- Sliding 30-day TTL, reset on every mutation; no scheduled sweeper (Redis does the work)
- MemoryPack serialization + FusionCache + `RedisBackplane` for multi-instance coordination; Basket uses its own named `FusionCache("basket")` so its TTL/eviction policy is isolated from the default app cache
- Per-user Redis lock (`basket-lock:{userId}`, 5 s) wraps the load-mutate-save path to provide atomic check-then-set against the `Version` envelope
- Side-car PostgreSQL `basket` schema holds only `outbox_messages` and `inbox_messages` — **no** `basket.baskets` table
- No EF `DbContext` for the Basket aggregate; the only DbContext in the service is for outbox/inbox reliability tables
- ACL to Catalog via `IProductCatalogQueryPort` (Application-owned port) + `ProductCatalogHttpAdapter` (Infrastructure-owned adapter); Catalog DTOs never escape `Basket.Infrastructure`
- Single external event `BasketCheckoutInitiatedEvent` published to topic `basket.sessions`, keyed by `UserId`, with its Avro schema stored in `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/`
- Outbox relay reuses the existing `Platform.OutboxRelay` worker, pointed at the `basket` schema
- Architecture tests enforce: `Basket.Domain` has zero references to EF Core / Redis / HTTP; `Basket.Application` has zero references to Redis / HTTP; `Basket.Infrastructure` does not reference any other BC's assemblies

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — the Checkout Saga (downstream consumer of `BasketCheckoutInitiatedEvent`) is hosted by the centralized saga service
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — describes how the Checkout Saga consumes Basket's single external event and coordinates Ordering, Inventory, and Payments
