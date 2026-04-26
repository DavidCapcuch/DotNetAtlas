# Basket M9 — Docs Self-Corrections + Session Summary

> Milestone M9 per `docs/implementation-prompts/basket.md` `<session_management>` step 9 — *"Docs self-corrections + session summary."* Branch: `aaqwdqwd`. Final Basket milestone — closes the BC's contract for downstream Wave-2 / Wave-3 agents.

## Scope

M9 is **docs-only**. No production code, no test code, no Avro schema, no docker-compose changes. The session reconciled three handler-vs-`use-cases.md` divergences flagged at the bottom of [basket-m8.md:149-157](basket-m8.md), mirrored the reconciliation into the BC chapter, and posted this summary. One user-explicit boundary extension was authorized at session start (editing `docs/bc-design/use-cases.md`, which is not in M9's writable set per `basket.md <boundaries>`).

## Files modified

```
code:                 0
tests:                0
Avro schemas:         0
docker-compose delta: 0
doc updates:          3
  - docs/bc-design/use-cases.md          (modify, § 2.1.2 / § 2.1.4 / § 2.1.5)
  - docs/bc-design/basket.md             (modify, § 6.2 / § 6.3 / § 6.6)
  - docs/implementation-prompts/session-summaries/basket-m9.md  (NEW)
```

`glossary-basket.md` and `example-mapping/basket.md` were spot-checked and required no edits — both are still consistent with the shipped implementation.

## Decisions taken (with rationale)

1. **Reconcile by editing `use-cases.md`, not by changing handlers.** M8's summary listed the three divergences as M9 follow-ups with two reconciliation paths: edit the docs to match the implementation, or edit the handlers to match the docs. Path 1 was chosen because (a) the implementation is internally consistent — all three commands ship a uniform "no basket exists → 204 idempotent no-op" rule that mirrors `GetBasketByUserIdQuery`'s already-shipped 200-with-empty-shape behavior in [use-cases.md § 2.2.1:740](../../../docs/bc-design/use-cases.md); (b) the 23 M8 functional tests pin this behavior — flipping handlers would invalidate a green test surface for no domain-level benefit; (c) idempotency is the friendlier API contract for clients (a client retry storm on a 404 would be more painful than a quiet 204). Trade-off: `use-cases.md` is technically out of M9's writable set per `basket.md <boundaries>`. Mitigated via explicit user authorization captured in the session's chat (`AskUserQuestion` header `Doc divergences` → answer `Permission to edit use-cases.md`).
2. **Skip the M10 handoff template.** Basket has nine milestones (M1–M9) per `basket.md <session_management>`. There is no M10 — the BC is complete after M9. Rather than emit a fictitious `_handoff-template.md` block with `{N+1}=10` (which would cosmetically chain to a non-existent next session), end-of-session emits a Basket-complete announcement with a state snapshot of the other Wave-1 BCs so the user can target the next dispatch cleanly. Authorization captured in the session's chat (`AskUserQuestion` header `M10 handoff` → answer `Basket-complete announcement`).
3. **Strict "docs-only" interpretation of M9.** Several gaps surfaced or carried forward from M8 (real WireMock'd Catalog ACL test, parallel-AddItem concurrency integration test, `basket-api` container in docker-compose, `nw-mutation-test` post-green pass) are NOT addressed in M9 — they belong to either DEVOPS / Wave-0 platform follow-ups or to a separate "post-DoD test fortification" milestone. M9 stays minimal to keep the diff focused and reviewable.

## ADR application notes

No regressions from prior milestones — M9 introduces no code change.

- **ADR-0008** (correlation-id) — propagation tested by M4 (outbox publisher), M6 (outbox roundtrip integration test), and M8 (FastEndpoints `app.UseCorrelationId()` middleware). No M9 change.
- **ADR-0010** (service-to-service auth) — Inbound JWT validation (M8). Outbound `ProductCatalogHttpAdapter` wired with `AddServiceAuth("catalog.read")` (M5). Functional tests substitute `IProductCatalogQueryPort` so the real outbound auth path is exercised by M5 unit tests, not by M8/M9 functional tests (carry-forward — see § "Improvements proposed"). No M9 change.
- **ADR-0012** (api versioning) — All HTTP routes under `/api/v1/basket/...` (M8). Confirmed by 23/23 functional tests on rerun. No M9 change.
- **ADR-0013** (idempotency-key) — `.Idempotency()` on `/items` (optional, double-click guard) and `/checkout` (required, in-handler check), backed by `redis-cache`. M8 verified empirically. No M9 change.
- **ADR-0015** (time/timezone) — `TimeProvider` injected throughout; M7 architecture test forbids `DateTime.UtcNow` in `Basket.Domain`. Confirmed by 36/36 architecture tests. No M9 change.
- **ADR-0016** (Redis topology) — Keyed `IConnectionMultiplexer` for `Redis:Basket` vs `Redis:Cache`; M7 architecture test enforces no cross-instance leakage. No M9 change.

## Inconsistencies found (file:line → description)

**Fixed in this session (the M8-flagged drifts):**

- [`use-cases.md:578, 584`](../../../docs/bc-design/use-cases.md) — § 2.1.2 RemoveItem: prescribed 404 on no-basket; handler at [`RemoveItemFromBasketCommandHandler.cs:36-39`](../../../services/Basket/Basket.Application/Baskets/RemoveItem/RemoveItemFromBasketCommandHandler.cs) returns `Result.Ok()` → 204. **Doc updated to 204 idempotent.**
- [`use-cases.md:625, 630`](../../../docs/bc-design/use-cases.md) — § 2.1.4 RefreshPrices: prescribed 404 on no-basket; handler at [`RefreshBasketPricesCommandHandler.cs:39-42`](../../../services/Basket/Basket.Application/Baskets/RefreshPrices/RefreshBasketPricesCommandHandler.cs) returns `Result.Ok()` for both no-basket and empty-basket → 204. **Doc updated to 204 idempotent (no-basket OR empty-basket); ACL is not consulted on the no-op path.**
- [`use-cases.md:648, 653`](../../../docs/bc-design/use-cases.md) — § 2.1.5 Clear: prescribed 404 on no-basket; handler at [`ClearBasketCommandHandler.cs:36-39`](../../../services/Basket/Basket.Application/Baskets/Clear/ClearBasketCommandHandler.cs) returns `Result.Ok()` → 204. **Doc updated to 204 idempotent.**

**Surfaced in this session, NOT fixed (out of M9's authorized scope — see § "Improvements proposed"):**

- [`use-cases.md:610`](../../../docs/bc-design/use-cases.md) — § 2.1.3 ChangeQuantity references `BasketErrors.ItemNotInBasket(productId)`, but [`BasketErrors.cs:49-53`](../../../services/Basket/Basket.Domain/Baskets/Errors/BasketErrors.cs) defines the factory as `ItemNotFound(Guid productId)`. The user authorization carve-out covered § 2.1.{2,4,5} only — § 2.1.3 was not in scope.
- [`error-taxonomy.md:99-122`](../../../docs/bc-design/error-taxonomy.md) — § 3.1 `BasketErrors` sketch is missing the `ItemNotFound(productId)` factory entirely. `error-taxonomy.md` is fully outside M9's writable boundary set.

## Improvements proposed (NOT implemented unless approved)

Carry-forward list — items observed during M9 audit but not addressed under M9's strict docs-only scope:

- **Real WireMock'd Catalog ACL test** (M8 carry-forward). Today the M8 functional fixture substitutes `IProductCatalogQueryPort` via NSubstitute; the real `ProductCatalogHttpAdapter` (correlation-id + ServiceAuth header propagation through the typed `HttpClient`) is exercised only by M5 unit tests. A WireMock-based functional test would close that gap.
- **Parallel-AddItem concurrency integration test** (M8 carry-forward, basket.md `<dod>` line 119). Two concurrent `AddItemToBasketCommand` calls for the same user should exercise the CAS retry. M8's existing integration tests cover the outbox roundtrip but not the concurrency scenario.
- **Two Redis Testcontainers** (M8 carry-forward). Today one container backs both `Redis:Basket` + `Redis:Cache` namespaces in functional tests; M7 arch tests prevent cross-instance leakage at compile-time, but a regression that bypasses that gate (e.g., via reflection or a test-only seam) would not be caught at runtime.
- **`basket-api` container in `docker-compose.yaml`** (DEVOPS wave). Local smoke runs `dotnet run` against compose-managed infra; ops would prefer a containerized service.
- **`nw-mutation-test` post-green pass.** `_shared.md § 7` recommends after the suite is green; the kill-rate target (≥ 80%) is meaningful as a quality signal for the existing Basket test surface. Defer until appetite returns.
- **`appsettings.json` placeholder-secret startup validator** (platform-level). M8 left literal `"ShouldBeInVault"` placeholders in `Basket.Api/appsettings.json`; a `Platform.ServiceDefaults` enhancement could fail-fast on these in deployed environments.
- **`use-cases.md § 2.1.3` doc-vs-code drift fix**: rename `ItemNotInBasket` → `ItemNotFound` to match the shipped factory at [`BasketErrors.cs:49-53`](../../../services/Basket/Basket.Domain/Baskets/Errors/BasketErrors.cs).
- **`error-taxonomy.md § 3.1`**: add the `ItemNotFound(Guid productId)` factory to the sketch so the cross-BC error taxonomy reflects the BC's actual error set.

## Domain self-corrections

The three lifecycle subsections of `docs/bc-design/basket.md` § 6 gained a one-bullet "no-basket idempotency" annotation each, mirroring the `use-cases.md` reconciliation so the BC chapter is internally consistent for any future reader who reaches it before reading `use-cases.md`:

- **§ 6.2** Mutations — adds a bullet stating that `RemoveItem` and `Clear` against a non-existent basket return 204 (no lazy creation on these paths; only `AddItem` creates), while `ChangeQuantity` keeps the 404 contract because changing the quantity of an item in a non-existent basket has no defensible meaning.
- **§ 6.3** User-requested price refresh — adds a bullet stating that refresh against a non-existent or empty basket returns 204 without calling the ACL.
- **§ 6.6** Clear (manual) — adds a bullet stating that Clear against a non-existent basket returns 204 (no row created, no event raised).

## Verification output

The four CI gates per `_shared.md § 12` ran clean against the M9 working tree:

```text
$ dotnet build -m
... NU1903 vulnerability warnings on System.Security.Cryptography.Xml — pre-existing
across many projects (Weather, Catalog, Inventory, Ordering, Invoicing, Payments,
saga, platform, basket-infrastructure, etc.). Same 90-warning baseline as M8.
    90 upozornění
    Počet chyb: 0
Uplynulý čas 00:03:17.48

$ dotnet restore --locked-mode
... All projects restored. 0 errors. NU1903 warnings unchanged (same packages).

$ dotnet format whitespace --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění
protokolovat, nastavte možnost verbosity na úroveň diagnostic.
exit 0 — 0 violations.

$ dotnet format style --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění
protokolovat, nastavte možnost verbosity na úroveň diagnostic.
exit 0 — 0 violations.
```

All four test slices stayed green vs the M8 baseline (no production code touched in M9, so this confirms the docs edits did not accidentally break a `<see cref="..."/>` cross-reference or similar):

```text
$ dotnet test test/Basket.UnitTests/         → 143/143 green (466 ms)
$ dotnet test test/Basket.ArchitectureTests/ →  36/36 green (401 ms)
$ dotnet test test/Basket.IntegrationTests/  →   3/3 green (8 s)
$ dotnet test test/Basket.FunctionalTests/   →  23/23 green (2 s)
                                              ----- ------
                                              205 / 205 green
```

Functional tests environment: HTTP_PROXY / HTTPS_PROXY env vars unset for the test process per the M8 environmental note.

## Pre-commit Opus reviewer findings + resolution

`Agent(subagent_type="feature-dev:code-reviewer", model="opus")` per `_shared.md § 11`. The user's dispatch prompt explicitly required this even though M9's diff is under the ≥ 5-files threshold of `_shared.md § 11` step 0.

The reviewer was given the full M9 diff (3 files: `use-cases.md` § 2.1.{2,4,5}, `basket.md` § 6.{2,3,6}, `basket-m9.md`) plus this summary's design-decisions and carry-forward sections. Findings + resolutions:

| Severity | ID | Finding | Resolution |
|---|---|---|---|
| CRITICAL | — | _None._ | — |
| HIGH | — | _None._ | — |
| MEDIUM | M-1 | `_template.md <session_summary>` lists `### Open questions` as a distinct section; M9 folds open items into "Improvements proposed" without a labeled heading. M8 precedent did the same. | Added a one-line `## Open questions` section ("None — Basket BC is complete; carry-forwards are deferred per § Improvements proposed") to the summary so the template skeleton is fully covered. |
| LOW | L-1 | `use-cases.md § 2.1.3` references `BasketErrors.ItemNotInBasket` while `BasketErrors.cs:49-53` defines `ItemNotFound`. Real drift but out of M9's authorization carve-out. | Logged as carry-forward in § "Improvements proposed". No action in M9. |
| LOW | L-2 | This findings table was a placeholder at review time. | Populated in this commit before staging. |

The reviewer also positively verified: doc-vs-code reconciliation accuracy (handler line numbers match), internal consistency between `basket.md § 6` and `use-cases.md § 2.1.{2,4,5}`, no cross-reference rot, "doc updates: 3" file-count math, boundary discipline against pre-existing uncommitted other-BC changes, and the verification-output claim's plausibility against the M8 baseline.

## Boundary discipline

Stayed inside M9's `<session_management>` boundary throughout, except for the **one user-authorized boundary extension** (use-cases.md edit, § 2.1.{2,4,5} only).

In-bounds writes (per `basket.md <boundaries>`):
- `docs/bc-design/basket.md` — § 6.2 / § 6.3 / § 6.6 annotations (self-correction).
- `docs/implementation-prompts/session-summaries/basket-m9.md` — NEW file (session summary location follows M8 precedent).

User-authorized boundary extension:
- `docs/bc-design/use-cases.md` — § 2.1.2 / § 2.1.4 / § 2.1.5 doc-vs-code reconciliation (HTTP 404 → 204 idempotent).

NOT touched:
- `services/**` — no code edits in M9.
- `test/Basket.*Tests/**` — no test edits in M9.
- `platform/**`, `saga/**`, `bff/**` — out of any BC's territory.
- `docker-compose.yaml` — no compose drift.
- `Directory.Packages.props` — no package additions.
- `docs/bc-design/error-taxonomy.md` — out-of-bounds; § 3.1 sketch drift logged as carry-forward.
- `docs/bc-design/use-cases.md § 2.1.3` (ChangeQuantity) — out-of-authorization (user carved `{2,4,5}` only); `ItemNotInBasket` vs `ItemNotFound` rename logged as carry-forward.
- `docs/bc-design/glossary-basket.md`, `docs/bc-design/example-mapping/basket.md` — read but no drift surfaced; no edits needed.
- Other BCs' docs.
- The pre-existing uncommitted modifications visible in `git status` at session start (Catalog, Inventory, ADRs, ordering.md, payments.md prompts, keycloak service-scope-matrix.md). Same disposition as M8 — left untouched. Only M9 files were staged.

## What "done" looks like for M9

- [x] Three M8-flagged handler/doc divergences reconciled in `use-cases.md` (§ 2.1.{2,4,5}).
- [x] Lifecycle reconciliation mirrored into `basket.md` (§ 6.2 / § 6.3 / § 6.6).
- [x] Session summary posted at `docs/implementation-prompts/session-summaries/basket-m9.md` mirroring `_template.md <session_summary>` + basket-m8.md depth.
- [x] Four CI gates green (build, restore, format whitespace, format style).
- [x] Four test slices green: 205 / 205.
- [x] Pre-commit Opus reviewer ran; findings triaged. 0 CRITICAL, 0 HIGH, 1 MEDIUM (M-1, addressed inline by adding `## Open questions` section), 2 LOW (logged in findings table).
- [x] M9 docs + summary committed on branch `aaqwdqwd` — single commit, all three files (`use-cases.md`, `basket.md`, `basket-m9.md`).
- [ ] `nw-software-crafter-reviewer` (Haiku) — runs after the commit lands; HIGH-severity findings (if any) fixed in a follow-up commit.
- [ ] Basket-complete announcement emitted as final user-facing chat output.

## Open questions

None — Basket BC is complete. Carry-forward items are tracked under § "Improvements proposed" (above) and remain available for any future Basket touch-up milestone or for adoption by the DEVOPS / platform-enhancement waves where applicable.

## Basket BC complete

All nine milestones — scaffold, domain, Redis repository, application, ACL adapter, infrastructure, architecture tests, functional tests + API wiring, and now docs self-corrections + session summary — have shipped on branch `aaqwdqwd`. The BC's contract surfaces are stable for downstream agents:

- **External event:** `BasketCheckoutInitiatedEvent` on topic `basket.sessions` (3 partitions, 30-day retention, FORWARD_TRANSITIVE compatibility), with the address-courier payload per ADR-0005.
- **HTTP routes:** All six commands + one query under `/api/v1/basket/...` per ADR-0012, with `Idempotency-Key` semantics per ADR-0013 (optional double-click guard on `/items`; required on `/checkout`).
- **Storage discipline:** Aggregate primary store on `redis-basket` (AOF + noeviction); idempotency cache on `redis-cache` (volatile); SQL side-car holds outbox + inbox only (no `DbSet<Basket>`, enforced by architecture test). All per ADR-0016.

A Wave-2 (Checkout saga) agent can consume `BasketCheckoutInitiatedEvent` end-to-end without modifying any Basket code. There is no M10 — Wave 1 continues independently on the other five BCs (Catalog / Ordering / Inventory / Payments / Invoicing) per the dispatch sequence in `_shared.md § 1`.
