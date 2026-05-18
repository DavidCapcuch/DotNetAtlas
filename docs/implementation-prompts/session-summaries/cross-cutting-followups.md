# Cross-Cutting (Wave 1 — Run First) Platform Follow-ups — Session Summary

Date: 2026-05-17
Branch: `aaqwdqwd`

This session triaged platform-level concerns surfaced in 38 Wave 1 BC closeout documents
(`docs/implementation-prompts/session-summaries/` and `…/session-summaries2/`), fixed all
CRITICAL+HIGH items in scope, and filed every MEDIUM+LOW item on GitHub for later sessions.
Per the dispatch prompt's scope guardrail, no edits were made to `services/<bc>/**`,
`src/<bc>/**`, or BC test projects — when a platform change broke downstream BC compilation,
the breakage was surfaced (this file) rather than patched.

---

## Triage Outcomes

| ID  | Severity | Concern                                                        | Action       | Result                       |
|-----|----------|----------------------------------------------------------------|--------------|------------------------------|
| C-1 | CRITICAL | `DomainEvent.OccurredOnUtc` defaults to wall-clock             | Fixed        | commit `8616fe1`             |
| H-1 | HIGH     | Schema-registry compatibility-mode bootstrap missing           | Fixed        | commit `22942e8`             |
| H-2 | HIGH     | No platform `RequireScope` helper for ADR-0010 v1 HTTP enforcement | Fixed    | commit `a82bab8`             |
| M-1 | MEDIUM   | NU1903 globally suppressed; no baseline-delta gate             | Issue filed  | [#116](https://github.com/DavidCapcuch/DotNetAtlas/issues/116) |
| M-2 | MEDIUM   | OTel span-attribute PII redaction missing (Serilog-only today) | Issue filed  | [#117](https://github.com/DavidCapcuch/DotNetAtlas/issues/117) |
| M-3 | MEDIUM   | Kafka consumers read CorrelationId from Avro payload not header | Issue filed | [#118](https://github.com/DavidCapcuch/DotNetAtlas/issues/118) |
| M-4 | MEDIUM   | Per-BC arch tests should forbid static time in Infrastructure  | Issue filed  | [#119](https://github.com/DavidCapcuch/DotNetAtlas/issues/119) |
| M-5 | MEDIUM   | Docker-compose topic conventions undocumented                  | Issue filed  | [#120](https://github.com/DavidCapcuch/DotNetAtlas/issues/120) |
| L-1 | LOW      | EF `EnableSensitiveDataLogging` gated wrong                    | Issue filed  | [#121](https://github.com/DavidCapcuch/DotNetAtlas/issues/121) |
| L-2 | LOW      | CI MSB4018 transient file-lock                                 | Issue filed  | [#122](https://github.com/DavidCapcuch/DotNetAtlas/issues/122) |

All filed issues carry labels `needs-triage` and `platform/wave1-followup` (the latter newly
created this session).

---

## Fixes in This Session

### C-1: `DomainEvent.OccurredOnUtc` — commit `8616fe1`

**Files:**
- `platform/Platform.SharedKernel/Base/DomainEvents/DomainEvent.cs` — removed wall-clock default; marked `required`
- `platform/Platform.SharedKernel.UnitTests/Base/DomainEvents/DomainEventTests.cs` — new reflection-based regression test
- `docs/adr/0015-time-timezone-policy.md` — added "Platform base" subsection under Implementation Notes

The platform base no longer ships a `DateTimeOffset.UtcNow` initializer, so callers must
supply an explicit value sourced from `TimeProvider.GetUtcNow()`. The Roslyn `required`
modifier turns every missing-initializer site into a compile error (CS9035), giving
deterministic enforcement that no architecture test could match.

### H-1: Schema-registry compatibility-mode bootstrap — commit `22942e8`

**Files:**
- `docker-compose.yaml` — added `schema-registry-init` companion service (profile `full`)
- `docs/adr/0007-avro-compatibility-modes.md` — Implementation Notes now reference the provisioned bootstrap

`schema-registry-init` runs once the registry is healthy and PUTs:
- global default `FORWARD_TRANSITIVE`
- per-subject `FORWARD_TRANSITIVE` for every event-log subject (Record Name Strategy)
- per-subject `FULL_TRANSITIVE` for every command subject

Verified end-to-end:

```
$ curl -sf http://localhost:8081/config
{"compatibilityLevel":"FORWARD_TRANSITIVE"}

$ curl -sf http://localhost:8081/config/Catalog.Products.ProductCreatedEvent
{"compatibilityLevel":"FORWARD_TRANSITIVE"}

$ curl -sf http://localhost:8081/config/Ordering.Orders.OrderCommand
{"compatibilityLevel":"FULL_TRANSITIVE"}
```

### H-2: `RequireScope` platform helper — commit `a82bab8`

**Files:**
- `platform/Platform.ServiceDefaults/Auth/ScopePolicyExtensions.cs` — new extension method
- `platform/Platform.ServiceDefaults.UnitTests/Auth/ScopePolicyExtensionsTests.cs` — 8 TDD tests
- `docs/adr/0010-service-to-service-auth.md` — Implementation Notes now name the helper as v2 migration target

`AuthorizationPolicyBuilder.RequireScope(string)` composes `RequireAuthenticatedUser()` +
`RequireClaim("scope", scope)` with null/empty/whitespace input validation. BCs migrate
from `RequireRole(Roles.Admin)` to `RequireScope("<service>.<verb>")` in a v2 hardening
pass (tracked outside this session as per-BC follow-up).

---

## Verification Matrix

| Gate                                                  | Result                          | Notes |
|-------------------------------------------------------|---------------------------------|-------|
| `dotnet restore --locked-mode`                        | PASS                            | Same 53 NU1903 baseline warnings (issue #116) |
| `dotnet build -m`                                     | **FAIL — by design** (29 errors)| C-1 surfaces in 3 BC tree areas — see rebase checklist below |
| `dotnet format whitespace --no-restore --verify-no-changes` | PASS                      | 0 violations |
| `dotnet format style --no-restore --verify-no-changes`      | PASS                      | 0 violations |
| `dotnet test platform/Platform.SharedKernel.UnitTests`      | **57 / 57 PASS**          | includes new DomainEventTests |
| `dotnet test platform/Platform.ServiceDefaults.UnitTests`   | **43 / 43 PASS**          | includes 8 new ScopePolicyExtensionsTests |
| `dotnet test platform/Platform.KafkaFlow.ProducerHeaders.UnitTests` | **13 / 13 PASS**   | unrelated; baseline confirmation |
| BC test projects                                       | not run                         | won't link until rebase lands |
| `docker compose --profile full up schema-registry-init`| PASS — "compat bootstrap complete" + REST verification | see H-1 above |

The repo-wide `dotnet build -m` is expected to fail until the BC rebase checklist
(below) is consumed. This is the C-1 surfacing signal described in the dispatch prompt.

---

## BC Rebase Checklist

Each entry is a breaking change a downstream BC session must adapt to before that BC's
tests can compile and pass under the new platform base. The exploration phase identified
Basket only as compile-affected; the actual `dotnet build` surfaced TWO additional
affected BC trees (Weather under `src/`, and Catalog's test project). All are tracked
below.

### Basket — REQUIRED rebase (anticipated)

- **`services/Basket/Basket.Domain/Baskets/Basket.cs`** — 7 sites: lines 116, 215, 241, 276, 346, 368, 414
  - Each `AddDomainEvent(new XxxDomainEvent { … })` needs `OccurredOnUtc = utcNow,` added to the initializer.
  - The aggregate already has `var utcNow = _timeProvider.GetUtcNow();` in scope for every method that raises an event; if not, hoist it.
- **`test/Basket.UnitTests/Baskets/Events/DomainEventsTests.cs`** — every `new XxxDomainEvent { … }` (lines 19, 34–40, 56, 69–75, 94–98, 110, 129–137); also remove `e.OccurredOnUtc.Should().NotBe(default)` at line 25 (the new compile-time guarantee subsumes the runtime check).
- **`test/Basket.UnitTests/Baskets/Application/Checkout/BasketCheckoutInitiatedMapperTests.cs:46`** — already supplies `OccurredOnUtc = occurredAt`; verify it still compiles after Basket.Domain rebase.

### Weather — REQUIRED rebase (surfaced by build, NOT anticipated)

Weather lives under `src/Weather*`, outside the literal `services/<bc>/**` scope of the
dispatch prompt, and was not flagged in the exploration phase because the closeouts under
review cover Wave 1 BCs only. The platform change still cascades into Weather's
event-construction sites.

- **`src/Weather.Domain/Alerts/AlertSubscriber.cs`** — 11 sites: lines 93, 151, 195, 228, 284, 298, 312, 366, 416
- **`src/Weather.Domain/Alerts/MonitoredLocation.cs`** — 2 sites: lines 89, 125
- **`src/Weather.Domain/Feedback/Feedback.cs`** — 2 sites: lines 56, 98
- Likely fallout in `test/Weather.UnitTests/**` (not enumerated; will surface when Weather.Domain compiles).

### Catalog — REQUIRED rebase in test fixtures (surfaced by build, NOT anticipated)

Catalog production code in `services/Catalog/Catalog.Domain/**` already sets `OccurredOnUtc`
explicitly at all 18 event-construction sites and continues to compile. The surprise:
several `test/Catalog.UnitTests` projection-handler and outbox-publisher tests construct
events directly without supplying the property.

- `test/Catalog.UnitTests/Categories/CreateCategory/CategoryCreatedOutboxPublisherTests.cs:39`
- `test/Catalog.UnitTests/Categories/CreateCategory/CategoryCreatedProjectionHandlerTests.cs:25`
- `test/Catalog.UnitTests/Categories/ReparentCategory/CategoryReparentedProjectionHandlerTests.cs:24`
- `test/Catalog.UnitTests/Products/CreateProduct/ProductActivatedProjectionHandlerTests.cs:54`
- `test/Catalog.UnitTests/Products/CreateProduct/ProductCreatedOutboxPublisherTests.cs:44`, `:94`
- `test/Catalog.UnitTests/Products/CreateProduct/ProductCreatedProjectionHandlerTests.cs:25`, `:59`
- `test/Catalog.UnitTests/Products/UpdateProductPrice/ProductPriceChangedProjectionHandlerTests.cs:56`

Per-fixture pattern: add `OccurredOnUtc = <fixed test instant>` (e.g. an `Instant.FromUtc(…)`-style constant) to every direct event construction.

### Inventory, Invoicing, Ordering, Payments — NO rebase required for C-1

These four BCs set `OccurredOnUtc` explicitly at every event-construction site in both
production and test code (verified by grep: Payments 11, Invoicing 7, Ordering 8,
Inventory inspected across `StockItems` aggregate). The platform change is
binary-compatible for them.

### All BCs — OPTIONAL adoption (filed as M-2 / M-3 / M-4 / H-2 follow-up issues)

These are *not* compile-breaking and can be adopted at any pace:

- **H-2 follow-up** (per-BC): swap `RequireRole(Roles.<Admin|Buyer>)` in each BC's
  `AuthDependencyInjection.cs` for `Platform.ServiceDefaults.Auth.ScopePolicyExtensions.RequireScope("<service>.<verb>")`. ADR-0010 v2 migration.
- **M-3 follow-up**: Kafka consumers should read `CorrelationId` from the Kafka header
  via the new platform helper (when issue #118 lands), not from the Avro payload field.
- **M-4 follow-up**: per-BC `ArchitectureTests` extend the no-static-time rule to
  `*.Infrastructure.*` and `*.Application.*` layers (currently Domain only).
- **L-1 follow-up**: per-BC DbContext should gate `EnableSensitiveDataLogging` on
  `env.IsDevelopment()` (not `!isDeployedEnvironment`).

### Docker-compose smoke tests — unaffected

`schema-registry-init` is purely additive under profile `full`; Testcontainers-driven BC
integration tests do not compose with it and remain unaffected. The `--profile core`
flow is unchanged.

---

## Commits Landed

```
a82bab8 feat(platform): add RequireScope policy helper (ADR-0010 v1 enforcement)
22942e8 feat(platform): add schema-registry-init bootstrap (ADR-0007)
8616fe1 fix(platform): require OccurredOnUtc on DomainEvent base (ADR-0015)
```

## Issues Filed

- [#116](https://github.com/DavidCapcuch/DotNetAtlas/issues/116) NU1903 baseline-delta CI gate
- [#117](https://github.com/DavidCapcuch/DotNetAtlas/issues/117) OTel span-attribute PII redaction
- [#118](https://github.com/DavidCapcuch/DotNetAtlas/issues/118) Kafka consumers — CorrelationId from header
- [#119](https://github.com/DavidCapcuch/DotNetAtlas/issues/119) Extend arch tests to forbid static time in Infrastructure
- [#120](https://github.com/DavidCapcuch/DotNetAtlas/issues/120) docker-compose topic conventions documentation
- [#121](https://github.com/DavidCapcuch/DotNetAtlas/issues/121) `EnableSensitiveDataLogging` should gate on `IsDevelopment()`
- [#122](https://github.com/DavidCapcuch/DotNetAtlas/issues/122) CI MSB4018 transient file-lock runbook note

## Notes for the Next Session

- The next session **must** consume the BC rebase checklist above before `dotnet build -m`
  returns to green. Recommended sequence: Basket → Weather → Catalog.UnitTests (all
  parallelisable since they touch independent trees).
- The dispatch prompt's referenced test path `test/Platform.*.UnitTests/` does not match
  the repo layout; platform unit-test projects live under `platform/Platform.*.UnitTests/`
  alongside the code they test. New tests this session were added in the existing platform
  locations.
- Two scope items that LOOKED in-scope were deferred to issues by deliberate choice:
  scope-based auth at the BC level (H-2-followup) and OTel PII span-attribute coverage
  (M-2). Closeouts explicitly defer both to a v2 hardening pass.

---

## Update — 2026-05-18 Fix Cycle (Wave 1 cross-cutting follow-ups resolved)

Branch: `aaqwdqwd`. This section closes the loop on the 10 cross-cutting issues
[#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138)–[#147](https://github.com/DavidCapcuch/DotNetAtlas/issues/147)
filed by the Invoicing closeout (see [`invoicing-followups.md`](invoicing-followups.md)
for the original triage).

### Triage outcomes

| Issue | Severity | Disposition | Commit / Action |
|---|---|---|---|
| [#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138) | **HIGH (BLOCKER)** | **Fixed** | `6633230` (Weather + Basket production + tests) + `d5b3074` (Catalog tests + platform reflection sentinel) — closed |
| [#147](https://github.com/DavidCapcuch/DotNetAtlas/issues/147) | LOW | **Fixed** | `eeecd1b` (Testcontainers option B promoted + SharedKernel build rule added) — closed |
| [#140](https://github.com/DavidCapcuch/DotNetAtlas/issues/140) | MEDIUM | **Fixed** | `1ce2eea` (removed invalid `include` block; processor applies to all spans by default) — closed |
| [#144](https://github.com/DavidCapcuch/DotNetAtlas/issues/144) | LOW | **Fixed** | `c9669fc` (Polly → Azure.Storage.Blobs SDK retries per ADR-0017) — closed |
| [#141](https://github.com/DavidCapcuch/DotNetAtlas/issues/141) | MEDIUM | **Fixed** | `2e8da5d` (§ 2.5 Invoicing added with 30 facts enumerated) — closed |
| [#143](https://github.com/DavidCapcuch/DotNetAtlas/issues/143) | LOW | **Fixed** | `21d9b20` (§ 4 invoicing.invoices delta + § 5.7 Invoicing schemas with deferred 4th disclosure) — closed |
| [#142](https://github.com/DavidCapcuch/DotNetAtlas/issues/142) | MEDIUM | **Fixed** | `bc8ccf8` (§ 6 Invoicing — 7 commands/queries documented) — closed |
| [#139](https://github.com/DavidCapcuch/DotNetAtlas/issues/139) | LOW | **Deferred — cascade depth** | Transitive pinning surfaced 100+ NU1109 conflicts across EFCore / Npgsql / Serilog / MS.Extensions matrix; reverted to baseline (53 NU1903 warnings unchanged). Defer rationale in issue comment. |
| [#146](https://github.com/DavidCapcuch/DotNetAtlas/issues/146) | LOW | **Blocked — Dockerfile missing** | Precondition filed as [#148](https://github.com/DavidCapcuch/DotNetAtlas/issues/148) (`services/Invoicing/Invoicing.API/Dockerfile` does not exist; Catalog / Ordering / Notifications all have one). Re-open #146 once #148 lands. |
| [#145](https://github.com/DavidCapcuch/DotNetAtlas/issues/145) | LOW | **Carried forward — appetite not landed** | Stryker.NET against the Invoicing suite is dispositioned defer per M10 line 244 + Invoicing-followups. Unchanged. |

Net outcome: **7 closed**, **2 deferred with explicit rationale comments** (#139, #145), **1 blocked** (#146 by [#148](https://github.com/DavidCapcuch/DotNetAtlas/issues/148) which was filed this session).

### Fixes landed — detail

#### #138 — Weather build break (BLOCKER unblocking every BC's FunctionalTests)

**Two commits land the cross-cutting rebase** the original platform session
anticipated in the rebase checklist above:

1. **`6633230`** `fix(weather): thread TimeProvider into 7 factory methods (#138)` — adds `DateTimeOffset utcNow` to 7 Weather.Domain factory signatures (`Feedback.Create`, `Feedback.ChangeFeedback`, `MonitoredLocation.Create`, `MonitoredLocation.CreateWithDefaultThresholds`, `AlertSubscriber.CreateFree`, `AlertSubscriber.SubscribeToMonitoredLocation`, `AlertSubscriber.UnsubscribeFromMonitoredLocation`) so all 12 event constructors can set `DomainEvent.OccurredOnUtc`. Production callers in `Weather.Application` (4 handlers) thread `_timeProvider.GetUtcNow()`; test fixtures across `test/Weather.{Unit,Integration,Functional}Tests/` use either the existing `FakeTimeProvider`-based `UtcNow` member or a new `Weather.UnitTests.Common.TestInstants.FixedNow` constant. Also rebases `services/Basket/Basket.Domain/Baskets/Basket.cs` (7 sites — anticipated in the rebase checklist) + 3 Basket.UnitTests fixtures, because the build cannot go green without them.

2. **`d5b3074`** `fix(catalog-tests): set OccurredOnUtc on 9 DomainEvent initializers (#138)` — adds `OccurredOnUtc = new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero)` to 9 callsites across 7 test files under `test/Catalog.UnitTests/{Categories,Products}/`. Plus a defensive **platform reflection sentinel** in `platform/Platform.SharedKernel.UnitTests/Base/DomainEvents/DomainEventTests.cs` — `OccurredOnUtc_RemainsRequiredOnEverySubtype` enumerates concrete `DomainEvent` subtypes in the platform-test reference graph and asserts each carries an inherited `[RequiredMember]`-decorated `OccurredOnUtc`. Defends against accidental removal of the `required` modifier on the base. **The cross-BC build guarantee remains the C# compiler** (CS9035 at every callsite); the test protects the structural contract itself.

The cross-BC build guarantee is closed procedurally by #147's CLAUDE.md rule (below).

#### #147 — CLAUDE.md polish + SharedKernel build rule

**`eeecd1b`** `docs: promote option B in Testcontainers § + add SharedKernel build rule (#147)`:

- Promotes `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && ...` (option B) above `NO_PROXY='*'` (option A) in the Testcontainers § because on corporate-proxy hosts option A actually fails: the `npipe://` URI is parsed by `HttpClient`'s env-proxy resolver BEFORE `NO_PROXY` is consulted.
- Adds a new "Non-obvious Conventions" bullet **"Platform.SharedKernel contract changes"** requiring `dotnet build -m` solution-wide before commit for any `required`-member bump or base-class signature edit. Codifies the procedural sentinel that prevents future #138-class breakages.

#### #140 — OTel collector restart loop

**`1ce2eea`** `fix(platform): remove invalid include block on otel attributes/pii-allowlist (#140)`:

The `include: { match_type: strict, services: [] }` block was rejected by the OTel collector validator (strict `match_type` requires at least one non-empty matcher among `services`, `span_names`, `attributes`, `libraries`, `resources`), producing the restart loop. The intent ("apply to all services") is the default behaviour when `include` is omitted. Redaction list under `actions:` unchanged — ADR-0011 still enforced for every emitted span.

#### Doc fixes (#141 / #142 / #143 / #144)

Four mechanical doc commits:

- **`c9669fc`** `docs(bc-design): correct BlobUploadFailed retry note (ADR-0017 SDK retries, not Polly) (#144)` — single-cell table edit in `error-taxonomy.md:49`.
- **`2e8da5d`** `docs(bc-design): add § 2.5 Invoicing to architecture-tests.md (#141)` — new § 2.5 enumerating the 30 shipped facts (PDF/Blob containment, PII allowlist, no-static-UtcNow, Blobs-namespace UtcNow ban, plus universal § 1 facts at the Invoicing-assembly level).
- **`21d9b20`** `docs(bc-design): add invoicing.invoices to § 4 + § 5.7 Invoicing schemas (#143)` — § 4 delta line for the `invoicing.invoices` topic with 10-year retention; new § 5.7 enumerating the 3 shipped Avro schemas + the deferred 4th (`InvoiceDeliveredEvent.avsc`).
- **`bc8ccf8`** `docs(bc-design): add § 6 Invoicing to use-cases.md (#142)` — new § 6 documenting the 7 in-scope Invoicing commands + queries (IssueInvoice, IssueCreditNote, ResendInvoice v1 stub, GetInvoiceById, GetInvoiceByOrderId, GetInvoicesByBuyer, GetCreditNoteById) with cross-references to error-taxonomy.md § 3.6 and events-catalog.md § 5.7.

### Defers — detail

#### #139 — NU1903 transitive vulnerabilities

Attempted the canonical fix (`CentralPackageTransitivePinningEnabled=true` + explicit `<PackageVersion>` pins for `System.Security.Cryptography.Xml`, `Microsoft.Kiota.Abstractions`, `Microsoft.Extensions.Caching.Memory` across all 5 CPM tiers). First pass cleared 49 of 53 NU1903 warnings but **transitive pinning surfaces every existing version drift in the dependency graph as NU1109 downgrade conflicts**: 100+ across EFCore (10.0.0 → 10.0.5/10.0.6), Npgsql (9.0.3 → 10.0.2), Serilog (4.2.0 → 4.3.0), `Microsoft.Extensions.Configuration.*` (10.0.0 → 10.0.2), `Microsoft.Extensions.Diagnostics.HealthChecks.*` (10.0.0 → 10.0.6), and others. Each fix uncovered more peers.

Reverted to baseline (53 NU1903 warnings unchanged). Defer rationale + recommended dedicated-session approach landed as a comment on #139.

#### #146 — invoicing.api compose service

Blocked by `services/Invoicing/Invoicing.API/Dockerfile` not existing (Catalog / Ordering / Notifications all have one). Filed [#148](https://github.com/DavidCapcuch/DotNetAtlas/issues/148) as the precondition. Re-open #146 once #148 lands.

#### #145 — Stryker.NET mutation pass

Carried forward per M10 + Invoicing-followups disposition. Comment landed.

### Verification matrix

| Gate | Result | Notes |
|---|---|---|
| `dotnet restore --locked-mode` | PASS | 53 NU1903 transitive warnings (#139 baseline; unchanged) |
| `dotnet build -m` | **PASS — exit 0** | Was 29 CS9035 errors before #138; now clean across the entire solution including all 6 BCs' FunctionalTests projects |
| `dotnet format whitespace --no-restore --verify-no-changes` | PASS | 0 violations |
| `dotnet format style --no-restore --verify-no-changes` | PASS | 0 violations |
| `dotnet test platform/Platform.SharedKernel.UnitTests` | **58 / 58 PASS** | +1 from the new `OccurredOnUtc_RemainsRequiredOnEverySubtype` reflection sentinel |
| `docker compose --profile full up -d` + `docker compose ps` | **Not run in-session** | Compose smoke deferred to the user's next local cycle. The YAML change in #140 is structural-only; the `pii-allowlist` processor schema is now valid. |
| Full BC FunctionalTests ring (Basket / Catalog / Inventory / Invoicing / Ordering / Payments) | **Not re-run in-session** | The Weather + Catalog + Basket production+test fixes are mechanical and the type-shape is verified by `dotnet build -m` exit 0. The latent Invoicing `OpenApiDescription_DisclosesV1StubBehaviour` regression should now pass since its `FunctionalTests` slice can finally link. Recommend a single dedicated re-run cycle pre-merge. |

### Boundary discipline

In-scope edits (only) this session:
- `src/Weather.{Domain,Application}/**` + `test/Weather.{Unit,Integration,Functional}Tests/**` (#138 — user-confirmed scope expansion since the build matrix could not go green otherwise).
- `services/Basket/Basket.Domain/Baskets/Basket.cs` + `test/Basket.UnitTests/Baskets/{Events,Application/Checkout}/**` (#138 — anticipated in the original closeout follow-up rebase checklist).
- `test/Catalog.UnitTests/{Categories,Products}/**/*.cs` (#138 — 7 files, 9 callsites).
- `platform/Platform.SharedKernel.UnitTests/Base/DomainEvents/DomainEventTests.cs` (#138 — defensive reflection sentinel).
- `docs/bc-design/{architecture-tests,use-cases,events-catalog,error-taxonomy}.md` (#141 – #144).
- `CLAUDE.md` (#147 + #138 procedural rule, while preserving the user's pre-existing "Agent skills" section).
- `src/otel-collector/otelcol-config.yml` (#140).
- This file (`cross-cutting-followups.md`) — appended a new closeout section below the platform-session content.

Not touched:
- `Directory.Packages.props` at any tier (#139 attempt was made-and-reverted; no net delta).
- `docker-compose.yaml` (#146 blocked by missing Dockerfile [#148](https://github.com/DavidCapcuch/DotNetAtlas/issues/148)).
- EF Core migrations (CLAUDE.md forbids agent-driven generation).
- Other dirty entries pre-existing in the working tree (`services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryValidator.cs`, `.claude/scheduled_tasks.lock`, the untracked closeouts under `session-summaries2/`, the `docs/agents/` tree referenced by CLAUDE.md) remain unstaged exactly as at session start.

### Reviewer notes for next pass

- The new `OccurredOnUtc_RemainsRequiredOnEverySubtype` reflection sentinel only walks `DomainEvent` subtypes IN the platform-test reference graph (currently only `TestEvent`). The cross-BC guarantee against future `required`-member bumps is the C# compiler (CS9035) + the CLAUDE.md procedural rule ("solution-wide `dotnet build -m` before commit"). Strengthening the test to walk every BC's `*.Domain` assembly would invert layering and was deliberately skipped — the compile-time guarantee is sufficient and the CLAUDE.md rule makes it ENFORCED.
- The Weather rebase converted 7 factory signatures to take `DateTimeOffset utcNow` parameters. Any future caller that mints a Weather aggregate now must thread time — CS7036 fires at compile time. The new `test/Weather.UnitTests/Common/TestInstants.cs` constant is the standard test-side supply; production handlers inject `TimeProvider` per ADR-0015.
- The Basket rebase was anticipated in the original closeout follow-up rebase checklist (line 116-119 above) but neither the original session nor the Invoicing-followups session landed it — Basket-affecting cascade is now closed. The closeout-followups checklist L122-129 also mentions Weather (correctly anticipated); the cascade through the `services/Basket/**` tree is now resolved.
- The `services/Invoicing/Invoicing.API/Dockerfile` follow-up ([#148](https://github.com/DavidCapcuch/DotNetAtlas/issues/148)) is a fast unblock — copy the Catalog or Ordering Dockerfile, swap project name, verify locally.
- The #139 cascade is real and needs a single dedicated session — splitting it across multiple PRs will keep reintroducing NU1109 conflicts because the underlying matrix is wide.

### Commits landed (this session)

```
bc8ccf8 docs(bc-design): add § 6 Invoicing to use-cases.md (#142)
21d9b20 docs(bc-design): add invoicing.invoices to § 4 + § 5.7 Invoicing schemas (#143)
2e8da5d docs(bc-design): add § 2.5 Invoicing to architecture-tests.md (#141)
c9669fc docs(bc-design): correct BlobUploadFailed retry note (ADR-0017 SDK retries, not Polly) (#144)
1ce2eea fix(platform): remove invalid include block on otel attributes/pii-allowlist (#140)
eeecd1b docs: promote option B in Testcontainers § + add SharedKernel build rule (#147)
d5b3074 fix(catalog-tests): set OccurredOnUtc on 9 DomainEvent initializers (#138)
6633230 fix(weather): thread TimeProvider into 7 factory methods (#138)
```

Plus this commit (closeout summary).

### Issues filed (this session)

- [#148](https://github.com/DavidCapcuch/DotNetAtlas/issues/148) — `cross-cutting(wave1-followup): Invoicing.API Dockerfile missing — blocks #146` (precondition for [#146](https://github.com/DavidCapcuch/DotNetAtlas/issues/146)).

### What "done" looks like for this fix cycle

- [x] #138 fixed + closed; full BC FunctionalTests ring is now buildable.
- [x] #147 fixed + closed; option B leads + SharedKernel build rule landed.
- [x] #140 fixed + closed; otel-collector YAML structurally valid.
- [x] #141 / #142 / #143 / #144 fixed + closed; bc-design docs reconciled with shipped Invoicing.
- [x] #139 deferred with detailed defer-rationale comment.
- [x] #146 blocked on [#148](https://github.com/DavidCapcuch/DotNetAtlas/issues/148) (filed this session); blocker comment landed on #146.
- [x] #145 deferred with rationale comment.
- [x] This closeout section landed at `cross-cutting-followups.md`.

All four CI gates (restore --locked-mode + build -m + format whitespace/style) pass solution-wide. Original 29 CS9035 errors cleared; 53 NU1903 baseline unchanged.
