# Catalog M8 — Docs Self-Corrections + Session Summary

> Milestone M8 per `docs/implementation-prompts/catalog.md` `<session_management>` step 8 — *"Docs self-corrections + session summary."* **Final** Catalog milestone — closes the BC's contract for downstream Wave-2 / Wave-3 agents. Branch: `aaqwdqwd`. Catalog-attributed commit: pending (this summary lands in the same M8 commit alongside the doc + CPM edits).

## Mission

M8 is docs-only by design. M7's summary at [catalog-m7.md:165-172](catalog-m7.md) (Open Questions block) explicitly handed two doc drifts forward to M8:

1. **Port drift** — `docs/implementation-prompts/catalog.md` `<verification>` block points at `localhost:8080`, but M7 mapped `catalog.api` to `8100:8080` in [docker-compose.yaml:449](../../../docker-compose.yaml) (host `8080` is `nginx-cdn` per compose:687).
2. **Auth drift** — same `<verification>` block does anonymous curls against `/api/v1/catalog/categories/tree` and `/api/v1/catalog/products?…`, but M6 wired `Policies(CatalogAuthorizationPolicies.ReadPolicy)` on those endpoints — anonymous returns 401 (verified live at catalog-m7.md:128-136).

A **third drift** surfaced during M8's exploration scan:

3. **Phantom error name** — [docs/bc-design/example-mapping/catalog.md:47](../../bc-design/example-mapping/catalog.md) referenced `CategoryErrors.CannotParentToSelfOrDescendant`, which does not exist in code. The actual taxonomy splits cleanly into `CannotParentToSelf()` (literal `parentId == self.Id`, raised inside the aggregate at [Category.cs:165](../../../services/Catalog/Catalog.Domain/Categories/Category.cs)) and `ReparentCreatesCycle(Guid, Guid)` (destination is a descendant of self, raised by the application-layer `CategoryAncestryService` at [ReparentCategoryCommandHandler.cs:68](../../../services/Catalog/Catalog.Application/Categories/ReparentCategory/ReparentCategoryCommandHandler.cs)).

A **fourth surprise** surfaced during gate verification at session start: the whole-solution `dotnet build -m` was **RED** at HEAD with 4× NU1902 errors on `OpenTelemetry.Exporter.OpenTelemetryProtocol 1.12.0` (GHSA-4625-4j76-fww9, medium severity, recently elevated from warning to error). Affected projects: `saga/SagaOrchestrators*` (3) + `services/Notifications/Notifications` (1). The `platform/Directory.Packages.props:43` tier already pins **1.15.3** (non-vulnerable); the root + saga + services CPMs lagged at 1.12.0. The user explicitly authorized this cross-boundary CPM bump via `AskUserQuestion` ("i give permission to directly bump them") so M8 would not ship with a broken first gate.

## Scope discipline note (user-authorized boundary extension)

Per `docs/implementation-prompts/catalog.md` `<boundaries>`, Catalog's writable set covers `services/Catalog/**`, `test/Catalog.*.Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/**`, `Directory.Packages.props` (Catalog-specific only), and Catalog's design docs. **The root + saga + services `Directory.Packages.props` files are not Catalog-specific** — they cover cross-cutting concerns. Bumping `OpenTelemetry.Exporter.OpenTelemetryProtocol` in those three tiers crosses the boundary, but the user explicitly authorized it as part of M8 ("i give permission to directly bump them") rather than accepting a red gate or scope-narrowing the verification command set. The bump is **minimal-touch** — only the GHSA-flagged package, not the broader OTEL core set (see Open Questions § OTEL coherence drift below). All other off-boundary files (other BCs, platform code, ADRs, cross-BC bc-design docs) remain untouched, mirroring catalog-m7 + basket-m9 disposition.

## Deliverables

### Files modified (5 + 47 lockfiles)

**Doc edits (in `<boundaries>`):**

