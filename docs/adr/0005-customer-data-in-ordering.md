# ADR-0005: Customer Data in Ordering (No Accounts BC in v1)

## Status

Accepted (2026-04-18)

## Context

Every Order in the eShop requires customer data that is not part of the Basket:
a shipping address, a billing address, and a selected payment method. In a
mature eShop, this data lives in a dedicated **Accounts / Customer** bounded
context that owns user profiles, saved addresses, saved payment methods,
preferences, and marketing opt-ins. Ordering would then reference a
`CustomerId` and snapshot the chosen fields at order time.

For the v1 reference solution we already introduce four new bounded contexts —
Catalog, Basket, Ordering, Inventory — alongside the existing Payments and
Notifications services. Adding a fifth BC for Accounts would expand the
learning surface without teaching a fundamentally new pattern; Customer CRUD
is a well-understood shape and would not introduce a showcase we don't already
have elsewhere (Catalog already demonstrates CRUD + read projections over SQL).

There are three complicating factors:

- **Identity already has an owner.** Keycloak is wired in `docker-compose.yaml`
  and is the authentication source of truth. It issues the JWT whose `sub`
  claim is the stable `UserId` every BC consumes. Introducing an Accounts BC
  creates an ambiguity: "does Keycloak own the user, or does Accounts own the
  user?" We want the answer to stay unambiguous — Keycloak owns identity;
  eShop BCs do not own user profile in v1.
- **Basket intentionally does not carry addresses.** Per
  `docs/bc-design/basket.md`, the Basket aggregate is a pre-checkout session
  keyed by `UserId` with only `Items`, `Version`, and timestamps. Addresses
  and payment-method selection are explicitly not part of the Basket. That
  means someone else has to collect and carry that data from the client into
  the `CreateOrderCommand`.
- **Order is the authoritative record.** For audit, dispute, reprint, refund,
  and fulfillment, the address and payment-method that matter are the ones
  as of the moment the buyer committed — not the buyer's *current* profile
  state. A post-order address change on a profile must not mutate historical
  orders.

The decision affects the Order aggregate design (snapshot fields vs ID
references), the API surface (which service owns customer CRUD), and
operational scope (one more service and DB or not).

## Decision Drivers (ranked)

1. **Minimize v1 BC count** — four new BCs is already a substantial teaching
   load; adding Accounts expands the blast radius without introducing a
   pattern not already shown elsewhere.
2. **Preserve identity ownership in Keycloak** — an Accounts BC that
   duplicates user data would muddy the "Keycloak is the identity authority"
   invariant that every other eShop BC relies on.
3. **Order is the authoritative checkout record** — for audit, dispute,
   reprint, and fulfillment, the snapshot fields on Order are what matter,
   not current profile state.
4. **BFF is the natural data-collection point** — the BFF already aggregates
   responses from services; it is the correct place to collect
   address/payment-method input from the client and package it into
   `CreateOrderCommand`.
