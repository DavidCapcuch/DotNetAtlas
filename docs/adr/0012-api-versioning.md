# ADR-0012: API Versioning Strategy — URL Path `/v{major}/`

## Status

Accepted (2026-04-19)

## Context

The eShop reference solution exposes HTTP APIs from every BC (Catalog, Basket, Ordering, Inventory, Payments, Invoicing) plus the BFF. Pre-ADR routes were unversioned (`/api/catalog/products/{id}`). Across the doc sweep a `/v1/` prefix was inserted (e.g., `/api/v1/catalog/products/{id}`) and this ADR ratifies that choice.

API versioning matters for two reasons the reference solution must teach:

1. **Consumer contracts** — once the BFF (or any other client) depends on a route shape, that shape is a contract. Changing it unilaterally breaks the client.
2. **Migration paths** — a reference solution is a living artifact. Contributors add features, rename fields, restructure responses. A versioning strategy is the only honest way to evolve an API without breaking every consumer at once.

Three mainstream strategies exist: URL path (`/api/v1/...`), header (`Api-Version: 1` or `Accept: application/vnd.myapi.v1+json`), and query parameter (`?api-version=1`). The .NET ecosystem has first-class support for all three via `Microsoft.AspNetCore.Mvc.Versioning` / `Asp.Versioning`.

## Decision Drivers (ranked)

1. **Discoverability** — a reader browsing the API surface should see the version immediately. Ops looking at logs should see it in the URL.
2. **Cacheability** — versioned URLs are independent resources from a caching perspective. Same URL → same response.
3. **Tooling friction** — Swagger / OpenAPI tooling, client SDK generation, and ops dashboards should work without custom configuration.
4. **Teachability** — the pattern must be obvious to a reader new to API versioning. Less debate about where the version lives is better.
5. **Compatible with rate-limit policies** — per-route rate limits (ADR-0009 / rate-limiting.md) must apply correctly, and differ between versions if desired.

## Considered Options

### Option 1: URL path versioning — `/api/v{major}/...`

Version appears as a literal path segment. `/api/v1/catalog/products/{id}`, `/api/v2/catalog/products/{id}`. Each version is a separate ASP.NET route.

### Option 2: Header versioning — `Api-Version: 1`

Version in a request header. Route is `/api/catalog/products/{id}`; handler dispatches based on header value.

### Option 3: Media-type versioning — `Accept: application/vnd.eshop.v1+json`

Version embedded in the Accept header's media type. Most "RESTful" per some interpretations of HATEOAS.

### Option 4: Query-string versioning — `?api-version=1`

Version as a query parameter.

## Evaluation Matrix

| Driver (ranked) | Option 1: URL path | Option 2: Header | Option 3: Media type | Option 4: Query string |
|---|---|---|---|---|
| 1. Discoverability | Version visible in every URL and log | Hidden unless logs capture headers | Hidden | Visible but easy to drop |
| 2. Cacheability | Trivial — URL fully identifies resource | Requires `Vary: Api-Version` on every response | Requires `Vary: Accept` | Trivial but visually noisy |
| 3. Tooling friction | Native in Swagger, SDK gen, YARP, rate-limit configs | Tooling support weaker | Media-type dispatch is rarely used; tooling gaps | Query-strings complicate caching rules in some CDNs |
| 4. Teachability | Obvious — "just add `/v1/`" | Requires explaining header dispatch | Requires explaining media-type negotiation | Obvious but less idiomatic |
| 5. Rate-limit compat | Per-route policies naturally differ per version | Requires a custom partition key | Same as header | Works but URL + query combinations explode |

## Decision

We will use **Option 1: URL path versioning** with the convention `/api/v{major}/{bc}/...` for BC APIs and `/api/v{major}/bff/...` for BFF endpoints. Major version is the only path-visible version; minor/patch changes are made in place with backward compatibility.

## Rationale

Option 1 wins on discoverability and tooling friction, which matter most for a reference solution. A reader or operator looking at a log line, an OpenAPI spec, or a YARP route config sees `/api/v1/...` and understands immediately. That signal is absent in Options 2 and 3, where version lives in a request header that normal `curl` usage doesn't print.

