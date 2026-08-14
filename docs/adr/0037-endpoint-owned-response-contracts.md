# ADR-0037: Endpoint-Owned Response Contracts

## Status

Accepted (2026-07-24)

## Context

Every BC API endpoint is a FastEndpoints endpoint; those that return a body do so as `Endpoint<TRequest, TResponse>` or `EndpointWithoutRequest<TResponse>`. An audit of the repo's endpoints found sibling slices in some BCs **sharing one response envelope** — two endpoints returning the same `TResponse`, or one endpoint's envelope reused as another's nested item type. Sharing a wire type couples the endpoints' evolution: changing one endpoint's contract forces reasoning about — and re-testing — every other endpoint that returns the same type.

The subject of this ADR is the **published wire contract** — the envelope a consumer deserializes and its nested item types — *not* the internal read-model helpers (EF `*Row` projection targets) that never cross the wire; those are governed by [ADR-0021](0021-read-side-no-specifications.md). Response types are **wire contracts**, not domain models. This ADR settles: when two endpoints return the same shape *today*, do they share the type or each own a copy? And where is the line between a contract shape (owned per endpoint) and a domain *value concept* (shared)?

This ADR establishes the policy. The per-BC code fixes for the non-compliant BCs are separate slices, each blocked by this one.

## Decision Drivers (ranked)

1. **Reversibility asymmetry** — the cost of wrongly-shared is far higher than the cost of duplication. Splitting a shared envelope *later* touches both endpoints, their tests, and every consumer; starting separate makes a future divergence a one-file edit. Under-coupling is cheap to correct; over-coupling is not.
2. **Bright-line rule** — "each endpoint owns its response type" is mechanically checkable (an architecture test can assert every response type is referenced by exactly one endpoint). "Share when they happen to be identical today" requires judgment at every PR and drifts.
3. **Independent evolvability** — a change to endpoint A's contract must not require reasoning about endpoint B. Shared response types turn every contract edit into a multi-endpoint blast radius.

## Considered Options

### Option 1: Each endpoint owns its response type; value concepts stay shared (chosen)

Response envelopes and their nested item types are **duplicated** per endpoint. Value concepts that pass the knowledge test are **shared** within a BC. Internal read-model projection helpers are out of scope (ADR-0021).

### Option 2: Share a response type whenever two endpoints return the same shape today

DRY the envelope across sibling endpoints; split only when a divergence actually arrives.

- Fewer type declarations while the shapes coincide.
- Every future contract change to one endpoint becomes a shared-type edit that ripples to its siblings; "are these still meant to be identical?" is a judgment call re-litigated at each PR, and the shared type silently drifts into a lowest-common-denominator shape.

## Evaluation Matrix

| Driver (ranked) | Opt 1: Endpoint-owned | Opt 2: Share-when-identical |
|---|---|---|
| 1. Reversibility asymmetry | Divergence is a one-file edit | Splitting later touches both endpoints + tests + consumers |
| 2. Bright-line rule | Arch-test-checkable | Judgment at every PR; drifts |
| 3. Independent evolvability | Contract edit is single-endpoint | Edit ripples to every sharing sibling |

## Decision

**Each endpoint owns its own response type. Value concepts stay shared.**

- **Duplicate**: response envelopes and their nested item types — the **published wire contract** a consumer deserializes.
- **Share**: a value concept that passes the **knowledge test** — *would a change to one site always require the same change to the other?* `MoneyDto` and `DimensionsDto` pass: their representation is an API-wide decision. `AddressDto` does not — see *Per-type rulings* in Implementation Notes. Being "a domain concept" is not sufficient; the test is applied per type, and a shared type must also carry a stated rule for how a change to it is absorbed. (Value DTOs are per-BC — duplicated *across* BCs by design, not hoisted to a single shared assembly. They are the wire-DTO analogue of the domain-layer share/duplicate line the shared-kernel value objects draw in [ADR-0036](0036-shared-kernel-value-objects.md).)
- **Share**: **standardized media types whose standard states how consumers absorb a change to them** — RFC 9457 `ProblemDetails`, produced centrally by `Platform.Api`'s response sender. RFC 9457 § 3.2 permits extension members, so the shape is not frozen and this repo populates it; what licenses sharing anyway is that the same section requires clients to ignore extensions they do not recognize. Neither structural genericity nor standardization on its own qualifies. A container an **endpoint constructs**, carrying no such rule, does not qualify however generic it looks: a paging envelope and a batch envelope are endpoint-owned wire contracts (see *Why a paging envelope is not a media type* below).
- **Out of scope**: internal read-model projection helpers (EF `*Row` SQL-projection targets) and the **persisted** shapes of serialized projection columns. Neither crosses the wire, so sharing one across queries of the same aggregate couples no consumer; both are read-model implementation decisions governed by [ADR-0021](0021-read-side-no-specifications.md), not this ADR. Note the traffic runs one way: a type this ADR rules **share** is still forbidden from being a persisted shape, because the two roles have unrelated reasons to change — see *A persisted projection shape is never a wire type* in ADR-0021.

