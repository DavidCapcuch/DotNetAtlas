# Master System Prompt — Implement the **Basket** Bounded Context

> Paste this as the first message in a fresh Claude Code session for `C:\Users\dcapc\Desktop\Git\DotNetAtlas`.

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
You implement the **Basket** bounded context — a Redis-backed aggregate with an Anti-Corruption Layer to Catalog. When the session ends, Basket's aggregate lives in `redis-basket` (MemoryPack + FusionCache + AOF), its SQL side-car schema holds only outbox + inbox, and `BasketCheckoutInitiatedEvent` triggers the Checkout saga reliably via the transactional outbox.
</mission>

<prerequisites>
- Wave 0 platform prep merged. Specifically: `redis-basket` container running with AOF; `redis-cache` available for output cache; `basket.sessions` topic + `outbox-relay-basket` container; Keycloak `basket-service` client; `Platform.SharedKernel` has `Money` + `Address`; `Platform.ServiceDefaults` has correlation-id + service-auth.
- Catalog's `GetProductByIdQuery` + `GetProductsByIdsQuery` HTTP endpoints reachable (Catalog scaffolded — at minimum its API contract). Basket can scaffold + unit-test in parallel with Catalog; integration-test the ACL after Catalog lands.
</prerequisites>

<role_in_system>
Per [ADR-0003](../adr/0003-basket-as-technical-bc.md), Basket is a **technical / session bounded context** — not a domain BC. Teaching purposes:

1. **Redis-backed aggregate** — the only aggregate in the solution that lives in a distributed cache instead of an OLTP table.
2. **Anti-Corruption Layer** — `IProductCatalogQueryPort` + `ProductCatalogHttpAdapter` translate Catalog DTOs into a local `ProductSnapshot` VO; Basket never references Catalog types.
3. **Frozen-pricing contract** — snapshot price at add-time is legally binding through checkout; price drift is surfaced by BFF, never auto-applied.

Downstream: BFF `GET /api/v1/bff/basket`; Checkout saga triggered by `BasketCheckoutInitiatedEvent`.
</role_in_system>

<contract>
LOCKED at the seams.

- One external event `BasketCheckoutInitiatedEvent` under namespace `Basket.Sessions` per `events-catalog.md § 5.2.1` (includes nested `CheckoutAddress` record + `PaymentMethodId`)
- Topic `basket.sessions` (30-day retention per `events-catalog.md` D-8)
- 6 commands + 1 query per `use-cases.md § 2` — especially `CheckoutBasketCommand` with **required** `Idempotency-Key` header (400 if missing)
- HTTP routes under `/api/v1/basket/...` per ADR-0012
- Addresses + payment-method flow through Basket from the BFF; Basket does NOT own them (per [ADR-0005](../adr/0005-customer-data-in-ordering.md))
- Schema FORWARD_TRANSITIVE per [ADR-0007](../adr/0007-avro-compatibility-modes.md)
- File ownership: see `<boundaries>`
</contract>

<design_open>
You own these. Justify each in your session summary.