- [docs/implementation-prompts/catalog.md:159-162](../catalog.md) — `<verification>` block port + endpoint swap. Lines were:
  ```
  # Catalog smoke checks
  curl -s http://localhost:8080/api/v1/catalog/categories/tree | jq .
  curl -s "http://localhost:8080/api/v1/catalog/products?text=demo&page=1&limit=10" | jq .
  ```
  Replaced with auth-free readiness probes that also document the auth gate inline:
  ```
  # Catalog readiness smoke (auth-free; protected query endpoints require catalog.read JWT scope per ADR-0010)
  curl -s http://localhost:8100/api/healthz   | jq .
  curl -s http://localhost:8100/api/readiness | jq .
  ```
  The kafka-topics block beneath the swap is unchanged.

- [docs/bc-design/example-mapping/catalog.md:47](../../bc-design/example-mapping/catalog.md) — phantom error name fixed. Bullet was:
  > **Then** the command returns `Result.Fail(CategoryErrors.CannotParentToSelfOrDescendant)`, no path changes, no event raised.

  Replaced with the real taxonomy + an inline parenthetical pointing the reader at the existing § Questions answer about cycle-detection layering:
  > **Then** the command returns `Result.Fail(CategoryErrors.ReparentCreatesCycle(category.Id, newParentId))`, no path changes, no event raised. The cycle is detected by `CategoryAncestryService` before the aggregate runs (see § Questions below).

**CPM bumps (off-boundary, user-authorized):**

- [Directory.Packages.props:67](../../../Directory.Packages.props) — `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.12.0 → 1.15.3
- [saga/Directory.Packages.props:39](../../../saga/Directory.Packages.props) — same bump
- [services/Directory.Packages.props:75](../../../services/Directory.Packages.props) — same bump
- [platform/Directory.Packages.props:43](../../../platform/Directory.Packages.props) — already 1.15.3 (no edit; this was the tier the others were lagging behind)

**Lockfiles (auto-regenerated by `dotnet restore --force-evaluate`):**

47 `packages.lock.json` files touched. Spot-checked diffs on `services/Catalog/Catalog.API/packages.lock.json`, `services/Ordering/Ordering.API/packages.lock.json`, and `test/Weather.UnitTests/packages.lock.json` — all show identical shape: `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.12.0 → 1.15.3 plus transitive `OpenTelemetry` SDK 1.12.0 → 1.15.3 with corresponding contentHash updates. No deeper transitive churn. The breadth (47/47) is the natural fan-out via `Platform.ServiceDefaults` — every API/Worker project + their test projects pull the OTLP exporter directly or transitively.

### File created (1)

- [docs/implementation-prompts/session-summaries/catalog-m8.md](catalog-m8.md) — this file.

### Pre-existing files NOT touched (per M8 boundaries + basket-m8/m9 + catalog-m7 precedent)

- `services/Catalog/**` (no production-code change required for docs-only milestone).
- Other BCs: `services/{Basket,Inventory,Invoicing,Ordering,Payments}/**`, `saga/**` (apart from CPM), `bff/**`.
- `platform/**` (apart from a single CPM file *not* bumped — already at 1.15.3).
- `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/**` (schemas locked since M3).
- ADRs (`docs/adr/0001` through `0019`).
- Cross-BC bc-design docs: `docs/bc-design/{events-catalog,use-cases,error-taxonomy,architecture-tests,kafka-dlq-strategy,avro-compatibility,rate-limiting}.md` and other BCs' `*.md` chapters.
- Pre-existing dirty files at session start (`docs/adr/{0004,0006,0010,0015}.md`, `docs/bc-design/ordering.md`, `docs/implementation-prompts/{checkout-saga,ordering,payments}.md`, `src/keycloak/service-scope-matrix.md`, `test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs`, `test/Inventory.{Functional,Integration}Tests/...`) — same disposition as basket-m8/m9 + catalog-m7. Verified by Opus reviewer; left unstaged.

## Design decisions taken (with rationale)

1. **Option A for the `<verification>` block — auth-free `/api/healthz` + `/api/readiness`, NOT `bearer-token + v1 endpoint` curls.** Rationale: smoke verification is supposed to test infra wiring (port mapping, container lifecycle, dependency probes), not the auth path. The 23 functional tests already validate auth roundtrips end-to-end via `Catalog.FunctionalTests`. Adding a Keycloak token-fetch step into the README-level `<verification>` block would couple the smoke script to admin-secret retrieval and make it harder to copy/paste in a production-debugging context. The new block also documents *why* it does not curl the v1 endpoints (the inline comment names ADR-0010 + the `catalog.read` scope) — anyone reading the prompt sees the auth gate and is not surprised by the 401s catalog-m7.md:128-136 captured.

