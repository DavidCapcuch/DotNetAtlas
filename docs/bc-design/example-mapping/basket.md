# Basket — Example Mapping Sessions

> Format: Matt Wynne's Example Mapping (Story / Rules / Examples / Questions) with BDD Given/When/Verify/Then for Examples. Each session corresponds to one non-trivial business rule or invariant on the BC's aggregate. These sessions are the seed for executable acceptance-test specs (SpecFlow / Reqnroll) during implementation.
>
> Color legend (echoing the reference images):
> - 📖 Yellow = Story
> - 📐 Blue = Rule (business invariant)
> - 🌱 Green = Example ("The one where...")
> - ❓ Pink = Question (open issue)
> - 💬 White = Answer / Note (resolved question)

---

## Session 1: Price drift between add-time snapshot and checkout

### 📖 Story
As a **shopper** I want **the price I saw when adding an item to my basket to remain the price I pay at checkout** so that **I am never charged more than the amount I agreed to when I put the item in the basket**.

### 📐 Rules
- **R1** — `ProductSnapshot.Price` is frozen at the moment the item is added; the Basket aggregate has no mutator that changes a snapshot's price.
- **R2** — Basket does NOT subscribe to Catalog's `ProductPriceChangedEvent` and does NOT auto-refresh prices in v1 — stale prices are intentional and the frozen-pricing contract is the whole point.
- **R3** — The only way to replace snapshots with current Catalog prices is for the user to explicitly issue `RefreshBasketPricesCommand`, which calls the ACL in batch and raises `BasketPricesRefreshedDomainEvent` listing only items whose price actually changed.
- **R4** — The BFF's `/api/bff/basket` fetches the Basket snapshot AND current Catalog prices and surfaces the delta to the UI; the Basket aggregate itself is not involved in delta rendering.
- **R5** — `Checkout` commits to the snapshot prices currently in the basket — it does NOT re-call Catalog or re-validate prices; snapshot price is the legally binding amount passed to the saga.
- **R6** — A discontinued Catalog product does NOT retroactively block Basket — Basket operates on its own snapshot; downstream services (Inventory, Ordering, saga) decide whether the checkout can complete.

### 🌱 Examples

#### The one where the Catalog price went up mid-session

- **Given** user `U1` has a basket containing `P1` with `Snapshot.Price = $100` (captured at add-time) and Catalog's current price for `P1` is `$120`
- **When** user `U1` issues `CheckoutBasketCommand(UserId=U1, CorrelationId=C1, ShippingAddress, BillingAddress, PaymentMethodId)`
- **Verify** R1, R5
- **Then** `BasketCheckedOutDomainEvent` carries `UnitPriceAmount = 100` plus the three courier fields (`ShippingAddress`, `BillingAddress`, `PaymentMethodId`) stamped from the command, the external `BasketCheckoutInitiatedEvent` on `basket.sessions` carries `UnitPriceAmount = 100`, and the saga/Order line uses `$100` as the transacted price.

#### The one where the user explicitly refreshes

- **Given** user `U1` has a basket containing `P1` with `Snapshot.Price = $100` and Catalog's current price for `P1` is `$120`
- **When** user `U1` issues `RefreshBasketPricesCommand(UserId=U1)`
- **Verify** R3
- **Then** the ACL returns `ProductSnapshot(P1, Price=$120, ...)`, the basket's snapshot is replaced at quantity-preserving, and `BasketPricesRefreshedDomainEvent` lists `(P1, OldPrice=$100, NewPrice=$120)`.

#### The one where the product was discontinued after add

- **Given** user `U1` has a basket containing `P1` with `Snapshot.Price = $100` and Catalog has since raised `ProductDiscontinuedEvent(P1)`
- **When** user `U1` issues `CheckoutBasketCommand(UserId=U1, CorrelationId=C2, ShippingAddress, BillingAddress, PaymentMethodId)`
- **Verify** R2, R5, R6
- **Then** Basket does NOT block the checkout — `BasketCheckoutInitiatedEvent` is emitted with the `$100` snapshot; the Checkout saga or Inventory downstream decides whether the order proceeds (Basket's contract ends at initiation).

### ❓ Questions
*(None — v1 deliberately defers any auto-refresh or auto-block behavior; the frozen-pricing contract is an explicit design choice.)*

---

## Session 2: Basket expires after 30 days of inactivity

### 📖 Story
As a **platform operator** I want **abandoned baskets to be automatically purged after 30 days of inactivity** so that **Redis storage stays bounded, users who return after a month start fresh, and no ceremony is needed to track "zombie" sessions**.

### 📐 Rules
- **R1** — Every basket mutation sets the Redis key's TTL to 30 days (sliding window) — inactivity, not absolute age, triggers expiry.
- **R2** — On successful checkout the Redis key is explicitly DELETED, not merely TTL'd — post-checkout baskets must not be reachable.
- **R3** — Redis AOF persistence (`--appendonly yes`) preserves baskets across a Redis restart with a ≤ 1 s data-loss window; expired keys remain expired.
- **R4** — Under `allkeys-lru` + `maxmemory 256mb`, least-recently-used baskets can be evicted before 30 days under memory pressure — this is acceptable data loss for an ephemeral session BC (per ADR-0003).
- **R5** — A user acting on an expired/evicted basket does NOT revive the old one — the next `AddItemToBasketCommand` lazily creates a fresh Basket with `Version = 1` and raises a new `BasketCreatedDomainEvent`.
- **R6** — No event is emitted on expiry in v1 (silent abandonment — no marketing hook).

### 🌱 Examples

#### The one where the user returns within the window

- **Given** user `U1` created a basket and added `P1` 29 days ago, with no activity since
- **When** user `U1` issues `AddItemToBasketCommand(UserId=U1, ProductId=P2, Quantity=1)`
- **Verify** R1
- **Then** the existing basket is loaded (version N), `P2` is appended, the basket is saved at version N+1 with TTL reset to 30 days, and only `ItemAddedToBasketDomainEvent` is raised (no `BasketCreatedDomainEvent`).

#### The one where the user returns after 31 days

- **Given** user `U1` created a basket 31 days ago; Redis has already purged the `basket:U1` key
- **When** user `U1` issues `AddItemToBasketCommand(UserId=U1, ProductId=P1, Quantity=1)`
- **Verify** R5
- **Then** `GetByUserIdAsync(U1)` returns null, a new `Basket.Create(U1)` is invoked (fresh `Version = 0 → 1`), both `BasketCreatedDomainEvent` and `ItemAddedToBasketDomainEvent` are raised, and the key is written to Redis with a 30-day TTL.

#### The one where Redis is restarted while baskets are still active

- **Given** 1,000 active baskets exist in Redis with TTLs between 1 and 29 days remaining; AOF persistence is enabled
- **When** the Redis container restarts
- **Verify** R3
- **Then** on startup Redis replays its AOF log, restores all basket entries with their original TTLs (minus up to 1 s of lost writes), and subsequent `GetByUserIdAsync` calls hit the restored entries.

#### The one where Redis evicts under memory pressure

- **Given** Redis is at `maxmemory 256mb`, eviction policy `allkeys-lru`, and `U1`'s basket is the least-recently-used entry
- **When** a burst of new basket writes pushes Redis over the memory limit
- **Verify** R4, R5
- **Then** Redis evicts `basket:U1` before its 30-day TTL, and on `U1`'s next `AddItemToBasketCommand` a fresh basket is created (no revival, no user-visible error beyond "your items are gone") — this is documented acceptable behavior per ADR-0003.

### ❓ Questions
*(None — cart-abandonment re-engagement is explicitly deferred to v2+ per basket.md § 15.)*
