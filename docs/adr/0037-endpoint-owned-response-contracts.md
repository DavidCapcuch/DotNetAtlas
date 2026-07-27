# ADR-0037: Endpoint-Owned Response Contracts

## Status

Accepted (2026-07-24)

## Context

Every BC API endpoint is a FastEndpoints `Endpoint<TRequest, TResponse>`. An audit of the repo's endpoints found sibling slices in some BCs **sharing one response envelope** — two endpoints returning the same `TResponse`, or one endpoint's envelope reused as another's nested item type. Sharing a wire type couples the endpoints' evolution: changing one endpoint's contract forces reasoning about — and re-testing — every other endpoint that returns the same type.

The subject of this ADR is the **published wire contract** — the envelope a consumer deserializes and its nested item types — *not* the internal read-model helpers (EF `*Row` projection targets) that never cross the wire; those are governed by [ADR-0021](0021-read-side-no-specifications.md). Response types are **wire contracts**, not domain models. This ADR settles: when two endpoints return the same shape *today*, do they share the type or each own a copy? And where is the line between a contract shape (owned per endpoint) and a domain *value concept* (shared)?

This ADR establishes the policy. The per-BC code fixes for the non-compliant BCs are separate slices, each blocked by this one.

## Decision Drivers (ranked)

1. **Reversibility asymmetry** — the cost of wrongly-shared is far higher than the cost of duplication. Splitting a shared envelope *later* touches both endpoints, their tests, and every consumer; starting separate makes a future divergence a one-file edit. Under-coupling is cheap to correct; over-coupling is not.
2. **Bright-line rule** — "each endpoint owns its response type" is mechanically checkable (an architecture test can assert every response type is referenced by exactly one endpoint). "Share when they happen to be identical today" requires judgment at every PR and drifts.
3. **Independent evolvability** — a change to endpoint A's contract must not require reasoning about endpoint B. Shared response types turn every contract edit into a multi-endpoint blast radius.

## Considered Options

### Option 1: Each endpoint owns its response type; value concepts stay shared (chosen)

Response envelopes and their nested item types are **duplicated** per endpoint. Value DTOs — `MoneyDto`, `DimensionsDto`, `ImageReferenceDto`, and similar — are **shared** within a BC, because they express a domain concept, not an endpoint's contract shape. Internal read-model projection helpers are out of scope (ADR-0021).

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
- **Share**: value DTOs (`MoneyDto`, `DimensionsDto`, `ImageReferenceDto`, and similar) — they express a *domain concept*, not an endpoint's contract shape, so a BC's sibling slices share one copy from that BC's `Common/Contracts` namespace. (Value DTOs are per-BC — duplicated *across* BCs by design, not hoisted to a single shared assembly. They are the wire-DTO analogue of the domain-layer share/duplicate line the shared-kernel value objects draw in [ADR-0036](0036-shared-kernel-value-objects.md).)
- **Share**: **standardized media types whose shape this repo does not author** — RFC 9457 `ProblemDetails` (the repo's universal error envelope, emitted centrally by `Platform.Api`'s response sender). The qualifying property is *not* that a type is structurally generic; it is that an **external standard has frozen the shape**, so no endpoint *can* have a reason to change it. Duplicating an IETF media type per endpoint would be absurd. A container **this repo authors** does not qualify however generic it looks — a paging envelope and a batch envelope are endpoint-owned wire contracts, not media types (see *Why a paging envelope is not a media type* below).
- **Out of scope**: internal read-model projection helpers (EF `*Row` SQL-projection targets). They never cross the wire, so sharing one across queries of the same aggregate couples no consumer; that sharing is a read-model implementation decision governed by [ADR-0021](0021-read-side-no-specifications.md), not this ADR.

**The line**: if the type answers *"what does this endpoint return?"* → duplicate. If it answers *"what is money?"* → share within the BC. If an **external standard** already answers *"how is this shaped?"* → share. If it never leaves the database layer → ADR-0021's call, not this one.

New response and item types are **immutable, property-style records**:

```csharp
public sealed record Foo
{
    public required string Bar { get; init; }
}
```

`required` forces every field at construction (construction-complete); `init` forbids mutation afterwards (immutable). This is the shape a wire contract wants, and it matches the value-DTO precedent already sitting in each BC's `Common/Contracts` namespace. Use **property-style** — not positional — records: property-style keeps existing `new Foo { ... }` object-initializer call sites compiling verbatim and reads better past ~4 members; positional is reserved for trivially flat types. For an `IReadOnlyList<T>` member, `init` gives only *shallow* immutability — it prevents swapping the reference, not mutating the underlying collection; that is acceptable for a serialize-immediately DTO and is not claimed to be deep immutability.