- `IBasketRepository` shape (CAS pattern via Lua script or FusionCache conditional set — both acceptable)
- Optimistic-concurrency envelope (`Version` field in the MemoryPack payload or a separate key)
- ACL adapter config in `ProductCatalogHttpAdapter`: `BaseAddress`, request `Timeout`, `AddCorrelationId()`, `AddServiceAuth("catalog.read")`. No Polly — cross-service HTTP resilience is handled by YARP at the edge.
- Post-checkout Redis-delete mechanics (direct `IConnectionMultiplexer` call bypassing FusionCache — you decide if there's a cleaner shape)
- Architecture-test enforcement that `BasketDbContext` has NO `DbSet<Basket>` (only outbox + inbox)
- Error-class API for `BasketErrors`
- Additional `example-mapping/basket.md` sessions for edge cases (e.g., "add to basket when Redis is unavailable — fail-fast vs queue")
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/basket.md` + `glossary-basket.md` + `example-mapping/basket.md`
3. `docs/bc-design/events-catalog.md` § 5.2 (Basket Avro)
4. `docs/bc-design/use-cases.md` § 2 (commands + query — note `CheckoutBasketCommand` with addresses)
5. `docs/bc-design/error-taxonomy.md` § 3.1 (`BasketErrors`)
6. `docs/eshop-master-design.md` § 3 + § 11.7
7. `docs/adr/0003-basket-as-technical-bc.md` + `0005-customer-data-in-ordering.md`
8. **All ADRs in `<applicable_adrs>` below**
9. Weather FusionCache reference: `src/Weather.Infrastructure/Common/PersistenceDependencyInjection.cs` + `src/Weather.Application/WeatherForecast/Services/CachedWeatherForecastService.cs`
</reading_order>

<applicable_adrs>
Cross-cutting decisions to apply:

- [ADR-0008](../adr/0008-correlation-id-propagation.md) — every command handler reads ambient `CorrelationId`; outbox publisher copies into the `BasketCheckoutInitiatedEvent` Avro header; inbox dedup key is `(UserId, CorrelationId)`
- [ADR-0010](../adr/0010-service-to-service-auth.md) — inbound JWT validation; `ProductCatalogHttpAdapter` outbound calls add service-auth token via `IHttpClientBuilder.AddServiceAuth("catalog.read")` extension
- [ADR-0012](../adr/0012-api-versioning.md) — all routes under `/api/v1/basket/...`
- [ADR-0013](../adr/0013-idempotency-key-http.md) — `POST /api/v1/basket/items` (double-click guard) AND `POST /api/v1/basket/checkout` (most expensive); use FastEndpoints `.Idempotency()` backed by `redis-cache`
- [ADR-0015](../adr/0015-time-timezone-policy.md) — `DateTimeOffset` for `CapturedAtUtc` on `ProductSnapshot`, `LastModifiedUtc` on `Basket`; inject BCL `TimeProvider` (use `FakeTimeProvider` in tests); arch test forbids `DateTime.UtcNow` in `Basket.Domain`
- [ADR-0016](../adr/0016-redis-topology.md) — Basket aggregate primary store is `redis-basket` (AOF, noeviction); idempotency middleware uses `redis-cache`. The two Redis instances must NOT be confused — arch test asserts `IBasketRepository` only references `Redis:Basket` connection string
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. Basket-specific:

| Phase | Skill | When |
|---|---|---|
| Designing the ACL adapter | `backend-development:architecture-patterns` | before `ProductCatalogHttpAdapter` — hexagonal / ports-and-adapters patterns |
| Classifying Basket as a BC | `modularity:balanced-coupling` | if tempted to add domain richness — this skill helps you evaluate "is this actually a domain BC?" |
| Designing Redis CAS | `superpowers:brainstorming` | before writing the concurrency envelope — explore Lua, FusionCache conditional, per-user mutex |
</skills>

<autonomous_evolution>
Basket-specific triggers:

- **Redis CAS pattern choice** — Lua script vs FusionCache conditional set vs per-user lock. Pick one, justify in summary.
- **AOF loss window** — if `_shared.md` / ADR-0003 didn't quantify it, propose an SLO (e.g., "up to 1 s of basket mutations lost on Redis kill -9").
- **ACL cache headers** — does the adapter cache? `basket.md` should say; if unclear, flag + decide (e.g., short-lived adapter cache, invalidated on Catalog events via BFF's invalidator consumer — but that couples Basket to BFF; document the trade-off).
- **Idempotency-Key storage** — FastEndpoints `.Idempotency()` writes to `redis-cache` (per ADR-0013). Confirm it does NOT collide with the inbox table (different storage) and does not interfere with Basket's primary store (`redis-basket` is separate per ADR-0016).
</autonomous_evolution>

<success_criteria>
- A Wave-2 (Checkout saga) agent can consume `BasketCheckoutInitiatedEvent` end-to-end without modifying Basket code.
- Basket's `BasketDbContext` is verifiably "outbox + inbox only" (architecture test).
- Aggregate roundtrip (load from Redis → mutate → save to Redis with CAS → publish via outbox) works under concurrent updates from the same user (integration test with two parallel `AddItemToBasketCommand` calls).
- Connection-string discipline — Basket aggregate writes go to `redis-basket`, idempotency cache to `redis-cache`, never crossed.
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] `BasketDbContext` has no `DbSet<Basket>` (arch test enforces)
- [ ] Basket key `basket:{userId}` with 30-day sliding TTL on `redis-basket` (verify compose config)
- [ ] `CheckoutBasketCommand` returns 400 if `Idempotency-Key` missing (integration test covers this path); replay returns the cached response (verified)
- [ ] Post-checkout Redis key delete happens AFTER SQL outbox commit; failure of delete does NOT roll back (outbox is source of truth, dedup on retry via correlation id)
- [ ] `IProductCatalogQueryPort` + `ProductCatalogHttpAdapter` with Wave-0's resilience preset; adapter converts Catalog DTO → `ProductSnapshot` VO; outbound calls carry service-auth token
- [ ] Integration tests cover both `example-mapping/basket.md` sessions (price drift + 30-day expiry) + the `CheckoutBasketCommand` happy path with + without Idempotency-Key
- [ ] `BasketErrors` mirrors `error-taxonomy.md § 3.1`
- [ ] All HTTP routes under `/api/v1/basket/...` (ADR-0012)
- [ ] Correlation-id roundtrips: HTTP → Redis Lua / repository → outbox → Kafka header (integration test)
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:** `services/Basket/**`, `test/Basket.*.Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/**`, `docker-compose.yaml` (touch only if topic / relay drifted from Wave 0), `Directory.Packages.props` (Basket-specific), `docs/bc-design/basket.md` + glossary + example-mapping (self-correction).

**Do not touch:** other services, saga, platform code (except `.avsc`), Weather, other BCs.
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- `redis-basket` and `redis-cache` are not separate containers (ADR-0016 violated — escalate).
- `Catalog`'s `GET /api/v1/catalog/products/{id}` or `/by-ids` is not reachable when integration tests run.
- The Wave-0 `Platform.ServiceDefaults.AddServiceAuth` extension doesn't exist.
- `events-catalog.md § 5.2.1` and `basket.md § 4` disagree on the `BasketCheckoutInitiatedEvent` shape.
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Suggested commit milestones:

1. Scaffold 4 layers + project references; verify Wave 0 platform additions resolve; `dotnet build` green
2. Domain layer (`Basket`, `BasketItem`, `ProductSnapshot`, internal events) + unit tests
3. `IBasketRepository` + Redis impl with CAS + unit tests
4. Application layer (commands, query, validators, outbox publisher) + outbox integration test
5. ACL adapter (`ProductCatalogHttpAdapter`) with typed HttpClient + service-auth (no Polly — YARP handles resilience at the edge)
6. Infrastructure layer (`BasketDbContext` outbox+inbox only, DI) + integration test
7. Architecture tests (no `DbSet<Basket>`, no Redis cross-use)
8. Functional tests + docker-compose smoke
9. Docs self-corrections + session summary
</session_management>

<verification>
```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Basket.UnitTests/
dotnet test test/Basket.ArchitectureTests/
dotnet test test/Basket.IntegrationTests/
dotnet test test/Basket.FunctionalTests/
docker compose --profile full up -d
# Basket smoke: add an item, observe Redis key + basket.sessions topic on checkout
curl -X POST http://localhost:8080/api/v1/basket/items -H "Content-Type: application/json" -H "Authorization: Bearer ..." -H "Idempotency-Key: 0193..." -d '{"productId":"...","quantity":1}'
docker compose exec redis-basket redis-cli KEYS 'basket:*'
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic basket.sessions
```

Paste actual output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
**Question:** Redis CAS pattern — Lua script vs FusionCache conditional set vs per-user mutex?

**Bad answer:** "Lua because it's atomic."

**Good answer:** "Lua script (`SET basket:{userId} {payload} XX` with version-check inside). Reasons: (1) atomic — version check + write happen in one Redis round-trip with no race; (2) FusionCache's conditional set is a wrapper over the same primitive but adds an in-process layer that obscures the Redis interaction (we want the teaching surface to be the raw distributed-cache pattern); (3) per-user mutex via `redis-basket` lock would be 2 round-trips and a TTL-management problem. Trade-off accepted: Lua scripts are slightly harder to test than C# code (one integration test against real Redis required, can't unit-test the `EVAL` invocation itself). Verified by `BasketRepositoryConcurrencyTests` exercising two parallel writes against the same key."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`. Basket-specific notes:

- Redis CAS pattern chosen + why
- Whether the ACL adapter caches + why
- AOF loss-window SLO proposed (or referenced from ADR-0003)
- Idempotency verification — both `/items` and `/checkout` confirmed cached on replay
- ADR-0016 enforcement — confirm arch test asserts no cross-Redis-instance use

Proceed.
</session_summary>