**The line**: if the type answers *"what does this endpoint return?"* → duplicate. If a change to it would necessarily be the same change at every other site → share within the BC. If an **external standard** answers *"how is this shaped?"* **and** states how consumers absorb its change → share. If it never leaves the database layer → ADR-0021's call, not this one.

New response and item types are **immutable, property-style records**:

```csharp
public sealed record Foo
{
    public required string Bar { get; init; }
}
```

`required` forces every field at construction (construction-complete); `init` forbids mutation afterwards (immutable). This is the shape a wire contract wants, and it matches the value-DTO precedent already sitting in `Catalog.Application.Common.Contracts`. Use **property-style** — not positional — records: property-style keeps existing `new Foo { ... }` object-initializer call sites compiling verbatim and reads better past ~4 members; positional is reserved for trivially flat types. For an `IReadOnlyList<T>` member, `init` gives only *shallow* immutability — it prevents swapping the reference, not mutating the underlying collection; that is acceptable for a serialize-immediately DTO and is not claimed to be deep immutability.

## Rationale

The three drivers all point the same way. Driver 1 is decisive **for endpoint-specific shapes**: response types are cheap to duplicate and expensive to un-share, so the asymmetry says start separate. Driver 2 makes the rule enforceable without per-PR vigilance — "one endpoint per response type" is a fact a NetArchTest rule can assert, whereas "share only when identical" is a standing judgment that erodes. Driver 3 is the payoff: contracts evolve one endpoint at a time.

Duplication here is not a DRY violation waiting to be refactored away — DRY is about a single source of *knowledge*, not a ban on identical text, and two endpoints returning the same shape today are two *independent contracts that happen to coincide*, not one contract used twice. [ADR-0021](0021-read-side-no-specifications.md) already records the read-side precedent: Ordering's `GetOrdersByBuyer` (list) and `GetOrderById` (detail) deliberately return **different** shapes — a narrow `OrderSummaryDto` vs the full `GetOrderByIdResponse` — and are "intentionally divergent, not duplication waiting to be refactored" ([ADR-0021 § Risks](0021-read-side-no-specifications.md)).

**Why sharing a value DTO is not the same coupling.** The obvious objection: a shared `MoneyDto` is embedded in every money-returning endpoint, so editing it changes them all — is that not exactly the coupling this ADR forbids? The resolving principle is that **coupling is wrong when the blast radius misrepresents the semantic scope of the change**. Money representation *is* an API-wide concept: if amounts become strings, that decision genuinely applies everywhere, so the blast radius equals the semantic scope and the coupling is honest. A shared envelope is the opposite — adding a field for one endpoint's screen is semantically a single-endpoint change, yet every sibling moves; the blast radius overstates what actually changed.

In practice the knowledge test is asked as a volatility question — **"does this type have an *endpoint-specific* reason to change?"** Envelopes do, constantly. Amount-plus-currency does not. Two phrasings of one criterion; the category question *"is this a value concept?"* is not the test.

The evidence that a published shape needs a change contract is not this repo's invention: `google.type.PostalAddress` carries an in-band `revision` plus a permanent backward-compatibility obligation, and JSON:API quarantines everything non-standard into `meta`. Google's reason for mandating one shared error type across every API is likewise consumer-side — so that clients can write one handler — even though its services construct those errors themselves. The rule is self-correcting: a value DTO that starts needing endpoint-specific variants was never one concept, and the split then has evidence behind it. Duplicating value DTOs instead would cost more than the coupling saves — N copies drift (`Currency` vs `CurrencyCode`, two decimals vs four), consumers lose the single representation that makes the API coherent, and under `ShortSchemaNames` the copies collapse into `Money` / `Money2` / `Money3` in the OpenAPI document.