## Rationale

The three drivers all point the same way. Driver 1 is decisive **for endpoint-specific shapes**: response types are cheap to duplicate and expensive to un-share, so the asymmetry says start separate. Driver 2 makes the rule enforceable without per-PR vigilance — "one endpoint per response type" is a fact a NetArchTest rule can assert, whereas "share only when identical" is a standing judgment that erodes. Driver 3 is the payoff: contracts evolve one endpoint at a time.

Duplication here is not a DRY violation waiting to be refactored away — DRY is about a single source of *knowledge*, not a ban on identical text, and two endpoints returning the same shape today are two *independent contracts that happen to coincide*, not one contract used twice. [ADR-0021](0021-read-side-no-specifications.md) already records the read-side precedent: Ordering's `GetOrdersByBuyer` (list) and `GetOrderById` (detail) deliberately return **different** shapes — a narrow `OrderSummaryDto` vs the full `GetOrderByIdResponse` — and are "intentionally divergent, not duplication waiting to be refactored" ([ADR-0021 § Risks](0021-read-side-no-specifications.md)).

**Why sharing a value DTO is not the same coupling.** The obvious objection: a shared `MoneyDto` is embedded in every money-returning endpoint, so editing it changes them all — is that not exactly the coupling this ADR forbids? The resolving principle is that **coupling is wrong when the blast radius misrepresents the semantic scope of the change**. Money representation *is* an API-wide concept: if amounts become strings, that decision genuinely applies everywhere, so the blast radius equals the semantic scope and the coupling is honest. A shared envelope is the opposite — adding a field for one endpoint's screen is semantically a single-endpoint change, yet every sibling moves; the blast radius overstates what actually changed.

So the operative test is not the category *"is this a value concept?"* but the volatility question **"does this type have an *endpoint-specific* reason to change?"** Envelopes do, constantly. Amount-plus-currency does not. The rule is self-correcting: a value DTO that starts needing endpoint-specific variants was never one concept, and the split then has evidence behind it. Duplicating value DTOs instead would cost more than the coupling saves — N copies drift (`Currency` vs `CurrencyCode`, two decimals vs four), consumers lose the single representation that makes the API coherent, and under `ShortSchemaNames` the copies collapse into `Money` / `Money2` / `Money3` in the OpenAPI document.

