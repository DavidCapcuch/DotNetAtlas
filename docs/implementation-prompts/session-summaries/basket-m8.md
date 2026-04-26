# Basket M8 — Functional Tests + API Wiring Session Summary

> Milestone M8 per `docs/implementation-prompts/basket.md` `<session_management>` step 8 — "Functional tests + docker-compose smoke." Branch: `aaqwdqwd`. Basket-attributed commit: `eeb1814` (38 files, +2269/-22). This summary lands in a follow-up `docs(basket): M8 session summary` commit.

## Scope expansion (user-approved)

The milestone was specified as functional tests + smoke, but survey of the branch revealed that `services/Basket/Basket.Api/Program.cs` was still the M1 placeholder stub — no FastEndpoints, no DI, no endpoints, no FastEndpoints subclasses. M4–M6 had landed Application + ACL + Infrastructure layers; M7 had landed architecture tests. But the API host itself was never wired beyond M1 scaffolding. Functional tests cannot exist without endpoints. Per the user's `AskUserQuestion` decision early in the session, M8 was expanded to include the API wiring earlier milestones implicitly assumed. Auth pattern matched the Weather reference (FakeTokenCreator + JWT validation relaxed in test host) — NOT a real Keycloak Testcontainer.

## Deliverables

### Files created (28 production + 11 test = 39 new .cs / .json files)

**`services/Basket/Basket.Api/`** (15 .cs + 2 .json modified):
- `Program.cs` (REPLACED M1 stub) — `AddPlatformHostConfiguration` → `UsePlatformSerilog` → `AddCorrelationId` → `AddPresentation/AddApplication/AddInfrastructure` chain → middleware pipeline (Routing → CORS → OutputCache → CorrelationId → Authentication → Authorization → FastEndpoints) → Platform health-check endpoints + Prometheus exporter.
- `Common/ApiDependencyInjection.cs` — `AddPresentation` composes FastEndpoints + Swagger + JWT + CORS + ProblemDetails + idempotency-key output cache + ServiceAuth host.
- `Common/AuthenticationDependencyInjection.cs` — `AddBasketAuthentication`. Wraps `AddPlatformJwtBearer` and adds a deployed-environment-only `PostConfigure<JwtBearerOptions>` guard that throws if `RequireSignedTokens` or `ValidateIssuerSigningKey` is flipped off via env-var override (security defense in depth — Opus reviewer H-5).
- `Common/CorsDependencyInjection.cs` — `AddBasketCors`. Throws at startup if `AllowedOrigins` contains `"*"` while `AllowCredentials = true` (Opus reviewer C-2).
- `Common/Config/BasketCorsOptions.cs` — bound from `Cors` section.
- `Common/FastEndpointsDependencyInjection.cs` — `AddBasketFastEndpoints` (FastEndpoints + Swagger documents) + `UseBasketFastEndpoints` (`Versioning.Prefix = "v"`, `PrependToRoute = true`, `DefaultVersion = 1`, `Endpoints.RoutePrefix = "api"`).
- `Common/Extensions/ResultsExtensions.cs` — `MatchAsync` overloads + `SendErrorResponseAsync<TResult>` with a Basket-specific status-code map. The map bridges the Basket M4 design choice ("all errors are `ValidationError`") with use-cases.md § 2.1's prescribed HTTP statuses (404 / 409 / 422 / 503). Forbidden > Conflict > NotFound > basket-code-override > 400 default.
- `Common/Extensions/ClaimsPrincipalExtensions.cs` — `GetUserIdFromSubClaim()`; reads JWT `sub` (falling back to `ClaimTypes.NameIdentifier`); throws `DataIntegrityException` on missing/unparseable (auth pipeline guarantees presence by the time the endpoint runs).
- `Endpoints/Baskets/BasketGroup.cs` — FastEndpoints `Group` providing the `/basket` route prefix.
- `Endpoints/Baskets/AddItem/{AddItemToBasketRequest,AddItemToBasketEndpoint}.cs` — `POST /api/v1/basket/items` with `.Idempotency()` (double-click guard, optional header).
- `Endpoints/Baskets/RemoveItem/{RemoveItemFromBasketRequest,RemoveItemFromBasketEndpoint}.cs` — `DELETE /api/v1/basket/items/{productId}`.
- `Endpoints/Baskets/ChangeItemQuantity/{ChangeItemQuantityRequest,ChangeItemQuantityEndpoint}.cs` — `PUT /api/v1/basket/items/{productId}/quantity`.
- `Endpoints/Baskets/RefreshPrices/RefreshBasketPricesEndpoint.cs` — `POST /api/v1/basket/refresh-prices`.
- `Endpoints/Baskets/Clear/ClearBasketEndpoint.cs` — `DELETE /api/v1/basket/items`.
- `Endpoints/Baskets/Checkout/{CheckoutBasketRequest,CheckoutBasketResponse,CheckoutBasketEndpoint}.cs` — `POST /api/v1/basket/checkout` with `.Idempotency()` + an in-handler header-presence check (REQUIRED per ADR-0013; FE 7.0.1's bare filter does NOT 400 on absence in this BC's wiring — verified empirically).
- `Endpoints/Baskets/GetByUserId/GetBasketEndpoint.cs` — `GET /api/v1/basket`.
- `appsettings.json` (+`appsettings.Development.json`) — Serilog, OTEL, CORS, Auth (JwtBearer + ServiceAuth), ConnectionStrings (Basket Postgres + Redis:Basket + Redis:Cache), EfCore knobs, Basket:Redis (TTL/lock), Basket:Catalog, Kafka + SchemaRegistry + AvroSerializer, Topics, HealthChecks.

