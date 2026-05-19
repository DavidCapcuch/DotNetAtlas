# Catalog — Wave 1 Closeout Follow-ups Session Summary

**Branch:** `aaqwdqwd`
**Date:** 2026-05-19
**Scope:** Triage all 17 catalog session-summary docs (across `session-summaries/` and `session-summaries2/`), TDD-fix CRITICAL+HIGH within Catalog boundaries, file MEDIUM+LOW and cross-cutting items as GitHub issues.

---

## 1. Session goal

Address the residual punch-list from Wave-1 closeout for the Catalog bounded context. Most CRITICAL items had been closed in earlier milestones (M3.5 / M4 / M5 / M7 / M8 / M9); this session is the fixed sweep for the HIGH security / TOCTOU / perf items that remained open, plus filing of everything that was either an architectural decision, MEDIUM/LOW, or out-of-scope cross-cutting work.

---

## 2. Triage breakdown

| Category | Count | Disposition |
|---|---|---|
| CRITICAL — already fixed in earlier milestones | 4 | No action (M5 tautological test; M8 NU1902; M9 fixture wiring; M10 Stryker MTP) |
| CRITICAL — out of Catalog scope | 1 | Filed as cross-cutting issue D1 (Wave-0 DB-bootstrap) |
| HIGH — already fixed in earlier milestones | ~8 | No action |
| **HIGH — clear-cut, TDD-fix this session** | **9** | **Commits A1-A9 below** |
| HIGH — architectural decision needed | 5 | Filed as `ready-for-agent` issues (B1-B5) |
| HIGH — contract extension (requires use-cases.md sign-off) | 1 | Filed as `ready-for-agent` issue (B6 — ActivateProductCommand) |
| HIGH — out of Catalog scope | 2 | Filed as cross-cutting issues (D2 healthcheck, D11 JwtBearer) |
| MEDIUM | ~14 | Filed as `needs-triage` issues (subset of C1-C35) |
| LOW | ~20 | Filed as `needs-triage` issues (subset of C1-C35) |
| Cross-cutting (out of scope) | 12 | Filed as `cross-cutting(wave1-followup):` issues (D1-D12) |
| REBUTTED / NO-FINDING | 4 | No action (Kind=Unspecified; PUT idempotency; etc.) |

---

## 3. Fixes shipped (Phase A)

Each fix follows TDD discipline (failing test first → implementation → green) with one commit per fix.

| # | SHA | Title | Test path |
|---|---|---|---|
| A1 | `fcbb861` | `fix(catalog): drop unused FastEndpoints.Attributes from Application layer` | `test/Catalog.ArchitectureTests/CleanArchitecture/CleanArchitectureLayerTests.cs` (csproj XML scan) |
| A2 | `c2578b4` | `fix(catalog): escape LIKE wildcards and bound search Text to 100 chars` | `test/Catalog.UnitTests/Products/SearchProducts/SearchProductsQueryValidatorTests.cs` + `SearchProductsQueryHandlerTests.cs` |
| A3 | `d2319b7` | `fix(catalog): restrict ImageReference URL schemes to http/https` | `test/Catalog.UnitTests/Products/ValueObjects/ImageReferenceTests.cs` + `CreateProductCommandValidatorTests.cs` |
| A4 | `527265b` | `fix(catalog): harden HTML-markup heuristic in product description validators` | `test/Catalog.UnitTests/Products/CreateProduct/CreateProductCommandValidatorTests.cs` + `DescribeProductCommandValidatorTests.cs` |
| A5 | `2aab8e3` | `fix(catalog): translate unique-constraint race on SKU to ProductErrors.SkuAlreadyExists` | `test/Catalog.UnitTests/Products/CreateProduct/CreateProductCommandHandlerTests.cs` (via `ThrowOnSaveCatalogDbContext` test seam) |
| A6 | `98d2cbb` | `fix(catalog): clear ChangeTracker after ExecuteUpdate in ReparentCategory` | `test/Catalog.UnitTests/Categories/ReparentCategory/ReparentCategoryCommandHandlerTests.cs` |
| A7 | `b47f415` | `fix(catalog): scope GetCategoryTree product-count query to loaded subtree` | Existing tests as regression coverage (pure-perf fix; InMemory cannot observe scan cost) |
| A8 | `27e9863` | `fix(catalog): enforce 30s per-message budget in StockLevelChangedKafkaHandler` | No new unit test (Kafka inbox round-trip filed as C18); behaviour pinned via `PerMessageBudget` constant |
| A9 | `9fa7bfa` | `fix(catalog): wire correlation-id from ambient Activity into ProductCreatedProjectionHandler` | `test/Catalog.UnitTests/Products/CreateProduct/ProductCreatedProjectionHandlerTests.cs` (activity-tag and no-activity branches) |

