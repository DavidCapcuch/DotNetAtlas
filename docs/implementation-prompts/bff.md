# Master System Prompt — Implement the **EShop.BFF** (Backend-for-Frontend, Wave 3)

> **The live exemplar of this kit.** The BFF is the one unbuilt unit; this prompt is both its dispatch spec and the worked example of `_template.md` — pointer-based DDD/EDA contract, non-negotiable gates with pasted output, and `daca-bc-consistency-reviewer` as the final DoD step (archetype: 2-layer aggregation gateway). Current truths it threads: **one consumer group per service** = `bff-group` ([`events-catalog.md § 3.1`](../bc-design/events-catalog.md)); the durable business key is **`OrderId`** ([ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)); telemetry correlates on W3C **`traceId`**; the FusionCache backplane is **`redis-cache`**, never `redis-basket` ([ADR-0016](../adr/0016-redis-topology.md)).

> Paste this as the first message in a fresh Claude Code session for your local DotNetAtlas clone.

<thinking_first>
Before writing any code, do these in your **first response** — explicitly, in order:

1. **Read every file under `<reading_order>`** in order. State your understanding of what's locked vs open.
2. **Verify prerequisites.** List anything in `<prerequisites>` that isn't satisfied. STOP and ask if any.
3. **Surface contradictions** (`file:line`).
4. **Confirm applicable ADRs.** For each ADR in `<applicable_adrs>`, name what it implies for THIS BC's code.
5. **State your plan.** Group `<dod>` items into commit milestones. Confirm with the user before starting code.
6. **Acknowledge stop conditions** from this prompt and `_shared.md § 9`.
</thinking_first>

<mission>
You implement the **EShop BFF** (Backend-for-Frontend) at `src/EShop.BFF/`. The BFF is NOT a bounded context — it's an **aggregation gateway** that composes responses from Catalog + Basket + Ordering + Inventory (+ Invoicing + Payments in follow-ups). Your output is a new service that exposes 4 public aggregation endpoints **plus the basket write surface** (item mutations + checkout, forwarded to Basket — consumer basket access is BFF-mediated, there is **no** direct SPA→Basket path), consumes BC events for cache invalidation, and reaches buyer-scoped BCs via RFC 8693 token exchange. When the session ends, the BFF runs alongside the BCs in `docker compose --profile full`, serves home-page + product-detail + basket + order-summary with reasonable caching and fail-safe behaviour, and fronts the basket mutations + checkout.
</mission>