2. **Minimal-touch CPM bump — only `OpenTelemetry.Exporter.OpenTelemetryProtocol`, NOT the coherent OTEL core set.** Rationale: GHSA-4625-4j76-fww9 names exactly that one package. The user's authorization was scope-bounded to "fix the failing build gate", not "refactor OTEL across the solution". Bumping the coherent core set (`Exporter.Console`, `Extensions.Hosting`, `Extensions.Propagators` — all currently 1.12.0) is a known correctness improvement per [OTEL .NET versioning policy](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/VERSIONING.md), but it's a separate platform decision worth its own commit + review (see Open Questions § OTEL coherence). The Opus pre-commit reviewer flagged this as M-1 and explicitly recommended the minimal-touch path "given the user's stated intent" with the caveat that the half-bumped state must be named in this summary's deferred-followups — which is what this paragraph + the explicit follow-up below accomplish.

3. **Inline parenthetical on the example-mapping bullet rather than splitting Rule R2 into R2a/R2b.** Rationale: example-mapping is rule-level, not enforcement-layer-level. Splitting R2 ("A category cannot be reparented under itself or any of its own descendants") into R2a (self) + R2b (descendant cycle) would correctly mirror the two-error / two-layer reality (`CannotParentToSelf` in the aggregate vs `ReparentCreatesCycle` from `CategoryAncestryService`), but it would also make R2 disproportionately heavy compared to R1, R3, R4, R5. The parenthetical "(see § Questions below)" directs readers to the existing § Questions block (line 57: *"cycle pre-check is explicitly the responsibility of `CategoryAncestryService` before calling the aggregate"*) — that's the canonical seam for the layering question. The Opus reviewer (L-1) verified this disposition is acceptable.

4. **Literal `M9` substitution in the handoff block at session end.** Rationale: the user's instruction was explicit — *"substitute catalog and 9 for me so I can paste it straight into a fresh session"*. Catalog's `<session_management>` formally enumerates only 8 milestones (M8 = the last, "Docs self-corrections + session summary"); there is no canonical M9. The literal substitution still emits a syntactically-correct handoff block (per `_handoff-template.md`) — the next session will negotiate what M9 means in context (likely a "post-DoD / no further work" check-in, the cross-cutting OTEL-coherence audit named below, the `architecture-tests.md § 2.1` doc-correction flagged in catalog-m5.md:115, or a `nw-mutation-test` post-green pass per `_shared.md § 7`).

## ADR compliance

M8 is docs-only; most ADRs are n/a:

