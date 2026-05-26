# Catalog M10 — HealthChecks timeout binding + Stryker.NET mutation baseline

> Milestone M10 per the user-dispatched generic milestone-N prompt.
> **No canonical M10 exists in [`catalog.md`](../catalog.md) `<session_management>`** (which
> formally enumerates only M1-M8); M9 was already a user-authorized verification-gap close,
> and M9's summary at [catalog-m9.md:378-381](catalog-m9.md) explicitly says "the next session
> will negotiate what M10 means in context". This is that session. Branch: `aaqwdqwd`.

## Mission

The dispatch asked me to "execute M10". With no canonical scope, I surfaced four candidates
via `AskUserQuestion`:

- **A** — Implement `ActivateProductCommand` (un-skip 5/6 functional tests; crosses
  `use-cases.md § 1.1` contract).
- **B** — Close `CorrelationIdRoundtripTests` (1/6 skips) via in-bounds fixture Activity;
  technical risk + may need platform code.
- **C+D** — `nw-mutation-test` pass on Catalog + wire the unused `HealthChecks:*Timeout` keys
  in `appsettings.json:96-101`. Fully in-bounds, no contract extension.
- **E** — M10 no-op closeout; ship only a handoff.

User picked **C+D**. The 6 skipped functional tests remain skipped after M10 and are
re-documented as carry-forwards.

## Scope discipline notes (boundary nuances surfaced this session)

