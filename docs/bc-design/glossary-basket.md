# Ubiquitous Language — Basket Bounded Context

> **Scope:** terms used *inside* the Basket BC. Cross-BC terms (Product, Order, Stock, etc.) are defined in the owning BC's glossary. This file is merged into `docs/eshop-ubiquitous-language.md` during Stage 5 synthesis.
> **Rule of thumb:** if the Ordering or Catalog BC uses the same word differently, that is a signal — not a contradiction to fix here, but a boundary to respect.

---

## Terms

### Basket
The single aggregate root of this BC. A per-user, ephemeral shopping session identified by `UserId` and stored in Redis. A Basket is **not** an Order — it has no billing, no shipping, no status machine, and does not persist past checkout. It exists to collect the user's intent before that intent is committed to Ordering via the Checkout Saga. *Distinct from:* "Cart" in external catalog systems (same idea, different word — we use "Basket" per Fowler/Evans convention to avoid confusion with ASP.NET `Cart` patterns).

### BasketItem
A single line in a Basket: the triple `(ProductId, ProductSnapshot, Quantity)`. A value object, not an entity — two `BasketItem` instances with identical fields are interchangeable. Adding a product that is already in the basket **does not** create a second `BasketItem`; it increases the existing line's `Quantity`. *Distinct from:* `OrderLine` in Ordering, which carries richer fulfillment state.

### ProductSnapshot
A frozen, point-in-time copy of the Catalog data that Basket cares about: `Sku`, `Name`, `Price`, `CapturedAtUtc`. Created by the ACL at `AddItem` time (and replaced wholesale by `RefreshPrices`). It is **not** a live reference — if Catalog changes a product's price or name, existing snapshots in baskets do not move. This is the **frozen-pricing contract**. *Distinct from:* Catalog's `Product` aggregate (the authority) and Catalog's `CatalogProductResponse` DTO (the wire shape — never visible inside Basket).

### BasketTotal
The computed sum `Σ(item.Snapshot.Price × item.Quantity)` across all items in the basket. A value object. Not stored — projected on demand. Empty basket yields `Money.Zero(defaultCurrency)`, but note that an empty basket cannot be checked out, so a zero total never appears in a `BasketCheckoutInitiatedEvent`.

### Money
A `(decimal Amount, string Currency)` pair, currency as ISO 4217. Defined in `Platform.SharedKernel` (shared kernel) — the one value object Basket shares with Catalog and Ordering by design. Arithmetic across different currencies throws `DataIntegrityException` (not a `Result.Fail`): mixed-currency math is a bug, not a user error.

### Frozen-pricing contract
The explicit rule that once a `ProductSnapshot` is placed in a basket, its price does not change until the user explicitly triggers `RefreshBasketPricesCommand`. Automatic refresh on Catalog price changes is **deliberately not implemented** — it would make checkout totals unpredictable for the user. Stale snapshots are the user's responsibility to resolve; checkout commits to whatever is currently in the basket.

### Checkout (Basket verb)
The terminal, one-way transition of a basket: from "exists in Redis" to "gone forever." Triggered by `CheckoutBasketCommand`. Atomically writes `BasketCheckoutInitiatedEvent` to the PostgreSQL outbox and then deletes the Redis entry. Cannot be reversed — if the user cancels later, that path belongs to Ordering, not Basket. *Distinct from:* the Checkout **Saga**, which is the orchestrator downstream; Basket's "checkout" is just the ignition.

### BasketCorrelationId
A fresh `Guid` generated when `CheckoutBasketCommand` is invoked. It becomes the `BasketCheckoutInitiatedEvent.BasketCorrelationId` field and, in turn, the `CorrelationId` of the Checkout Saga state machine. This ID is how every downstream step (order creation, stock reservation, payment, confirmation) is tied back to this one checkout attempt.

### Version (Basket)
An `int` on the Basket aggregate, starting at 0 on creation and incremented on every successful save. Used for optimistic concurrency: the repository's `SaveAsync(basket, expectedVersion)` compares and fails with `BasketConcurrencyError` on mismatch. *Distinct from:* Avro schema version (external event contract evolution) and from an Inventory event-store version (a log position).

### Basket expiry
The silent purge of a basket after 30 days of no mutation, implemented via Redis TTL. No event is emitted. No cart-abandonment ping is triggered in v1 — the feature is deferred. Expiry is the answer to "what if the user just walks away?" — do nothing; let Redis handle it.

### Catalog Unavailable (error)
The `BasketErrors.CatalogUnavailable` result-error returned by the ACL when the Catalog service cannot be reached or responds with a 5xx. Commands that depend on the ACL (`AddItem`, `RefreshPrices`) fail the user's request on this error. Checkout does **not** depend on the ACL and is therefore immune to Catalog outages.

### ACL (Anti-Corruption Layer) — for Basket
The `IProductCatalogQueryPort` port in `Basket.Application.Abstractions` and its `ProductCatalogHttpAdapter` implementation in `Basket.Infrastructure.ExternalServices`. Together they translate Catalog's transport DTO (`CatalogProductResponse`) into Basket's internal value object (`ProductSnapshot`) and classify HTTP failures into Basket-meaningful `Result` errors. The ACL is the only place in the solution where Catalog's shape meets Basket's shape.

### Redis-backed aggregate
The architectural pattern where an aggregate's state lives in a distributed cache (Redis via FusionCache with MemoryPack serialization) rather than in a relational table. For Basket, this is the primary storage — there is no SQL state table for the aggregate. The PostgreSQL `basket` schema holds only the outbox and inbox tables.

### Outbox side-car
The minimal relational footprint of this BC: the `basket.outbox_messages` and `basket.inbox_messages` tables in PostgreSQL. Called a "side-car" because SQL plays a supporting role (publication guarantees) rather than the primary role (aggregate storage). This term captures the asymmetry: Redis holds the thing; Postgres holds the promise.