- **ADR-0007** (Avro FORWARD_TRANSITIVE) — n/a (M8 doesn't touch schemas).
- **ADR-0008** (correlation-id) — n/a (closed by M3-M5).
- **ADR-0010** (service-to-service auth) — explicitly *referenced* in the new `<verification>` block comment ("auth-free; protected query endpoints require `catalog.read` JWT scope per ADR-0010"). Documents the gate without exercising it.
- **ADR-0012** (versioning) — n/a (no route changes).
- **ADR-0013** (idempotency) — n/a.
- **ADR-0015** (time policy) — n/a; arch-tests already enforce.
- **ADR-0016** (Redis topology) — n/a.

## Verification output (executed at HEAD before commit)

### Four gates (per `_shared.md § 12` + CLAUDE.md)

```
$ dotnet build -m --no-restore
... (47 NU1903 warnings on System.Security.Cryptography.Xml — pre-existing transitive-vuln baseline; same shape as catalog-m7.md:60-62 minus the 4 NU1902 errors that the CPM bump cleared)
    47 upozornění
    Počet chyb: 0
Uplynulý čas 00:00:27.85

$ dotnet restore --locked-mode
... NU1903 warnings on System.Security.Cryptography.Xml (pre-existing)
  Všechny projekty jsou v aktuálním stavu pro obnovení.

$ dotnet format whitespace --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění protokolovat, nastavte možnost verbosity na úroveň diagnostic.
exit=0

$ dotnet format style --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění protokolovat, nastavte možnost verbosity na úroveň diagnostic.
exit=0
```

All four gates **GREEN**. The CPM bump (Task 1) cleared the 4 NU1902 errors that were red at session start. The 47 NU1903 warnings on `System.Security.Cryptography.Xml` (versions 9.0.0 + 10.0.1 across projects) are inherited from the build graph and unchanged by M8.

### Test slices (Catalog-scoped)

```
$ dotnet test test/Catalog.UnitTests/ --no-build --no-restore
Úspěšné!    - Neúspěšné:     0, Úspěšné:   255, Přeskočeno:     0, Celkem:   255, Doba trvání: 2 s

$ dotnet test test/Catalog.ArchitectureTests/ --no-build --no-restore
Úspěšné!    - Neúspěšné:     0, Úspěšné:    41, Přeskočeno:     0, Celkem:    41, Doba trvání: 595 ms
```

255/255 + 41/41 — same baseline as M5/M7. Docs-only edits + a transitive CPM bump that doesn't change a public type signature cannot regress code; this is sanity confirmation.

### Test slices (intentionally deferred)

`dotnet test test/Catalog.IntegrationTests/` and `dotnet test test/Catalog.FunctionalTests/` were gated on a Rancher Desktop Testcontainers `npipe` issue documented in catalog-m5.md and catalog-m7.md:81-91. M8 does not change the local Docker environment; the same gate applies, and the gate is **not introduced by M8**. CI environments without Rancher Desktop's named-pipe quirk are unaffected.

### Spot-checks on the edits

```
$ grep -n 'localhost:8100' docs/implementation-prompts/catalog.md
161:curl -s http://localhost:8100/api/healthz   | jq .
162:curl -s http://localhost:8100/api/readiness | jq .

$ grep -rn 'CannotParentToSelfOrDescendant' docs/
(no matches — drift gone)

$ grep -n 'ReparentCreatesCycle' docs/bc-design/example-mapping/catalog.md
47:- **Then** the command returns `Result.Fail(CategoryErrors.ReparentCreatesCycle(category.Id, newParentId))`, no path changes, no event raised. ...

$ grep -n 'OpenTelemetry.Exporter.OpenTelemetryProtocol' \
    Directory.Packages.props saga/Directory.Packages.props \
    services/Directory.Packages.props platform/Directory.Packages.props
Directory.Packages.props:67:    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
saga/Directory.Packages.props:39:    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
services/Directory.Packages.props:75:        <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3"/>
platform/Directory.Packages.props:43:        <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3"/>
```

All four CPM tiers coherent at 1.15.3 for the bumped package.

## Pre-commit Opus reviewer findings + resolution

`Agent(subagent_type="feature-dev:code-reviewer", model="opus")` per `_shared.md § 11`. Brief covered: the three doc fixes, the off-boundary CPM bump rationale + user authorization, the 47-lockfile breadth, the four gates output, and what's intentionally deferred. Reviewer ran with explicit instructions to push back on framing.

**Outcome: 0 CRITICAL, 0 HIGH, 1 MEDIUM, 2 LOW. Nothing to fix before staging.** All MEDIUM/LOW findings accepted-with-rationale per the table below; documented here + in commit body per `_shared.md § 11`.

| Severity | ID | Finding | Resolution |
|---|---|---|---|
| MEDIUM | M-1 | OTEL core-set coherence violation. The OTEL .NET versioning policy treats `OpenTelemetry`, `OpenTelemetry.Api`, `Exporter.OpenTelemetryProtocol`, `Exporter.Console`, and `Extensions.Hosting` as a co-released coherent set. After the M8 bump, root + saga + services tiers run a half-bumped state: `Exporter.OpenTelemetryProtocol` 1.15.3 (forces transitive SDK to 1.15.3) but `Exporter.Console` 1.12.0 + `Extensions.Hosting` 1.12.0 + `Extensions.Propagators` 1.12.0 still pin SDK 1.12.0 in their declared deps. NuGet resolves to 1.15.3 (highest wins), but the half-bumped state is policy-non-compliant. | **ACCEPTED — minimal-touch retained per user-stated intent.** The user authorized fixing the failing build gate, not refactoring OTEL across the solution. Build is green; tests pass; lockfile resolution is healthy (verified). Named follow-up below escalates this to a separate platform-level commit with its own review. |
| LOW | L-1 | `example-mapping/catalog.md:21` Rule R2 elides the two-layer enforcement reality (aggregate `CannotParentToSelf` vs `ReparentCreatesCycle` from `CategoryAncestryService`). Splitting into R2a / R2b would match precisely. | **ACCEPTED — current shape preferred.** The example-body's parenthetical + the existing § Questions block (line 57) already cover the layering. Splitting R2 would make it disproportionately heavy compared to R1, R3, R4, R5. |
| LOW | L-2 | Cross-doc inconsistency on health-check endpoint paths (`docs/eshop-master-design.md:531`, `docs/bc-design/rate-limiting.md:224`, `docs/adr/0012-api-versioning.md:104` reference `/healthz/liveness` + `/healthz/readiness`, while the platform implementation in `platform/Platform.ServiceDefaults/WebApplicationExtensions.cs:18-39` exposes `/api/healthz` + `/api/readiness`). | **OUT OF Catalog `<boundaries>` — deferred follow-up.** Documented below. Same disposition as catalog-m5.md's `architecture-tests.md § 2.1` flag (cross-BC docs are not Catalog's to fix). |