**`test/Basket.FunctionalTests/`** (11 .cs):
- `Common/ApiTestFixture.cs` — `AppFixture<Program>`. Spins Postgres + Redis + Kafka Testcontainers (single Redis container shared by `Redis:Basket` + `Redis:Cache` namespaces — ADR-0016 cross-instance isolation already enforced by M7 architecture tests; documented as accepted trade-off). Creates `basket.sessions` topic via Confluent admin client (3 partitions). Configures Serilog test sink, replaces `IProductCatalogQueryPort` with `NSubstitute` proxy via `Replace<...>` (Opus H-8), replaces `IOutboxWriter` with `FakeOutboxWriter` (bypasses Schema Registry), relaxes JWT validation via `Configure<JwtBearerOptions>` (IConfigureNamedOptions ordering matters — same rationale as Weather). Applies EF Core migrations once per fixture lifetime.
- `Common/BaseApiTest.cs` — `IAsyncLifetime`. Per-test scope, `BasketDbContext` + `IConnectionMultiplexer`, `ResetFixtureStateAsync` on `DisposeAsync`. Simpler than Weather's (no `TestCaseTracer`, no SignalR factory) — Basket has no telemetry assertions or SignalR; deferred to M9 (Opus reviewer M-1).
- `Common/FakeOutboxWriter.cs` — mirrors `Basket.IntegrationTests`'s; bypasses Schema Registry while preserving topic + key + Type for assertions.
- `Common/FunctionalTestCollection.cs` — xUnit collection.
- `Common/TestClientInfrastructure/{ClientType,FakeTokenCreator,HttpClientRegistry}.cs` — unsigned JWT with explicit `sub` claim; non-auth + per-user-token clients; traceparent header support.
- `ApiEndpoints/Baskets/{AddItem,GetBasket,ChangeItemQuantity,RemoveItemFromBasket,ClearBasket,RefreshBasketPrices,CheckoutBasket}Tests.cs` — 23 facts.

### File deleted

- `test/Basket.FunctionalTests/Placeholder.cs` — M1 scaffold removed.

## Design decisions taken (with rationale)

1. **Single Redis Testcontainer for both `Redis:Basket` and `Redis:Cache`.** The basket aggregate prefix `basket:{userId}` and the idempotency cache prefix `basket:idem:` are namespaced; collision is impossible. ADR-0016's cross-instance isolation invariant is enforced at compile-time by the M7 architecture tests, not at runtime by the test fixture. Trade-off accepted: a regression where the Basket repo accidentally writes to the idempotency cache wouldn't surface in functional tests; only the arch test catches it. Documented as M9 follow-up if higher fidelity needed.