<prerequisites>
- **Wave 1 BCs scaffolded at minimum** (Catalog HTTP endpoints reachable; Basket + Ordering + Inventory + Payments + Invoicing optional but preferred).
- **Wave 2 Checkout saga implemented** — BFF's `POST /api/v1/bff/checkout` triggers the saga flow.
- Wave 0 platform prep merged. Specifically: `redis-cache` container running; `Platform.ServiceDefaults` has service-auth + feature-flags + JSON `DateTimeOffset` converter; Keycloak `bff` service client with scopes `catalog.read`, `basket.read`, `basket.write`, `ordering.read`, `inventory.read`, `invoicing.read` (the buyer-scoped Basket/Ordering/Invoicing routes are reached via RFC 8693 token exchange so the buyer `sub` is preserved — see [bff.md § 2.3](../bc-design/bff.md), decided [#323](https://github.com/DavidCapcuch/DotNetAtlas/issues/323)).
</prerequisites>

<role_in_system>
The BFF sits between clients (SPA / mobile / YARP) and internal services. Teaching purposes:

1. **Cross-BC composition** — product-page needs Catalog + Inventory; basket needs Basket + Catalog (current prices) + Inventory (availability)
2. **Resilience** — Polly timeout + retry + circuit-breaker shields BCs from client abuse or upstream outages
3. **Edge caching** — FusionCache + Redis backplane on `redis-cache`, with fail-safe serving stale data on upstream failure
4. **Cache invalidation via Kafka** — BFF subscribes to BC external events and invalidates FusionCache tags
</role_in_system>

<contract>
LOCKED at the seams.

- 4 aggregation GET endpoints per `bff.md § 3`: `GET /api/v1/bff/home-page`, `GET /api/v1/bff/product-page/{id}`, `GET /api/v1/bff/basket`, `GET /api/v1/bff/order-summary/{id}`; plus `POST /api/v1/bff/checkout` (per ADR-0013)
- Basket write surface forwarded to Basket (`bff.md § 3.6`) — consumer basket access is BFF-mediated, no direct SPA→Basket path (`bff.md § 2.5`): `POST /api/v1/bff/basket/items`, `PUT /api/v1/bff/basket/items/{productId}/quantity`, `DELETE /api/v1/bff/basket/items/{productId}`, `DELETE /api/v1/bff/basket/items` — thin forwarders via `basket.write` token exchange; the BFF invalidates `basket-bff-{userId}` synchronously on success
- Response shapes per `bff.md § 3` (each endpoint documents its composed JSON)
- Cache-invalidator consumer group: **`bff-group`** — one group per service per [`events-catalog.md § 3.1`](../bc-design/events-catalog.md) (NOT a per-purpose `bff-cache-invalidator` group)
- Invalidation topic → FusionCache tag map per `bff.md § 2.2`
- Topics consumed (five — the v1 `bff-group` set per [`events-catalog.md § 2`](../bc-design/events-catalog.md)): `catalog.products`, `catalog.categories`, `inventory.stock-events`, `ordering.orders`, `basket.sessions`. **NOT `invoicing.invoices`** — a BFF invoice view is *planned-not-v1* (events-catalog § 5: `InvoiceIssuedEvent` has **no v1 BFF consumer** by design); if you add it, see `<autonomous_evolution>`
- Outbound auth per ADR-0010 (§ 2.3): public endpoints send no token; non-buyer-scoped reads (Catalog, Inventory) attach a `client_credentials` service token via `AddServiceAuth`; buyer-scoped calls (Basket read + mutations + checkout; Ordering / Invoicing reads) use an **RFC 8693 token exchange** of the user JWT — preserves the buyer `sub`, re-audiences to the callee. (Supersedes the pre-#323 "forward user JWT + attach service token" model: one exchanged token reaches the callee, not two.)
- NO outbox, NO new Avro schemas, NO `DbSet<>` — BFF is stateless
- Rate limiting lives in YARP (NOT the BFF) — see `rate-limiting.md`
- HTTP routes under `/api/v1/bff/...` per ADR-0012
- File ownership: see `<boundaries>`

> **EDA / architecture discipline is contract, not taste.** Consumer-group naming (`bff-group`), the consume-only (no outbox / no producer) rule, result-returning typed clients, and Api→Infrastructure layering are SSOT in [`conventions.md`](../bc-design/conventions.md) and **executably enforced** by the BFF's arch tests (no `DbSet<>`, no Kafka producer, backplane ≠ `redis-basket`). A change that would fail an arch test is a real failure, not a style nit.
</contract>

<design_open>
You own these. Justify each in your session summary.

- Polly pipeline composition (order + granularity of timeout / retry / circuit-breaker — `bff.md § 2.1` gives target values; use Wave 0's named resilience presets from `Platform.ServiceDefaults` where applicable)
- Typed client interfaces + DTOs (method signatures per `bff.md § 4`; DTO shapes are yours, with Result-returning methods)
- FusionCache configuration (tags, TTLs, FailSafeMaxDuration — defaults in `bff.md`; tune if justified)
- Endpoint handler composition strategy (manual aggregation vs mediator pipeline)
- Fallback response shape when an upstream fails (include `HasStaleData: true` indicator? — ADR-worthy if you introduce it)
- Integration test harness: containerized upstreams OR in-memory stubs
- Architecture tests: no `DbSet<>`, no Kafka producer (only consumer)
- Authentication-forwarding middleware shape (`DelegatingHandler` subclass vs `IHttpClientFactory` message-handler chain)
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/bff.md` — the full spec
3. `docs/bc-design/rate-limiting.md` — YARP is in front of you; the BFF itself is unrestricted
4. `docs/bc-design/events-catalog.md` — your invalidator consumes these topics
5. `docs/bc-design/bff.md § 5` (Shared Upstream Batch Reads) + the per-BC **HTTP surface** subsections in `use-cases.md` — your upstream contract. Catalog's bulk `GetProductsByIdsQuery` (`GET /api/v1/catalog/products/by-ids`) backs `/api/v1/bff/basket`; if any required upstream endpoint were actually missing, FLAG per `<stop_conditions>` rather than working around it.
6. `docs/eshop-master-design.md` — the BFF-relevant sections (§ 3 event discipline, § 9 BFF overview, § 11.3 observability, § 11.7 async/sync). **Additive to `_shared.md § 2`'s universal reads, not a replacement.**
7. **All ADRs in `<applicable_adrs>` below**
8. Golden reference (`_shared.md § 4`): **Basket** (`services/Basket/`) for the FusionCache + Redis wiring, and existing BCs' typed-client setup (`grep -rn "AddHttpClient" services/`).
</reading_order>

<applicable_adrs>
Cross-cutting decisions to apply:

- [ADR-0010](../adr/0010-service-to-service-auth.md) — **BFF makes outbound HTTP to every BC.** Non-buyer-scoped reads (Catalog, Inventory) use a `client_credentials` service token via `IHttpClientBuilder.AddServiceAuth("<scope>")` (`catalog.read`, `inventory.read`). **Buyer-scoped calls** (Basket `GET /basket` + `POST /checkout`; buyer-owned Ordering / Invoicing reads) use an **RFC 8693 token exchange** via the `bff` client's matching scope (`basket.read` / `basket.write` / `ordering.read` / `invoicing.read`) so the buyer `sub` is preserved — rationale in [bff.md § 2.3 / § 3.5](../bc-design/bff.md), decided [#323](https://github.com/DavidCapcuch/DotNetAtlas/issues/323)
- [ADR-0011](../adr/0011-pii-handling-gdpr.md) — BFF composes addresses + buyer names into responses but **does NOT persist them**; OTEL allowlist forbids tagging spans with composed address fields; Serilog `[PII]` policy applies
- [ADR-0012](../adr/0012-api-versioning.md) — all routes under `/api/v1/bff/...`
- [ADR-0013](../adr/0013-idempotency-key-http.md) — **`POST /api/v1/bff/checkout` is the #1 idempotency target** (customer double-click on pay) via FastEndpoints `.Idempotency()` backed by `redis-cache`
- [ADR-0014](../adr/0014-feature-flags-openfeature.md) — **explicit consumer of `bff.home-page-eager-cache-warm`** flag — read via `IFeatureClient` in the startup `IHostedService` that pre-warms the home-page cache; kill-switch pattern (default ON; flip OFF under load)
- [ADR-0015](../adr/0015-time-timezone-policy.md) — BFF response DTOs use `DateTimeOffset` for any timestamp field; upstream DTO mapping preserves offset
- [ADR-0016](../adr/0016-redis-topology.md) — BFF's FusionCache backplane points at `redis-cache` (volatile); NEVER `redis-basket` (arch test asserts this)
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. BFF-specific:

| Phase | Skill | When |
|---|---|---|
| Designing endpoint composition | `backend-development:api-design-principles` | before wiring the 4 aggregation endpoints — REST aggregation, response composition, content negotiation |
| Pattern depth for BFF | `backend-development:microservices-patterns` | BFF-pattern specifically; when to aggregate vs orchestrate |
| Polly + cache tuning | `Agent(subagent_type="application-performance:performance-engineer")` | when tuning timeouts, retry policies, cache TTLs, circuit-breaker thresholds — this is an **agent**, not a Skill; dispatch it, don't `Skill(...)` it |
| Invalidation-consumer semantics | `grill-with-docs` (phase 0) | invalidator has subtle ordering / fan-out semantics — sharpen against `bff.md § 2.2` + the events-catalog before committing |
</skills>

<autonomous_evolution>
BFF-specific triggers:

- **Catalog's by-ids endpoint** — if `GetProductsByIdsQuery` / `GET /api/v1/catalog/products/by-ids?ids=…` does not exist in Catalog, STOP and report per `<stop_conditions>`. Do NOT implement a workaround (e.g., N concurrent single-product calls) without user approval.
- **Payments/Invoicing BFF integration** — `/api/v1/bff/order-summary/{id}` may need invoice link (`GET /api/v1/invoicing/invoices/by-order/{orderId}`). Confirm whether Invoicing is in scope for v1 of BFF or stubbed; leave clear TODO + feature flag if deferred.
- **Stale-data response header** — if you introduce `HasStaleData` / `X-BFF-Stale: true` semantics, document in `bff.md § 3` (Endpoints / response shapes) or § 6 (Failure-Mode table) as a self-correction before implementation.
- **Cache-stampede** on home-page FusionCache expiry — confirm FusionCache's stampede protection is enabled (default with `EagerRefreshThreshold`); coordinates with `bff.home-page-eager-cache-warm` feature flag.
- **Feature-flag-gated path** — verify `bff.home-page-eager-cache-warm` controls the startup `IHostedService` that warms the cache; flipping it OFF must cleanly skip the warm (no half-baked state).
</autonomous_evolution>

<success_criteria>
- Home page, product page, basket, and order summary return composed JSON end-to-end with all upstream BCs running.
- Simulating a Catalog outage causes `/api/v1/bff/home-page` to return stale-but-valid cached data (verified via integration test that kills the Catalog container mid-flight).
- Client retry of `POST /api/v1/bff/checkout` with the same `Idempotency-Key` returns the original response (verified via integration test).
- Flipping `bff.home-page-eager-cache-warm` OFF in the flags file stops the warm-up `IHostedService` on next restart without errors.
- Arch test asserts BFF's FusionCache backplane connection string is `Redis:Cache`, never `Redis:Basket`.
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12` adapted (2 layers not 4):

- [ ] `EShop.BFF.Api` + `EShop.BFF.Infrastructure` projects compile
- [ ] 4 aggregation GET endpoints + `POST /checkout` + the basket write forwarders (`POST/PUT/DELETE /api/v1/bff/basket/items*`, `bff.md § 3.6`) + typed HTTP clients + response DTOs
- [ ] Polly resilience pipeline using Wave 0's named presets where applicable: per-call 2s (batch 10s) timeout, max 2 retries with exponential backoff, CB opens after 5/10s fail-rate
- [ ] Outbound auth per ADR-0010 (§ 2.3): `client_credentials` service token on non-buyer-scoped reads (Catalog, Inventory); RFC 8693 token exchange on buyer-scoped calls (Basket read + mutations + checkout; Ordering / Invoicing reads) preserving the buyer `sub`; public endpoints send no token
- [ ] Kafka cache-invalidator consumer (group `bff-group`) — topic→tag map per `bff.md § 2.2`
- [ ] FusionCache + Redis distributed cache + backplane pointed at `redis-cache` (per ADR-0016)
- [ ] `POST /api/v1/bff/checkout` has `.Idempotency()` (per ADR-0013)
- [ ] `bff.home-page-eager-cache-warm` feature flag wired + both states tested
- [ ] All routes under `/api/v1/bff/...` (per ADR-0012)
- [ ] Architecture tests: no `DbSet<>` in BFF; no Kafka producer; only consumer + HTTP client + cache; FusionCache backplane ≠ `Redis:Basket`
- [ ] Integration tests: (a) happy-path composition; (b) upstream timeout → fail-safe returns stale cache; (c) upstream 5xx → fallback; (d) invalidator consumer fires on fake `ProductPriceChanged` → tag removed; (e) checkout idempotency
- [ ] Docker-compose: BFF container + healthcheck (no new topics, no outbox-relay)
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Review stack (`_shared.md § 11`) run end-to-end: Opus pre-commit → gates pasted → `daca-dod-reviewer` blockers fixed (Role 3; delegates to `daca-bc-consistency-reviewer` + `daca-documentation-reviewer`); its Self-attested bucket attested
</dod>

<boundaries>
**You may write:** `src/EShop.BFF/**`, `test/EShop.BFF.*.Tests/**`, `docker-compose.yaml` (BFF container only — no new topics or relays), `DotNetAtlas.slnx` (new projects), `Directory.Packages.props` (Polly + FusionCache if missing), `docs/bc-design/bff.md` (self-correction only).

**Do not touch:** `services/*`, `saga/*`, `platform/*` (NO `.avsc` changes — you only consume), any BC's docker-compose entry.
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- **Catalog's `GetProductsByIdsQuery` endpoint does not exist** — BFF is BLOCKED on basket/order enrichment. Do NOT workaround with N single-product calls.
- **Wave 2 Checkout saga is not implemented** — `POST /api/v1/bff/checkout` has no downstream; test-harness must simulate or session must defer.
- Any typed HTTP client can't resolve its upstream BC's base URL (means the service isn't up or isn't in docker-compose).
- `redis-cache` is not running at `redis-cache:6379` (Wave 0 prerequisite missing).
- Feature-flag provider (`IFeatureClient`) is not resolvable from DI (Wave 0 `AddFeatureFlags` wiring missing).
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Suggested commit milestones:

1. Scaffold 2 layers + project references; `dotnet build` green
2. Typed clients (one per upstream BC) with Polly presets + outbound auth (service token for non-buyer-scoped reads; token exchange for buyer-scoped calls, § 2.3)
3. FusionCache config against `redis-cache` + backplane + arch test
4. 4 aggregation GET endpoints + response DTOs + happy-path integration test
5. `POST /api/v1/bff/checkout` with `.Idempotency()` + basket write forwarders (`bff.md § 3.6`, sync cache invalidation) + integration tests
6. Kafka invalidator consumer (group `bff-group`) + tag-map + invalidation integration test
7. `bff.home-page-eager-cache-warm` feature flag + startup warmer + both-state tests
8. Fail-safe / stale-data integration tests (kill upstream containers mid-flight)
9. Architecture tests + integration tests
10. docker-compose smoke + session summary
</session_management>

<verification>
Run the **non-negotiable gates** via `daca-gates` (build / restore / format / the three `EShop.BFF.*Tests` projects / compose health; repo deltas in [`.claude/verification-gates.md`](../../.claude/verification-gates.md)), then the BFF smoke below. **Paste the actual output** into the session summary.

```bash
# BFF smoke, after the standard gates:
curl -s http://localhost:8080/api/v1/bff/home-page | jq .
curl -s http://localhost:8080/api/v1/bff/product-page/<productId> | jq .
curl -s -H "Authorization: Bearer <user-jwt>" http://localhost:8080/api/v1/bff/basket | jq .
curl -s -H "Authorization: Bearer <user-jwt>" http://localhost:8080/api/v1/bff/order-summary/<orderId> | jq .
# Checkout idempotency: same key twice should return same response
curl -X POST -H "Idempotency-Key: 0193..." -H "Authorization: Bearer ..." http://localhost:8080/api/v1/bff/checkout
curl -X POST -H "Idempotency-Key: 0193..." -H "Authorization: Bearer ..." http://localhost:8080/api/v1/bff/checkout  # same output
# Simulate Catalog down → home-page returns stale cached data
docker compose stop catalog-api
curl -s http://localhost:8080/api/v1/bff/home-page  # should still 200 with stale data + maybe X-BFF-Stale: true
docker compose start catalog-api
```

Paste actual output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
**Question:** Endpoint handler composition strategy — manual aggregation (parallel awaits in the endpoint) vs mediator pipeline (CQRS-style query handlers)?

**Bad answer:** "Mediator for consistency with BCs."

**Good answer:** "Manual aggregation via typed clients inside each endpoint. Reasons: (1) BFF is stateless + composition-heavy — CQRS pattern here is overkill because there's no domain model to protect, just upstream calls to orchestrate; (2) `Task.WhenAll(catalogCall, inventoryCall)` is explicit and debugs cleanly in Jaeger; a mediator would add an indirection layer for zero benefit; (3) `bff.md § 5` writes each endpoint as 3–5 upstream calls with clear concurrency shape — lifting that into handler classes obscures the aggregation pattern. Trade-off accepted: BFF endpoints have more LOC than equivalent CQRS handlers; justified because the LOC is the aggregation logic, which is exactly what we want to show. Verified by `HomePageEndpointTests` asserting exactly 2 parallel upstream calls per request."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11` (three roles). Before declaring DoD met:

1. **Role 1** — Opus `feature-dev:code-reviewer` ran pre-commit on every ≥ 5-file milestone; CRITICAL/HIGH fixed.
2. **Role 2 (gate)** — run every command in `<verification>`; paste the **actual** output; invoke `superpowers:verification-before-completion`.
3. **Role 3 (DoD gate)** — self-attest the **Self-attested** bucket of `daca-dod-reviewer`'s bar in your summary, then run `daca-dod-reviewer` on your diff with its delegates run as siblings: Architecture/DDD → `daca-bc-consistency-reviewer` (archetype: 2-layer aggregation gateway) vs the golden reference + `conventions.md` + the BFF arch tests; Documentation → `daca-documentation-reviewer`. Fix every blocker.
4. **Security pass** — run `/security-review` scoped to the auth-forwarding (user-JWT + service-token attachment) and PII-composition paths. The BFF is the system's highest-risk surface and arch tests won't catch a leaked token, a JWT written to a log, or a span tagged with a composed address (ADR-0011 forbids it). (This is the `daca-dod-reviewer`'s applicability-gated `/security-review` trigger — auth / PII / secrets / new external endpoint — firing here; the BFF is its highest-risk instance, not a special case.)
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`, plus BFF-specifics:

- Polly tuning (timeout + retry + CB values) with rationale for any deviation from `bff.md`
- Upstream endpoint coverage (which upstream endpoints existed? any missing that you flagged?)
- Cache-invalidation verification (integration test for `ProductPriceChanged` → tag removal)
- Payments/Invoicing v1 integration status (invoice link present, deferred, or stubbed)
- ADR-0010 service-auth — every typed client carries service token AND user JWT (where authed)
- ADR-0013 idempotency — checkout replay test evidence
- ADR-0014 feature flag — `bff.home-page-eager-cache-warm` both states verified
- ADR-0016 Redis — arch test asserts backplane points at `redis-cache`

Proceed.
</session_summary>