Cacheability is a secondary but real win. The BFF in this solution caches product detail responses (`bff.md`); having the version as part of the cache key (automatically, because it's in the URL) eliminates a whole class of version-mismatch cache bugs. Options 2–3 require `Vary:` headers on every response, which is one more thing to get right.

Option 1 does have aesthetic critics ("not properly RESTful") — REST purists prefer media-type versioning. The reference solution optimizes for teaching and operational simplicity, not URI-design purity.

## Consequences

### Positive

- Version visible in every log line, trace, Swagger UI, YARP route, rate-limit rule.
- Route explosion is explicit, not hidden — two versions means two `MapGroup("/api/v1/catalog")` and `MapGroup("/api/v2/catalog")` declarations. This is a feature: evolution has a visible cost.
- Rate-limit policies (per `rate-limiting.md`) partition by URL path naturally.
- Swagger generates per-version documents cleanly via `Asp.Versioning.ApiExplorer`.
- Client SDKs auto-generate with versioned base URLs.

### Negative

- Route clutter — every BC gets a `/api/v1/{bc}/...` prefix on every route. Mitigation: `MapGroup` + `RequireRateLimiting` at the group level keeps per-route lines clean.
- Minor / patch changes look like "no version change" which may surprise readers. Mitigation: document "major version = breaking change" explicitly.
- Cross-version resource sharing (same entity, two shapes) is slightly awkward; both routes hit the same domain but map to different DTOs.
- Admin endpoints — whether they get versioned too or are exempt — needs a call. We version them too (for consistency) even though admin consumers are all internal.

### Risks

- **Version forks silently accumulate** — a contributor adds `/v2/` for a field rename and never deprecates v1. Mitigation: each new version must ship with a deprecation timeline in its introducing PR.
- **Gateway routing complexity grows with each version** — YARP config must distinguish `v1` and `v2` paths. Minor; YARP supports path-prefix matching natively.

## Implementation Notes

- Use `Asp.Versioning.Http` + `Asp.Versioning.Mvc.ApiExplorer` packages (or FastEndpoints' versioning if FastEndpoints is the chosen endpoint framework — pick once and use consistently).
- Route convention in each BC's `Api/Program.cs`:
  ```csharp
  var v1 = app.MapGroup("/api/v1/catalog").WithTags("Catalog v1");
  v1.MapGet("/products/{id:guid}", GetProductById).WithName("GetProductV1");
  ```
- Swagger: one document per major version (`/swagger/v1/swagger.json`). OpenAPI group name follows `v{major}` convention.
- YARP routes: path prefix `/api/v1/**` → backend service; one YARP route per version. Rate-limit policies attached per version.
- BFF aggregates from upstream `/v1/` routes. BFF's own version (`/api/v1/bff/...`) is independent of the versions it calls; BFF may call `catalog/v1` while exposing `bff/v1` or `bff/v2`.
- Version bump workflow:
  1. Create `/v2/` routes alongside `/v1/` in a feature branch.
  2. Write v2 integration tests; v1 tests continue to pass.
  3. Land the PR with docs noting the deprecation date (minimum 90 days from release; adjusted per reader feedback).
  4. After the deprecation window, remove `/v1/` routes in a follow-up PR.
- Health, metrics, liveness, readiness endpoints are NOT versioned (`/api/healthz`, `/api/readiness`, `/metrics` — see [`Platform.ServiceDefaults.WebApplicationExtensions`](../../platform/Platform.ServiceDefaults/WebApplicationExtensions.cs)). These are platform concerns, not API.
- Docs convention: every HTTP route in `bc-design/*.md`, `implementation-prompts/*.md`, `use-cases.md` uses the versioned form.

## Related Decisions

- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — rate limits partition by versioned route
- [ADR-0013: Idempotency-Key HTTP Pattern](0013-idempotency-key-http.md) — idempotency middleware registers per versioned endpoint group
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — complementary pattern for Kafka contracts; HTTP uses URL path version, Kafka uses schema-registry compatibility mode