The reviewer also confirmed:
- The three doc fixes are correct (Option A for verification, `ReparentCreatesCycle` for the cycle case).
- 47-lockfile breadth is normal for an OTEL bump given Platform.ServiceDefaults' transitive dep graph.
- Pre-existing dirty files at session start were correctly left unstaged.
- No additional Catalog-boundary drift exists. Specifically: `bc-design/catalog.md:94` references `CategoryErrors.HasDependents` for a deferred `DeleteCategoryCommand` (per catalog-m3.md:37 + catalog-m4.md:9,74,78 + error-taxonomy.md:169) — that's deferred-feature documentation, not drift; leave alone.
- All endpoint references are uniformly auth-policy-correct in code (FastEndpoints `Policies(CatalogAuthorizationPolicies.{ReadPolicy,WritePolicy})` across 13 endpoint files); the catalog-m7 `<deferred_followups>` note's informal `RequireAuthorization("catalog.read")` phrasing was nomenclature drift but not enforcement drift.

## Domain self-corrections

- **Drift #3 reconciled** in [example-mapping/catalog.md:47](../../bc-design/example-mapping/catalog.md). Phantom `CannotParentToSelfOrDescendant` replaced with the real `ReparentCreatesCycle(category.Id, newParentId)` factory + an inline § Questions cross-reference. No code change required — the BC was already implementing the canonical taxonomy; only the doc was wrong.

## Open questions / improvements proposed but NOT implemented

### Carried forward from M5 / M7

- **`docs/bc-design/architecture-tests.md § 2.1` doc-correction.** Doc references a singular `ProductSearchViewProjectionHandler`, but Catalog implements one class per internal domain event with `*ProjectionHandler` suffix per [catalog.md `<example_design_decision>`](../catalog.md). Flagged at [catalog-m5.md:115](catalog-m5.md). Out of Catalog `<boundaries>` (cross-BC doc); needs a separate doc-only commit.
- **Wave-0 DB-bootstrap gap.** `docker-compose.yaml:22` mounts only `./src/keycloak/init-db.sql` — no per-BC database creation script. The `outbox-relay-catalog` container has been silently failing for the lifetime of the compose stack. M7 manually created the `Catalog` Postgres database to verify the smoke flow; production needs (a) an init script that creates every per-BC database known to the realm, and (b) a deterministic EF-migration step at cold start. Flagged at [catalog-m7.md:167](catalog-m7.md). Belongs to platform / Wave-0 follow-up.
- **`catalog.api` compose `healthcheck:` directive.** The chiseled-extra base image lacks `curl`/`wget`/shell, so a Compose-level HEALTHCHECK requires a self-contained healthcheck binary or a non-chiseled base. All seven `outbox-relay-*` services follow the same no-healthcheck pattern. Flagged at [catalog-m7.md:169](catalog-m7.md). Recommended longer-term fix: ship a small `Catalog.Healthcheck.dll` and reference it from `HEALTHCHECK CMD`.
- **Wave-level `HealthChecksOptions` timeout binding.** Catalog's `appsettings.json:96-101` carries unused timeout config. Either wire it (parallel to `Platform.OutboxRelay.WorkerService/Common/HealthChecksDependencyInjection.cs`) or remove it. Cross-BC harmonization concern; flagged at [catalog-m7.md:170](catalog-m7.md).