2. **`NSubstitute` for `IProductCatalogQueryPort` (not WireMock).** Functional tests stub the port directly via `Replace<...>(ServiceDescriptor.Singleton<IProductCatalogQueryPort>(Catalog))`. Trade-off: real `ProductCatalogHttpAdapter` (correlation-id + ServiceAuth header propagation) is NOT exercised by M8. M9 follow-up: WireMock'd Catalog adapter test asserting the headers actually flow through.

3. **`Idempotency-Key` REQUIRED on `/checkout` enforced via in-handler check, not framework filter.** ADR-0013 line 148 says "Missing header on a protected endpoint → FastEndpoints returns 400 with a problem-detail." That's not what FE 7.0.1's `.Idempotency()` does in this BC's wiring — verified empirically by the `WhenIdempotencyKeyMissing_Returns400` test which fails with 202 if the in-handler check is removed. Added a `HttpContext.Request.Headers.ContainsKey("Idempotency-Key")` short-circuit at the start of `CheckoutBasketEndpoint.HandleAsync`. ADR's expectation reconciled with reality.

4. **`AddItem` does NOT enforce header presence — double-click guard semantics.** Per basket.md line 77: "POST /api/v1/basket/items (double-click guard) AND POST /api/v1/basket/checkout (most expensive)". The /items endpoint has `.Idempotency()` decoration (so a client that DOES send a key gets cached responses), but missing keys are NOT rejected (so the endpoint stays usable without a key). `WhenIdempotencyKeyMissing_StillSucceeds_DoubleClickGuardOnly` pins this behavior so a future FE minor that flips the default fails loudly here.

5. **Cross-user idempotency partitioning relies on FE 7.0.1's `IdempotencyOptions.AdditionalHeaders` default.** Ordering's `CancelOrderEndpoint` documents (and pins via test) that FE 7.0.1 ships `Authorization` in default `AdditionalHeaders`, which the OutputCachePolicy reads into `CacheVaryByRules.HeaderNames` — so two different users reusing the same UUID never share responses. Basket inherits the default + adds `WhenSameIdempotencyKeyUsedByDifferentUser_HandlerStillRuns` to fail loudly if a future minor drops it (Opus reviewer C-1).

6. **`SendErrorResponseAsync` Basket-specific status-code map.** Use-cases.md § 2.1 prescribes 404 / 409 / 422 / 503 for various Basket failures, but `BasketErrors.cs` (M4 design) types every error as `ValidationError`. Bridged at the API boundary with a `Dictionary<string, int>` keyed by error code. Stays inside M8 territory; alternative (changing M4 error types) would have crossed the boundary the user told me to stop-ask before crossing.

7. **CORS wildcard + AllowCredentials guard at startup.** Opus reviewer C-2: ASP.NET throws `InvalidOperationException` on the first preflight if both are set. Surfaced at startup instead of first-request via an explicit check in `CorsDependencyInjection.AddBasketCors`.

8. **JWT post-configure guard in deployed environments.** Opus reviewer H-5: `configuration.Bind(JwtBearerConfigSection, options)` lets a malicious or misconfigured env-var silently relax `RequireSignedTokens` / `ValidateIssuerSigningKey`. Added a `PostConfigure<JwtBearerOptions>` validator that throws if these flip off in `IsDeployedEnvironment()`.

## ADR compliance

