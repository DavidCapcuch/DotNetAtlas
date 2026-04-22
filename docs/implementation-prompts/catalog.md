# Master System Prompt — Implement the **Catalog** Bounded Context

> Paste this as the first message in a fresh Claude Code session for `C:\Users\dcapc\Desktop\Git\DotNetAtlas`.

<thinking_first>
Before writing any code, do these in your **first response** — explicitly, in order:

1. **Read every file under `<reading_order>`** in order. State your understanding of what's locked vs open.
2. **Verify prerequisites.** List anything in `<prerequisites>` that isn't satisfied. STOP and ask if any.
3. **Surface contradictions** between docs (`file:line`). Do not silently pick a side.
4. **Confirm applicable ADRs.** For each ADR in `<applicable_adrs>`, name what it implies for THIS BC's code (one line each).
5. **State your plan.** Group `<dod>` items into commit milestones (typically 5–8). Confirm with the user before starting code.
6. **Acknowledge stop conditions** from this prompt and `_shared.md § 9`.
</thinking_first>

<mission>
You implement the **Catalog** bounded context. Your output is a working, tested, compiling service that showcases the **CQRS read-projection** pattern on PostgreSQL. When the session ends, `docker compose --profile full up -d` starts your service, every test passes, and Catalog's events are reliably published via the transactional outbox.
</mission>

<prerequisites>
- Wave 0 platform prep merged (`docs/implementation-prompts/wave-0-platform-prep.md`) — `Platform.SharedKernel` has `Money` / `Address`; BCL `TimeProvider` is auto-registered by the Generic Host; `Platform.ServiceDefaults` has correlation-id + service-auth; `docker-compose.yaml` has `catalog.products` + `catalog.categories` topics + `outbox-relay-catalog` container; Keycloak realm has `catalog-service` client.
</prerequisites>

<role_in_system>
Catalog is the **product-information authority** — the only BC that writes product + category data. Basket reads via ACL snapshots; Inventory consumes `ProductCreatedEvent` to initialize stock streams; BFF composes product detail pages. Catalog's teaching purpose: a **denormalized read projection** (`product_search_view`) built in-process by a domain-event handler **in the same transaction as the write-model save** — pure CQRS on one database, no eventual-consistency lag within the BC. Pricing stays **flat and inline** per [ADR-0002](../adr/0002-pricing-in-catalog.md); future Pricing extraction is a v2+ seam via `ProductPriceChanged`.
</role_in_system>

<contract>
LOCKED at the seams.

- Every external event under `Catalog.Products` + `Catalog.Categories` namespaces per `events-catalog.md § 5.1` (exact field sets; no shape changes)
- All commands + queries per `use-cases.md § 1` (HTTP routes, request shapes, validator rule sets — internal mechanics are yours)
- HTTP routes under `/api/v1/catalog/...` per ADR-0012
- Avro schemas FORWARD_TRANSITIVE per [ADR-0007](../adr/0007-avro-compatibility-modes.md)
- Topics `catalog.products` + `catalog.categories` (infinite retention, 3 partitions each)
- File ownership: see `<boundaries>`
</contract>

<design_open>
You own these. Justify each in your session summary.