1. **Stryker.NET incompatibility false alarm — user push-back was correct.** My initial run
   (and a follow-up tight-scope sanity check) returned **0 killed mutants** with the
   warning `It looks like the test coverage capture failed. Disable coverage based
   optimisation.` I prematurely concluded Stryker.NET 4.14.1 was incompatible with the
   repo's `xunit.v3` + Microsoft Testing Platform (MTP) stack and proposed skipping Group C.
   The user pushed back ("can you verify that stryker is really incompatible with xunit v3?
   I think they added support for it"). Verification via DeepWiki + WebSearch surfaced
   Stryker.NET's [MTP runner blog post](https://stryker-mutator.io/blog/stryker-net-mtp-runner/)
   announcing **preview MTP support starting in Stryker.NET 4.13**, opt-in via
   `"test-runner": "mtp"` in `stryker-config.json`. After enabling it, the mutation pass
   ran cleanly (159 killed / 176 survived / 0 timeout / 46.90 %). Lesson: when a tool
   produces structurally-impossible output (0 killed against 255 robust tests), the first
   hypothesis should be misconfiguration, not incompatibility. Logged here so the next
   reviewer doesn't repeat the misdiagnosis.

2. **First mutation run was sleep-corrupted; the recorded 9:54 elapsed is wall-clock,
   not actual test time.** Machine slept overnight mid-mutation-pass; Stryker recorded
   `Runner N: the computer slept during the testing, need to retry` warnings on resume
   and marked 90 mutants as Timeout. That run's results were discarded after the MTP
   config landed and a fresh re-run completed in 5:41 with no sleep.

3. **Pre-existing dirty Invoicing state appeared mid-session.** `git status` was clean at
   session start ("nothing to commit, working tree clean"). Mid-session — after my first
   `dotnet build -m` — `git status` surfaced 1 `D` and 11 `??` Invoicing changes
   (`services/Invoicing/Invoicing.{Application,Domain,Infrastructure}/IAssemblyMarker.cs`
   plus `test/Invoicing.ArchitectureTests/{BaseTest.cs,Application/,CleanArchitecture/,
   CrossBoundedContext/,Domain/,Infrastructure/,Pii/,Rules/}` and a deleted `Placeholder.cs`).
   None of these are my work — they're either a concurrent Invoicing session in this repo,
   or a build-step side effect for a different BC. Left untouched per boundary discipline;
   the M10 commit does not stage them. Same disposition as catalog-m7/m8/m9.

4. **`.config/dotnet-tools.json` is a cross-cutting bootstrap.** It's NOT inside Catalog
   `<boundaries>` (not under `services/Catalog/**`, `test/Catalog.*Tests/**`,
   `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/**`, `docker-compose.yaml`,
   `Directory.Packages.props`, or Catalog's design docs). It's a NEW dotnet-tools manifest
   at the repo root, created to install Stryker.NET 4.14.1 as a local tool for Group C.
   The approved plan flagged this as "Possibly `Directory.Packages.props` /
   `services/Directory.Packages.props` if Stryker requires a tool reference (Catalog-specific
   package additions are allowed per `<boundaries>`)." `.config/dotnet-tools.json` is a
   different artifact (CLI tools, not NuGet packages) but the same spirit applies — it's the
   bootstrap for the BC-scoped Group C work. Other BCs may reuse the manifest (it'd be a
   trivial `dotnet tool restore` to pick up Stryker). Logged here so the boundary-discipline
   reviewer sees the rationale, not silent crossing.

## Files modified

```
code (production):     1   services/Catalog/Catalog.Infrastructure/Common/HealthChecksDependencyInjection.cs  (+14 / -1)
code (NEW config):     1   services/Catalog/Catalog.Infrastructure/Common/Config/HealthChecksOptions.cs       NEW (+33)
appsettings:           1   services/Catalog/Catalog.API/appsettings.json                                       (-1 line: DatabaseTimeout)
Avro schemas:          0
docker-compose delta:  0
tooling (NEW):         2   .config/dotnet-tools.json + test/Catalog.UnitTests/stryker-config.json              NEW
.gitignore:            1   .gitignore                                                                          (+3: Stryker output)
doc updates:           1   docs/implementation-prompts/session-summaries/catalog-m10.md                        NEW
```

**Total: 7 files staged (6 code/config/tooling + 1 NEW summary).** Above the 5-file
threshold for the mandatory pre-commit Opus reviewer per `_shared.md § 11` step 0.

## Group D — HealthChecks timeout binding (3 files)

### Design decisions

1. **POCO mirrors the platform reference at
   [`platform/Platform.OutboxRelay.WorkerService/Common/Config/HealthChecksOptions.cs`](../../../platform/Platform.OutboxRelay.WorkerService/Common/Config/HealthChecksOptions.cs).**
   Shape: `public const string Section = "HealthChecks";` + `[Required]` +
   `[Range(typeof(TimeSpan), "00:00:01", "00:01:00")]` + `required TimeSpan` properties.
   The platform reference exposes `SelfTimeout`, `OutboxRelayExecutionTimeout`, and
   `KafkaTimeout`. Catalog exposes only the ones whose underlying `.AddXxx` registration
   accepts a `timeout` parameter: `SelfTimeout`, `KafkaTimeout`, `RedisTimeout`.
   `.AddDbContextCheck<CatalogDbContext>` and `.AddUrlGroup(schemaRegistryUri, ...)` do not
   accept `timeout:` arguments — so I omit those from the POCO rather than carry
   silently-ignored config keys.

2. **`DatabaseTimeout` deletion was a deliberate decommission, not an oversight.** The
   pre-M10 `appsettings.json:96-101` carried `"DatabaseTimeout": "00:00:05"`, but
   `.AddDbContextCheck<>` doesn't take a `timeout:` parameter (the EF Core command timeout
   governs query duration, set elsewhere via `Npgsql` options). The platform reference at
   `OutboxRelay/Common/HealthChecksDependencyInjection.cs` doesn't pass a DB timeout either.
   Trade-off accepted: deleting the key is a tighter contract; a future operator who wants
   a DB-level timeout would either move from `.AddDbContextCheck` to `.AddNpgSql` (which
   does take `timeout:`) or wire a `CommandTimeout` into the `EfCoreOptions` POCO. Both are
   separate decisions worth their own commit + review.

3. **Single fail-fast binding via `AddOptionsWithValidateOnStart` +
   `BindConfiguration` + `ValidateDataAnnotations`.** Mirrors the pattern used by every
   other `*Options` POCO in `Catalog.Infrastructure.Common` (see
   [PersistenceDependencyInjection.cs:25-31](../../../services/Catalog/Catalog.Infrastructure/Common/PersistenceDependencyInjection.cs)
   for `EfCoreOptions` + `ConnectionStringsOptions`, and
   [MessagingDependencyInjection.cs:46-56](../../../services/Catalog/Catalog.Infrastructure/Common/MessagingDependencyInjection.cs)
   for `KafkaOptions` + `CatalogTopicsOptions` + `StockLevelChangedConsumerOptions`).
   Catalog now has 6 fail-fast-bound options POCOs; the pattern is uniform.

4. **No `Microsoft.Extensions.Options` using directive needed.** I added it briefly,
   noticed sister DI files in `Catalog.Infrastructure.Common` don't import it (the
   `AddOptionsWithValidateOnStart` / `BindConfiguration` / `ValidateDataAnnotations`
   extensions are surfaced via the `Microsoft.Extensions.DependencyInjection` namespace
   already imported), and reverted the added `using`. Sibling files for reference:
   [PersistenceDependencyInjection.cs:1-9](../../../services/Catalog/Catalog.Infrastructure/Common/PersistenceDependencyInjection.cs),
   [MessagingDependencyInjection.cs:1-15](../../../services/Catalog/Catalog.Infrastructure/Common/MessagingDependencyInjection.cs).

### Verification at HEAD (post-Group D + pre-Group C)

```
$ dotnet test test/Catalog.UnitTests/ --no-build --no-restore
Úspěšné!    - Neúspěšné:     0, Úspěšné:   255, Přeskočeno:     0, Celkem:   255, Doba trvání: 1 s

$ dotnet test test/Catalog.ArchitectureTests/ --no-build --no-restore
Úspěšné!    - Neúspěšné:     0, Úspěšné:    41, Přeskočeno:     0, Celkem:    41, Doba trvání: 462 ms
```

