# Basket Bounded Context

> **Classification:** Technical / Session BC (see [ADR-0003](../adr/0003-basket-as-technical-bc.md)).
> **Upstream to:** Ordering (checkout handoff via `BasketCheckoutInitiatedEvent`).
> **Downstream from:** Catalog (product data consumed via Anti-Corruption Layer).
> **Agent ownership:** Stage 1 Agent 2. This section is inserted into `docs/eshop-master-design.md § 5.2`.

---

## 1. Ubiquitous Language Summary

Basket is deliberately narrow — it is a **pre-checkout session** for one user, not a persistent domain aggregate. Its language is purposefully distinct from Ordering's:

| Basket term | What it means here | Maps to (elsewhere) |
|-------------|--------------------|---------------------|
| **Basket** | Ephemeral shopping session keyed by `UserId`, lives in Redis, auto-expires | ≠ `Order` (Ordering); ≠ `Cart` in external catalog systems |
| **BasketItem** | One line in the basket: `ProductId` + frozen `ProductSnapshot` + `Quantity` | ≠ `OrderLine` (enriched differently after checkout) |
| **ProductSnapshot** | Frozen copy of Catalog product data captured at add-time — Sku, Name, Price, CapturedAtUtc | Translated from Catalog's `CatalogProductResponse` DTO via ACL |
| **Price refresh** | User-initiated reconciliation between snapshots and current Catalog prices | Deliberate — no auto-refresh |
| **Checkout** | Irreversible transition: basket → `BasketCheckoutInitiatedEvent` → basket deleted | Hand-off to Ordering/Checkout Saga |
| **Basket expiry** | 30-day sliding TTL purge by Redis (no event emitted; abandonment is silent) | — |
| **Version** | Monotonic integer for optimistic concurrency on the Redis entry | Not the same as an Ordering/ES version |

Everything in this BC is scoped by `UserId` — there is never a cross-user invariant. This is a key reason Basket qualifies as a **technical** BC rather than a classical domain one (ADR-0003).

Full glossary: [glossary-basket.md](glossary-basket.md).

---

## 2. Aggregates

### 2.1 `Basket` — the sole aggregate root

**Identity:** `UserId` (Guid). Unlike most aggregates in the platform which use `Id = Guid.CreateVersion7()`, Basket uses the **user's** identifier as its key because (a) each user has exactly one basket and (b) this drives the Redis key directly (`basket:{userId}`).

**Inheritance:** `Basket : AggregateRoot<Guid>` (where `TId` is `UserId`).

**State:**

| Member | Type | Notes |
|--------|------|-------|
| `UserId` (= `Id`) | `Guid` | Aggregate key; immutable post-creation |
| `Items` | `IReadOnlyCollection<BasketItem>` | Backed by private `List<BasketItem>`; max 50 distinct products |
| `Version` | `int` | Optimistic concurrency token; incremented on every mutation |
| `CreatedAtUtc` | `DateTimeOffset` | Set by `Create` factory |
| `LastModifiedAtUtc` | `DateTimeOffset` | Refreshed on every mutation (also drives TTL reset) |
| `Total` | `BasketTotal` (computed) | `sum(item.Snapshot.Price * item.Quantity)`; not stored — projected on demand |

**Invariants:**

1. `UserId != Guid.Empty` and immutable.
2. `0 ≤ Items.Count ≤ 50` (distinct products; quantity increases do not count against the limit).
3. For every item: `Quantity ≥ 1`.
4. No duplicate `ProductId` in `Items` — duplicate adds collapse into a quantity increase on the existing line.
5. `ProductSnapshot.Price.Currency` is uniform across all items in the basket (first add sets the currency; subsequent adds in another currency fail).
6. Snapshots are immutable after insertion — only `RefreshPrices` can replace them wholesale.
7. `Version` is strictly monotonic: every successful mutation does `Version++`.

**Factories & methods:**

```text
static Basket Create(Guid userId)
    Throw.If(userId == Guid.Empty, DataIntegrityException("Basket.InvalidUserId"))
    → AddDomainEvent(BasketCreatedDomainEvent)

Result AddItem(ProductSnapshot snapshot, int quantity)
    → invariants: quantity ≥ 1, ≤ 50 distinct products, currency uniform
    → if productId exists: increase quantity
    → else append new BasketItem
    → AddDomainEvent(ItemAddedToBasketDomainEvent)
    → returns BasketErrors.MaxItemsReached / InvalidQuantity / CurrencyMismatch on failure

Result RemoveItem(Guid productId)
    → no-op + Result.Ok if not present (idempotent)
    → else remove and AddDomainEvent(ItemRemovedFromBasketDomainEvent)

Result ChangeQuantity(Guid productId, int newQuantity)
    → invariants: newQuantity ≥ 1, item exists
    → AddDomainEvent(ItemQuantityChangedDomainEvent)

Result RefreshPrices(IReadOnlyList<ProductSnapshot> freshSnapshots)
    → replaces snapshots for known productIds; preserves quantities
    → snapshots for unknown productIds are ignored (not added)
    → AddDomainEvent(BasketPricesRefreshedDomainEvent) listing items whose price actually changed

void Clear()
    → removes all items
    → AddDomainEvent(BasketClearedDomainEvent)

Result Checkout(Guid correlationId)
    → Fail if Items.Count == 0 → BasketErrors.EmptyBasket
    → Snapshots + totals copied into the domain event payload
    → AddDomainEvent(BasketCheckedOutDomainEvent { Snapshot = FullBasketSnapshot })
    → the handler is responsible for writing the external event + deleting the Redis entry
```

All mutating methods end with `Version++` and `LastModifiedAtUtc = utcNow` (injected via application layer — the domain does not read `DateTime.UtcNow` directly; handlers pass `TimeProvider.GetUtcNow()` or tests pass a fake).