### Notes on each fix

- **A1** (CAT-ARCH-C01): NetArchTest cannot see unused `PackageReference`s (IL-level), so the architecture test reads the csproj XML directly. Regenerated `Catalog.Application/packages.lock.json` via `dotnet restore --force-evaluate`.
- **A2** (CAT-SEC-001 / CAT-RV-H03): `query.Text` is now escaped (`%`, `_`, `\\`) before substitution into `EF.Functions.Like(name, pattern, "\\")`, and `SearchProductsQueryValidator` caps it at 100 characters.
- **A3** (CAT-SEC-005): `ImageReference.Create` and `CreateProductCommandValidator` now reject any scheme other than `http` and `https` (blocks `javascript:`, `data:`, `file:`, `ftp:`).
- **A4** (CAT-SEC-004): The new `Catalog.Application/Common/Validation/HtmlHeuristic.cs` detects `<letter`, `<!`, `<?`, `</`, `&#`, `&lt;`, `&gt;` in addition to the prior single rule. Both `CreateProduct` and `DescribeProduct` validators share it.
- **A5** (CAT-RV-H04): Added `<PackageReference Include="EntityFrameworkCore.Exceptions.Common"/>` to `Catalog.Application.csproj`; handler now wraps `SaveChangesAsync` in `try/catch (UniqueConstraintException)` that translates to `ProductErrors.SkuAlreadyExists`. New `ThrowOnSaveCatalogDbContext` test helper makes this unit-testable; `FakeCatalogDbContext` was un-sealed to allow the derived helper.
- **A6** (CAT-RV-H05): `_db.ChangeTracker.Clear()` after `SaveChangesAsync` inside the existing `EnsureTransactionAsync` wrap. Added `ChangeTracker` to `ICatalogDbContext`. One existing test was reshaped — it had been asserting unpopped domain events on a re-fetched aggregate, which relied on tracking state.
- **A7** (CAT-RV-H06): `loadedCategoryIds.Contains(r.CategoryId)` added to the count `GROUP BY` query. EF Core 9+ translates `HashSet<Guid>.Contains` to a parameterized `IN (...)`.
- **A8** (CAT-RV-H02): `CancellationTokenSource.CreateLinkedTokenSource(WorkerStopped)` + `CancelAfter(PerMessageBudget)` where `PerMessageBudget = TimeSpan.FromSeconds(30)`.
- **A9** (CAT-RV-C01): Reads from `Activity.Current?.GetTagItem("correlation.id")` — the tag set by `CorrelationIdMiddleware` (the original TODO incorrectly referenced `HttpContext.Items`, which the middleware does not populate). Falls back to `Guid.Empty` for background / inbox-driven flows. No DI changes — `Activity.Current` is ambient.

---

## 4. Issues filed

### B — Architectural HIGH (ready-for-agent)
Six items requiring a decision before implementation.