**Why this is stricter than the Rule of Three.** The usual guidance — tolerate duplication until a third instance justifies extracting it — governs *internal* code, where extraction is cheap and reversible. It does not transfer to a **published** contract: once a shape is observable, consumers depend on it (Hyrum's Law), so un-sharing later is a breaking change to parties you cannot enumerate. The stricter rule is not dogma; it is the Rule of Three applied to a surface where the "extract later" escape hatch does not exist. Inside a BC, ordinary refactoring judgment still applies — this ADR binds the wire, not the implementation.

### Why a paging envelope is not a media type

A paginated endpoint's envelope is *structurally* a `PagedResult<T>` with no endpoint-specific member, which makes it the hardest case for the share/duplicate line. It is **endpoint-owned**, on three independent grounds:

- **Driver 1 does not reach it.** Reversibility asymmetry measures *consumer* blast radius (Hyrum's Law, above), and a structural container has the same blast radius in both directions: collapsing N identical envelopes into one generic leaves the JSON body byte-identical, and un-sharing it again leaves it byte-identical. (Each direction renames the type in the OpenAPI document, churning generated clients — but *symmetrically*, so no asymmetry survives.) Driver 1 is therefore **neutral** here, leaving drivers 2–3 to decide; both favour endpoint-owned. An argument for sharing that invokes driver 1 is measuring files touched in a refactor, which is not the cost the driver ranks.
- **No published absorption rule, and endpoint-specific reasons to change.** `ProblemDetails` is shared because RFC 9457 publishes a rule for absorbing its change and `Platform.Api` emits it centrally. A page envelope has neither, and it acquires endpoint-specific reasons to change constantly — a *search* page wants facet counts a category listing does not, an admin page wants a cross-status total. Inventory's `GetStockLevelsBulkResponse` is the live proof — a repo-authored container that already carries an endpoint-specific `MissingProductIds`.
- **Nobody ships a shared generic page envelope, and the published guidance does not ask for one.** [Google AIP-158](https://google.aip.dev/158) prescribes pagination *fields* (`page_size`, `page_token`, `next_page_token`), while [AIP-132](https://google.aip.dev/132) requires the response message be named for its own RPC — per-method envelopes, not a shared one. Inspected specs agree: Stripe declares a list envelope inline per endpoint, DigitalOcean names one per resource (sharing only a `pagination` sub-object via `allOf`), Xero wraps per resource, and GitHub returns bare arrays with paging in `Link` headers. Nor is there a standard to adopt — no IANA-registered media type describes a page envelope, and the IETF's own paged-collection model ([RFC 5005](https://www.rfc-editor.org/rfc/rfc5005)) carries links and no metadata at all, defining neither a total nor a count.

  Stripe also supplies the live proof of the argument above: its search endpoints carry `total_count` and `next_page` that its list endpoints do not — the same divergence this repo would have between a search page and a category listing.

**Why the repo's page shape is not the house guidelines' page shape.** [Zalando](https://opensource.zalando.com/restful-api-guidelines/), [Stripe](https://docs.stripe.com/api/pagination) and [Microsoft Azure](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md) converge on two rules this repo deliberately breaks — **cursor over offset** (Zalando: prefer cursor, avoid offset; Stripe is cursor-only) and **no exact total** (Azure: "SHOULD NOT return a `count` … may be expensive to compute"; Stripe returns none). Google is the exception rather than the rule here: AIP-158 makes `total_size` an affirmative **MAY**, asking only that an estimate be documented as one. This repo pages by `PageNumber`/`PageSize` and returns a `required` exact `Total` because the API is modelled for a **page-numbered storefront** — "showing 1–20 of 348", jump to page 7 — which cursor paging structurally cannot render. Those standards shape large resource APIs consumed by generated SDKs; this is a deliberate choice for a different consumer, not drift.

Azure's cost warning is incurred here, not dodged: `SearchProductsQueryHandler` issues a second `CountAsync` over the *filtered* queryable, whose text filter is a leading-wildcard `LIKE` over `product_search_view` — a projection **table**, not a materialized view — that no index serves. The exact `Total` is the expensive half of a text search, and no consumer renders it yet: the BFF discards it at the ACL boundary. The cost is accepted for the storefront the API is modelled on, not because it is negligible.

Two consequences follow:

- **Endpoint-owned envelopes keep a future paging migration incremental.** *When to revisit*: when the count cost above starts to bite under load, or a consumer needs stable paging over data that mutates between requests, **that endpoint** moves to keyset/cursor paging in its own slice — and text search, whose count is already a sequential scan, is the first candidate. A shared `PagedResult<T>` would instead freeze the shape all four standards reject into a platform type, forcing a big-bang across every paginated BC or leaving two page envelopes coexisting indefinitely.
- **The same reasoning decides any container this repo authors.** A batch envelope resolves identically — `GetStockLevelsBulkResponse` is the worked example, and its endpoint-specific `MissingProductIds` is precisely what a shared `BatchResult<T>` could never have carried.

## Consequences

### Positive

- A contract change is a one-endpoint, one-file edit — no sibling blast radius and no "is this still supposed to match?" at review time.
- The rule can be arch-test-enforced, so compliance becomes CI-checkable rather than review-dependent.
- Every type a BC shares will be named in one reviewable place — its arch test's allow-list — so adding to it is a decision someone makes, not a file quietly appearing in a folder.

### Negative

- Two endpoints that genuinely return the same shape carry two type declarations. Accepted: the duplication is precisely the mechanism that lets them diverge for free, and the shapes are meant to be independent contracts.
- Every paginated endpoint declares its own `{ Total, PageNumber, PageSize, Items }` envelope, so a BC with three paginated endpoints carries three of them. Accepted for the reasons in *Why a paging envelope is not a media type* — chiefly that it keeps a later migration to keyset paging an endpoint-at-a-time change.

### Risks

- **Simple-name collisions in the generated OpenAPI document.** `SwaggerDependencyInjection` sets NSwag's `ShortSchemaNames = true`, so two DTOs with the same *simple* name in different namespaces collapse to `Foo` / `Foo2` in the generated document — an ambiguous, position-dependent contract. **Duplicated envelopes MUST have globally unique simple names across the whole document** (e.g. `GetProductByIdResponse` and `SearchProductsResultItem`, not two `ProductDetail` types). Mitigation: unique names by construction, plus the arch test below.

  **The collision domain is the document, not an assembly.** A BC's document draws schemas from every assembly that contributes a type NSwag can reach: request types from `.Api`, envelopes and item and value DTOs from `.Application`, and `.Domain` wherever an enum is exposed directly as a wire property (Inventory's `ReservationAuditResponse` does this). A rule — or an arch test — scoped to one assembly leaves same-name types in the others colliding silently while passing.

  **Today the collapse costs readability, not a build.** No client generation consumes these documents; the BFF hand-declares its upstream types as an anti-corruption layer, so nothing downstream binds to a schema name. *When to revisit*: if generated clients return, a position-dependent name becomes a consumer-breaking change rather than a cosmetic one, and the custom-generator option below gets materially stronger.

  Endpoint-owned contracts mint near-identically-shaped types by design, so this constraint tightens with every slice. It is kept deliberately, on three grounds:

  - **Short names serve the majority; collisions are the minority.** `ShortSchemaNames = false` flattens the whole namespace into every schema name, colliding or not — `Catalog.Application.Categories.GetProductsByCategory.GetProductsByCategoryResponse` is emitted as `CatalogApplicationCategoriesGetProductsByCategoryGetProductsByCategoryResponse`, and this repo's namespace depth puts most names in that 60–80 character band. Turning the setting off taxes every schema in the document to disambiguate the few that would clash.
  - **Unique simple names are wanted on their own merits.** They keep a type greppable by name and let one contract nest another without a `using` alias. The OpenAPI collapse is what makes the preference non-negotiable rather than stylistic — it is what the arch test's *scope* (document-wide, not per-file) is calibrated to, so the test tracks this setting and is not independent of it.
  - **A custom schema-name generator costs more than the rule it replaces.** NSwag accepts a `SchemaNameGenerator`, and one that namespace-qualifies only the colliding types keeps short names everywhere else. The cost is permanent platform machinery — a reflection scan over the type set, a cache, and an emitted name that no longer follows from reading `SwaggerDependencyInjection`. A naming rule adds no platform code to maintain.

- **The value-DTO carve-out is conditional, not categorical.** It holds only while the shared type stays low-churn (see Rationale). Watch for the tell: a value DTO accumulating optional or endpoint-flavoured members is one that has stopped being a single concept — split it per endpoint rather than widening it. A second dependency worth naming for anyone porting this rule: the carve-out leans on this repo's allowance for **in-place breaking changes**; where contracts may not break in place, an edit to a shared value DTO silently alters every contract embedding it, and the carve-out needs a stricter change process than "edit the type."

## Compliance audit (2026-07-27)

Endpoint-to-**wire-contract** map over every response-returning endpoint — both `Endpoint<TRequest, TResponse>` and `EndpointWithoutRequest<TResponse>`, since the rule binds the response type regardless of whether the endpoint takes a request. Covers response envelopes plus their nested item types; internal projection rows are out of scope (see Decision). A unit is compliant when every wire type belongs to exactly one endpoint.

| Unit | Status | Remediation |
|---|---|---|
| Ordering | compliant | — |
| Basket | compliant | — |
| Notifications | compliant (vacuous) | — |
| EShop.BFF | compliant | [#361](https://github.com/DavidCapcuch/DotNetAtlas/issues/361) — enablement, not remediation |
| Catalog | **non-compliant** | [#352](https://github.com/DavidCapcuch/DotNetAtlas/issues/352), [#353](https://github.com/DavidCapcuch/DotNetAtlas/issues/353), [#354](https://github.com/DavidCapcuch/DotNetAtlas/issues/354), [#355](https://github.com/DavidCapcuch/DotNetAtlas/issues/355), [#356](https://github.com/DavidCapcuch/DotNetAtlas/issues/356) |
| Inventory | **non-compliant** | [#357](https://github.com/DavidCapcuch/DotNetAtlas/issues/357) |
| Invoicing | **non-compliant** | [#358](https://github.com/DavidCapcuch/DotNetAtlas/issues/358) |
| Payments | **non-compliant** | [#360](https://github.com/DavidCapcuch/DotNetAtlas/issues/360) |

**The enforcing arch test lands per unit**, on the ticket named in that unit's row above — the repo copies arch-test rule logic per BC rather than extracting a shared arch-test project. So a rule stated in *Implementation Notes* is a running check in a unit whose ticket has landed, and a specification for the work in every unit whose ticket has not.

Each ticket owns its own detail — which type is over-shared, from which slice, and what the fix is. Restating that here would be a second copy that drifts as the slices land. A dash means nothing outstanding; the BFF's ticket is *enablement*, not a contract fix, so its compliance is not in question.

Per-unit notes, for what no ticket covers:

- **Ordering** — `GetOrdersByBuyerResponse` is a slice-owned paging envelope: the shape *Why a paging envelope is not a media type* prescribes.
- **Notifications** — its browser surface is the SignalR bell hub ([ADR-0035](0035-edge-owned-cors-yarp.md)), so compliance holds vacuously: it exposes no response-returning endpoint at all.
- **EShop.BFF** — the one value DTO its three page endpoints share (`MoneyDto`) is shared *by design*. Because the exemption is by type name, it needs no relocation: where it is declared raises no compliance question.
- **A command's response binds exactly as a query's does.** A post-mutation snapshot returned by a command endpoint is a published wire contract on the same footing — the rule draws no query/command line.

## Implementation Notes

- New response and item types follow the immutable, property-style-record shape above, matching the value-DTO precedent in `Catalog.Application.Common.Contracts` — so that folder stops teaching two contradictory conventions (`record { init }` for value DTOs, `class { set }` for envelopes).
- Globally-unique simple names across a BC's whole document are not optional. The convention is wanted independently, and `ShortSchemaNames` additionally makes it load-bearing — see Risks for the trade-off and for why the domain is the document rather than one assembly.
- **The enforcing arch test asserts "referenced by exactly one endpoint" over response envelopes and the types reachable from them, minus an explicit allow-list of type names.** Each BC's test names the exact types it exempts. **Nothing is exempt by where it is declared.** The allow-list is that test's own constant — this ADR does not restate the per-BC values, because a doc-side copy of a code-side list drifts on the next slice.

  **A namespace-anchored exemption was considered and rejected.** Anchoring on the declaration site looks more mechanical, but it licenses whatever is dropped into the folder: an envelope relocated there escapes the rule silently, and the test goes green over the violation it exists to catch. The sink is trivial to fill — `SliceIndependenceTests` excludes `Common` from its scan, so relocating a type there satisfies that rule whatever the type is, and a location anchor would then exempt it from this one too. That test's failure message steers a wire contract to a per-slice copy rather than the sink, but a message is prose a developer must read and follow, not a constraint anything enforces. A location anchor therefore needs its own policing rule, and an ordering precondition (evict every envelope before the test can be trusted). A name list needs neither, and is the stricter test besides: exact match on a type name, and growing it is a reviewable diff rather than a file quietly appearing in a folder.

  `Common/Contracts` keeps its jobs — a home for shared value DTOs, a slice-independence sink. It loses only the power to *license*. **Paging envelopes are not exempt** — each is referenced by its one endpoint and satisfies the rule directly.

- **`ProblemDetails` needs no allow-list entry.** It is `Microsoft.AspNetCore.Mvc.ProblemDetails` — a framework type no BC declares — so *Stop the reachability walk at the BC's own assemblies* below already puts it outside the rule's subject. Listing it would suggest the walk reaches framework types, which invites an implementation without the assembly stop and then a broad BCL exemption list: the hiding place again, one level up.
- **Per-type rulings.** Apply the knowledge test (*would a change to one site always require the same change to the other?*) per type, not per category — "value DTO" is not itself an answer. For repo-owned value DTOs the absorption rule the Decision requires is this repo's allowance for **in-place breaking changes** (see Risks); where contracts may not break in place, sharing needs a stricter change process than "edit the type."

  | Type | Ruling | Why |
  |---|---|---|
  | `MoneyDto` | **share** | Amount/currency representation — decimal vs string, two decimals vs four, `Currency` vs `CurrencyCode` — is an API-wide decision. Two endpoints of one BC emitting money differently is a defect, not divergence. |
  | `DimensionsDto` | **share** | Same character: how length/width/height/unit are represented is a representation decision, not an endpoint's. |
  | `ImageReferenceDto` | **share** | Answers *"what is an image reference?"*, not *"what does this endpoint return?"* — the weakest of the three, and the first to re-examine if it starts carrying endpoint-flavoured members. |
  | `AddressDto` | **duplicate** | The knowledge test fails here. A billing address and a shipping address diverge in practice — different members, different validation. Published commerce APIs bear this out: Adyen's `DeliveryAddress` carries `firstName`/`lastName` its `BillingAddress` does not; Xero ships `Address` and `AddressForOrganisation` with identical member sets as separate schemas; Stripe's `address` reaches 388 operations yet ships structurally identical twins namespaced per product area. None of the three shares one address schema. |

  Same-simple-name types across BCs — `MoneyDto`, `AddressDto` — are separate types in separate assemblies, duplicated across BCs by design; never unify them.

- **Count distinct endpoints, not reference sites.** One response may reach a type through several members: `GetOrderByIdResponse` has both a `ShippingAddress` and a `BillingAddress` of type `AddressDto`, and that is one endpoint, not two. A rule implemented as "referenced exactly once" fails a compliant BC.
- **Stop the reachability walk at the BC's own assemblies.** "Types reachable from the response" otherwise reaches `Guid`, `string`, `decimal`, `DateTimeOffset` and `IReadOnlyList<T>`, each referenced by many endpoints. The walk covers types the BC itself declares — across `.Api`, `.Application` and `.Domain`, since all three can contribute a schema (see Risks); BCL and framework types are outside the rule's subject. The repo's arch tests scan `.Application` almost exclusively today, so a rule anchored on a single assembly marker will under-scan.
- **The repo's one `PagedResult<T>` is not a counter-example.** `EShop.BFF.Infrastructure.Clients.Catalog.PagedResult<T>` is `internal` — the BFF's anti-corruption mirror of an upstream page, deserialized *inbound* ([bff.md § 4.1](../bc-design/bff.md)), which that doc also plans to reuse for the Ordering client. It is not a published wire contract, so this ADR does not govern it, and it is deliberately free to differ from whatever Catalog emits.
- The rule, the duplicate/share line, and the record shape are mirrored in [conventions.md § 10](../bc-design/conventions.md).

## Related Decisions

- [ADR-0021: Read-side, no specifications](0021-read-side-no-specifications.md) — the jurisdiction boundary. **This ADR governs the published wire contract** (envelopes + item types, read or write); **ADR-0021 governs internal read-model projection helpers** (`*Row` SQL-projection targets), which it deliberately permits sharing across queries of one aggregate (drift mitigated by a handler comment + integration tests). It also owns the case that falls under both headings — a type that is a wire contract *and* the persisted shape of a serialized projection column — and forbids it outright. No supersession; the two partition cleanly. ADR-0021 also records the list/detail divergence precedent this ADR builds on (Ordering's `OrderSummaryDto` vs `GetOrderByIdResponse`).
- [ADR-0036: Shared-Kernel Value Objects](0036-shared-kernel-value-objects.md) — the domain-layer analogue of the share/duplicate line: universal *value concepts* are shared, everything contract-shaped stays local.
- [ADR-0012: API Versioning (`/v{major}/`)](0012-api-versioning.md) — orthogonal. A version bump creates a new endpoint class with a new response type by construction, so a `/v1/` contract is frozen when `/v2/` is cut, never dragged; this rule governs contracts *within* a version and does not depend on the versioning strategy.