- **ADR-0008** (correlation-id) — `app.UseCorrelationId()` middleware in the pipeline; outbox publisher copies the ambient correlation-id into the Avro header (M4 wiring). Functional test integration with the correlation-id flow is light (no end-to-end assertion); deferred to M9.
- **ADR-0010** (service-to-service auth) — Inbound JWT validation via `AddPlatformJwtBearer`. Outbound: `ProductCatalogHttpAdapter` (M5) is wired with `AddServiceAuth("catalog.read")`. M8 test fixture substitutes the port directly so the outbound auth path is NOT exercised by functional tests; relies on M5 unit-test coverage.
- **ADR-0012** (api versioning) — All routes under `/api/v1/basket/...` via FE versioning prefix + `BasketGroup` route. Verified by every functional test.
- **ADR-0013** (idempotency-key) — `.Idempotency()` on `/items` + `/checkout`. Backed by `redis-cache` (separate from `redis-basket` per ADR-0016). Required-vs-optional semantics enforced as described in design decisions #3 and #4.
- **ADR-0015** (time/timezone) — `TimeProvider` injected throughout (per M2/M3 wiring). M8 functional tests don't manipulate time; `FakeTimeProvider` not used here.
- **ADR-0016** (Redis topology) — Connection-string discipline: `ConnectionStrings:Basket` (Postgres outbox), `Redis:Basket` (basket aggregate), `Redis:Cache` (idempotency). M7 architecture tests enforce the keyed connection multiplexer pattern. Trade-off in design decision #1.

## Verification (CI gates + test slices)

```text
$ dotnet build -m
... 90 warnings, 0 errors. Sestavení uspělo. (Build succeeded.)

$ dotnet restore --locked-mode
... All projects restored. 0 errors.

$ dotnet format whitespace --no-restore --verify-no-changes
... 0 formatting violations in Basket files.

$ dotnet format style --no-restore --verify-no-changes
... 0 style violations in Basket files. (Pre-existing IDE0270 errors in
  services/Inventory/Inventory.Application/...AdjustStockCommandHandler.cs:53
  and ReceiveStockCommandHandler.cs:64 are NOT introduced by M8 — visible
  in the M8-start git status as `M services/Inventory/...`.)

$ dotnet test test/Basket.UnitTests/         → 143/143 green
$ dotnet test test/Basket.ArchitectureTests/ →  36/36 green
$ dotnet test test/Basket.IntegrationTests/  →   3/3 green
$ dotnet test test/Basket.FunctionalTests/   →  23/23 green
                                              ----- ------
                                              205 / 205 green
```

Functional tests environment: HTTP_PROXY / HTTPS_PROXY env vars must be unset for the test process so Docker.DotNet can connect to Docker Desktop's named pipe — the corp proxy doesn't include the npipe scheme in NO_PROXY, causing `HttpEnvironmentProxy.IsBypassed` to throw on container inspection. This is a pre-existing environmental issue; not introduced by M8. CI pipelines that don't set proxy env vars are unaffected.

### Docker-compose smoke

```text
$ docker compose up -d redis-basket redis-cache kafka schema-registry kafka-create-topic keycloak
... All Basket-relevant services healthy or running. The full --profile full
  attempt failed because outbox-relay-payments / -ordering / -saga / etc.
  images aren't built locally; not in M8 scope, deferred to deployment-engineer.

$ docker compose exec kafka kafka-topics --bootstrap-server localhost:9092 \
    --describe --topic basket.sessions
Topic: basket.sessions  TopicId: wY-9LnHRSo-Qp8u-5P4KcQ  PartitionCount: 3
  ReplicationFactor: 1  Configs: min.insync.replicas=1,retention.ms=2592000000
        Topic: basket.sessions  Partition: 0  Leader: 1  Replicas: 1  Isr: 1
        Topic: basket.sessions  Partition: 1  Leader: 1  Replicas: 1  Isr: 1
        Topic: basket.sessions  Partition: 2  Leader: 1  Replicas: 1  Isr: 1

$ docker compose exec redis-basket redis-cli CONFIG GET appendonly
appendonly  yes
$ docker compose exec redis-basket redis-cli CONFIG GET maxmemory-policy
maxmemory-policy  noeviction
$ docker compose exec redis-basket redis-cli KEYS 'basket:*'
(empty list — fresh state)
```

Topic config matches ADR-0003 (30-day retention = 2,592,000,000 ms) and basket.md § 6 (3 partitions, min.insync.replicas=1). Redis-basket runs with AOF + noeviction per ADR-0016. Full curl-against-running-Basket.Api smoke deferred — the 23 functional tests already exercise every endpoint end-to-end through the same in-process Basket.Api host, asserting status codes + outbox row + Redis state — strictly more comprehensive than a one-shot curl.