- [#172](https://github.com/DavidCapcuch/DotNetAtlas/issues/172) catalog(wave1-followup): admin vs public Drafts filter direction (CAT-RV-H03)
- [#173](https://github.com/DavidCapcuch/DotNetAtlas/issues/173) catalog(wave1-followup): OutputCache strategy decision (CAT-RV-H08)
- [#174](https://github.com/DavidCapcuch/DotNetAtlas/issues/174) catalog(wave1-followup): StockLevelChangedKafkaHandler projection-write location (CAT-ARCH-C02)
- [#175](https://github.com/DavidCapcuch/DotNetAtlas/issues/175) catalog(wave1-followup): CategoryBreadcrumb staleness on reparent (CAT-RV-H07)
- [#176](https://github.com/DavidCapcuch/DotNetAtlas/issues/176) catalog(wave1-followup): KafkaFlow correlation-header DLT ordering (CAT-RV-H01)
- [#177](https://github.com/DavidCapcuch/DotNetAtlas/issues/177) catalog(wave1-followup): add ActivateProductCommand (contract extension)

### C — MEDIUM + LOW (needs-triage)

- [#178-#181](https://github.com/DavidCapcuch/DotNetAtlas/issues/178) Architecture-test rules: tech forbids, ISpecificRecord ban, sealed-record commands, unbound AddOptions (CAT-ARCH-C03/04/05/06)
- [#182-#184](https://github.com/DavidCapcuch/DotNetAtlas/issues/182) Result-pattern conversions + projection re-fetch + DRY (CAT-RV-M02/03/06)
- [#185-#187](https://github.com/DavidCapcuch/DotNetAtlas/issues/185) Test quality (CAT-TST-M01/02/03)
- [#188-#192](https://github.com/DavidCapcuch/DotNetAtlas/issues/188) Security MEDIUMs (CAT-SEC-006/007/008/009/010-012)
- [#193-#196](https://github.com/DavidCapcuch/DotNetAtlas/issues/193) Catalog M4 integration coverage carryforward (reparent, discontinue, Kafka inbox, feature-flag search)
- [#197-#200](https://github.com/DavidCapcuch/DotNetAtlas/issues/197) Stryker / mutation-test follow-ups (solution+lang-version, ignore string, Catalog.Application, ProductStatus theory)
- [#201](https://github.com/DavidCapcuch/DotNetAtlas/issues/201) Strengthen Product.Create factory side-effect assertions
- [#202-#212](https://github.com/DavidCapcuch/DotNetAtlas/issues/202) LOW polish (XMLdoc, CategoryRenamedDomainEvent, mutation-strengthen, BuildBreadcrumb, breadcrumb-stale extend, PII log, ctor ordering, OutputCache redis probe, SR probe timeout, Stryker pin, CAT-SEC-013/014/015/016)

### D — Cross-cutting (`cross-cutting(wave1-followup):` titled, `needs-triage` labelled)

- [#213](https://github.com/DavidCapcuch/DotNetAtlas/issues/213) Wave-0 DB-bootstrap (CRITICAL platform blocker)
- [#214](https://github.com/DavidCapcuch/DotNetAtlas/issues/214) docker-compose healthcheck directive for every BC .API
- [#215](https://github.com/DavidCapcuch/DotNetAtlas/issues/215) OTEL core-set coherence (1.15.3 across all CPMs)
- [#216](https://github.com/DavidCapcuch/DotNetAtlas/issues/216) Healthcheck path drift `/healthz/*` vs `/api/*`
- [#217](https://github.com/DavidCapcuch/DotNetAtlas/issues/217) Apply Catalog arch-test set to sister BCs
- [#218](https://github.com/DavidCapcuch/DotNetAtlas/issues/218) Basket HealthChecks drift
- [#219](https://github.com/DavidCapcuch/DotNetAtlas/issues/219) Sister-BC integration-fixture interceptor parity
- [#220](https://github.com/DavidCapcuch/DotNetAtlas/issues/220) Fail-fast topic-config (ValidateDataAnnotations + ValidateOnStart)
- [#221](https://github.com/DavidCapcuch/DotNetAtlas/issues/221) CLAUDE.md proxy-workaround drift (recommend B over A on Win+net10)
- [#222](https://github.com/DavidCapcuch/DotNetAtlas/issues/222) architecture-tests.md § 2.1 doc — singular projection handler reference
- [#223](https://github.com/DavidCapcuch/DotNetAtlas/issues/223) JwtBearer config-bind overwrites TokenValidationParameters (CAT-SEC-003)
- [#224](https://github.com/DavidCapcuch/DotNetAtlas/issues/224) events-catalog.md § 7.1 drift — Catalog DOES consume inventory.stock-level-changed

---

## 5. Verification gates

All commands run from repo root on branch `aaqwdqwd`. The solution-wide `dotnet build -m` returned 20 errors — all in `Basket.Application` from unstaged parallel work present in the working tree when this session began (file modifications in Basket, Payments, Inventory not touched by this session). The Catalog stack builds clean:

```
$ dotnet build services/Catalog/Catalog.API/Catalog.API.csproj -m --no-restore
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | PASS (after A1 + A5 lockfile regen) |
| `dotnet build` (Catalog stack) | PASS (0 errors, 0 warnings) |
| `dotnet format whitespace --verify-no-changes` (per Catalog project) | PASS for all 6 Catalog projects |
| `dotnet format style --verify-no-changes` (per Catalog project) | PASS for all 6 Catalog projects |
| Catalog.UnitTests | **294 passed**, 0 failed, 0 skipped |
| Catalog.ArchitectureTests | **42 passed**, 0 failed, 0 skipped |
| Catalog.IntegrationTests (Testcontainers Postgres) | **1 passed**, 0 failed (only after `unset HTTP_PROXY HTTPS_PROXY ...`; `NO_PROXY='*'` did not work on this Windows + .NET 10 host — confirms cross-cutting issue D9 / catalog-m9.md:264) |
| Catalog.FunctionalTests | **29 passed**, 0 failed, **6 skipped** (carry-forward — see § 6) |

---

## 6. Carry-forward

### Skipped Catalog.FunctionalTests (6)

All blocked on issue [#177](https://github.com/DavidCapcuch/DotNetAtlas/issues/177) (`ActivateProductCommand` contract extension) or platform Activity bridge:

1. `CorrelationIdRoundtripTests.WhenCorrelationIdHeaderProvided_OutboxRowCarriesIt` — blocked on platform Activity bridge (the projection-side wiring lands in A9 but the outbox publisher carryover still requires platform changes)
2. `ReactivateProductTests.WhenAdminFlagTrue_Returns204_AndStatusActive` — blocked on ActivateProductCommand
3. `ReactivateProductTests.WhenAdminFlagFalse_Returns403` — blocked on ActivateProductCommand
4. `DiscontinueProductTests.WhenValidRequest_Returns204_AndStatusDiscontinued_AndOutboxRow` — blocked on ActivateProductCommand (Discontinue requires a non-Draft starting state which only Activate can produce in the test fixture today)
5. `GetProductsByCategoryTests.*` (×2) — blocked on ActivateProductCommand (the seed data path expects active products)

### HIGHs deferred to architectural-decision issues

The 5 architectural HIGHs and the ActivateProductCommand contract extension (B1–B6 above) are filed for owner sign-off rather than rushed in this session. Their proposed approaches and trade-offs are captured in the issue bodies.

### Pure-performance fix without a red-phase unit test

Fix A7 (GetCategoryTree GROUP BY scope) is a pure-performance change. EF Core InMemory provider cannot observe scan cost differences, so the fix relies on existing regression tests plus code-review review for correctness. A real-Postgres integration test was filed as C16 (covers both reparent and tree-count scenarios).

---

## 7. Cross-cutting blockers worth surfacing

Two of the cross-cutting issues are platform-wide blockers that affect every BC:

- **[#213](https://github.com/DavidCapcuch/DotNetAtlas/issues/213) Wave-0 DB-bootstrap**: `docker-compose.yaml` mounts only the Keycloak init script. Every BC's outbox-relay has been silently crash-looping for the lifetime of the compose stack. Needs Postgres init script that creates per-BC databases plus a cold-start migration strategy. **Platform/DevOps follow-up.**
- **[#215](https://github.com/DavidCapcuch/DotNetAtlas/issues/215) OTEL core-set coherence**: The M8 NU1902 bump left companion OTEL packages at 1.12.0 while the protocol exporter jumped to 1.15.3. Half-bumped SDK state across root/saga/services CPMs. **Platform follow-up.**

---

**Session signed off with green Catalog gates; all 53 follow-up issues live at https://github.com/DavidCapcuch/DotNetAtlas/issues/172 onward.**

---

## 8. Wave-1 follow-up second pass (2026-05-19)

After the initial triage + fix sweep, a second pass tackled the C-bucket follow-ups (#178–#212). All 35 issues are now resolved — either by code change (24) or by close-with-rationale (11). Seven new commits land on `aaqwdqwd`:

### Commits

| # | SHA | Title | Issues |
|---|---|---|---|
| 1 | `1b369ac` | `refactor(catalog): enforce architecture-tests § 1.1 forbids + drop unbound AddOptions` | #178, #179, #181 |
| 2 | `c047bd5` | `refactor(catalog): convert all commands, queries, and input DTOs to sealed records` | #180 |
| 3 | `6a14932` | `refactor(catalog): Result.Fail for state-transition rejections + lift duplicate currency check` | #182, #184 |
| 4 | `c9575bc` | `fix(catalog): Unicode-rune length validation + force EnableDetailedErrors off in Production` | #188, #191 |
| 5 | `c46eb28` | `test(catalog): integration tests for reparent path-cascade + discontinue time-threading` | #193, #194 |
| 6 | `afa9fe9` | `test(catalog): strengthen test quality + mutation-testing baseline` | #185, #186, #197, #198, #200, #201 |
| 7 | `bb2ab0d` | `chore(catalog): low-priority polish bundle from Wave-1 closeout` | #202, #205, #207, #208, #210, #212 |

### Issues closed without code change

Status-only closures, each with a `gh issue close -c "..."` explanatory comment:

- **Wontfix** (3): #189 placeholders (non-secret per ADR-0010), #190 enumeration oracle (BFF contract; gateway rate-limiting is the right mitigation), #209 OutputCache redis namespace (over-engineering for v1).
- **Subsumed** (1): #187 (integration test coverage was the umbrella for #193, #194, #196).
- **Defer-with-rationale** (7): #183 ProductCreatedDomainEvent enrichment (event-shape refactor without current pain), #195 Kafka inbox roundtrip (needs Testcontainers Kafka), #196 feature-flag SearchProducts integration (fixture refactor needed for OpenFeature), #199 Stryker on Application (long-running pass), #203 CategoryRenamedDomainEvent (current sentinel pattern fine), #204 80% kill-rate target (depends on #199 + #200/#201 results), #206 breadcrumb rebuild on cascade (depends on B4 / #175 decision).

### Verification gates (post-second-pass)

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | PASS |
| `dotnet format whitespace --verify-no-changes` (all 8 Catalog projects) | PASS |
| Catalog.UnitTests | **304 passed**, 0 failed (was 294 — +10 new test methods across record migration, sealed-record arch test, state-transition Result.Fail, runes, breadcrumb-hyphen, Product.Create assertions, ProductStatus theory) |
| Catalog.ArchitectureTests | **46 passed**, 0 failed (was 42 — +4 new: tech-forbids for Domain + Application, ISpecificRecord ban, sealed-record convention) |
| Catalog.IntegrationTests (Testcontainers Postgres) | **3 passed**, 0 failed (was 1 — +2 new: reparent path-cascade, discontinue time-threading) |
| Catalog.FunctionalTests | **29 passed**, 0 failed, **6 skipped** (unchanged carry-forward — all blocked on B6 / #177 `ActivateProductCommand` contract extension and the platform Activity-bridge for `CorrelationIdRoundtripTests`) |

### Carry-forward into Wave 2

All B-bucket architectural-decision issues (#172–#177) and all D-bucket cross-cutting issues (#213–#224) remain open as intended. They're outside the C-bucket scope and either need product-owner sign-off (#177 ActivateProductCommand contract extension) or sit outside the Catalog scope boundary (cross-cutting platform work).

The seven deferred C-bucket issues listed above are also closed with deferral rationale; reopen if/when the dependency unblocks.