**Why this is stricter than the Rule of Three.** The usual guidance — tolerate duplication until a third instance justifies extracting it — governs *internal* code, where extraction is cheap and reversible. It does not transfer to a **published** contract: once a shape is observable, consumers depend on it (Hyrum's Law), so un-sharing later is a breaking change to parties you cannot enumerate. The stricter rule is not dogma; it is the Rule of Three applied to a surface where the "extract later" escape hatch does not exist. Inside a BC, ordinary refactoring judgment still applies — this ADR binds the wire, not the implementation.

### Why a paging envelope is not a media type

A paginated endpoint's envelope is *structurally* a `PagedResult<T>` with no endpoint-specific member, which makes it the hardest case for the share/duplicate line. It is **endpoint-owned**, on three independent grounds:

- **Driver 1 does not reach it.** Reversibility asymmetry measures *consumer* blast radius (Hyrum's Law, above), and a structural container has the same blast radius in both directions: collapsing N identical envelopes into one generic leaves the JSON body byte-identical, and un-sharing it again leaves it byte-identical. (Each direction renames the type in the OpenAPI document, churning generated clients — but *symmetrically*, so no asymmetry survives.) Driver 1 is therefore **neutral** here, leaving drivers 2–3 to decide; both favour endpoint-owned. An argument for sharing that invokes driver 1 is measuring files touched in a refactor, which is not the cost the driver ranks.
- **The volatility test fails.** `ProblemDetails` has no endpoint-specific reason to change because the IETF froze it and this repo cannot extend it. A hand-rolled page envelope has such reasons constantly — a *search* page wants facet counts a category listing does not, an admin page wants a cross-status total. Inventory's `GetStockLevelsBulkResponse` is the live proof: a repo-authored batch container that already carries an endpoint-specific `MissingProductIds`.
- **The standards that share a page envelope froze the mechanism first.** [Google AIP-158](https://google.aip.dev/158), [Zalando](https://opensource.zalando.com/restful-api-guidelines/), [Stripe](https://docs.stripe.com/api/pagination) and [Microsoft Azure](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md) each prescribe exactly one collection envelope — because each first mandated a house-wide pagination *strategy* that the envelope embodies and endpoints may not extend. This repo has made no such decision; identical page shapes across BCs are convergence, not a standard.

**Why the repo's page shape is not the standards' page shape.** Those four converge on two rules this repo deliberately breaks — **cursor over offset** (Zalando: prefer cursor, avoid offset) and **no exact total** (Azure: "SHOULD NOT return a `count` … may be expensive to compute"; Stripe returns none; Google permits only an estimate). This repo pages by `PageNumber`/`PageSize` and returns a `required` exact `Total`, because the consumer is a **page-numbered storefront** — "showing 1–20 of 348", jump to page 7 — which cursor paging structurally cannot render, and Catalog counts over the materialized `product_search_view` where `COUNT(*)` is cheap. Those standards shape large resource APIs consumed by generated SDKs; this is a product decision for a different consumer, not drift.

Two consequences follow:

- **Endpoint-owned envelopes keep a future paging migration incremental.** *When to revisit*: if a collection outgrows a cheap `COUNT(*)`, or a consumer needs stable paging over data that mutates between requests, **that endpoint** moves to keyset/cursor paging in its own slice. A shared `PagedResult<T>` would instead freeze the shape all four standards reject into a platform type, forcing a big-bang across every paginated BC or leaving two page envelopes coexisting indefinitely.
- **The same reasoning decides any container this repo authors.** A batch envelope resolves identically — `GetStockLevelsBulkResponse` is the worked example, and its endpoint-specific `MissingProductIds` is precisely what a shared `BatchResult<T>` could never have carried.

## Consequences

### Positive

- A contract change is a one-endpoint, one-file edit — no sibling blast radius and no "is this still supposed to match?" at review time.
- The rule can be arch-test-enforced, so compliance becomes CI-checkable rather than review-dependent.
- A BC's `Common/Contracts` namespace teaches one convention: shared *value* DTOs (immutable records), never endpoint envelopes.

### Negative

- Two endpoints that genuinely return the same shape carry two type declarations. Accepted: the duplication is precisely the mechanism that lets them diverge for free, and the shapes are meant to be independent contracts.
- Every paginated endpoint declares its own `{ Total, PageNumber, PageSize, Items }` envelope, so a BC with three paginated endpoints carries three of them. Accepted for the reasons in *Why a paging envelope is not a media type* — chiefly that it keeps a later migration to keyset paging an endpoint-at-a-time change.

### Risks

- **Simple-name collisions in the generated OpenAPI document.** `SwaggerDependencyInjection` sets NSwag's `ShortSchemaNames = true`, so two DTOs with the same *simple* name in different namespaces of one assembly collapse to `Foo` / `Foo2` in the generated document — an ambiguous, position-dependent contract. **Duplicated envelopes MUST have globally unique simple names within their assembly** (e.g. `GetProductByIdResponse` and `SearchProductsResultItem`, not two `ProductDetail` types). Mitigation: unique names by construction; the same arch-test that enforces one-endpoint-per-type can also assert simple-name uniqueness.

- **The value-DTO carve-out is conditional, not categorical.** It holds only while the shared type stays low-churn (see Rationale). Watch for the tell: a value DTO accumulating optional or endpoint-flavoured members is one that has stopped being a single concept — split it per endpoint rather than widening it. A second dependency worth naming for anyone porting this rule: the carve-out leans on this repo's allowance for **in-place breaking changes**; where contracts may not break in place, an edit to a shared value DTO silently alters every contract embedding it, and the carve-out needs a stricter change process than "edit the type."

## Compliance audit (2026-07-27)

Endpoint-to-**wire-contract** map over every unit exposing `Endpoint<TRequest, TResponse>` — response envelopes plus their nested item types; internal projection rows are out of scope (see Decision). A unit is compliant when every wire type belongs to exactly one endpoint.

| Unit | Status | Over-shared wire type | Consumers |
|---|---|---|---|
| Ordering | compliant | — | — |
| Basket | compliant | — | — |
| Notifications | compliant (vacuous) | — | exposes no `Endpoint<TRequest, TResponse>` |
| EShop.BFF | compliant | — | — |
| Catalog | **non-compliant** | `SearchProductsResponse` | 2 — `SearchProducts`; `SearchAdminProducts` |
| Catalog | **non-compliant** | `ProductDetailResponse` | 2 — `GetProductById`; nested as `GetProductsByIdsResponse.Products` |
| Payments | **non-compliant** | `GetPaymentByIdResponse` | 2 — `GetPaymentById`; nested as `GetPaymentsByOrderResponse.Payments` |
| Invoicing | **non-compliant** | `GetInvoiceByIdResponse` | 3 — `GetInvoiceById`; `GetInvoiceByOrderId`; nested as `GetInvoicesByBuyerResponse.Items` |
| Inventory | **non-compliant** | `StockLevelResponse` | 3 — `GetStockLevel`; `AdjustStock`; `ReceiveStock` |

Per-unit notes:

- **Ordering** — its list/detail pair is divergent *by design* (see Rationale; [ADR-0021 § Risks](0021-read-side-no-specifications.md)). `GetOrdersByBuyerResponse` is a slice-owned paging envelope: the shape *Why a paging envelope is not a media type* prescribes.
- **Notifications** — its browser surface is the SignalR bell hub ([ADR-0035](0035-edge-owned-cors-yarp.md)), so compliance holds vacuously.
- **Payments** — its `PaymentTransactionRow` projection row *is* shared across both query handlers, but that is an internal read-model helper — ADR-0021's domain, not a violation of this ADR.
- **Invoicing** — one wire type serves three endpoints: two return it directly, the third nests it as its list item type. The nesting is documented on the type as deliberate ("so summary and detail queries never drift"), which is this ADR's rejected Option 2.
- **Inventory** — `StockLevelResponse` is returned by one query and two *command* endpoints as a post-mutation snapshot; the rule binds command responses exactly as it binds query responses. Its `ReservationAuditResponse` has a single consumer and is compliant, though it shares the same `StockItems/Common` namespace.

Per policy, **no remediation tickets are opened for compliant units.** Each non-compliant BC is brought into line by remediation slices, blocked by this ADR.

## Implementation Notes

- New response and item types follow the immutable, property-style-record shape above, matching the value-DTO precedent in each BC's `Common/Contracts` (`MoneyDto`, `DimensionsDto`, `ImageReferenceDto`) — so the folder stops teaching two contradictory conventions (`record { init }` for value DTOs, `class { set }` for envelopes).
- Globally-unique simple names per assembly are a hard requirement, not a preference — see Risks.
- **The enforcing arch test asserts "referenced by exactly one endpoint" over response envelopes and the types reachable from them, minus a declared exemption set**: the BC's `Common/Contracts` namespace (shared value DTOs) and externally-standardized media types (`ProblemDetails`). Anchoring the exemption to the **declaration site** rather than to a judgment about a type's meaning is what keeps the rule mechanically checkable — the namespace *is* the bright line. **Paging envelopes are not exempt** — each is referenced by its one endpoint and satisfies the rule directly.
- **The exemption presumes `Common/Contracts` holds only value DTOs.** An *envelope* declared in that namespace is exempted by the anchor and escapes the rule entirely — so a BC's remediation must relocate every envelope out of `Common/Contracts` into its owning slice **before** the test can be trusted. Until that holds, a green run proves nothing about the types still sitting there.
- **The repo's one `PagedResult<T>` is not a counter-example.** `EShop.BFF.Infrastructure.Clients.Catalog.PagedResult<T>` is `internal` — the BFF's anti-corruption mirror of Catalog's page, deserialized *inbound* ([bff.md § 4.1](../bc-design/bff.md)). It is not a published wire contract, so this ADR does not govern it, and it is deliberately free to differ from whatever Catalog emits.
- The rule, the duplicate/share line, and the record shape are mirrored in [conventions.md § 10](../bc-design/conventions.md).

## Related Decisions

- [ADR-0021: Read-side, no specifications](0021-read-side-no-specifications.md) — the jurisdiction boundary. **This ADR governs the published wire contract** (envelopes + item types, read or write); **ADR-0021 governs internal read-model projection helpers** (`*Row` SQL-projection targets), which it deliberately permits sharing across queries of one aggregate (drift mitigated by a handler comment + integration tests). No supersession — the two partition cleanly. ADR-0021 also records the list/detail divergence precedent this ADR builds on (Ordering's `OrderSummaryDto` vs `GetOrderByIdResponse`).
- [ADR-0036: Shared-Kernel Value Objects](0036-shared-kernel-value-objects.md) — the domain-layer analogue of the share/duplicate line: universal *value concepts* are shared, everything contract-shaped stays local.
- [ADR-0012: API Versioning (`/v{major}/`)](0012-api-versioning.md) — orthogonal. A version bump creates a new endpoint class with a new response type by construction, so a `/v1/` contract is frozen when `/v2/` is cut, never dragged; this rule governs contracts *within* a version and does not depend on the versioning strategy.