### Surfaced in M8

- **OTEL core-set coherence drift across root/saga/services CPMs (M-1 from Opus pre-commit review).** After M8's minimal-touch bump, `OpenTelemetry.Exporter.OpenTelemetryProtocol` is at 1.15.3 (transitively forcing SDK + Api to 1.15.3) while `OpenTelemetry.Exporter.Console`, `OpenTelemetry.Extensions.Hosting`, and `OpenTelemetry.Extensions.Propagators` remain at 1.12.0. NuGet resolves cleanly (1.15.3 ≥ 1.12.0), and runtime smoke is unaffected today. Per the [OTEL .NET versioning policy](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/VERSIONING.md), the core/co-released set should travel together. **Recommended follow-up:** a single platform-level commit bumping the three companion packages to 1.15.3 across `Directory.Packages.props`, `saga/Directory.Packages.props`, and `services/Directory.Packages.props` (the platform tier is already coherent at 1.15.3). Non-core `Instrumentation.*` packages can remain on 1.12.x — policy explicitly allows that drift.
- **Cross-doc health-check endpoint path drift (L-2 from Opus pre-commit review).** `docs/eshop-master-design.md:531`, `docs/bc-design/rate-limiting.md:224`, and `docs/adr/0012-api-versioning.md:104` reference `/healthz/liveness` + `/healthz/readiness` (or informal `/healthz`). The platform implementation at `platform/Platform.ServiceDefaults/WebApplicationExtensions.cs:18-39` exposes `/api/healthz` + `/api/readiness` (no `/liveness` suffix; readiness is at `/api/readiness`, not `/healthz/readiness`). Out of Catalog `<boundaries>` — same disposition as the architecture-tests.md flag. Worth a single doc-set audit in a separate commit.
- **`nw-mutation-test` post-green pass.** `_shared.md § 7` recommends mutation testing post-green for ≥ 80% kill rate. Not run for Catalog (or any other Wave-1 BC) yet. Belongs to a separate "test fortification" milestone.
- **`nw-software-crafter-reviewer` (Haiku) follow-up review.** Per `_shared.md § 11` step 3, this review "complements but does not substitute for" the Opus pre-commit pass. It runs against this summary as input. Invocation pending (this file is the input; the reviewer can be dispatched against it post-commit). The Opus pre-commit pass already validated the diff; the Haiku pass is value-add coverage on the summary + decisions, not a gating step.

## File-touch audit

Per `<boundaries>` in [docs/implementation-prompts/catalog.md:120-122](../catalog.md):

**In-scope (Catalog `<boundaries>`):**
- ✓ `docs/implementation-prompts/catalog.md` — `<verification>` block self-correction (allowed by the `<boundaries>` "self-correction only" clause).
- ✓ `docs/bc-design/example-mapping/catalog.md` — Drift #3 self-correction (same clause).
- ✓ `docs/implementation-prompts/session-summaries/catalog-m8.md` — NEW (this file; session-summary location follows catalog-m3/m4/m5/m7 + basket-m8/m9 precedent).

**Off-boundary, USER-AUTHORIZED:**
- ✓ `Directory.Packages.props` — root CPM, OTEL bump.
- ✓ `saga/Directory.Packages.props` — saga CPM, OTEL bump.
- ✓ `services/Directory.Packages.props` — services CPM, OTEL bump.
- ✓ 47 `packages.lock.json` files (every API/Worker project + their test projects) — auto-regenerated transitive consequence of the CPM bump via `dotnet restore --force-evaluate`.