255 + 41 baseline match. `AddOptionsWithValidateOnStart<HealthChecksOptions>()` is exercised
indirectly via the Catalog.API host bootstrap; no Catalog unit test directly hosts the API,
so the binding is verified at integration / functional level (re-run below) and at
`dotnet stryker`'s initial build pass (which also bootstraps the API host).

## Group C — Stryker.NET mutation baseline on Catalog.Domain

### Tool bootstrap

- **NEW** `.config/dotnet-tools.json` — dotnet local-tools manifest at the repo root
  (created via `dotnet new tool-manifest`).
- **NEW Stryker.NET 4.14.1** installed as a local tool (`dotnet tool install dotnet-stryker`).
- **NEW** `test/Catalog.UnitTests/stryker-config.json` — Stryker.NET config scoped to:
  - Production project: `Catalog.Domain.csproj`
  - Test driver: `Catalog.UnitTests.csproj` (255 tests)
  - Mutation scope: `Products/**/*.cs` + `Categories/**/*.cs`
  - Thresholds: `high: 90`, `low: 80`, `break: 0` (informational — never fails the build)
  - **Test runner: `"mtp"`** — explicit opt-in to the preview Microsoft Testing Platform
    runner (Stryker.NET 4.13+) required because the test stack is xunit.v3 + MTP. Without
    this flag, Stryker falls back to the legacy VSTest path, coverage capture fails, and
    every mutated build is incorrectly reported as "tests passed" → 0 killed. Documented
    at [stryker-mutator.io/blog/stryker-net-mtp-runner/](https://stryker-mutator.io/blog/stryker-net-mtp-runner/).
- **NEW** `.gitignore` entry: `**/StrykerOutput/` — Stryker emits HTML + JSON reports per
  run, never committed.

### Mutation result (final, MTP runner, fresh state, no sleep)

```
[15:46:53 WRN] The Microsoft Test Platform testrunner is currently in preview. Results
              should be verified since this feature is still being tested.
[15:46:54 INF] Number of tests found: 255 for project ...Catalog.Domain.csproj. Initial
              test run started.
[15:47:02 INF] 385 mutants created
[15:47:02 INF] Capture mutant coverage using 'CoverageBasedTest' mode.
[15:47:02 INF] Starting coverage capture for MTP runner
[15:47:04 INF] Coverage capture complete: 365 mutations covered, 4 static mutations
[15:47:04 INF] 16    mutants got status CompileError. Reason: Mutant caused compile errors
[15:47:04 INF] 4     mutants got status NoCoverage.   Reason: Not covered by any test.
[15:47:04 INF] 30    mutants got status Ignored.      Reason: Removed by block already covered filter
[15:47:04 INF] 50    total mutants are skipped for the above mentioned reasons
[15:47:04 INF] 335   total mutants will be tested

Killed:    159
Survived:  176
Timeout:     0

[15:51:00 INF] Time Elapsed 00:05:41.7479421
[15:51:00 INF] The final mutation score is 46.90 %
```

### Survivor analysis (informational; no test-strengthening in M10)

Of the 176 survivors:

- **~88 are likely equivalent mutants** — String mutations on exception / error-message
  text. Catalog's unit tests assert error CODES (e.g., `Product.SkuRequired`) via
  `FluentResults`, never message text. Replacing `"Cannot activate product in status …"`
  with `""` or any alternative does not change observable behaviour. Stryker can't detect
  this equivalence and counts these as survivors.
  - Top equivalent-survivor files (>=4 string-mutation survivors each):
    `CategoryErrors.cs` (11), `ProductErrors.cs` (9), `Category.cs` (9 string), `ImageReferenceErrors.cs` (8),
    `SkuErrors.cs` (6), `Dimensions.cs` (5), `ProductNameErrors.cs` (4), `BrandNameErrors.cs` (4),
    `DimensionsErrors.cs` (4), `ImageReference.cs` (4), `CategoryPathErrors.cs` (4),
    `CategoryPath.cs` (4 string).
- **~88 are likely real test gaps** — Behavioural mutations (Boolean / Equality /
  Block-removal / Statement / Logical / Conditional / Negate / LogicalNot) on aggregate
  methods + VO factories that the existing 255 unit tests don't cover at the mutation
  level. Highest-signal hotspots:
  - `Products/Product.cs` — 8 Statement + 5 Block-removal + 3 Equality + 1 LogicalNot
    survivors (Product.Create factory + Discontinue + Reactivate + Activate preconditions).
  - `Categories/Category.cs` — 6 Block-removal + 5 Equality + 2 Statement + 2 Conditional(false)
    + 2 Conditional(true) + 2 Logical + 2 Negate survivors (CreateRoot + Reparent +
    path-recompute statements).
  - `Products/ValueObjects/ProductStatus.cs` — 6 Boolean + 1 Logical + 1 Statement
    survivors (state-transition truth-table gaps in `CanTransitionTo`).
  - `Products/ValueObjects/Dimensions.cs` — 3 Equality + 2 Logical survivors (length / unit
    validation boundaries).
  - `Products/ValueObjects/ImageReference.cs` — 4 Block-removal + 3 Equality survivors.
  - `Categories/ValueObjects/CategoryPath.cs` — 4 Equality + 2 Block-removal + 1 Negate.

**Effective (equivalent-excluded) kill rate ≈ 159 / (335 - 88) ≈ 64.4 %** — still below the
`_shared.md § 7` 80 % target.

### Disposition for M10

Per the approved plan's stop-condition ("If kill rate is severely under (< 50 %), stop and
ask before doing a large test-strengthening pass — that's a session of its own"), I surfaced
the 46.90 % raw + ~64 % effective numbers + survivor breakdown to the user. The user's
verbatim reply (recorded for audit trail per the Opus reviewer's I2 note):

> **User:** "47% is ok"

M10 records the baseline as **informational** and does **not** strengthen tests in this
session. The 88 likely-real gaps are documented as carry-forwards below; closing them is a
dedicated test-strengthening milestone of its own.

### What Group C ships

- The Stryker.NET tool manifest + scoped config so future sessions don't re-bootstrap.
- The MTP-runner gotcha + opt-in flag, captured in this summary (not re-discovered).
- A baseline kill-rate number that future test-strengthening passes can measure against.

### What Group C explicitly does NOT ship

- Test-strengthening edits to `test/Catalog.UnitTests/**`. The Catalog test suite is
  unchanged at HEAD.
- A re-run of mutation testing against `Catalog.Application` (Application-layer mutation
  pass is a follow-up; it'd cover the projection handlers + outbox publishers + command
  handlers, where the integration-test surface arguably covers more of the behavioural
  mutants).
- A `services/Directory.Packages.props` or root `Directory.Packages.props` change (Stryker
  is a CLI tool, installed via `.config/dotnet-tools.json` rather than a `PackageReference`).

## ADR compliance

- **ADR-0008** (correlation-id) — n/a; M10 doesn't touch the correlation pipeline. The M3.6
  TODO at `ProductCreatedProjectionHandler.cs:78-80` is unchanged. The
  `CorrelationIdRoundtripTests` skip remains carry-forward.
- **ADR-0010** (service-to-service auth) — n/a; HealthChecks endpoints `/api/healthz` +
  `/api/readiness` remain auth-free per ADR.
- **ADR-0012** (versioning) — n/a; no route changes.
- **ADR-0013** (idempotency) — n/a; `redis-cache` health check still wired, now with a 4s
  timeout enforced via `RedisTimeout`.
- **ADR-0015** (time policy) — n/a; `TimeProvider` unchanged.
- **ADR-0016** (Redis topology) — Reaffirmed: the `redis-cache` health probe is bound to
  `IdempotencyKeyServiceCollectionExtensions.RedisConnectionStringName` per ADR-0013 + 0016;
  the new `RedisTimeout` enforces the probe-level deadline (4 s).
- **All other ADRs** — n/a (M10 is config wire-up + mutation-test tooling).

## Verification output (raw)

### Four CI gates (per `_shared.md § 12`)

```
$ dotnet build -m --no-restore
... pre-existing NU1903 transitive vuln warnings (53 on incremental no-restore build;
    106 on full restore build — same baseline as catalog-m9.md, no change vs HEAD).
    53 upozornění
    Počet chyb: 0
Uplynulý čas 00:00:59.66

$ dotnet restore --locked-mode
... NU1903 warnings (pre-existing transitive baseline).
EXIT=0

$ dotnet format whitespace --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění
protokolovat, nastavte možnost verbosity na úroveň diagnostic.
EXIT=0

$ dotnet format style --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění
protokolovat, nastavte možnost verbosity na úroveň diagnostic.
EXIT=0
```

All four gates **GREEN**. The 53/106 warning-count delta between the
`--no-restore`-incremental build (53) and the implicit-restore full build (106) is
mechanical: the implicit-restore path re-emits one NU1903 line per (project × vuln) tuple,
while the incremental path emits only one line per vuln (the lockfile is read once). No
new warnings.

### Four Catalog test slices (final state at M10 HEAD)

```
$ dotnet test test/Catalog.UnitTests/ --no-restore
Úspěšné!  - Neúspěšné: 0, Úspěšné: 255, Přeskočeno: 0, Celkem: 255, Doba trvání: 3 s

$ dotnet test test/Catalog.ArchitectureTests/ --no-restore
Úspěšné!  - Neúspěšné: 0, Úspěšné:  41, Přeskočeno: 0, Celkem:  41, Doba trvání: 896 ms

$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Catalog.IntegrationTests/ --no-restore
Úspěšné!  - Neúspěšné: 0, Úspěšné:   1, Přeskočeno: 0, Celkem:   1, Doba trvání: 2 s

$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Catalog.FunctionalTests/ --no-restore
Úspěšné!  - Neúspěšné: 0, Úspěšné:  29, Přeskočeno: 6, Celkem:  35, Doba trvání: 8 s
```

**Summary: 326/326 non-skipped tests green; 6 conditionally-skipped functional tests
unchanged from M9.** Test-count baselines match M9 exactly — no regressions, no new tests
in M10.

### docker-compose smoke (skipped per plan)

The approved plan made the docker-compose subset OPTIONAL ("M7 already executed it and M9
didn't need it. Re-running it is a sanity check that the HealthChecks timeout binding still
resolves cleanly under a live container. If time-constrained, skip the docker-compose
subset and document that decision in the summary"). Given the session's already-large time
investment on the corrupted first Stryker run + MTP-runner diagnosis + full Domain
mutation pass, the docker-compose smoke is **deferred to a follow-up sanity check** —
either the next session (M11+) or any subsequent operator run. The HealthChecks options
binding is exercised at API host bootstrap, which is exercised by every functional test
(29 passed); a docker-compose runtime re-validation is value-add, not gating, at this
point.

### Mutation report artifact

- Path (gitignored): `test/Catalog.UnitTests/StrykerOutput/2026-05-10.15-45-18/reports/`
  - `mutation-report.html` — full per-line diff view
  - `mutation-report.json` — programmatic source for the survivor analysis above
- Final mutation score: **46.90 %** (159 killed / 176 survived / 0 timeout out of 335
  tested + 50 skipped = 385 mutants generated).
- Run duration: **00:05:41** (5 m 41 s, no sleep, no retries).

### Diff stat (M10-staged files only)

```
.gitignore                                                                            +3 / -0 lines
.config/dotnet-tools.json                                                             NEW
services/Catalog/Catalog.API/appsettings.json                                         +0 / -1 lines
services/Catalog/Catalog.Infrastructure/Common/Config/HealthChecksOptions.cs          NEW (+33)
services/Catalog/Catalog.Infrastructure/Common/HealthChecksDependencyInjection.cs    +14 / -1 lines
test/Catalog.UnitTests/stryker-config.json                                            NEW
docs/implementation-prompts/session-summaries/catalog-m10.md                          NEW (this file)
```

## Pre-commit Opus reviewer findings + resolution

`Agent(subagent_type="feature-dev:code-reviewer", model="opus")` per `_shared.md § 11`
step 0 (also explicitly required by the dispatch prompt). Brief covered: Group D wire-up +
`DatabaseTimeout` decommission rationale, Group C Stryker bootstrap + MTP-runner finding +
46.90 % baseline disposition (47 % accepted per user), boundary-discipline note on
`.config/dotnet-tools.json`, and what's intentionally deferred (the 6 functional-test
skips, the ~88 likely-real survivor mutants, the docker-compose smoke).

**Outcome: 0 CRITICAL, 0 HIGH, 3 MEDIUM, 3 LOW, 5 INFO. Verdict: ship-as-is.** Reviewer
cross-validated Group D against the platform reference at
`platform/Platform.OutboxRelay.WorkerService/Common/HealthChecksDependencyInjection.cs` +
`Config/HealthChecksOptions.cs`, against the Basket precedent, and against the Weather +
Notifications shapes. The DI binding mirrors the platform reference 1:1; the
`DatabaseTimeout` decommission rationale is sound (`AddDbContextCheck<>` does not expose a
`timeout:` parameter); the `.config/dotnet-tools.json` boundary discussion is honest and
self-aware (matches the M7/M8/M9 "flag + justify" discipline); the 255/41/1/29-skip-6 test
baseline at HEAD matches M9 exactly with no regression.

| Severity | ID | Finding | Resolution |
|---|---|---|---|
| MEDIUM | M1 | Session summary findings table left as `_populated post-review_` placeholder ([catalog-m10.md:363-367](catalog-m10.md)). Would commit literal placeholder text to history. | **Addressed by populating this table** with the 8 actual findings before staging. |
| MEDIUM | M2 | `concurrency: 4` in `stryker-config.json:17` is non-portable; Stryker's default (`logical cores / 2`) scales to the host. Pin oversubscribes on 4-core, underutilizes on 16-core CI. | **Addressed inline** — `concurrency` line dropped from `test/Catalog.UnitTests/stryker-config.json`. Future overrides go via CLI `--concurrency`. |
| MEDIUM | M3 | `stryker-config.json:3` lacks `"solution"` + `"language-version"`. `"project": "Catalog.Domain.csproj"` resolves by filename today; adding `"solution"` makes resolution explicit and survives a hypothetical second `Catalog.Domain.csproj` (e.g. legacy / copy). | **Deferred to M11+** — out of M10 scope per the user's "47 % is ok" disposition; the current config resolves correctly at HEAD and the hypothetical second-csproj scenario is unlikely. Logged as a follow-up. |
| LOW | L1 | Diff-stat reads "Total: 7 files (6 staged + 1 NEW summary)" but the summary IS being committed — misleading copy. | **Addressed inline** — reworded to "7 files staged (6 code/config/tooling + 1 NEW summary)". |
| LOW | L2 | `[Required]` annotation is redundant on a C# `required` non-nullable `TimeSpan` property (the `required` keyword enforces presence at the C# level; `[Range]` does the validation work). Same pattern in platform precedent, so changing here would create asymmetric style. | **Accepted-with-rationale — keep parallel to platform precedent** at `platform/Platform.OutboxRelay.WorkerService/Common/Config/HealthChecksOptions.cs:12-22`. A cross-codebase cleanup pass is a separate sweep. |
| LOW | L3 | Magic-number range `"00:00:01"` to `"00:01:00"` in `HealthChecksOptions.cs:22,26,30` is unexplained. Matches platform precedent which is also unexplained. | **Accepted-with-rationale — leave unchanged.** A documentation refinement could justify the 60s upper bound vs Kubernetes liveness periods, but it's cross-codebase consistency-only; defer. |
| INFO | I1 | Schema-Registry probe (`AddUrlGroup`) has no enforced timeout. Today a slow SR can stall `/api/readiness` past the other 4 probes' combined budget. | **Deferred to M11+** — out of M10 scope. Path forward: named `HttpClient` config with `Timeout = TimeSpan.FromSeconds(5)`, or migrate to a custom `AddCheck<>` wrapper with explicit `CancellationToken`. |
| INFO | I2 | The user's "47 % is ok" disposition is recorded in narrative form, not as an explicit quoted reply — future auditors verifying user acceptance have to trust the summary author. | **Addressed inline** — quoted-block reply added to "Disposition for M10" section above, matching the M9 disposition recording pattern. |
| INFO | I3 | `.config/dotnet-tools.json` boundary discussion is honest and self-aware. **Positive observation.** | **No action.** Logged so the rationale is recognized as load-bearing, not glossed over. |
| INFO | I4 | Pre-existing dirty Invoicing files dispositioned correctly (surfaced, scoped out, not silently absorbed). **Positive observation.** | **No action.** Matches the M7/M8/M9 precedent. |
| INFO | I5 | Stryker run details are reproducible (version 4.14.1, runner `mtp`, thresholds, mutate-globs, 159/176/0 on 335 tested). The MTP-runner discovery story will save the next Stryker adopter the same diagnostic walk. **Positive observation.** | **No action.** Logged for the next implementer's benefit. |

The reviewer also called out a **cross-BC drift** worth noting as a carry-forward: Basket's
`appsettings.json:99` still carries `DatabaseTimeout` AND has none of its probe timeouts
wired into `services/Basket/Basket.Infrastructure/Common/HealthChecksDependencyInjection.cs`.
That's pre-existing drift, properly out of M10 scope, but is a candidate for a cross-BC
sweep alongside the OTEL coherence drift + health-check path drift already on the
carry-forward list.

## Open questions / improvements proposed (NOT implemented unless approved)

### Surfaced in M10

1. **Stryker.NET `ignore-mutations` config improvement.** Per the survivor analysis,
   ~88 of 176 survivors are equivalent string mutations on error / exception message
   text — these will never be killed because the test suite (correctly) asserts error
   CODES, not message strings. A `"ignore-mutations": ["string"]` filter in
   `stryker-config.json` would skip generating these mutants, yielding a cleaner kill rate
   that reflects behavioural test quality. Trade-off: filtering away string mutations
   loses signal on i18n / message-formatting bugs (irrelevant in this codebase). Worth a
   1-line config change in M11+ before re-running mutation testing.

2. **Mutation-test pass on `Catalog.Application`.** M10 covered only `Catalog.Domain`.
   The Application layer (commands + handlers + projection handlers + outbox publishers)
   has more behavioural surface area; a mutation pass there would surface gaps that the
   integration tests (1 today) plus unit tests fail to cover. Larger scope; estimated
   10-20 min run time. Pairs naturally with #1.

3. **`ProductStatus.cs` Boolean-mutation gaps.** 6 of the real survivors target
   `CanTransitionTo` — meaning the state-transition truth table isn't asserted at every
   `(from, to)` pair. Adding a parameterized `[Theory]` over all 9 `(from × to)` pairs in
   `Catalog.UnitTests/Domain/Products/ProductStatusTests` would close most of these in
   a single PR.

4. **`Product.Create` factory side-effect assertions.** 8 Statement + 3 Block-removal
   survivors in `Product.cs` cluster around the constructor body (lines 68-76). These
   suggest that `Product.Create(...)` tests assert the returned aggregate's getters but
   not all side effects (`AddDomainEvent`, audit-property init, etc.). One test
   strengthening pass over `CreateProductCommandHandlerTests` could close many of these.

5. **Stryker.NET 4.14.1 MTP runner is preview.** The runner emits the warning
   `The Microsoft Test Platform testrunner is currently in preview. Results should be
   verified since this feature is still being tested.` at every run. Today this is fine —
   the 159 killed mutants are clearly killed (build broke or assertion failed). But a
   future Stryker upgrade may change behaviour; pin Stryker.NET to a specific version
   when more BCs adopt mutation testing.

6. **Cross-BC HealthChecks drift (Opus reviewer observation).** Basket's
   `appsettings.json:99` still carries `DatabaseTimeout` AND has **none** of its probe
   timeouts wired into
   `services/Basket/Basket.Infrastructure/Common/HealthChecksDependencyInjection.cs`. That
   is pre-existing cross-BC drift, properly out of M10 scope (Catalog `<boundaries>`), but
   is a candidate for a cross-BC sweep alongside the OTEL coherence drift + health-check
   path drift already on the carry-forward list. Worth a single platform-level commit that
   propagates Catalog's M10 wire-up pattern to every BC's `HealthChecksDependencyInjection.cs`.

7. **Stryker `stryker-config.json` resilience (Opus reviewer M3).** Today the config uses
   `"project": "Catalog.Domain.csproj"` which resolves by filename. Adding `"solution":
   "../../DotNetAtlas.sln"` (or `.slnx`) and `"language-version": "preview"` makes
   resolution explicit and survives a hypothetical second `Catalog.Domain.csproj` (e.g. a
   future legacy / copy). Out of M10 scope per the "47 % is ok" disposition; logged for M11+.

### Carried forward from M5 / M7 / M8 / M9 (unchanged disposition; all out-of-bounds for
Catalog or deferred until contract / platform decisions are taken)

- **6 conditionally-skipped Catalog functional tests** ([catalog-m9.md:104-109](catalog-m9.md))
  — `CorrelationIdRoundtripTests` (platform-level Activity-bridge),
  `ReactivateProductTests` ×2 + `DiscontinueProductTests` + `GetProductsByCategoryTests`
  ×2 (all blocked on a deferred `ActivateProductCommand` which is NOT in
  `use-cases.md § 1.1` and would extend the locked contract). M10 explicitly does not
  open either of these per the user's scope decision; both remain candidates for a
  user-authorized M11+ session.
- **OTEL core-set coherence drift across root/saga/services CPMs** ([catalog-m8.md:195](catalog-m8.md))
  — out of Catalog `<boundaries>`, needs platform-level commit.
- **Cross-doc health-check endpoint path drift** ([catalog-m8.md:196](catalog-m8.md))
  — out of Catalog `<boundaries>`, cross-BC doc audit.
- **`docs/bc-design/architecture-tests.md § 2.1` doc-correction** ([catalog-m5.md:115](catalog-m5.md))
  — cross-BC doc, out of Catalog boundary.
- **Wave-0 DB-bootstrap gap** ([catalog-m7.md:167](catalog-m7.md))
  — platform / Wave-0 follow-up.
- **`catalog.api` compose `healthcheck:` directive** ([catalog-m7.md:169](catalog-m7.md))
  — platform / chiseled-base-image follow-up.
- **CLAUDE.md proxy-workaround documentation drift** ([catalog-m9.md:264-272](catalog-m9.md))
  — single-machine observation; needs cross-machine reproduction.
- **`AddCatalogApplication` configuration-binding ergonomics** ([catalog-m9.md:273-281](catalog-m9.md))
  — platform-level decision (a, b, or c).
- **Sister-BC integration-fixture parity audit** ([catalog-m9.md:282-285](catalog-m9.md))
  — cross-BC sweep.
- **Cross-BC fail-fast on missing topic-config keys** ([catalog-m9.md:286-293](catalog-m9.md))
  — Opus M9-L5; cross-BC sweep.
- **`nw-software-crafter-reviewer` (Haiku) follow-up review** — value-add, not gating;
  same disposition as M8/M9.

## Domain self-corrections

None this session — no production-code behaviour change (Group D is config-binding, Group
C is observation-only). The `Product` aggregate, `Category` aggregate, and all VOs are
unchanged at HEAD. The 88 likely-real mutation survivors are test-suite gaps, not
production-code bugs.

## File-touch audit

Per [catalog.md `<boundaries>`:120-122](../catalog.md):

**In-scope:**
- ✓ `services/Catalog/Catalog.Infrastructure/Common/Config/HealthChecksOptions.cs` (NEW).
- ✓ `services/Catalog/Catalog.Infrastructure/Common/HealthChecksDependencyInjection.cs` (EDIT).
- ✓ `services/Catalog/Catalog.API/appsettings.json` (EDIT — drop one key).
- ✓ `test/Catalog.UnitTests/stryker-config.json` (NEW).
- ✓ `docs/implementation-prompts/session-summaries/catalog-m10.md` (NEW; this file).

**Off-boundary, plan-authorized:**
- ✓ `.config/dotnet-tools.json` (NEW) — Stryker.NET bootstrap. Plan covered this as
  "Possibly `Directory.Packages.props` / `services/Directory.Packages.props` if Stryker
  requires a tool reference"; the actual artifact is `.config/dotnet-tools.json` (dotnet
  CLI manifest, not a NuGet `PackageReference`), but the spirit is the same — Group C
  bootstrap.
