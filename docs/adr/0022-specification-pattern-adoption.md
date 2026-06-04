# ADR-0022: Specification pattern adoption criteria

## Status

Accepted (2026-05-29)

> **Amended (2026-06-01):** added the "Naming write-side load abstractions" section below — a
> shape-conditional naming rule refining criterion 1. The shaping rule is documentation-only and
> inert until a non-owned aggregate child exists (the trigger criterion 2 shares); the
> `*Spec` suffix it references is now enforced by a `SpecificationTests` NetArchTest in every
> spec-framework BC (Basket exempt — see the Enforcement note below).
>
> **Amended (2026-06-03):** [ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md) re-keyed
> the saga on `OrderId`. The business-named saga-idempotency spec is keyed on `OrderId`
> (`PaymentByOrderIdSpec`); `CreateOrderCommandHandler` idempotency is an inline `OrderId`
> primary-key lookup (no spec). The per-BC table and examples below reflect this.

Complements [ADR-0021](0021-read-side-no-specifications.md), which governs the **read** side
("query handlers must not depend on `Ardalis.Specification`"). This ADR governs the **write**
side and the cleanup of specs that did not earn their keep: *when* does loading an aggregate for
mutation justify an `Ardalis.Specification` class versus inline LINQ?

## Context

`Ardalis.Specification` was adopted unevenly across the `services/*` bounded contexts:

- **Ordering** went all-in — every saga command handler loaded the aggregate via a spec.
- **Invoicing** had a single spec (`InvoiceByIdSpec`).
- **Payments** used a hand-rolled `IPaymentRepository` instead of specs.
- Several specs were pure ceremony: `OrderByIdSpec` and `InvoiceByIdSpec` were
  `Where(x => x.Id == id)` lookups with no business meaning.

One EF Core fact reframes the whole question. **Every aggregate child collection in `services/*`
is mapped as an EF owned type** (`builder.OwnsMany(...)`): `Order.Items`, `Invoice.Lines`,
`Invoice.VatLines`, `CreditNote.Lines`. Owned types are **loaded automatically with their owner**
— you never write `.Include()` for them and you cannot under-load them. Consequently:

- A spec's "fixed include-shape" value (the consistency-boundary argument) is **real only for
  aggregates with non-owned `HasMany`/`HasOne` children**. No aggregate in `services/*` has one
  today, so "encodes the include-shape" justifies **zero** specs at present.
- `InvoiceByIdSpec`'s `.Include(Lines).Include(VatLines)` was both **redundant** (owned →
  auto-loaded) and **dead** (`ResendInvoiceCommandHandler` only reads `Status`). It was therefore
  indistinguishable from `OrderByIdSpec`: a tagged primary-key lookup.

## Decision

A write-side aggregate load is spec-worthy when **at least one** of these holds:

1. **Business-named predicate** — the filter has a domain name, not a structural one:
   `ByOrderId` (saga idempotency), `OverdueInvoices`, `ActiveReservationsForProduct` — **not**
   `ById`. A single call site is acceptable when the name carries ubiquitous-language value.
2. **Fixed include-shape for a `HasMany`/`HasOne` aggregate** — where `.Include(...)` is genuinely
   required to materialise the consistency boundary. (Owned-type children auto-load, so this
   criterion is inert for every `services/*` aggregate today; it is stated for the day a
   non-owned child is introduced.)
3. **Predicate composition** — AND/OR/NOT of business rules that benefit from a single named,
   testable definition.

A load is **not** spec-worthy — use inline LINQ — when:

- It is a **pure primary-key/ID equality lookup**: `.Where(x => x.Id == id).FirstOrDefaultAsync(ct)`
  or `FindAsync(id)`, with **no `TagWith`** (a `WHERE id = @p0` query is self-evident in logs).
- It is a **read-side projection** — query handlers *and* background-worker scans over read-model
  tables — which stays inline `.Select(...)` per ADR-0021.
- The spec would have **no Domain home** because its target type lives in the Application or
  Infrastructure layer (e.g. a read-model row). That placement is itself the signal that the load
  is read-side and should not be a spec.