5. **Keep the upgrade path open** — if v2 adds an address book ("my saved
   addresses"), introducing an Accounts BC is a later refactor; Order's
   snapshot fields remain valid unchanged because they represent the
   commitment-at-checkout.

## Considered Options

### Option 1: New Accounts bounded context (deferred to v2+)

Introduce a new service `services/Accounts/` with a `Customer` aggregate
holding `UserId`, `Addresses[]`, `PaymentMethods[]`, and preferences.
Ordering references Customer via `CustomerId` and takes a snapshot at order
time. Accounts owns CRUD for addresses and saved payment methods; Ordering
is a pure consumer at checkout. The BFF calls Accounts to hydrate the
checkout form (saved addresses, default payment method), then forwards the
buyer's selection into `CreateOrderCommand`.

- **Pros:** Realistic eShop modeling. Addresses are reusable across orders;
  address-book UX becomes straightforward; aligns with how production eShops
  structure customer profile ownership. Marketing/opt-in state has a natural
  home.
- **Cons:** Another BC, another DB schema, another API surface, another set
  of integration tests, another outbox relay — without teaching a pattern we
  don't already show. Creates an ownership ambiguity with Keycloak for the
  core `UserId` concept. Meaningfully expands v1 scope for a modest UX gain.

### Option 2: Customer data snapshotted into Order at creation (chosen)

The client collects address and payment-method selection at checkout. The
BFF packages that input together with the basket `CorrelationId` into
`CreateOrderCommand` that the Ordering service receives. The Order
aggregate holds `ShippingAddress` and `BillingAddress` as value objects,
immutable after creation. There is no Customer aggregate. Identity
continues to come from the Keycloak JWT (`sub` claim → `BuyerId`).

- **Pros:** One fewer service and DB; Order is fully self-contained (no FK
  chasing for audit); identity ownership stays with Keycloak; BFF's
  value-add (aggregating client input) is visible and tested; the teaching
  point "not every data bundle deserves a BC" is made concrete.
- **Cons:** No reusable address book in v1; buyers re-enter address on every
  order (a UX limitation, not an architectural one).

### Option 3: Hybrid — thin Accounts BC

An `Accounts` service with a read-only `Customer.GetAddresses()` query only
(no aggregate writes — addresses would be stored as Keycloak user
attributes, with Accounts as a thin projection over Keycloak's Admin API).

- **Pros:** Some address reuse without a full write-side aggregate. Keeps
  writes at Keycloak, so the "identity authority" answer technically holds.
- **Cons:** Bleeds identity concerns into a second BC; the "where is an
  address stored" answer becomes "Keycloak, projected via Accounts,
  snapshotted onto Order" — a three-hop mental model for a trivial data
  bundle. Unclear ownership; any write path would eventually need to re-open
  the Accounts-writes-too question. Forces a Keycloak Admin-API dependency
  that no other BC needs.

## Evaluation Matrix

| Driver (ranked)                               | Option 1: Accounts BC               | Option 2: Snapshot in Order        | Option 3: Hybrid (thin Accounts)     |
|-----------------------------------------------|-------------------------------------|------------------------------------|--------------------------------------|
| 1. Minimize v1 BC count                       | ❌ New BC, new DB, new API           | ✅ No new service                   | ⚠️ New service, thin but real        |
| 2. Identity ownership stays in Keycloak       | ❌ Creates ambiguity                 | ✅ Keycloak is sole source          | ⚠️ Straddles Keycloak + Accounts     |
| 3. Order is the authoritative checkout record | ✅ Snapshot still lives on Order     | ✅ Snapshot is the whole story      | ✅ Snapshot still lives on Order      |
| 4. BFF is the natural data-collection point   | ⚠️ BFF calls Accounts then Ordering  | ✅ BFF aggregates direct input      | ⚠️ BFF reads via Accounts            |
| 5. Upgrade path to address book               | ✅ Already the endpoint              | ✅ Clean later addition             | ⚠️ Writes would need new design      |

## Decision

We will use **Option 2: Customer data snapshotted into Order**, with
**Keycloak as the sole identity authority**. No Accounts bounded context in
v1.

## Rationale

**Identity stays cleanly separated.** Keycloak owns `UserId` (the `sub`
claim on every JWT); every eShop BC consumes the claim and never claims
ownership over user identity. Introducing an Accounts BC in v1 would
create a duplicate "who owns the user" question that we would have to
answer every time someone asks where to put a new per-user field. The
cleanest answer — "identity is Keycloak; everything else is per-BC and
scoped to its own aggregate" — is preserved only by *not* adding
Accounts. Keycloak already stores enough per-user identity state
(`sub`, email, locale) for v1; there is nothing missing that would force
an Accounts BC into existence today.

**Order's snapshot fields are what audit, dispute, and fulfillment
actually need.** When a buyer disputes a charge or a warehouse prints a
pick list, the address that matters is the one the buyer committed to at
checkout — not the address on their current profile. By storing
`ShippingAddress` and `BillingAddress` as immutable value objects inside
Order, we make that commitment explicit and tamper-evident (no "customer
updated their profile and the old order moved" surprise). Even if we add
an Accounts BC in v2, Order will still snapshot these fields — the VOs
don't go away, they just gain a `CustomerId` next to `BuyerId` as a
provenance breadcrumb. The snapshot fields are a v2-safe investment.

**The BFF already has the right responsibility.** The BFF's job is to
collect frontend input and compose commands to internal services. Address
+ payment-method selection is exactly the kind of form data the BFF is
positioned to gather from the client and forward. Hiding that collection
behind an Accounts BC would bury the pattern we're trying to showcase —
that the BFF is a composition + collection layer, not a pass-through.
The teaching point is worth stating plainly: **not every bundle of
related data deserves its own bounded context; sometimes it's a form
submission captured on the aggregate that needs it.**

## Consequences

### Positive

- Simpler v1 architecture — one fewer service, one fewer database schema,
  one fewer outbox relay, one fewer set of integration tests
- Order is fully self-contained — the aggregate carries everything needed
  for audit, dispute, and fulfillment without cross-service lookups
- Keycloak remains the single identity source; "who owns the user?" has
  exactly one answer across all BCs
- BFF's responsibility as a form-data aggregator is visible and exercised
  in the reference solution
- The `ShippingAddress` / `BillingAddress` value objects inside Ordering
  are usable unchanged if Accounts is added in v2
- Payments's existing ownership of saved payment methods is unchanged; no
  duplication of the "saved cards" concept

### Negative

- **No reusable address book** — buyers re-enter address on every order.
  This is a UX limitation, not an architectural one. Clients may mitigate
  with LocalStorage caching on the frontend (out of scope for v1).
- **No "saved cards" management endpoint owned by eShop** — the *vault*
  of saved payment methods already lives in the existing Payments service.
  Buyers who want to manage saved cards go to a Payments endpoint, not an
  Accounts endpoint. Order references `PaymentMethodId` into Payments;
  Payments-reuse is unchanged by this decision.
- **Harder to express "buyer preferences"** (e.g., default shipping
  carrier, marketing opt-in, preferred currency) — v1 simply does not
  model them; v2 would introduce Accounts if needed. A feature request
  for "remember my last address" is an explicit v2 signal.

### Risks

- **Feature pressure to add an address book** — stakeholders may quickly
  ask for "my saved addresses." Mitigation: this ADR is explicit that
  the upgrade path is to create a thin Accounts BC as a later refactor
  (a CRUD service over Keycloak + its own DB for addresses only);
  Order's snapshot fields remain valid. A later ADR will formalize that
  design.
- **Duplicate address entry across repeated purchases** — a minor UX
  friction, acceptable in a v1 reference solution whose teaching goal is
  architectural, not conversion-rate optimization.
- **GDPR / privacy: per-order PII** — Order stores a customer's shipping
  address per-order. Deletion-of-personal-data requests must redact
  Order snapshots (and their event log entries) on request. This
  complexity is the same whether or not an Accounts BC exists — an
  Accounts deletion still leaves historic Orders holding the snapshotted
  PII; the redaction work would still happen at the Order level. Not
  worse than the alternative.
- **Client-side data loss** — if a buyer abandons checkout before the
  BFF forwards `CreateOrderCommand`, their entered address is lost
  (unless the client persists it locally). Acceptable trade-off for
  v1 — the alternative (persisting half-entered checkout forms in a
  session store) creates its own GDPR surface.

## Implementation Notes

- **`ShippingAddress` and `BillingAddress`** are immutable value objects
  inside the Order aggregate. Fields: `Street1` (required, max 200 chars),
  `Street2` (nullable, max 200 chars), `City` (required, max 100 chars),
  `State` (nullable — countries without states omit it, max 100 chars),
  `PostalCode` (required, max 20 chars), `CountryCode` (ISO 3166-1
  alpha-2, exactly 2 uppercase letters). Factory is `Address.Create(...)
  → Result<Address>` with structural validation; no country whitelist in
  v1.
- **`BuyerId`** on the Order aggregate is the Keycloak user `sub` claim, captured at creation (immutable — see `docs/bc-design/ordering.md` invariant I-4). `BuyerId` never leaves Ordering's boundary for profile lookup — it is an identity pointer only, opaque to every BC except for row-level authorization ("does this JWT's `sub` equal the order's `BuyerId`?").
- **`PaymentMethodId`** on the Order aggregate is a `Guid` reference to a payment method managed by the existing **Payments** service. Ordering treats it as opaque; no change to Payments is required. Buyers who want to add, list, or remove saved payment methods use Payments's endpoints, not an Accounts endpoint. This preserves the existing Payments-as-payment-vault pattern unchanged.
- **`CreateOrderCommand`** (saga → Ordering, per [ADR-0004](0004-checkout-saga-topology.md)) carries: `CorrelationId`, `BuyerId`, `Items[]`, `ShippingAddress`, `BillingAddress`, `PaymentMethodId`, and `RequestedAtUtc`. The BFF is responsible for collecting address + payment-method input from the client and for populating these fields before handing the command off through the saga starting point. The BFF validates structurally (non-empty, length, ISO country format) before forwarding; Ordering's `Address.Create` re-validates on the aggregate side as defense-in-depth.
- **GDPR / privacy redaction path** — to redact a user's orders, operators run a scoped update: `UPDATE ordering.orders SET shipping_address = '<redacted>', billing_address = '<redacted>' WHERE buyer_id = ?`. The Kafka event log retains the original event for audit lineage. For full redaction semantics, a compensating redaction-tombstone table may be needed in v2 to ensure projections downstream of the event log also drop the data. This work is out of v1 scope but is called out here so maintainers see the load-bearing assumption.
- **Architecture tests** must continue to enforce that Ordering does not depend on any Accounts-shaped library; the `BuyerId` contract is a raw `Guid` derived from the JWT and nothing else. If v2 introduces Accounts, the architecture tests will need an explicit allowance and a matching ADR.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — orchestration lives in the saga service; Ordering is a command responder that consumes `CreateOrderCommand` with the customer snapshot already populated.
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — `CreateOrderCommand` carries the customer snapshot (addresses + payment method) into Ordering; the saga does not fetch customer data from an Accounts BC because none exists.
- Future: **ADR-XXXX — Accounts bounded context** (placeholder; to be authored if/when v2 introduces an address book, saved preferences, or marketing opt-in surface that forces a dedicated aggregate).