- ✓ `.gitignore` (EDIT — add `**/StrykerOutput/`) — plan explicitly authorized
  ("`.gitignore` (add `StrykerOutput/` if not already excluded — check first)").

**Untouched (per `<boundaries>` "Do not touch"):**
- `services/Catalog/Catalog.Domain/**` and `services/Catalog/Catalog.Application/**`
  production code — no behavioural change in M10 (mutation testing observation only).
- Other BCs (`services/{Basket,Inventory,Invoicing,Ordering,Payments}/**`, `bff/**`,
  `saga/**`).
- `platform/**`.
- `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/**` (schemas locked since M3).
- ADRs.
- Cross-BC bc-design docs (`use-cases.md`, `events-catalog.md`, `error-taxonomy.md`,
  `architecture-tests.md`, etc.) — including the `use-cases.md § 1.1` extension that the
  user declined in M10's `AskUserQuestion` (Group A path).
- `docker-compose.yaml`, `Directory.Packages.props` (any tier), `Directory.Build.props`
  (any tier).
- Pre-existing dirty Invoicing files that appeared mid-session — left unstaged.

## What "done" looks like for M10

- [x] Group D delivered: `HealthChecksOptions` POCO + DI binding via
  `AddOptionsWithValidateOnStart` + `BindConfiguration` + `ValidateDataAnnotations`;
  `SelfTimeout`, `KafkaTimeout`, `RedisTimeout` enforced on respective probes;
  `DatabaseTimeout` decommissioned with rationale.