**Why a single aggregate (Vernon's rules)**

- **Rule 1 — Model true invariants within a consistency boundary:** max-50 items, unique-product, uniform-currency, and version monotonicity all span all items and must be enforced atomically.
- **Rule 2 — Design small aggregates:** Basket is small by construction — items are value objects, not entities; no child aggregates. The root + value-typed collection pattern is Vernon's ~70% case.
- **Rule 3 — Reference other aggregates by identity:** Items hold `ProductId` only; the snapshot is a *copied value*, not a reference to Catalog's `Product` aggregate. This is the exact shape the ACL enforces.
- **Rule 4 — Use eventual consistency outside the boundary:** Ordering, Catalog, Inventory are never touched in the same transaction as Basket. The checkout handoff goes through the outbox → Kafka, not a synchronous call.

---

## 3. Value Objects

### 3.1 `BasketItem`

```text
record BasketItem(
    Guid ProductId,          // Catalog reference — by identity only (Vernon rule 3)
    ProductSnapshot Snapshot,
    int Quantity             // ≥ 1, enforced by the Basket aggregate (AddItem / ChangeQuantity)
)
```

Constructed only by the `Basket` aggregate via `AddItem` / `ChangeQuantity`, each of which enforces `quantity >= 1` and returns `BasketErrors.InvalidQuantity` on failure. The internal `BasketItem.BuildUnchecked` is the trusted constructor used by the aggregate and by the persistence rehydration seam (`BasketStateMapper`); no public validating factory is exposed. Equality: structural (record). No mutable state.

### 3.2 `ProductSnapshot`

```text
record ProductSnapshot(
    string Sku,              // Copied from Catalog at add-time
    string Name,             // Copied from Catalog at add-time
    Money Price,             // Copied from Catalog at add-time
    DateTimeOffset CapturedAtUtc
)
```

The linchpin of the ACL: this is what the `IProductCatalogQueryPort` adapter produces, and it is the only shape the Basket domain knows about. The Catalog's response DTO never leaks past the Infrastructure layer.

**Frozen-pricing contract:** once a snapshot is inside a basket, it does not change until the user explicitly triggers `RefreshBasketPricesCommand`. This protects the user from silent price jumps mid-session and makes checkout totals predictable.

### 3.3 `BasketTotal`

```text
record BasketTotal(Money Amount)
```

Computed from `Items.Sum(i => i.Snapshot.Price.Amount * i.Quantity)`. Not persisted. Returned from `Basket.Total` getter and included in `BasketCheckedOutDomainEvent` and in read DTOs. Empty basket returns `new BasketTotal(Money.Zero(defaultCurrency))` — but for the checkout invariant, an empty basket cannot be checked out at all (see `Checkout` method).

### 3.4 `Money` — shared across Catalog, Basket, Ordering

```text
record Money(decimal Amount, string Currency)  // ISO 4217
```

**Location decision:** Money is defined in **`platform/Platform.SharedKernel/ValueObjects/Money.cs`** (shared kernel), not duplicated per BC. Rationale:

- Money is a universal, stable concept with no BC-specific semantics. All three BCs (Catalog, Basket, Ordering) need to agree on the decimal+currency pair representation for checkout math to be consistent.
- Avro schemas already encode money as `(bytes decimal, string currency)`; a shared `Money` type is the natural .NET counterpart on both publisher and consumer.
- The type is intentionally anemic (no domain rules beyond validation of currency format and non-negative amount): no BC wants different Money rules.

This is the only value object promoted to the shared kernel for v1. Everything else lives in the owning BC.

`Money` operations: `Add(Money other)` throws if currencies differ (`Throw.If` with `DataIntegrityException`); `Multiply(int factor)` returns `Money` with same currency; static `Money.Zero(currency)`.

---

## 4. SmartEnums

**None.** Basket has no status machine — a basket is either "exists with items" or "absent" (deleted after checkout, or expired by Redis TTL). There are no intermediate states like `Pending`, `Locked`, `Held`, etc. Adding a SmartEnum here would be ceremony without domain benefit. Ordering (the downstream BC) is where rich status modelling belongs — see its design.

This is deliberate: Basket's simplicity is the point. The moment you feel like adding `BasketStatus`, reconsider whether that state actually belongs in Ordering instead.

---

## 5. Storage Architecture

The defining feature of the Basket BC is that **the aggregate itself never touches EF Core or a relational table.** PostgreSQL is used only for the outbox/inbox side-car.

### 5.1 Primary store — Redis via FusionCache

| Concern | Choice |
|---------|--------|
| Distributed cache | `RedisCache` (StackExchange.Redis) — already wired in `PersistenceDependencyInjection.AddCache` (lines 108–150) |
| Cache facade | `FusionCache` from `ZiggyCreatures.Caching.Fusion` — exact library already in use |
| Multi-instance coordination | `RedisBackplane` — already configured |
| Serialization | `FusionCacheCysharpMemoryPackSerializer` — already configured. The Basket aggregate and its VOs stay infra-free; serialization goes through a persistence DTO (`BasketStateDocument` + nested `BasketDocument` / `BasketItemDocument` / `ProductSnapshotDocument` under `Basket.Infrastructure.Persistence.Documents`) that carries the `[MemoryPackable] partial record` annotation. The domain never depends on MemoryPack, and shared-kernel `Money` is flattened to `(decimal PriceAmount, string PriceCurrencyName)` on the DTO side — mapped in `BasketStateMapper`. (2026-04-22 self-correction: earlier drafts annotated the domain types directly, which would have required `Money` in `Platform.SharedKernel` to gain `[MemoryPackable]`; routing through a DTO keeps the Basket boundary clean.) |
| Persistence mode | Redis AOF (`--appendonly yes` — see `docker-compose.yaml:47`). Survives restarts at ≤ 1 s data-loss window. |
| Eviction | `allkeys-lru` + `maxmemory 256mb` (docker-compose). Under pressure, least-recently-used baskets are evicted before TTL — acceptable for an ephemeral session BC. |

**Important:** the Basket does *not* reuse the *default* app `IFusionCache`. It gets its **own named cache** (`"basket"`) so that its eviction/TTL policy is isolated from the default app cache's policies. Rationale: the default cache is configured with short durations for query caching; Basket needs 30-day entries that must never fail-safe to a stale value. The Basket cache is registered in `Basket.Infrastructure` via `services.AddFusionCache("basket")` with its own `WithOptions` / `WithDefaultEntryOptions`.

### 5.2 Key pattern

```
basket:{userId}     →  { Version: int, Payload: byte[] (MemoryPack of BasketDocument) }
```

Where `BasketDocument` is the persistence DTO under `Basket.Infrastructure.Persistence.Documents` — see § 5.1's MemoryPack note.

- Lowercase, colon-separated, kebab-safe (already the prefix scheme for existing cache keys).
- `{userId}` is the raw `Guid` in `D` format (lowercase 36 chars with dashes).
- The *value* stored is a small envelope `(Version, Payload)`, not the aggregate directly, so that the concurrency check can be done without deserializing on every CAS attempt.

### 5.3 TTL strategy

- **Sliding 30-day TTL**, set on every mutation via `SetDuration(TimeSpan.FromDays(30))`. FusionCache's `SetEagerRefresh` is **disabled** for basket entries — baskets must not auto-renew themselves in the background.
- On checkout, the entry is explicitly `RemoveAsync`d (see Lifecycle § 6.4).
- No scheduled sweeper. Redis does the work.
- **No abandonment event** is emitted in v1. If we later want "cart abandonment" marketing, a Redis `__keyevent@0__:expired` subscriber would be added to a future Notifications consumer — explicitly out of scope for v1.

### 5.4 Concurrency — optimistic with `Version`

Algorithm for `IBasketRepository.SaveAsync(Basket basket, int expectedVersion)`:

1. Read current `(currentVersion, _)` from Redis using FusionCache's `GetOrDefaultAsync`.
2. If `currentVersion != expectedVersion` → return `Result.Fail(BasketConcurrencyError)`.
3. Otherwise serialize `Basket` via MemoryPack, wrap into `(expectedVersion + 1, payload)`, and `SetAsync` with 30-day TTL + tag `basket`.
4. Because the check-then-set is not atomic in Redis out-of-the-box, the repository uses a **per-user Redis lock** via `IConnectionMultiplexer.GetDatabase().StringSetAsync("basket-lock:{userId}", …, NX, 5s)` wrapping steps 1–3. The lock is released on completion. Lock contention is rare in practice because a given user has one session.

The application handler wrapping `SaveAsync` is expected to **retry exactly once** on `BasketConcurrencyError` (re-load + re-apply command), then surface the failure to the user. This is the documented retry policy — higher retry counts trade correctness for a worse user experience (item added twice, etc.).

**Why not Redis `WATCH/MULTI/EXEC`?** FusionCache sits in front of the raw Redis client; reaching around it for WATCH semantics fights the abstraction. The lock + CAS pattern above is explicit, testable, and sufficient for the sub-millisecond contention profile of a single-user basket.

### 5.5 Side-car SQL (`basket` schema)

Only **two** tables live in PostgreSQL for Basket:

| Table | Purpose |
|-------|---------|
| `basket.outbox_messages` | Transactional outbox — holds serialized `BasketCheckoutInitiatedEvent` payloads awaiting relay to Kafka topic `basket.sessions` |
| `basket.inbox_messages` | Idempotent consumer dedup — holds processed message IDs for any Kafka topics this BC consumes (currently *none* in v1; reserved) |

These are the standard platform outbox/inbox tables (`Platform.ReliableMessaging`). There is **no** `basket.baskets` table — stating this explicitly because it is the single most likely mistake a reader will make.

The outbox write is done in the same `SaveAsync` flow during `CheckoutBasketCommand`:

1. Open a DbContext transaction for the `basket` schema.
2. Write outbox row (`BasketCheckoutInitiatedEvent`, topic `basket.sessions`, key `{userId}`).
3. Commit the SQL transaction.
4. Only after SQL commit succeeds, delete the Redis entry.

Steps 3 → 4 are not atomic across the two stores. If the service crashes between them, the Redis entry remains. On the user's next checkout attempt the repository detects the already-emitted outbox message via correlation and treats it as a duplicate (handler-level idempotency on `BasketCorrelationId`). This is the canonical "outbox then delete" ordering — SQL is the source of truth for "did we publish?".

---

## 6. Lifecycle

The basket lifecycle from birth to death:

### 6.1 Creation (lazy)

- No user action explicitly creates a basket.
- First `AddItemToBasketCommand` for a `UserId` with no existing Redis entry:
  1. ACL fetches `ProductSnapshot` from Catalog.
  2. `Basket.Create(userId)` is invoked → `BasketCreatedDomainEvent` is queued on the aggregate.
  3. `basket.AddItem(snapshot, qty)` is invoked → `ItemAddedToBasketDomainEvent` is queued.
  4. `IBasketRepository.SaveAsync(basket, expectedVersion: 0)` persists to Redis at `Version = 1`.
  5. In-process `IDomainEventHandler`s fan out (logging, metrics). **No external event is emitted on creation.**

### 6.2 Mutations (add, remove, change quantity)

- Load the aggregate via `IBasketRepository.GetByUserIdAsync(userId)` → `Basket` + `Version = N`.
- Invoke the domain method → raises the matching `*DomainEvent`.
- `SaveAsync(basket, expectedVersion: N)` → persists at `Version = N+1`, resets the 30-day TTL.
- No external event. Internal events are in-process only.
- **No-basket idempotency.** `RemoveItem` and `Clear` against a non-existent basket are no-ops that return 204 — the aggregate is **not** lazily created on these paths (only `AddItem` creates). `ChangeQuantity` keeps the 404 contract because changing the quantity of an item in a basket that does not exist has no defensible meaning. See [use-cases.md § 2.1.2 / § 2.1.5](use-cases.md).

### 6.3 User-requested price refresh

- `RefreshBasketPricesCommand` → handler loads basket.
- Calls `IProductCatalogQueryPort.GetManyAsync(distinct productIds)` — one round-trip, not N.
- If the ACL fails → whole command fails with `BasketAclErrors.CatalogUnavailable`.
- On success: `basket.RefreshPrices(snapshots)` → `BasketPricesRefreshedDomainEvent` with only the items whose price actually changed.
- Save as a normal mutation.
- Auto-refresh is **not** implemented. Stale prices are the user's responsibility to resolve before checkout, and checkout itself **does not silently re-check prices** — it commits to what is in the basket at the instant of checkout. This is a deliberate correctness-over-freshness trade-off.
- **No-basket / empty-basket idempotency.** Refresh against a non-existent or empty basket returns 204 without calling the ACL — there is nothing to refresh. See [use-cases.md § 2.1.4](use-cases.md).

### 6.4 Checkout (terminal)

1. `CheckoutBasketCommand { UserId, CorrelationId, ShippingAddress, BillingAddress, PaymentMethodId }` — `CorrelationId` is generated by the caller (Api layer or BFF) via `Guid.CreateVersion7()` and becomes the Checkout Saga `CorrelationId`. Addresses + `PaymentMethodId` are **pass-through courier data** per [ADR-0005](../adr/0005-customer-data-in-ordering.md): Basket validates only basic shape (non-empty strings, ISO 3166-1 alpha-2 country code, non-empty payment method id) and stamps the values onto the external event unchanged.
2. Handler loads the basket.
3. `basket.Checkout(correlationId, shipping, billing, paymentMethodId, utcNow)` is invoked:
   - Invariant: `Items.Count > 0`. Empty-basket checkout fails with `BasketErrors.EmptyBasket`.
   - Queues `BasketCheckedOutDomainEvent` with a full `BasketSnapshot` payload plus the three courier fields — the outbox publisher reads all five values from the event alone, keeping the in-process fan-out decoupled from the command boundary.
4. A `BasketCheckoutInitiatedOutboxPublisherDomainEventHandler` (the in-process domain event handler) transforms the internal event into the **external** Avro-compiled `BasketCheckoutInitiatedEvent` and writes it to the outbox (topic `basket.sessions`, key = `{userId}`).
5. Outer transaction commits the outbox write to `basket` schema in PostgreSQL.
6. On SQL commit success, the repository deletes the Redis entry: `IConnectionMultiplexer.GetDatabase().KeyDeleteAsync("basket:{userId}")` (executed outside FusionCache to bypass distributed-cache buffering).
7. The outbox relay (existing `Platform.OutboxRelay` worker) picks up the row and publishes to Kafka with schema-registry validation.
8. No archive of the basket is kept. The full line-item history lives on the saga's Order aggregate downstream — that is Ordering's job, not Basket's. Checkout is a one-way door.

**Idempotency:** if step 6 fails (process crashes), the next `CheckoutBasketCommand` for the same `UserId` will see an empty-ish basket if Redis is already gone, or a still-present basket if Redis write succeeded. The handler dedups using the outbox row's natural key `(UserId, CorrelationId)` — same `CorrelationId` re-issued = same outbox row, which the relay has already sent. Different `CorrelationId` = different checkout attempt; the user simply re-checkouts.

### 6.5 Expiry (silent)

- After 30 days of no mutation, Redis removes the key.
- No event. No marketing ping (v1). The user begins fresh on their next `AddItem`.

### 6.6 Clear (manual)

- `ClearBasketCommand` → `basket.Clear()` → `BasketClearedDomainEvent` → save empty basket (still at `Version+1`).
- The basket is **not** deleted — it remains reachable and TTL is reset. Only checkout deletes.
- **No-basket idempotency.** Clear against a non-existent basket returns 204 — no row is created and no event raised. See [use-cases.md § 2.1.5](use-cases.md).

---

## 7. Internal Domain Events (in-process)

All of these inherit from `Platform.SharedKernel.Base.DomainEvents.DomainEvent` and are sealed records. They are dispatched in-process only — **never** sent to Kafka. They exist for observability (logging/metrics), for the checkout-to-external-event transformation, and for future in-BC side-effects.

| Event | When raised | Payload |
|-------|-------------|---------|
| `BasketCreatedDomainEvent` | `Basket.Create` | `UserId`, `OccurredOnUtc` |
| `ItemAddedToBasketDomainEvent` | `Basket.AddItem` (including quantity bump on existing line) | `UserId`, `ProductId`, `Quantity`, `CapturedPrice` (Money), `OccurredOnUtc` |
| `ItemRemovedFromBasketDomainEvent` | `Basket.RemoveItem` | `UserId`, `ProductId`, `OccurredOnUtc` |
| `ItemQuantityChangedDomainEvent` | `Basket.ChangeQuantity` | `UserId`, `ProductId`, `OldQuantity`, `NewQuantity`, `OccurredOnUtc` |
| `BasketPricesRefreshedDomainEvent` | `Basket.RefreshPrices` | `UserId`, `ItemsWithNewPrices` (list of `(ProductId, OldPrice, NewPrice)`), `OccurredOnUtc` |
| `BasketClearedDomainEvent` | `Basket.Clear` | `UserId`, `OccurredOnUtc` |
| `BasketCheckedOutDomainEvent` | `Basket.Checkout` | `UserId`, `CorrelationId`, `Snapshot` (full `BasketSnapshot` VO: items, total), `ShippingAddress`, `BillingAddress`, `PaymentMethodId` (all three are courier fields per [ADR-0005](../adr/0005-customer-data-in-ordering.md) — aggregate does not own them, only ferries them onto the event for the outbox publisher), `OccurredOnUtc` |

**Per § 3 of the master design** (Event Discipline), none of these have an Avro schema, none carry `Event` suffix, and none are published externally. The downstream world does not care how many times a user toggled a quantity. It only cares when a basket transitions to checkout — and that single transition has its own external schema.

---

## 8. External Summary Events

### 8.1 Inventory

**Exactly one external event.** Topic `basket.sessions`.

| Event | Triggered by | Consumers | Compensation? |
|-------|--------------|-----------|---------------|
| `BasketCheckoutInitiatedEvent` | `BasketCheckedOutDomainEvent` (in-process handler) | Checkout Saga (`saga/SagaOrchestrators/Checkout/`) — initiator; this is the saga's starting stimulus | No — Basket cannot "un-checkout". Cancel path is owned by Ordering. |

### 8.2 `BasketCheckoutInitiatedEvent` Avro schema

- **Namespace:** `Basket.Sessions`
- **File path:** `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc`
- **Key:** `{userId}` as string (Kafka message key; keeps all checkouts for a user on the same partition for saga correlation)
- **Topic:** `basket.sessions`

```json
{
    "type": "record",
    "name": "BasketCheckoutInitiatedEvent",
    "namespace": "Basket.Sessions",
    "doc": "Emitted when a user checks out their basket. Triggers the Checkout Saga. The basket is deleted from Redis after this event is written to the outbox.",
    "fields": [
        {
            "name": "BasketCorrelationId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Correlation identifier for the checkout flow. Becomes the CorrelationId of the downstream Checkout Saga state machine. Generated when CheckoutBasketCommand is invoked."
        },
        {
            "name": "UserId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Identifier of the user whose basket is being checked out. Also the Kafka message key for partitioning."
        },
        {
            "name": "Items",
            "type": {
                "type": "array",
                "items": {
                    "type": "record",
                    "name": "BasketCheckoutItem",
                    "namespace": "Basket.Sessions",
                    "doc": "One line of the basket at the moment of checkout. Prices reflect the snapshot captured at add-time (or at the last explicit refresh).",
                    "fields": [
                        {
                            "name": "ProductId",
                            "type": {
                                "type": "string",
                                "logicalType": "uuid"
                            },
                            "doc": "Catalog Product identifier. Consumers use this to reserve stock and to load authoritative product data."
                        },
                        {
                            "name": "Sku",
                            "type": "string",
                            "doc": "Catalog SKU at the time of checkout. Copied from the snapshot for consumer convenience."
                        },
                        {
                            "name": "Name",
                            "type": "string",
                            "doc": "Product name at the time of checkout. Copied from the snapshot for order history/display."
                        },
                        {
                            "name": "UnitPriceAmount",
                            "type": {
                                "type": "bytes",
                                "logicalType": "decimal",
                                "precision": 19,
                                "scale": 4
                            },
                            "doc": "Snapshot unit price amount. Decimal 19,4 matches the Catalog price precision."
                        },
                        {
                            "name": "UnitPriceCurrency",
                            "type": "string",
                            "doc": "ISO 4217 currency code of UnitPriceAmount. Uniform across all items (enforced by the Basket aggregate invariant)."
                        },
                        {
                            "name": "Quantity",
                            "type": "int",
                            "doc": "Number of units of this product in the basket. Always >= 1."
                        },
                        {
                            "name": "LineTotal",
                            "type": {
                                "type": "bytes",
                                "logicalType": "decimal",
                                "precision": 19,
                                "scale": 4
                            },
                            "doc": "UnitPriceAmount * Quantity, pre-computed for consumer convenience. Equals the domain value of the line at checkout."
                        }
                    ]
                }
            },
            "doc": "All line items of the basket at the moment of checkout. Never empty (empty-basket checkout is rejected at the aggregate)."
        },
        {
            "name": "TotalAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Sum of all LineTotal values. Pre-computed so consumers do not have to re-sum; they SHOULD re-verify."
        },
        {
            "name": "Currency",
            "type": "string",
            "doc": "ISO 4217 currency code for TotalAmount. Equals all item UnitPriceCurrency values."
        },
        {
            "name": "ShippingAddress",
            "type": {
                "type": "record",
                "name": "CheckoutAddress",
                "namespace": "Basket.Sessions",
                "doc": "Postal address supplied at checkout. Basket is a pass-through: it does not validate deeply (only non-empty + ISO country code) and does not persist addresses beyond this event. The Ordering service re-snapshots this into the Order aggregate.",
                "fields": [
                    { "name": "Street1", "type": "string", "doc": "Primary street line." },
                    { "name": "Street2", "type": ["null", "string"], "default": null, "doc": "Optional second street line (apartment, suite, etc.)." },
                    { "name": "City", "type": "string", "doc": "City name." },
                    { "name": "State", "type": ["null", "string"], "default": null, "doc": "Optional state/province/region. Null for countries without this concept." },
                    { "name": "PostalCode", "type": "string", "doc": "Postal or ZIP code." },
                    { "name": "CountryCode", "type": "string", "doc": "ISO 3166-1 alpha-2 country code (e.g., 'US', 'CZ')." }
                ]
            },
            "doc": "Shipping address collected by the BFF/client at checkout time and passed through the CheckoutBasketCommand. Basket does NOT own addresses; it carries this payload to the saga unchanged."
        },
        {
            "name": "BillingAddress",
            "type": "Basket.Sessions.CheckoutAddress",
            "doc": "Billing address. Same shape as ShippingAddress; may be identical to it."
        },
        {
            "name": "PaymentMethodId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Reference to a saved payment method in the Payments service. Collected at checkout by the BFF/client; passed through unchanged. Validation is delegated to Payments during payment."
        },
        {
            "name": "InitiatedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the CheckoutBasketCommand was handled and the domain event was raised."
        }
    ]
}
```

### 8.3 Why so few external events?

The entire raison d'être of the internal/external split (master design § 3) is that downstream services do not care about the intermediate grind of a shopping session — only the point-in-time commitment. Publishing `ItemAddedToBasketEvent` would tempt consumers (e.g., "Recommendations") to build against a chatty, unstable contract. Keep the external surface tight; expose richer history later via a dedicated analytics BC if the need materialises.

---

## 9. ACL Design — Basket → Catalog

### 9.1 Why an ACL here specifically

Catalog owns the Product aggregate, its pricing, its hierarchy. Basket needs *only* a narrow projection: Sku, Name, Price. If Basket consumed Catalog's full `CatalogProductResponse` DTO directly (categories, inventory flags, image URLs, localization, etc.), the Basket domain would silently couple to Catalog's transport shape. Any Catalog DTO change would ripple into Basket.

The ACL enforces: **Catalog's shape stops at the Infrastructure seam. Only `ProductSnapshot` crosses into Basket.Domain/Basket.Application.**

### 9.2 Port — `IProductCatalogQueryPort`

Location: `Basket.Application.Abstractions` (port owned by the Application layer, so handlers depend on it).

```text
interface IProductCatalogQueryPort
{
    Task<Result<ProductSnapshot>> GetProductSnapshotAsync(
        Guid productId,
        CancellationToken ct);

    Task<Result<IReadOnlyList<ProductSnapshot>>> GetManyAsync(
        IEnumerable<Guid> productIds,
        CancellationToken ct);
}
```

**Contract rules:**

- Returns `Result.Fail(BasketAclErrors.CatalogUnavailable)` on any of: HTTP 5xx, network error, timeout, cancellation-by-timeout.
- Returns `Result.Fail(BasketAclErrors.ProductNotFound(productId))` on HTTP 404.
- `GetManyAsync` is **partial-tolerant**: if the Catalog response includes 9 of 10 requested products, the result is `Result.Ok(IReadOnlyList with 9 items)`. The missing `productId`s are silently dropped — the caller (e.g., `RefreshPricesCommandHandler`) decides what to do (here: leave the existing snapshot untouched).
- Both methods are read-only and idempotent.
- No retries or circuit-breaking at this layer. Cross-service HTTP resilience is handled by YARP at the edge, not by per-service Polly pipelines. The adapter configures only `BaseAddress`, request `Timeout`, `AddCorrelationId()`, and `AddServiceAuth("catalog.read")`.

### 9.3 Adapter — `ProductCatalogHttpAdapter`

Location: `Basket.Infrastructure.ExternalServices`.

- Injected typed `HttpClient` (`"Catalog"`), configured with a `BaseAddress` reading from `CatalogServiceOptions.BaseUrl` and a request timeout of ~2 seconds (subject to revision by solution-architect).
- Calls: `GET /api/v1/catalog/products/{id}` (single) and `GET /api/v1/catalog/products/by-ids?ids=id1,id2,...` (by-ids, max 100) — these are Catalog's endpoints (see Catalog BC design).
- Deserializes into **private internal DTOs** (e.g., `CatalogProductResponse`) that **never** escape this assembly. The DTOs match Catalog's wire shape.
- Maps `CatalogProductResponse → ProductSnapshot` using a small, explicit `Map(CatalogProductResponse r) → ProductSnapshot` method. Fields not needed by Basket (categories, images, availability flags, translations) are dropped on the floor.
- **Failure behavior, in priority order:**
  1. `TaskCanceledException` or `HttpRequestException` → log at warning, return `Result.Fail(BasketAclErrors.CatalogUnavailable)`.
  2. HTTP 404 → return `Result.Fail(BasketAclErrors.ProductNotFound(productId))`.
  3. HTTP 5xx → return `Result.Fail(BasketAclErrors.CatalogUnavailable)` (bucketed as "unreachable" from Basket's perspective; Catalog's own availability story is not Basket's concern).
  4. HTTP 4xx other than 404 (e.g., 400 malformed) → log at error, return `Result.Fail(BasketAclErrors.CatalogUnavailable)` — treat as a programming bug on our own call.

### 9.4 Command-side behavior under Catalog outage

- `AddItemToBasketCommandHandler`: single-product lookup — if ACL fails, the command fails. User sees "Catalog temporarily unavailable, please retry." No basket mutation.
- `RefreshBasketPricesCommandHandler`: batch lookup — if ACL fails *entirely*, the command fails. No partial refresh on transport failure.
- `CheckoutBasketCommand`: **does not call the ACL.** Checkout commits to the snapshots already in the basket. This is the trade-off documented in § 6.3.
- `RemoveItemFromBasketCommand`, `ChangeItemQuantityCommand`, `ClearBasketCommand`: do not touch Catalog at all — they operate on existing snapshots.

### 9.5 What the ACL does NOT do

- It does not subscribe to Catalog's Kafka events (e.g., `ProductPriceChangedEvent`). Basket deliberately does not auto-update snapshots on price changes — that is the whole point of the frozen-pricing contract.
- It does not cache Catalog responses on the Basket side. Catalog owns its own caching; Basket pays one HTTP hop per `AddItem` and one batched hop per refresh. Adding a Basket-side read-through cache here would obscure the freshness contract and create stale-snapshot debugging pain.
- It does not translate user-facing strings (Basket has no UI concerns).

---

## 10. Invariants Summary (consolidated)

| # | Invariant | Enforcement point |
|---|-----------|-------------------|
| 1 | `UserId != Guid.Empty` and immutable | `Basket.Create` factory (`Throw.If`) |
| 2 | `1 ≤ Quantity` per line | `AddItem` / `ChangeQuantity` (Result.Fail with `BasketErrors.InvalidQuantity`) |
| 3 | Items.Count ≤ 50 (distinct products) | `AddItem` (Result.Fail) |
| 4 | No duplicate ProductId in Items | `AddItem` (collapses into quantity increase) |
| 5 | Uniform currency across all items | `AddItem` (Result.Fail with CurrencyMismatch) |
| 6 | Snapshot price immutable until explicit refresh | Domain has no mutator — `RefreshPrices` is the only entry point |
| 7 | Empty basket cannot be checked out | `Checkout` (Result.Fail with EmptyBasket) |
| 8 | `Version` strictly monotonic | `SaveAsync` CAS (Result.Fail with BasketConcurrencyError) |
| 9 | 30-day inactivity → expiry | Redis TTL (infrastructure-level, not domain) |

---

## 11. Use Case Surface (commands/queries — details in master design § 7.2)

**Commands (mutating):**

- `AddItemToBasketCommand(UserId, ProductId, Quantity)`
- `RemoveItemFromBasketCommand(UserId, ProductId)`
- `ChangeItemQuantityCommand(UserId, ProductId, NewQuantity)`
- `RefreshBasketPricesCommand(UserId)`
- `ClearBasketCommand(UserId)`
- `CheckoutBasketCommand(UserId, CorrelationId, ShippingAddress, BillingAddress, PaymentMethodId)` — Basket is a **pass-through courier** for addresses and payment method per [ADR-0005](../adr/0005-customer-data-in-ordering.md); it validates only basic shape (non-empty strings, ISO 3166-1 alpha-2 country code) and stamps the values straight into `BasketCheckoutInitiatedEvent`. Exact field spec in [events-catalog.md § 5.2.1](events-catalog.md) and [use-cases.md § 2.1.6](use-cases.md).

**Queries (read-only):**

- `GetBasketQuery(UserId) → BasketDto` — returns items + computed total, or 404 if no basket exists.

All commands/queries route through `Platform.CQRS` mediator. Handlers sit in `Basket.Application/Baskets/{UseCase}/`.

---

## 12. Pattern Showcases

### 12.1 Pattern A — Redis-backed aggregate (the defining pattern)

**Teaching goal:** show how an aggregate can live in a distributed cache as its primary store, with no SQL state table, while still following DDD's "aggregate as consistency boundary" discipline and the platform's transactional outbox pattern.

**What it demonstrates:**

- **Repository interface lives in the Application layer.** `IBasketRepository` in `Basket.Application.Abstractions`. The *implementation*, `RedisBasketRepository` in `Basket.Infrastructure.Persistence`, is entirely a Redis/FusionCache construct — no DbContext, no EF entity mapping.
- **MemoryPack serialization via a persistence DTO.** The domain aggregate stays free of serialization attributes. The repository maps `Basket` ↔ `BasketStateDocument` at the persistence seam; only the DTO carries `[MemoryPackable] partial`. Round-trip: Redis `byte[]` → MemoryPack deserialize to `BasketStateDocument` → `BasketStateMapper.ToDomain` → domain `Basket` → mutate → `BasketStateMapper.ToDocument` → MemoryPack serialize → Redis `byte[]`. The DTO is a thin mirror of aggregate state with `Money` flattened to primitives — this is the standard DDD repository pattern and it keeps `Platform.SharedKernel.Money` clean of per-BC serialization concerns. (2026-04-22 self-correction: earlier drafts annotated the domain types directly.)
- **Optimistic concurrency without a database transaction.** The `(Version, Payload)` envelope plus per-user Redis lock gives CAS-like semantics. No database-level `ROWVERSION`. No pessimistic locking.
- **TTL as soft "garbage collection".** No scheduled sweeper. Abandonment is free.
- **Outbox-driven cross-BC integration, despite the aggregate living outside SQL.** The PostgreSQL `basket` schema holds only the outbox — just enough relational storage to satisfy the outbox pattern. This is the crucial architectural insight: **the outbox and the aggregate do not need to share a store, as long as the outbox write is transactionally consistent with whatever the system considers the "source of truth for publication intent."** In Basket's case, that source of truth is the outbox itself, and Redis aggregate deletion is a best-effort follow-up.

**Non-goals of this showcase (explicit):**

- Not Event Sourcing. The Basket is state-stored, not event-stored. Internal domain events are for in-process handlers, not for reconstruction. Inventory is the BC that demonstrates ES.
- Not a write-behind cache pattern. The cache *is* the store. There is no lazy write to SQL.
- Not a multi-level cache. No L1 in-process layer. FusionCache is configured with memory-level disabled for basket entries to avoid stale reads across instances outside the backplane invalidation.

### 12.2 Pattern B — Anti-Corruption Layer to Catalog

**Teaching goal:** show how to consume an upstream service without leaking its transport shape or model into your own domain.

**What it demonstrates:**

- **Port-Adapter pattern.** Port (`IProductCatalogQueryPort`) in Application, adapter (`ProductCatalogHttpAdapter`) in Infrastructure. Zero Catalog types in `Basket.Domain` or `Basket.Application`.
- **Translation layer.** Explicit mapping from Catalog's `CatalogProductResponse` DTO to internal `ProductSnapshot`. The adapter intentionally drops everything Basket does not need.
- **Explicit failure classification.** Network-layer errors collapse to `BasketAclErrors.CatalogUnavailable`; 404s become `BasketAclErrors.ProductNotFound`. The domain never sees HTTP status codes.
- **Partial-success tolerance** on batch reads (§ 9.2) — a behavioral choice the caller decides how to react to.
- **Snapshot-freeze discipline.** The ACL is called on add and on explicit refresh only. Checkout does *not* re-call. This is a conscious architectural commitment and is testable end-to-end.

**How the two patterns interact:** the ACL produces `ProductSnapshot` (a value object); the Redis-backed aggregate stores it as part of `BasketItem`; the aggregate never calls the ACL itself (handlers do). This is the clean separation the showcase is designed to make tangible.

---

## 13. Integration Points

| Direction | Mechanism | What flows | Freshness |
|-----------|-----------|------------|-----------|
| Catalog → Basket | HTTP (sync, via ACL) | `CatalogProductResponse` → `ProductSnapshot` | Point-in-time on add / on refresh |
| Basket → Ordering/Checkout Saga | Kafka `basket.sessions` (async, via outbox) | `BasketCheckoutInitiatedEvent` (Avro) | At-least-once |
| *(explicitly not a consumer)* | — | — | Basket does not subscribe to any Kafka topic in v1 |

Basket is a **downstream consumer of Catalog** (via ACL — conformist-prevention pattern) and an **upstream producer for Checkout** (via outbox). There is no bidirectional integration.

---

## 14. Infrastructure Notes

### 14.1 Service project layout (Basket service)

```
services/Basket/
├── Basket.Api/                         # Minimal API endpoints, OpenAPI, auth
├── Basket.Application/
│   ├── Abstractions/
│   │   ├── IBasketRepository.cs        # Port — Redis-backed in Infrastructure
│   │   └── IProductCatalogQueryPort.cs # Port — HTTP-backed in Infrastructure
│   └── Baskets/                        # Commands + queries + domain-event handlers
├── Basket.Domain/
│   ├── Baskets/
│   │   ├── Basket.cs                   # Aggregate root (MemoryPackable lives on the persistence DTO, not here)
│   │   ├── Errors/BasketErrors.cs
│   │   ├── Events/                     # 7 internal domain events
│   │   └── ValueObjects/               # BasketItem, ProductSnapshot, BasketTotal
│   └── (no SmartEnums)
└── Basket.Infrastructure/
    ├── Persistence/
    │   └── RedisBasketRepository.cs    # IBasketRepository impl via IFusionCache("basket")
    ├── ExternalServices/
    │   └── ProductCatalogHttpAdapter.cs
    ├── Database/
    │   └── BasketDbContext.cs          # Only outbox/inbox — no Basket entities
    └── Common/
        └── BasketDependencyInjection.cs
```

Architecture tests (`test/Basket.ArchitectureTests`) must enforce:

- `Basket.Domain` has **zero** references to `Microsoft.EntityFrameworkCore`, `StackExchange.Redis`, `ZiggyCreatures.Caching.Fusion`, `System.Net.Http`.
- `Basket.Application` has zero references to `StackExchange.Redis`, `System.Net.Http`.
- `Basket.Infrastructure` has no references to any other BC's assemblies.
- `Basket.Infrastructure.Persistence.Documents.BasketStateDocument` is `[MemoryPackable]` (ArchUnit-style check for attribute presence). The domain `Basket` aggregate is NOT `[MemoryPackable]` — serialization is a DTO concern that does not leak into the domain.

### 14.2 Configuration

New configuration section(s) under `appsettings.json`:

```text
Basket:
  Redis:
    CacheName: "basket"
    TtlDays: 30
    LockTimeoutSeconds: 5
  Catalog:
    BaseUrl: "http://catalog-api"
    TimeoutSeconds: 2
```

### 14.3 Database & schemas

- `basket` schema in PostgreSQL — contains only `outbox_messages` and `inbox_messages`. Per CLAUDE.md, migrations are generated deterministically by the user; this document only specifies the schema contract.
- No need for a reseed — there is no aggregate data in SQL.

### 14.4 Docker Compose additions

- **Kafka topic (via `kafka-init`):** `basket.sessions` — 3 partitions, compact=false, 7-day retention (will be bikeshed in Stage 2 Event Catalog § 6.3).
- **Outbox relay worker:** a new instance of the existing `Platform.OutboxRelay` worker pointed at the `basket` schema. Registered in `docker-compose.yaml` under a new service `basket-outbox-relay`.

### 14.5 Observability

- OpenTelemetry traces span: HTTP → Command → Repository → Redis → Outbox.
- A custom span `Basket.RepositorySaveAsync` tags `basket.user_id`, `basket.version`, `basket.item_count`.
- Dashboards (Grafana) should visualize: basket creation rate, checkout rate, abandonment proxy (expired keys via Redis `keyspace_events` metric), ACL latency p50/p95/p99.

### 14.6 Testing layers

| Layer | Scope |
|-------|-------|
| Unit | `Basket` aggregate invariants; VO construction/equality; Money arithmetic |
| Integration | `RedisBasketRepository` against Testcontainers Redis; `ProductCatalogHttpAdapter` against WireMock; full outbox-write round-trip against Testcontainers Postgres |
| Architecture | Layer boundary enforcement (§ 14.1) |
| Functional | Full HTTP stack via `WebApplicationFactory` — at minimum one full lifecycle: add → refresh → checkout → verify outbox row → verify Redis deletion |

---

## 15. Open Questions / Deferred

Planned scope is catalogued in [roadmap.md § 2.3 Basket](../roadmap.md):

- **Cart abandonment re-engagement** (would hook Redis keyspace events → Notifications).
- **Guest baskets (no `UserId`)** — current scope assumes authenticated users only. A guest-basket pattern (keyed by anonymous session cookie, short TTL, merged on login) is deferred.
- **Saved-for-later** — a parallel collection to the basket.
- **Coupons / promotions** — see [roadmap.md § 2.2](../roadmap.md); Basket today has no discount concept.
- **Price refresh automation** — intentionally not implemented. If the product team later wants "refresh on session resume," that is a command-triggering client behavior, not a domain change.

---

### Error types

Basket's error class set is the authoritative table in **[error-taxonomy.md § 1 + § 3.2](error-taxonomy.md)** (look for the `BasketErrors` rows + the C# sketch in § 3). Single source of truth; do not duplicate here. Note: Basket's `ConcurrentUpdate` error is retried once by the handler (Redis CAS mismatch) before surfacing to the client — see `error-taxonomy.md` retry-ability column.

---

**End of § 5.2 — Basket Bounded Context.**
