# ADR-0036: Shared-Kernel Value Objects (`Money`, `Address`)

## Status

Accepted (2026-07-24)

## Context

DotNetAtlas is a multi-BC solution (Catalog, Basket, Ordering, Inventory, Invoicing, Notifications, Payments) plus a centralized Checkout saga. Two value-object concepts are needed — in an identical shape — by more than one BC:

- **`Money`** (`decimal Amount` + a `CurrencyCode` ISO 4217 currency) — Catalog prices a `Product`, Basket snapshots price into `ProductSnapshot`, Ordering freezes `OrderItem.UnitPrice`, Invoicing lines and Payments transactions carry amounts. The checkout money-chain (basket total → order total → invoice total → payment amount) is only consistent if every BC agrees on the decimal+currency representation. `Money`'s currency dimension is the `CurrencyCode` `SmartEnum` (a curated, closed ISO 4217 set) it is composed of.
- **`Address`** (postal address, ISO 3166-1 alpha-2 country) — collected at checkout, ferried by Basket ([ADR-0005](0005-customer-data-in-ordering.md)), re-snapshotted by Ordering (shipping + billing), and carried onto Invoicing. Same shape end-to-end.

The default DDD stance is that each bounded context owns its own value objects; a **shared kernel** is a deliberate, tightly-governed exception (Evans), because every shared type couples the contexts that depend on it. This ADR settles whether these two universal concepts justify promotion, and — more importantly — the **criterion** that keeps the kernel from growing into a dumping ground.

This decision was taken in Wave 0 and is **already implemented** (`Money` and `Address` live in `platform/Platform.SharedKernel/ValueObjects/`); it predates its ADR. This record captures the decision and is the anchor the two types' shared-kernel citations reference.

## Decision Drivers (ranked)

1. **Cross-BC representational consistency on the critical path** — the money-chain and address mapping span every commerce BC; divergent `Money` or `Address` shapes make cross-BC totals or DTO mapping inconsistent or lossy.
2. **Single source of validation** — one currency-format / ISO-country rule, not N per-BC copies that drift apart.
3. **Minimal kernel** — every shared type is a coupling point; only concepts with zero BC-specific semantics may be promoted, so the kernel stays a small, stable, low-churn core.
4. **Alignment with the Avro wire contracts** — money already crosses the wire as `(bytes decimal, string currency)` and the checkout address as a fixed record; a shared .NET type is the natural counterpart on both publisher and consumer.

## Considered Options

### Option 1: Promote genuinely-universal VOs to the shared kernel (chosen)

`Money` and `Address` live once in `Platform.SharedKernel`; every BC references them directly. Anything with BC-specific semantics stays BC-local.

### Option 2: Duplicate each VO per BC

Pure context isolation — each BC defines its own `Money` / `Address`. No shared-kernel coupling, but currency and country-code validation is copy-pasted into every BC and mapped at every boundary.

### Option 3: Shared wire-contract DTOs only

Keep VOs per-BC but share a thin DTO library at the messaging/HTTP boundary; map DTO↔VO at each edge. The shared surface is DTOs, not domain types.

## Evaluation Matrix

| Driver (ranked) | Opt 1: Shared kernel | Opt 2: Duplicate per BC | Opt 3: Shared DTOs only |
|---|---|---|---|
| 1. Cross-BC consistency | One representation, guaranteed | Drifts silently across copies | Consistent on the wire, re-diverges in each domain |
| 2. Single validation | One rule | N copies, drift-prone | Validation still duplicated per BC VO |
| 3. Minimal kernel | Kept minimal by the criterion | No kernel (and no reuse) | No VO kernel; a DTO layer instead |
| 4. Avro alignment | Direct .NET counterpart | Manual re-derivation per BC | DTO maps, VO still per-BC |

## Decision