- [x] Group C tooling bootstrapped: `.config/dotnet-tools.json` + Stryker.NET 4.14.1 +
  `test/Catalog.UnitTests/stryker-config.json` with the **MTP runner opt-in**.
- [x] Group C baseline captured: 46.90 % raw (159 K / 176 S / 0 T) on
  `Catalog.Domain.{Products,Categories}/**/*.cs`; survivor analysis split into ~88
  likely-equivalent string mutants + ~88 likely-real behavioural gaps.
- [x] User-accepted 47 % baseline; no test-strengthening in M10.
- [x] All four CI gates green: build (0 errors, 53 NU1903 incremental / 106 full
  pre-existing), restore --locked-mode (exit 0), format whitespace (exit 0), format style
  (exit 0).
- [x] All four Catalog test slices green: 255/255 unit + 41/41 arch + 1/1 integration +
  29/35 functional (6 skipped, baseline match with M9).
- [x] Boundary discipline: pre-existing dirty Invoicing files at session start left
  unstaged; only M10-scope + plan-authorized files staged.
- [x] Session summary posted at this path.
- [ ] Pre-commit Opus reviewer ran — findings table populated above.
- [ ] M11 handoff block emitted in the closing chat reply, with `{BC}=catalog` and
  `{N+1}=11` substituted (the dispatch prompt's explicit closing instruction).
- [ ] `nw-software-crafter-reviewer` (Haiku) follow-up review against this summary —
  value-add, not gating; same disposition as catalog-m8.md:236 / catalog-m9.md:314.

## Catalog BC: empirically complete (still)

M10 adds two things on top of M9's empirically-validated atomicity baseline:

1. **HealthChecks timeouts now enforced** on Self, Kafka, and Redis probes (was: silently
   ignored config keys). One step closer to production-grade observability.
2. **Mutation-test tooling bootstrapped + baseline captured** (46.90 % raw / ~64 %
   effective). Future test-strengthening work can measure deltas against this baseline.

The BC's contract surface is unchanged. Wave-2 (Checkout saga) and Wave-3 (BFF) agents
consume the same external events + HTTP API as documented at M8 (catalog-m8.md:240-249).
The 6 skipped functional tests + the ActivateProductCommand contract-extension question
remain the principal candidates for any future M11+ Catalog session.

---

> **Note on M11.** There is still no canonical M11 in `<session_management>`. Per the same
> reasoning as M9 + M10, the literal handoff block emitted in the closing chat is a
> placeholder; the next session will negotiate scope. Top candidates: (1) the
> ActivateProductCommand contract extension (un-skip 5 functional tests), (2) the
> mutation-test sweep on `Catalog.Application` + `ignore-mutations: ["string"]` config
> refinement, (3) test-strengthening over the ~88 real survivors, (4) any of the
> long-standing cross-BC carry-forwards (OTEL coherence, health-check path drift,
> architecture-tests.md doc correction).
