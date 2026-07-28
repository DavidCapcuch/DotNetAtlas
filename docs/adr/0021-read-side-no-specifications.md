# ADR-0021: Ardalis.Specification is forbidden on the CQRS read side

## Status

Accepted (2026-05-25)

> **Partially revisited by [ADR-0022](0022-specification-pattern-adoption.md) (2026-05-29).**
> ADR-0022 adds the write-side adoption criteria and, applying them, deleted the pure-PK specs
> `OrderByIdSpec` and `InvoiceByIdSpec` (now inlined at their call sites). The "Specs that survive"
> list below is updated accordingly; the read-side decision and rationale here are unchanged.
>
> **Enforcement removed (2026-05-29).** The per-BC `QueryHandlerSpecificationTests` NetArchTest
> mandated below (Ordering + Invoicing) was deleted as overkill for a reference solution. The
> read-side convention stands but is now documentation-only, not CI-enforced — references to the
> arch-test in the body below are historical.

## Context

The codebase uses `Ardalis.Specification` to factor reusable `Where / Include / OrderBy` predicates into single-purpose specification classes (e.g. `OrderByIdSpec` — since deleted; see the status note).
The original intent was DRY: command handlers that load an aggregate by id and read handlers that fetch the same aggregate for projection could share one spec.

Issue [#238](https://github.com/DavidCapcuch/DotNetAtlas/pull/238) measured what that sharing actually costs on the read side. The before-shape of
[GetOrdersByBuyerQueryHandler](../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs)
loaded `Order` aggregates via `WithSpecification(OrdersByBuyerSpec)` + `ToListAsync`, then ran an in-memory `OrderProjection.ToResponse` to flatten the result. Every non-projected column travelled to the client (`RowVersion`, all six audit timestamps, `StockReservationId`, `PaymentTransactionId`, the entire `OrderItem` collection with its `ProductSnapshot` owned type, etc.), and the EF Core change tracker materialised six owned tables per row. #238 replaced this with an inline `.Where(...).Select(...) → GetOrderByIdResponse` that projects SQL-side; only the columns the response uses traverse the wire, and the optional VOs (`Cancellation`, `Failure`, `Shipment`) translate cleanly under conditional projection (`o.Foo == null ? null : new FooDto(...)`) on EF Core 10.

The structural reason the conversion was easy: **Ardalis specifications encode `Where`, `Include`, `OrderBy`, and paging — but not `Select`.** The `Select` is what CQRS read paths need most, because it's the only operator that actually shapes the response. Once a handler is driven SQL-side, the spec becomes a thin filter wrapper that doesn't pay for the indirection.

There is also a model-purity argument independent of perf: the four invoice/credit-note query handlers that #277 also converts were sharing specs across read and write sides. `InvoiceByIdSpec` was consumed by both `GetInvoiceByIdQueryHandler` (read) and `ResendInvoiceCommandHandler` (write), with different `.Include` requirements being papered over by always including everything. This couples the read model to the write model against the spirit of CQRS — a change to one path's read needs forces a recompile of the other.

#277 rolls #238's pattern out across the codebase, deletes the orphaned read-side specs, and pins the rule with per-BC architecture tests so a future contributor cannot quietly reintroduce a spec on a query handler.

## Decision Drivers (ranked)

1. **Wire-cost transparency on the read path** — the projection shape is the load-bearing decision for a query handler. Hiding it behind a spec turns "what does this endpoint actually return?" from a one-screen reading into a multi-file trace.
2. **CQRS read/write decoupling** — the read model and write model must be free to evolve independently. A spec consumed by both creates an implicit contract that a change to either side can silently break.
3. **Code locality** — filter + ordering + paging + projection live in the same `Select(...)` chain when inlined. Splitting them across `Where`-in-a-spec + `Select`-in-a-handler scatters the read shape across two files.
4. **Arch-test enforceability** — `NotHaveDependencyOnAny("Ardalis.Specification")` scoped to `*QueryHandler` was a four-line static rule (the `QueryHandlerSpecificationTests` fact, since removed — see the status note). A "specs are only used carefully on read paths" rule is unenforceable in code review.
5. **DRY-on-read-side is overstated** — in practice, read-side `Where` predicates rarely repeat across handlers. The supposed sharing benefit of specs (a single source of truth for "find by id") does not survive contact with response-shaping requirements.

## Considered Options

### Option 1: Status quo — specs allowed on both read and write sides

Keep specs as the default lookup primitive for both query and command handlers; accept the wire-cost and read/write coupling.

- ➕ Zero migration cost; familiar pattern; some DRY when an aggregate is genuinely fetched by-id from many places.
- ➖ Read paths pay the materialisation tax and ship every column to the client.
- ➖ Read↔write spec sharing couples the two models; the `Include` graph becomes a lowest-common-denominator union.
- ➖ Adding a `Select` to a spec is awkward — `Ardalis.Specification` supports `.Select()` via `ISpecification<TEntity, TResult>`, but every spec then becomes typed to one response shape, defeating the reuse argument.

### Option 2: Inline LINQ on the read side; specs reserved for write-side aggregate loading (chosen)

Query handlers use `dbContext.<Aggregates>.AsNoTracking().Where(...).Select(...).FirstOrDefaultAsync()` directly. Command handlers continue to use `WithSpecification(new FooSpec(...))` for write-side aggregate loading where `Include`-graph reuse is real. The rule is pinned per-BC with a NetArchTest fact.

- ➕ Read shape (filter + projection + paging) is one screen of code per handler.
- ➖ Slightly more line-count per read handler (~30–50 lines for projecting a rich aggregate).
- ➕ SQL-side projection skips every unprojected column; no full-aggregate materialisation; no change-tracker overhead.
- ➕ Write-side keeps its DRY benefit unchanged.
- ➕ Arch-test makes the rule self-enforcing.

### Option 3: Per-query read-projection class (one mapper per query)

Keep the spec but route through a dedicated `*Projection` static class (`OrderProjection.ToResponse` was the pre-#238 shape). The handler does `WithSpecification + ToListAsync + .Select(Projection.ToResponse)`.

- ➕ The projection shape is centralised in one named place.
- ➖ The aggregate is still fully materialised; the projection runs in-memory after the round-trip. The wire cost identical to Option 1.
- ➖ The mapper becomes a parallel definition that drifts from the response DTO; #238's `GetOrdersByBuyerQueryHandler` carried a long comment explicitly noting "keep the two shapes in sync."

## Evaluation Matrix

| Driver (ranked) | Option 1 (status quo) | Option 2 (inline LINQ) | Option 3 (projection class) |
|---|---|---|---|
| 1. Wire-cost transparency | ❌ hidden behind spec | ✅ filter + projection co-located | ⚠️ projection co-located but still materialises full aggregate |
| 2. CQRS read/write decoupling | ❌ spec shared across sides | ✅ separate code paths | ⚠️ spec still shared |
| 3. Code locality | ❌ split across spec + handler | ✅ single `Select` chain | ⚠️ split across spec + mapper |
| 4. Arch-test enforceability | n/a — no rule to enforce | ✅ four-line static rule | ❌ no way to distinguish "good" spec from "bad" |
| 5. DRY on read side | ⚠️ rarely realised in practice | ⚠️ explicit per-handler `Where` (acceptable) | ⚠️ same as Option 1 |

## Decision

We will adopt **Option 2**:

> **CQRS query handlers (`*QueryHandler`) must not depend on `Ardalis.Specification`.** They use inline LINQ with SQL-side `Select` projection. `Ardalis.Specification` is reserved for command-handler aggregate loading and for read-stores in the infrastructure layer where the `Include` graph is genuinely shared.

The rule was originally pinned per-BC with a NetArchTest fact asserting `Types.That().HaveNameEndingWith("QueryHandler").Should().NotHaveDependencyOnAny("Ardalis.Specification")`. _(Those `QueryHandlerSpecificationTests` facts for Ordering and Invoicing were later removed as overkill — see the status note at the top. The convention is now documentation-only.)_

## Rationale

**#238 is the precedent**, not a one-off optimisation. The handler-by-handler audit done in #277 confirmed every Ordering and Invoicing read handler had the same pattern (`WithSpecification(...) + ToListAsync + InProjection.ToResponse`) and the same hidden wire cost. Generalising from "we did this once for performance" to "this is how reads work" is what an ADR is for.

**Specs are not the wrong primitive — they're a write-side primitive.** Command handlers fetching an aggregate to mutate it need the full `Include` graph; reusing the spec for that across handlers is real DRY. The seven write-side handlers that consume `OrderByIdSpec` (CancelOrder, ConfirmOrder, MarkOrder*) all need the same load shape; centralising it in the spec is the right call. Promoting specs to the read side blurs that line.

**The line-count cost is small and one-sided.** The five converted handlers in this issue grew by ≈ 30–50 lines each (the projection now lives in the handler instead of in a `Projection.ToResponse` mapper that was already there). The deleted specs return roughly the same number of lines to the codebase, plus three orphaned `*Projection` static classes are also deleted. Net change is approximately flat.

## Consequences

### Positive

- Read endpoints ship only the columns they return — no audit timestamps, `RowVersion`, or `StockReservationId` travelling on a `GET /orders/{id}` call.
- Read shape is one screen per handler; new contributors can read a handler top-to-bottom and know exactly what the API returns.
- Write side keeps `Ardalis.Specification`'s DRY benefit for `Include` graphs; the constraint is targeted, not blanket.
- Arch-test rule is self-enforcing — no review-time vigilance required.
- The pattern generalises cleanly to future BCs: a new BC that exposes a CQRS read path adds the same four-line arch-test fact.

### Negative

- Read handlers grew in line count where projection used to live in a separate `*Projection` static class. The shape of the response is now spread across the response DTO and the handler's `.Select(...)` instead of the DTO and the dedicated mapper.
- Duplication of `Where`-style predicates **across read handlers** is no longer factor-able through specs. If two read handlers ever need the literal same filter, they will copy it. (Pragmatic mitigation: extension methods on `IQueryable<TAggregate>` for the rare case where this matters; not adopted as a default to avoid premature abstraction.)
- The four Invoicing handlers that need a per-request SAS URL had to introduce a small shared projection target (`InvoiceRow` / `CreditNoteRow`) carrying the response fields plus the blob name needed for SAS minting. This is a minor cousin of Option 3's mapper-class problem — but the row type is an EF-projection target (translated to SQL), not an in-memory mapper from a fully materialised aggregate, so the wire-cost argument doesn't apply.

### Risks

- **Owned-collection projection translation regressions.** Some EF Core projection shapes (`.Select(...).ToList()` on owned collections, conditional projection of nullable owned VOs) translate cleanly on EF Core 10 but might regress on a future major version. Mitigation: the per-handler integration characterisation tests in `*.IntegrationTests` exercise the SQL path end-to-end against a real Postgres container, so any translation regression fails at CI time, not in production. (Note: InMemory provider tests cannot verify these projections — an integration test against real Postgres is the only place this path is exercised.)
- **A projection row shared between a *list* query and a *detail* query is the weak case.** Two handlers with genuinely identical column needs — Invoicing's by-id and by-order-id reads differ only in their `WHERE` — are one piece of knowledge with two call sites, and sharing one EF expression property is right. A list is not that: it rarely needs the detail shape, and inheriting one makes it pay the detail query's cost on every row. **The operative variable is that per-row cost, not the list/detail category** — a shared row of flat scalars costs a list essentially nothing, which is why Payments' list and detail correctly share one. The cost appears with owned collections, wide columns, or per-row I/O. Ordering is the precedent for divergence — `GetOrdersByBuyer` returns a narrower `OrderSummaryDto` and `GetOrderById` the full envelope, per [use-cases.md § 3.4.1 / § 3.4.2](../bc-design/use-cases.md); the two are intentionally divergent, not duplication waiting to be refactored.

  Splitting a shared row requires the forward test — *when one query's column needs change, is the other's forced to change too?* — answering **no**, plus either a named cost or a correctness reason. Symmetry and aesthetics are neither.

  Where a row stays shared, the integration tests pinning the wire shape are what actually guard against drift. The sibling-pointer comment in each handler is a signpost, not a guard: it fails silently against a reader who never opens the sibling, which is why the gate above asks for a named cost rather than for reviewer vigilance. The reasoning, and what the primary sources do and do not support, is in [read-model-sharing.md](../research/read-model-sharing.md) and [dry-and-duplication.md](../research/dry-and-duplication.md).

## Implementation Notes

### Pattern for SAS-URL-minting read handlers

The four Invoicing query handlers each need to mint a per-request SAS URL after materialisation (`_blobStore.GetSasUrlAsync(...)`). The response classes (`GetInvoiceByIdResponse`, `GetCreditNoteByIdResponse`) are `sealed class { required ... init; }` — once constructed, the URL fields cannot be reassigned. The pattern adopted:

1. Define an internal `record` row type carrying every response field plus `PdfBlobName` — the one column NOT in the response but needed to decide whether to mint. Where it is declared follows *Projection-row placement* below.
2. Expose `public static Expression<Func<TAggregate, TRow>> Projection => i => new TRow(...)` so EF translates the body to SQL.
3. Handler does `.Select(TRow.Projection).FirstOrDefaultAsync(ct)`, then in-memory: if `row.PdfBlobName is not null`, await `GetSasUrlAsync`, then map the row to **that slice's own response type**, passing the URL and its expiry.

This preserves the SQL-side-projection contract: no full-aggregate materialisation.

**The row carries no endpoint's wire type.** Row → response mapping lives in each consuming slice, not on the row — a `ToResponse()` on a shared row binds every consumer to one endpoint's contract, and a shared row that has moved into a `Common` sink carries that binding where the slice-independence check cannot see it. `ProductSearchResultRow` is the worked example.

### Projection-row placement

A projection row consumed by **more than one slice** is declared in a **non-slice namespace**. A row with a single consuming slice stays in that slice. (The unit is the slice, not the reference — a `<see cref>` or a unit test does not make a row multi-consumer.)

This governs only *where a shared row is declared*, not whether sharing is allowed; the § Risks test above decides that. The constraint is the intra-BC slice-independence rule in [eshop-master-design.md § 11.4](../eshop-master-design.md): a type declared in a depth-2 `{Root}.{Area}.{Feature}` namespace is slice-private, so a sibling slice referencing it is a violation however legitimate the sharing. **A non-slice namespace is any namespace that rule's slice discovery excludes** — read the discovery logic in a BC's `SliceIndependenceTests.cs` rather than re-listing the exclusions here; `Common` sits at either the area or the feature position, and shallower-than-depth-2 is never a slice. Enforcement is a per-BC NetArchTest fact and is **not yet present in every BC**, so a misplaced row may go uncaught until a sibling references it.

Three qualifications:

- **It is a namespace rule, not a folder rule** — the arch test keys off namespaces. `PaymentTransactionRow` is the standing counter-example: it sits in the `GetPaymentById/` folder but declares `namespace Payments.Application.Transactions;` (root+1), so it already complies and must not be relocated.
- **The subject is EF projection targets that are never persisted** — the `Expression<Func<TAggregate, TRow>>` shapes above, which exist only to narrow a `SELECT`.
- **`DbSet`-mapped read-model entities are excluded.** Some `*Row` types in the same folders are mapped entities with an `IEntityTypeConfiguration<>` and a **namespace-qualified identity recorded in the generated model snapshot**. Moving one changes EF's model identity and emits a spurious migration — and the snapshot files are agent-deny-protected (`CLAUDE.md`), so whoever triggers it cannot repair it. Their placement is fixed by EF, not by this rule; check for a `DbSet<>` before applying it.

### A persisted projection shape is never a wire type

*Projection-row placement* above governs EF projection targets that are **never persisted**. A
projection column whose contents are *serialized* — a JSONB document — is the other case, and it is
the one that bites: **the serialized shape is an internal persistence contract and gets its own
type.** Never a published wire type, however identical the members look today.

The reason is the knowledge test, applied to the two roles rather than the two shapes. *Would a
change to the wire contract always require the same change at rest?* No — the opposite: a member
added for one endpoint's screen must leave bytes already written untouched, and a key stored last
year must keep deserializing after the API renames its own. Sharing one type makes every contract
edit a silent rewrite of what stored rows mean.

Silent only up to a point. Because wire DTOs are `required` property-style records (ADR-0037),
`System.Text.Json` **throws** on a missing required member — so a rename, a retype, or a new
required member turns into a `JsonException` on **every historical row**, at read time, in
production. The blast radius is the whole read path, not one endpoint.

Two treatments, in preference order:

1. **Flatten it out of existence.** If the shape is flat, store scalar columns instead of a document
   — there is then no serialized contract to guard, and no frozen-literal test to write. Catalog's
   dimensions are the worked example: four `dimensions_*` columns on `product_search_view` mirroring
   the write model, exactly as `PriceAmount`/`PriceCurrency` already do on the same row.

   Flattening trades one hazard for another: a value object that was atomic inside a single document
   becomes N independently-nullable columns, so "all set or all null" stops being structural. **Put
   it back as a table `CHECK`** — `num_nonnulls(...) IN (0, N)` — rather than leaving the rule to a
   comment and a tolerant reader.
2. **Give it its own internal type**, mapped to and from the wire DTO at the read and write
   boundaries. Required where the shape is a collection, which a flat row cannot hold.
   `ProductImageDocument` is the worked example.

**A shape that stays serialized needs a frozen-literal test** — a real stored JSON string, checked
in as a literal, asserted to still deserialize. This is not belt-and-braces: separation alone does
not remove the hazard, because the internal type is itself all-`required`, so a rename of *its*
members still throws on every historical row. Separation narrows the blast radius to storage; the
literal is what catches the break.

A round-trip through the type under test is worthless here — it renames symmetrically with the code
and stays green through exactly the change that breaks production. **The literal earns its keep only
while nothing regenerates it**, which is a stronger constraint than it looks: a serialization
assertion of the form `Serialize(x) == TheLiteral` forces the literal to be rewritten on any shape
change, and a rewritten literal is no longer a historical row. Keep the deserialize-direction
literal separate and marked never-regenerate; assert the write direction against the emitted **key
set**, which pins the reader/writer agreement without pinning bytes that Postgres `jsonb` normalizes
away anyway.

### Specs that survive (still used by write side)

> Updated per [ADR-0022](0022-specification-pattern-adoption.md): the two pure-PK specs were
> deleted and inlined; one business-named saga-idempotency spec survives (keyed on `OrderId`).

- [`PaymentByOrderIdSpec`](../../services/Payments/Payments.Domain/Transactions/Specifications/PaymentByOrderIdSpec.cs) — Capture / Void command handlers (saga idempotency, keyed on `OrderId`).
- `CreateOrderCommandHandler`'s idempotency check is an inline primary-key lookup on the pre-assigned `OrderId` — no spec.
- ~~`OrderByIdSpec`~~ — deleted (ADR-0022); the 7 write-side handlers now load by primary key inline.
- ~~`InvoiceByIdSpec`~~ — deleted (ADR-0022); `ResendInvoiceCommandHandler` now loads by primary key inline.

### Out of scope for this ADR

- Weather BC and the other BCs outside `services/*` at the time (Catalog, Basket, Inventory, Payments, Notifications) per the #277 boundary statement. Weather had a single read-side spec usage but lived outside the `services/*` boundary and was excluded from this rollout; it has since been removed with the reference service, so the exclusion is moot. The other five were outside the #277 boundary and untouched by this rollout, despite their present place under `services/*`.
- Promoting the per-BC arch-test rule into a shared helper in `Platform.Test.Framework`. The four-line fact is below the threshold where indirection pays off; the per-BC duplication is intentional and reads cleanly in isolation.

## Related Decisions

- [`#238`](https://github.com/DavidCapcuch/DotNetAtlas/pull/238) — the perf-driven precedent that motivated codifying this rule.
- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md) — adjacent CQRS-shaped decision (Inventory's read side is built from event projections, a different mechanism but the same read/write-decoupling philosophy).
- [ADR-0017: Blob Storage / CDN](0017-blob-storage-cdn.md) — defines the per-request SAS URL contract that motivated the row+SAS pattern in the four Invoicing read handlers.