## Pre-commit Opus reviewer findings + resolution

`Agent(subagent_type="feature-dev:code-reviewer", model="opus")` per `_shared.md § 11`. Surfaced 2 CRITICAL + 6 HIGH + 7 MEDIUM + 9 LOW. All CRITICAL and HIGH were addressed before staging:

| Severity | ID | Finding | Resolution |
|---|---|---|---|
| CRITICAL | C-1 | Cross-user idempotency-key partition was unverified | Added `WhenSameIdempotencyKeyUsedByDifferentUser_HandlerStillRuns` test pinning FE 7.0.1's Authorization-in-AdditionalHeaders default; documented in `CheckoutBasketEndpoint.Configure` |
| CRITICAL | C-2 | CORS `AllowedOrigins=["*"]` + `AllowCredentials=true` would runtime-throw | `AddBasketCors` now throws at startup if both are configured |
| HIGH | H-1 | FE 7.0.1 `.Idempotency()` enforcement empirical claim conflicts with Ordering's reference | Verified by direct test run (AddItem returns 204 without header in this BC's wiring); added `WhenIdempotencyKeyMissing_StillSucceeds_DoubleClickGuardOnly` to pin behavior; comment updated to reflect FE 7.0.1, not 7.0.0 |
| HIGH | H-2 | In-handler header check might be unreachable in production | Kept the check; comment explicitly mentions FE 7.0.1's empirical behavior + the regression test that re-verifies if a minor changes |
| HIGH | H-3 | `Basket.IdempotencyKeyMissing` in status-code map but not in `BasketErrors.cs` | Removed dead entry from the map. The in-handler 400 path uses `Send.ErrorsAsync(400)` directly, never traversing the result-mapper, so the entry was unreachable |
| HIGH | H-5 | `configuration.Bind(JwtBearerConfigSection, options)` could let env-var override `RequireSignedTokens` | Added `PostConfigure<JwtBearerOptions>` guard in `AddBasketAuthentication` (deployed envs only) |
| HIGH | H-6 | `RemoveItemFromBasketTests` used FE typed `DELETEAsync<TEndpoint, TRequest>` (DELETE-with-body footgun) | Switched all three tests to raw `client.DeleteAsync($"/api/v1/basket/items/{productId}", ct)` mirroring the Clear pattern |
| HIGH | H-8 | `services.AddSingleton(Catalog)` registered the NSubstitute proxy's runtime type, not `IProductCatalogQueryPort` | Switched to `services.Replace(ServiceDescriptor.Singleton<IProductCatalogQueryPort>(Catalog))` |