`TagWith(nameof(Spec))` is reserved for business-named specs, where grepping the SQL log by spec
name (e.g. tracing a saga's idempotency check under Kafka redelivery) pays for itself.

## Per-BC decisions

| BC | Outcome | Why |
|----|---------|-----|
| **Ordering** | Delete `OrderByIdSpec` → inline at 7 saga handlers (no tag); `CreateOrderCommandHandler` idempotency is an inline `OrderId` PK lookup (no spec). | `ById` is a pure PK lookup over an owned-child aggregate; the saga-idempotency check is a single PK lookup on the pre-assigned `OrderId`, not worth a spec. |
| **Invoicing** | Delete `InvoiceByIdSpec` → inline at `ResendInvoiceCommandHandler`. No new specs. | The spec's `.Include`s were redundant (owned types auto-load) and unread; it was a PK lookup. |
| **Payments** | Migrate off `IPaymentRepository`: add `PaymentByOrderIdSpec`; inline the PK + read-side lookups; delete `IPaymentRepository` + `PaymentRepository`. | The saga-idempotency lookup (Capture/Void/RequestRefund) keyed on `OrderId` is criterion 1; `GetById` is PK; the read methods are read-side. |
| **Catalog** | No specs. | Cycle detection lives in `ICategoryAncestryService` (an algorithmic graph walk, not a query predicate); the category-subtree load is a read-side query handler; product lookups are PK. |
| **Inventory** | No specs. | Command handlers are event-sourced (`IEventStore`, no EF aggregate load). The only EF query — the expiry-worker scan — targets `ReservationAuditRow`, an Application-layer read model: a spec over it would have no Domain home and is read-side (it already projects `.Select(...)`), so it stays inline. |
| **Basket** | No specs. | Redis-primary aggregate; the `IBasketRepository` abstraction is the correct boundary. |
| **Notifications** | No specs. | Worker BC with no aggregate loads. |

**Net change:** deleted 2 specs (`OrderByIdSpec`, `InvoiceByIdSpec`), created 1
(`PaymentByOrderIdSpec`), deleted the `IPaymentRepository`/`PaymentRepository` pair.

## Naming write-side load abstractions

Criterion 1 rejects *structural* predicate names (`ById`) but does not settle how to name the
specs that survive. A second rule applies to every write-side load abstraction — an
`Ardalis.Specification` class first, but equally a repository finder
(`IBasketRepository.GetByUserIdAsync`) or a private `LoadX(...)` helper factored out of a command
handler.

**Name a write-side load for its use case when — and only when — its load *shape* (the
`.Include(...)` graph, filter composition, or tracking choice) is sized for one operation's
invariant checks.** A use-case name (`OrderForCancellationSpec`) commits the class to that shape,
so reusing it for a differently-shaped load surfaces as a naming mismatch at the import site — the
read/write coupling [ADR-0021](0021-read-side-no-specifications.md) corrects, caught one level
earlier. When two operations on one aggregate need different graphs — cancellation must load
shipments to test "can still cancel?", dispatch must load the delivery address —
`OrderForCancellationSpec` and `OrderForShipmentDispatchSpec` are two correctly-distinct loads;
naming both `OrderByIdSpec` would erase *which* shape you get.

**Keep the identity-predicate name (or inline per the rule above) when the load is a bare
aggregate-identity fetch with a single natural shape reused across operations.** A use-case name
there is either a lie — `PaymentByOrderIdSpec` resolves the aggregate for Capture *and* Void
(RequestRefund loads by primary key instead), so no single `ForX` name is honest — or pure duplication: N identical
`Where(x => x.Id == id)` specs differing only in name. `GetByUserIdAsync` loads the basket by its
identity for six command handlers; `ByOrderId` resolves the saga's aggregate by *its*
ubiquitous-language identity. Both correctly keep their names. Business *filter* names that already
carry domain meaning and return a set (`OverdueInvoices`, `ActiveReservationsForProduct`) are not
identity loads and are unaffected — there the predicate *is* the use case.

**This rule is inert today, for the same structural reason criterion 2 is.** The use-case-naming
branch only fires when an operation-specific `.Include(...)` graph exists — i.e. an aggregate with
a non-owned `HasMany`/`HasOne` child (criterion 2). Every `services/*` aggregate child is an owned
type that auto-loads (see Context), so no two write-side loads of the same aggregate diverge in
shape; every surviving load is a bare-identity or business-filter predicate that keeps its name.
The convention becomes operative the day a non-owned child is introduced — the same trigger that
activates criterion 2 — at which point the two loads sized for two operations take `ForUseCase`
names rather than a shared `ById`.

**Enforcement — two layers, deliberately split.** The `*Spec`/`*Specification` **suffix** is
mechanically checkable and pinned per-BC by a `SpecificationTests` NetArchTest
(`Specifications_Should_HaveNameEndingWith_Spec_Or_Specification`), present in every BC that
references the specification framework — it keeps specs greppable and guards the first spec a BC
adds, so it is kept even where a BC has none yet. (Basket is exempt: a Redis-primary BC with no
`Ardalis.Specification` dependency per [ADR-0003](0003-basket-as-technical-bc.md) — a spec cannot
appear there without first adding the package, which is the natural point to add the test.) The
**use-case-vs-predicate choice** above is **documentation-only** — a static rule cannot tell
whether a `ByX` name *should have been* `ForY` without knowing the include-graph divergence it
commits to. Both are distinct from the read-side `QueryHandlerSpecificationTests` that ADR-0021 and
this ADR removed: that asserted a *dependency* (query handlers must not reference the framework),
not a name.

## Examples

- **Good spec** —
  [`PaymentByOrderIdSpec`](../../services/Payments/Payments.Domain/Transactions/Specifications/PaymentByOrderIdSpec.cs):
  business-named (saga idempotency, keyed on `OrderId`), write-side, `TagWith`-tagged for
  saga-replay observability.
- **Cleanup result** — `OrderByIdSpec` and `InvoiceByIdSpec` are gone; their loads are now
  `.Where(x => x.Id == id).FirstOrDefaultAsync(ct)` inline at each call site. The owned child
  collections still materialise automatically, so correctness is unchanged.

## Consequences

### Positive

- One consistent, statable rule (business name → spec; PK → inline) replaces three ad-hoc styles.
- Removes ceremony: tagged PK-lookup specs no longer sit beside genuinely-useful business specs,
  so the surviving specs all signal "this predicate has a name worth knowing."
- Payments now matches the rest of `services/*`: command handlers load off the DbContext port
  directly, the read side is inline LINQ, and there is one fewer abstraction to maintain.

### Negative / accepted trade-offs

- Pure PK loads are now duplicated inline across call sites (7 in Ordering) rather than centralised.
  Accepted: the duplication is a one-line `.Where(o => o.Id == id)` and the `TagWith` it loses adds
  nothing to a PK query's log line.
- The `Ardalis.Specification.EntityFrameworkCore` package reference was added to
  `Payments.Domain` (it was the only Domain project missing it) so `PaymentByOrderIdSpec`
  can live in the Domain layer beside the aggregate, matching the other BCs.

### Enforcement boundary

ADR-0021 originally pinned its read-side rule with a per-BC `QueryHandlerSpecificationTests`
NetArchTest, present only in **Ordering** and **Invoicing** (a partial rollout). For a reference
solution that automated enforcement is overkill, so this change **removes** those two arch-tests
rather than extending them: the "specs are write-side only" rule now lives purely in ADR-0021 and
this ADR as a documented convention. A future contributor who wants CI enforcement can reinstate
the four-line fact across the BCs at that time.

## Related Decisions

- [ADR-0021: Ardalis.Specification forbidden on the CQRS read side](0021-read-side-no-specifications.md)
  — the read-side half of the same question; this ADR is its write-side complement.
- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md) — explains why
  Inventory has no EF aggregate loads to spec.
- [ADR-0011: PII Handling & GDPR](0011-pii-handling-gdpr.md) — the encrypted `*_enc` columns that
  make the Payments read handlers project after materialisation rather than SQL-side.
