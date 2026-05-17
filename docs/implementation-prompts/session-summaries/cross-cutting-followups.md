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