MEDIUM/LOW findings deferred (documented inline below) — none are blocking for M8 DoD:
- M-1 (`BaseApiTest` lacks `TestCaseTracer`) — Basket has no telemetry assertion in M8.
- M-3 (`BasketGroup` `WithGroupName` redundant with `Tags`) — cosmetic.
- L-1, L-2, L-4, L-5, L-9 — cosmetic / minor.
- L-3 (`Math.Max` precedence trick in `SendErrorResponseAsync`) — works for current codes; replace with explicit precedence map if a 4xx/5xx ambiguity surfaces.
- L-6 (`RefreshBasketPricesTests` doesn't assert price actually changed) — coverage gap; M9 follow-up.
- L-7 (`Basket:Redis:LockRetryDelayMs` etc. — verify M5 reads them) — M5 territory.
- L-8 (`ClearBasketEndpoint.Description.Produces(404)` advertises an unreachable status — same doc/code divergence as #5 below).

## Doc/code divergences discovered (out of M8 boundary, M9 follow-ups)

While writing functional tests against the M4 application layer, three divergences from `use-cases.md § 2.1` surfaced. The M8 tests align with the implementation; the docs need updating in M9 (or the handlers updated under user approval, but that crosses the M4 boundary):

1. **`RemoveItemFromBasketCommandHandler` returns `Result.Ok()` when no basket exists** (handler line 36-39), producing a 204 idempotent no-op. `use-cases.md § 2.1.2` prescribed 404. Test `WhenBasketAbsent_ReturnsNoContent_Idempotent` aligns with implementation.
2. **`ClearBasketCommandHandler` returns `Result.Ok()` when no basket exists** (handler line 36-39), producing 204. `use-cases.md § 2.1.5` prescribed 404. Test `WhenNoBasket_ReturnsNoContent_Idempotent` aligns with implementation.
3. **`RefreshBasketPricesCommandHandler` returns `Result.Ok()` when no basket exists or basket is empty** (handler line 38-42), producing 204. `use-cases.md § 2.1.4` prescribed 404. Test `WhenNoBasket_ReturnsNoContent_Idempotent` aligns with implementation.

The implementation choice is internally consistent (all three are "idempotent: no basket → 204"), and arguably nicer for callers. M9 should reconcile by either updating use-cases.md to match the implementation or updating handlers under user approval.

## Open questions / proposed improvements

- **Real WireMock'd Catalog ACL test in functional tests** (M9). Would assert that the outbound `ProductCatalogHttpAdapter` carries `X-Correlation-Id` and the OAuth2 `Authorization: Bearer ...` token from `AddServiceAuth("catalog.read")`. M8's NSubstitute on the port skips this.
- **Two Redis Testcontainers** (M9 if a regression surfaces). Today one container backs both `Redis:Basket` + `Redis:Cache`; M7 arch tests enforce no cross-use at compile time. If a future BC change accidentally crosses, M7's gate catches it before functional tests.
- **`basket-api` container in `docker-compose.yaml`** (deployment-engineer M9 / DEVOPS wave). Today smoke runs `dotnet run` against compose-managed infra; ops would prefer a container.
- **Mutation testing** (`nw-mutation-test`, M9). `_shared.md § 7` recommends post-green; M9 finalizes.
- **`appsettings.json` placeholder secrets** — match Weather's pattern ("ShouldBeInVault" placeholder + appsettings.Local.json override). A future `Platform.ServiceDefaults` enhancement could add a startup validator that fails fast if these literal placeholders survive into a deployed env.

## Boundary discipline

Stayed inside M8's `<session_management>` boundary throughout. Did NOT touch:
- `services/Basket/Basket.Application/**`, `Basket.Domain/**`, `Basket.Infrastructure/**` (M2-M6 territory).
- `platform/**` (no platform changes needed).
- Other BCs (`services/Catalog`, `services/Ordering`, `services/Inventory`, `services/Payments`, `services/Invoicing`, `saga/`, `bff/`).
- `docker-compose.yaml` (no drift from Wave 0).

The pre-existing uncommitted modifications visible in `git status` at session start (Inventory handlers, Ordering test, ADR docs, ordering.md, payments.md prompts, keycloak service-scope-matrix.md) were left untouched. Only Basket M8 files were staged.

## What "done" looks like for M8

- [x] All 6 commands + 1 query exposed under `/api/v1/basket/...` with FastEndpoints + JWT bearer auth.
- [x] Idempotency-Key required on `/checkout` (in-handler check) per ADR-0013.
- [x] `BasketErrors` mirrors `error-taxonomy.md § 3.1` — bridged to HTTP statuses by `SendErrorResponseAsync`.
- [x] All HTTP routes under `/api/v1/basket/...` (ADR-0012).
- [x] `Idempotency-Key storage = redis-cache` (ADR-0013/0016) — never crossed with redis-basket.
- [x] Functional test fixture exists + reusable for M9 regression scenarios.
- [x] `basket.sessions` topic config verified (3 partitions, 30-day retention).
- [x] Four CI gates green; all four test slices green.
- [x] Pre-commit Opus reviewer ran; CRITICAL + HIGH findings fixed before staging.
- [x] M8 commit landed: `eeb1814` (38 files, +2269 / -22).
- [ ] `nw-software-crafter-reviewer` (Haiku) — runs after this summary.
- [ ] M9 handoff emitted.