**Untouched (per `<boundaries>` "Do not touch"):**
- `services/Catalog/**` (no production-code change required).
- Other BCs (`services/{Basket,Inventory,Invoicing,Ordering,Payments}/**`, `bff/**`).
- `saga/SagaOrchestrators*/**.cs` (only the saga CPM file was touched, not the saga code).
- `platform/**` (no platform `.cs` change; only verified the platform CPM was already coherent).
- `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/**` (schemas locked since M3).
- `Directory.Build.props`, `global.json`, `NuGet.config`, root `Dockerfile`.
- ADRs (`docs/adr/0001` through `0019`).
- Cross-BC bc-design docs (`use-cases.md`, `error-taxonomy.md`, `events-catalog.md`, `architecture-tests.md`, `kafka-dlq-strategy.md`, `avro-compatibility.md`, `rate-limiting.md`, other BCs' `*.md`).
- Pre-existing dirty files at session start (`docs/adr/{0004,0006,0010,0015}.md`, `docs/bc-design/ordering.md`, `docs/implementation-prompts/{checkout-saga,ordering,payments}.md`, `src/keycloak/service-scope-matrix.md`, `test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs`, `test/Inventory.{Functional,Integration}Tests/...`) — same disposition as basket-m8/m9 + catalog-m7.

## What "done" looks like for M8

- [x] Three doc drifts reconciled (port + auth-free verification block; phantom error name).
- [x] Build gate restored to green via user-authorized minimal-touch CPM bump.
- [x] All four gates green: build (0 errors), restore --locked-mode (clean), format whitespace (exit 0), format style (exit 0).
- [x] Catalog test slices: 255/255 unit + 41/41 architecture green (M5/M7 baseline).
- [x] Pre-commit Opus reviewer ran (`feature-dev:code-reviewer` model=opus); 0 CRITICAL, 0 HIGH, 1 MEDIUM (M-1 OTEL coherence — accepted-with-rationale + named follow-up below), 2 LOW (L-1 R2 split — accepted; L-2 cross-doc paths — out of boundary, deferred).
- [x] Boundary discipline: pre-existing dirty files left unstaged; only M8-scope files staged.
- [x] Session summary posted at this path.
- [ ] M9 handoff block emitted in the session's closing message (per `_shared.md § 10` + the user's explicit closing instruction; literal `{BC}=catalog` / `{N+1}=9` substitution).
- [ ] `nw-software-crafter-reviewer` (Haiku) follow-up review against this summary — value-add post-commit coverage; not a gating step.

## Catalog BC complete

All eight Catalog milestones — scaffold (M1), domain (M2), application (M3), infrastructure (M4), architecture tests (M5), HTTP layer + functional tests (M6), docker-compose smoke + readiness probes (M7), and now docs self-corrections + session summary (M8) — have shipped on branch `aaqwdqwd`. The BC's contract surface is stable for downstream Wave-2 (Checkout saga) and Wave-3 (BFF) agents:

- **External Avro events**: `ProductCreatedEvent`, `ProductPriceChanged`, `ProductDiscontinuedEvent`, `CategoryCreatedEvent` published via the transactional outbox to `catalog.products` + `catalog.categories` topics (3 partitions, infinite retention per ADR-0007). Inventory's `StockLevelChanged` consumer integration verified at M4.5.
- **HTTP API** under `/api/v1/catalog/...` per ADR-0012: `GET /products/{id}`, `GET /products?text=…&categoryPath=…&minPrice=…&maxPrice=…&status=…&page=…&limit=…`, `GET /products/by-ids?ids=…` (max 100, partial-tolerant), `GET /categories/tree`, `GET /products/by-category/{categoryId}`, plus admin POST commands with `.Idempotency()` filters backed by `redis-cache` per ADR-0013.
- **CQRS read projection** (`product_search_view`) maintained atomically with the write model in the same `SaveChangesAsync` via 8 `*ProjectionHandler` classes (one per internal domain event) — pure CQRS on one database, no eventual-consistency lag.
- **Feature-flag-gated search path** (`catalog.show-discontinued-in-search`) via OpenFeature per ADR-0014; flips behaviour without restart.
- **Service-to-service auth** per ADR-0010 via inbound JwtBearer + scopes `catalog.read` / `catalog.write`.
- **Correlation-id propagation** (HTTP → handler → outbox row → Kafka header → projection's `correlation_id` column) per ADR-0008.
- **Time discipline** (BCL `TimeProvider` injected throughout; arch-test forbids `DateTime.UtcNow` in domain) per ADR-0015.
