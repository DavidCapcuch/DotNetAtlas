# ADR-0022: Specification pattern adoption criteria

## Status

Accepted (2026-05-29)

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
   `ByCorrelationId` (saga idempotency), `OverdueInvoices`, `ActiveReservationsForProduct` — **not**
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
| **Ordering** | Keep `OrderByCorrelationIdSpec`. Delete `OrderByIdSpec` → inline at 7 saga handlers (no tag). | `ByCorrelationId` is a business name (saga idempotency, criterion 1); `ById` is a pure PK lookup over an owned-child aggregate. |
| **Invoicing** | Delete `InvoiceByIdSpec` → inline at `ResendInvoiceCommandHandler`. No new specs. | The spec's `.Include`s were redundant (owned types auto-load) and unread; it was a PK lookup. |
| **Payments** | Migrate off `IPaymentRepository`: add `PaymentByCorrelationIdSpec`; inline the PK + read-side lookups; delete `IPaymentRepository` + `PaymentRepository`. | `GetByCorrelationId` (Capture/Void/RequestRefund) is saga idempotency (criterion 1); `GetById` is PK; the read methods are read-side. |
| **Catalog** | No specs. | Cycle detection lives in `ICategoryAncestryService` (an algorithmic graph walk, not a query predicate); the category-subtree load is a read-side query handler; product lookups are PK. |
| **Inventory** | No specs. | Command handlers are event-sourced (`IEventStore`, no EF aggregate load). The only EF query — the expiry-worker scan — targets `ReservationAuditRow`, an Application-layer read model: a spec over it would have no Domain home and is read-side (it already projects `.Select(...)`), so it stays inline. |
| **Basket** | No specs. | Redis-primary aggregate; the `IBasketRepository` abstraction is the correct boundary. |
| **Notifications** | No specs. | Worker BC with no aggregate loads. |

**Net change:** deleted 2 specs (`OrderByIdSpec`, `InvoiceByIdSpec`), kept 1
(`OrderByCorrelationIdSpec`), created 1 (`PaymentByCorrelationIdSpec`), deleted the
`IPaymentRepository`/`PaymentRepository` pair.

## Examples

- **Good spec** —
  [`OrderByCorrelationIdSpec`](../../services/Ordering/Ordering.Domain/Orders/Specifications/OrderByCorrelationIdSpec.cs)
  and the new
  [`PaymentByCorrelationIdSpec`](../../services/Payments/Payments.Domain/Transactions/Specifications/PaymentByCorrelationIdSpec.cs):
  business-named, write-side, `TagWith`-tagged for saga-replay observability.
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
  `Payments.Domain` (it was the only Domain project missing it) so `PaymentByCorrelationIdSpec`
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