- Concrete code structure inside Catalog's 4 layers (folders, internal helpers, seam interfaces)
- Specification classes for `SearchProductsQuery` + `GetProductsByCategoryQuery` (Ardalis.Specification patterns)
- Validator expression style + DRY helpers (RULES are listed in `use-cases.md`; mechanics are yours)
- `CatalogErrors` static-class API (types named in `error-taxonomy.md`; factory shape is yours)
- `ProductSearchViewProjectionHandler` organization — one class handling all 4 events OR one per event (justify either way)
- Test strategy depth — unit vs integration split; what to fake vs containerize
- Architecture-test tooling (NetArchTest or ArchUnit.NET — pick one, justify)
- Whether admin POST endpoints adopt FastEndpoints `.Idempotency()` (recommended for `CreateProduct`, optional for `Reactivate`)
- Additional `example-mapping/catalog.md` sessions if integration tests surface unwritten rules (e.g., "what happens when re-creating a Product with a soft-deleted SKU?")
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/catalog.md` + `glossary-catalog.md` + `example-mapping/catalog.md`
3. `docs/bc-design/events-catalog.md` § 5.1 (Catalog Avro schemas)
4. `docs/bc-design/use-cases.md` § 1 (Catalog commands + queries)
5. `docs/bc-design/error-taxonomy.md` § 3.2 (`CatalogErrors`)
6. `docs/eshop-master-design.md` § 3 (event discipline) + § 11.7 (async/sync)
7. `docs/adr/0002-pricing-in-catalog.md` + `0007-avro-compatibility-modes.md`
8. **All ADRs in `<applicable_adrs>` below**
9. Weather templates per `_shared.md § 4`
</reading_order>

<applicable_adrs>
Cross-cutting decisions to apply:

- [ADR-0008](../adr/0008-correlation-id-propagation.md) — every command/query handler reads ambient `CorrelationId` from `HttpContext`; outbox publishers copy it into the Avro event header; the `correlation_id` column on `product_search_view` carries it for forensic queries
- [ADR-0010](../adr/0010-service-to-service-auth.md) — inbound `JwtBearer` validation for admin endpoints; scopes `catalog.read` (queries) / `catalog.write` (admin commands); no outbound service-auth calls (Catalog doesn't call other BCs over HTTP)
- [ADR-0012](../adr/0012-api-versioning.md) — all routes under `/api/v1/catalog/...`; FastEndpoints `MapGroup("/api/v1/catalog")`
- [ADR-0013](../adr/0013-idempotency-key-http.md) — apply FastEndpoints `.Idempotency()` to admin POST endpoints (`CreateProductCommand`, `CreateCategoryCommand`) backed by `redis-cache`
- [ADR-0014](../adr/0014-feature-flags-openfeature.md) — explicit consumer of `catalog.show-discontinued-in-search` flag (gates a filter predicate in `ProductSearchViewProjectionHandler` / search query); use `IFeatureClient` from `Platform.ServiceDefaults.AddFeatureFlags()`
- [ADR-0015](../adr/0015-time-timezone-policy.md) — every timestamp `DateTimeOffset` (persisted as `timestamptz`); inject BCL `TimeProvider` for "now" (`FakeTimeProvider` in tests); arch test forbids `DateTime.UtcNow` in `Catalog.Domain`
- [ADR-0016](../adr/0016-redis-topology.md) — if you cache HTTP responses (e.g., `GetCategoryTreeQuery`), use `redis-cache` (NOT `redis-basket`); idempotency middleware also points at `redis-cache`
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. Catalog-specific:

| Phase | Skill | When |
|---|---|---|
| Designing the projection handler | `backend-development:cqrs-implementation` | before writing `ProductSearchViewProjectionHandler` — CQRS projection patterns |
| Designing GET endpoints | `backend-development:api-design-principles` | before wiring search + tree + by-ids endpoints — REST + pagination + filtering conventions |
| When projection composition gets complex | `superpowers:brainstorming` | before choosing one-class-per-event vs one-class-for-all-events |
</skills>

<autonomous_evolution>
Catalog-specific triggers:

- **`Money` VO ownership** — Wave 0 added `Money` to `Platform.SharedKernel`. Verify Catalog uses the shared-kernel one and does not re-declare it. If Wave 0 didn't land it, STOP and ask.
- **Category path recomputation** — `catalog.md` says path-recompute is a domain-service operation (outside the aggregate). Implement this; if you see a cleaner shape that keeps it in-aggregate without violating transactional scope, propose it.
- **`IsSellable` coupling** — Catalog consumes `StockLevelChanged` from Inventory to compute `IsSellable` on the read view. Verify the consumer group naming (no collision with Inventory's own groups per `events-catalog.md § 7`).
- **By-ids product endpoint** — BFF needs `GET /api/v1/catalog/products/by-ids?ids=…` for basket/order enrichment. Implement as `GetProductsByIdsQuery`, max 100 IDs per call, partial-tolerant response `{ products, missingProductIds }`.
- **Feature-flag-gated path** — verify `catalog.show-discontinued-in-search` is wired through `IFeatureClient.GetBooleanValueAsync(...)` and that disabling it actually filters discontinued products from `SearchProductsQuery` results (integration test required).
</autonomous_evolution>

<success_criteria>
- A Wave-2 (Checkout saga) and Wave-3 (BFF) agent can consume Catalog's external contract (`ProductCreatedEvent`, `ProductPriceChanged`, `ProductDiscontinuedEvent`, `CategoryCreatedEvent`) and HTTP API (`/api/v1/catalog/...`) without modifying Catalog code.
- Inventory's stream-init flow works: `ProductCreatedEvent` published → Inventory consumer initializes stream — verifiable by integration test running both BCs.
- Switching `catalog.show-discontinued-in-search` flag changes search results without redeploy.
- `product_search_view` is updated atomically with the write model on every command (single `SaveChangesAsync`).
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] `ProductSearchViewProjectionHandler` upserts in the same DbContext transaction as writes (verify by integration test: single `SaveChangesAsync` → both tables updated atomically)
- [ ] 4 external Avro schemas + 8 internal `*DomainEvent` records + 4 outbox publishers
- [ ] Architecture tests: (a) no cross-BC references; (b) aggregate private ctor + static factory; (c) internal vs external event naming (`_shared.md § 4` + `architecture-tests.md § Catalog`); (d) no `DateTime.UtcNow` in domain (ADR-0015)
- [ ] Integration tests cover both `example-mapping/catalog.md` sessions (reparent + reactivate) + the feature-flag-gated search path
- [ ] `CatalogErrors` static class mirrors `error-taxonomy.md § 3.2` row set
- [ ] BFF-facing endpoints stable + documented: `GET /api/v1/catalog/products/{id}`, `GET /api/v1/catalog/products?…` (paginated: `text`, `categoryPath`, `minPrice`, `maxPrice`, `status`, `page`, `limit`), `GET /api/v1/catalog/categories/tree`, `GET /api/v1/catalog/products/by-ids?ids=…` (by-ids, max 100, partial-tolerant)
- [ ] Admin POST endpoints have `.Idempotency()` filter backed by `redis-cache` (per ADR-0013)
- [ ] Correlation-id roundtrips: HTTP → handler → outbox row → Kafka header (integration test)
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:** `services/Catalog/**`, `test/Catalog.*.Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/**`, `docker-compose.yaml` (Catalog topics already exist post-Wave 0; only touch if you find drift), `Directory.Packages.props` (Catalog-specific packages only), `docs/bc-design/catalog.md` + glossary + example-mapping (self-correction only).

**Do not touch:** other services, saga, platform code (except `.avsc`), Weather, other BCs' Avro schemas, other BCs' topic entries, `Platform.ServiceDefaults` or `Platform.SharedKernel` (Wave 0 owns those).
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- `Platform.SharedKernel.Money` does not exist (Wave 0 prerequisite missing).
- The `catalog.products` topic doesn't exist when `docker compose --profile full up -d` runs.
- `events-catalog.md § 5.1` and `catalog.md § 4` disagree on event field sets.
- A `use-cases.md § 1` command is missing from the BC chapter (or vice versa).
- The `catalog.show-discontinued-in-search` flag isn't readable via `IFeatureClient` (means Wave 0 OpenFeature wiring is broken — escalate, don't work around).
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Suggested commit milestones:

1. Scaffold 4 layers + project references; verify Wave 0 platform additions resolve; `dotnet build` green
2. Domain layer (`Product`, `Category`, VOs, SmartEnums, internal events) + unit tests
3. Application layer (commands, queries, validators, projection handler, outbox publishers) + outbox integration test
4. Infrastructure layer (DbContext, EF mappings, Kafka consumer for `StockLevelChanged`, DI) + integration test
5. Architecture tests
6. Functional tests (HTTP roundtrips through `/api/v1/catalog/...`)
7. docker-compose smoke + Avro schema registration
8. Docs self-corrections + session summary
</session_management>

<verification>
```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Catalog.UnitTests/
dotnet test test/Catalog.ArchitectureTests/
dotnet test test/Catalog.IntegrationTests/
dotnet test test/Catalog.FunctionalTests/
docker compose --profile full up -d
# Catalog smoke checks
curl -s http://localhost:8080/api/v1/catalog/categories/tree | jq .
curl -s "http://localhost:8080/api/v1/catalog/products?text=demo&page=1&limit=10" | jq .
# Verify topics exist with correct retention
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic catalog.products
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic catalog.categories
```

Paste actual output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
**Question:** `ProductSearchViewProjectionHandler` organization — one class handling all 4 events, or one class per event?

**Bad answer:** "One per event for cleanliness."

**Good answer:** "One class per event (4 handlers). Reasons: (1) test isolation — `WhenProductPriceChanged_ProjectionUpdatesAmountColumn` is one test class with one fixture, not one class with 4 fixtures; (2) DI ordering — `IDomainEventHandler<T>` is registered per closed type so per-event classes match the dispatcher's natural scan; (3) consumer-group analogy — Kafka consumers are per-event-type, the projection mirrors that. Trade-off accepted: 4 small classes (~30 LOC each) instead of 1 multiplexer; the duplication is `_db.Set<ProductSearchViewRow>().Update(...)` which is shorter than the multiplexer's switch statement. Verified by `ProductSearchViewProjectionTests` covering all 4 events independently."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`. Catalog-specific notes:

- Decision on projection handler organization (one-per-event vs multiplexer) + rationale
- Decision on `Money` VO source (shared-kernel vs duplicated) — should be shared-kernel post-Wave 0
- Feature-flag verification — confirm `catalog.show-discontinued-in-search` flips behaviour without restart
- ADR-0008 correlation-id roundtrip evidence (which test verifies HTTP → outbox → Kafka header)
- ADR-0013 idempotency — which admin endpoints adopted `.Idempotency()`

Proceed.
</session_summary>