Promote the **`Money`** value object (together with **`CurrencyCode`**, the ISO 4217 currency `SmartEnum` it is composed of) and the **`Address`** value object to `platform/Platform.SharedKernel/ValueObjects/`. `Money` and `Address` are immutable records, self-validating via `Create(...) : Result<T>`, with **no BC-specific semantics**. `CurrencyCode` is promoted as `Money`'s constituent, not as an independent candidate — a shared `Money` is meaningless without a shared currency type — and is a closed enumeration rather than a validated free VO.

**Promotion criterion** — a value object earns *independent* shared-kernel promotion only when *all* hold:

- it is a **universal, ubiquitous concept** identical across BCs (no per-context meaning);
- its validation is **self-contained** and would otherwise be duplicated;
- it is consumed by **≥2 BCs** on a shared path (contract math, cross-BC mapping);
- it carries **no aggregate-level invariant** — those stay in the owning aggregate.

The last clause is the boundary that keeps the kernel honest. `Money` is deliberately **permissive on sign** — a credit-note line is legitimately negative; each holding aggregate enforces its own positivity rule (`Product.Price > 0`, `OrderItem.UnitPrice > 0`, `CreditNoteLine < 0`). Sign is not `Money`'s concern *precisely because* it is BC-specific. Any candidate carrying that kind of rule stays out of the kernel.

## Rationale

Option 1 is the only one that satisfies drivers 1–2 without paying them back elsewhere: one representation, one validation. Option 2 trades all reuse for an isolation the domain does not need — `Money` and `Address` have no divergent per-BC meaning, so duplication buys only drift risk. Option 3 keeps the wire consistent but lets each domain re-diverge behind the DTO boundary, and still duplicates validation. Driver 3 is honored by the explicit criterion: the kernel holds a curated handful by design, not a grab-bag — a candidate that carries an aggregate invariant (or any per-BC meaning) is rejected, so the kernel stays a stable core rather than a magnet.

## Consequences

### Positive

- Cross-BC money-chain and address mapping share one representation — no drift, no per-edge remap of the core shape.
- Currency-format and ISO-country validation live once.
- The shared types are the natural .NET counterpart to the Avro money/address records on both publisher and consumer.

### Negative

- Every consuming BC is coupled to the shared-kernel package; a breaking change to `Money` / `Address` is a cross-cutting change. Mitigated: these are stable, low-churn concepts, and this is a reference solution where breaking changes are acceptable.
- The kernel is a standing temptation to over-share; the promotion criterion is the guardrail.

### Risks

- **Kernel creep** — a future author promotes a VO with BC-specific semantics "because it's reused." Mitigate with the four-part criterion: a VO that carries an aggregate invariant is the tell to keep it local.

## Implementation Notes

- `Money` — `platform/Platform.SharedKernel/ValueObjects/Money.cs`; pricing-specific rationale in [ADR-0002](0002-pricing-in-catalog.md) and [basket.md §3.4](../bc-design/basket.md). Permissive on sign; positivity enforced per aggregate.
- `CurrencyCode` — `platform/Platform.SharedKernel/ValueObjects/CurrencyCode.cs`; a closed `SmartEnum` of ISO 4217 codes, promoted as `Money.Currency`'s type. BCs needing more codes extend the list.
- `Address` — `platform/Platform.SharedKernel/ValueObjects/Address.cs`; ISO 3166-1 alpha-2 country, self-validating; ferried by Basket at checkout ([ADR-0005](0005-customer-data-in-ordering.md)), re-snapshotted by Ordering, carried by Invoicing.
- Everything else stays in its owning BC.

## Related Decisions

- [ADR-0002: Pricing inside Catalog](0002-pricing-in-catalog.md) — establishes `Money` as shared-kernel for the pricing path; this ADR generalizes the promotion rule and adds `Address`.
- [ADR-0005: Customer Data in Ordering](0005-customer-data-in-ordering.md) — `Address` is a courier field ferried through Basket; Ordering owns the persisted copy.
