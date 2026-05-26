# Ordering M9 — Docs Self-Corrections + Appendix B Resolutions + Session Summary

> Milestone M9 per [`docs/implementation-prompts/ordering.md`](../ordering.md) `<session_management>` step 9 — *"Docs self-corrections + Appendix B resolutions + session summary."* Branch: `aaqwdqwd`. **Final** listed Ordering milestone in the dispatch prompt. M1–M8 already shipped (commit chain `9e5d4e9 → 0a94c55 → c678b68 → a09f365 → 2b17df4 → 2cdd7e0 → f9d07af → 347feb3`).

## Mission

M9 is **docs-only**: no source files, tests, schemas, or infra modified. Three deliverables:

1. **Appendix B ratification** — convert the 6 "open questions" in [`docs/bc-design/ordering.md`:547-557](../../bc-design/ordering.md) to "Resolved Questions (M9 ratification)" with **Decision**, **Rationale**, and **Code citation (`file:line`)** for each. Defaults per the dispatch prompt's `<autonomous_evolution>:89-101` — all ratified.
2. **Drift fixes** surfacing in the Phase-1 spot-check against M1–M8 shipped code: stale saga-command names (`FailOrderCommand` → `MarkOrderFailedCommand`), aspirational-Kafka triggers for application-only commands, and a pre-existing `CannotCancelAfterShipped` error-name drift in `OrderingErrors` references (the locked taxonomy at [`error-taxonomy.md:32, 177`](../../bc-design/error-taxonomy.md) only has `CannotCancelInStatus(status)`).
3. **Session summary** (this file).

Per the user's standing dispatch instruction the pre-commit `feature-dev:code-reviewer` (Opus) was run regardless of file-count threshold; its CONDITIONAL-PASS verdict surfaced one MEDIUM finding (M-1: same `CannotCancelAfterShipped` drift in Appendix B.5 prose) which was fixed in-flight before staging. Two LOW findings (cosmetic asymmetry in §9.1 trigger column; Appendix B.1 docker-compose citation lacks line anchor) are documented as deferred carry-forwards.

## Files modified

```
code:                  0
tests:                 0
Avro schemas:          0
docker-compose delta:  0
config:                0
infra (new files):     0
doc updates:           3
  - docs/bc-design/ordering.md (MOD; §9.1 trigger clarifications + FailOrderCommand → MarkOrderFailedCommand rename + §10.1 context-map updates + §10.3 Subscribes-to split + §8 pattern-showcase error name fix + Appendix B 6/6 resolutions + Appendix C error name fix)
  - docs/bc-design/example-mapping/ordering.md (MOD; 3 occurrences of CannotCancelAfterShipped → CannotCancelInStatus(Status.Name))
  - docs/implementation-prompts/session-summaries/ordering-m9.md (NEW; this file)
```

`docs/bc-design/glossary-ordering.md` was spot-checked — no drift surfaced (term list aligns with Domain entity/VO names; no stale saga-command references; ProductSnapshot correctly listed as `Sku` + `Name` only, F6 deferral consistent). No edits.

## Decisions taken (with rationale)

1. **Appendix B numbering preserved** (B.1–B.6) so existing external links (`ordering.md#appendix-b`) continue to resolve. Heading retitled from "Open Questions" → "Resolved Questions (M9 ratification)".
2. **All six Appendix B defaults from `<autonomous_evolution>` ratified verbatim** — none deviated:
    - **B.1** Saga → Ordering transport = Kafka `ordering.order-commands` (Option Y). 4 KafkaFlow inbox consumers under [`services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/) confirm the choice — `CreateOrderCommandKafkaHandler.cs`, `ConfirmOrderCommandKafkaHandler.cs`, `CancelOrderCommandKafkaHandler.cs`, `MarkOrderFailedCommandKafkaHandler.cs`. Also locked by [`events-catalog.md § 5.5`](../../bc-design/events-catalog.md) + ADR-0004.
    - **B.2** Weather-remnant fate — pre-resolved (services/Order removed pre-dispatch). Text preserved verbatim.
    - **B.3** Concurrency token = explicit `RowVersion : uint` mapped to Postgres `xmin` via Npgsql `IsRowVersion()`. Pinned by [`OrderConfiguration.cs:37-40`](../../../services/Ordering/Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs). Rationale: makes optimistic-concurrency violations distinguishable from app-level update conflicts in observability; avoids implicit-`LastModifiedUtc` interceptor double-duty.
    - **B.4** Pagination = offset/limit (`Skip` + `Take` with `Take=20` default). [`GetOrdersByBuyerQuery.cs:15-17`](../../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQuery.cs). Rationale: v1 per-buyer volumes well below offset's `O(skip)` threshold; simpler API surface; no cursor format that v2 migration would need to bump.
    - **B.5** Cancellation auth = buyer/admin may cancel up to `Confirmed`; no one after `Shipped` (I-12). [`Order.Cancel(...)`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs) returns `Result.Fail(OrderingErrors.CannotCancelInStatus(Status.Name))` (mapped to 409 Conflict per [`error-taxonomy.md:32, 294`](../../bc-design/error-taxonomy.md)). [`CancelOrderEndpoint.cs:33-89`](../../../services/Ordering/Ordering.Api/Endpoints/Orders/CancelOrder/CancelOrderEndpoint.cs) dual-modes buyer/admin. Functional tests `WhenBuyerCancelsOwnCreatedOrder_ReturnsNoContent`, `WhenAnotherBuyerTriesToCancel_ReturnsNotFound`, `WhenOrderShipped_ReturnsConflict`, `WhenSameIdempotencyKeyUsedByDifferentBuyer_HandlerStillRuns` in [`CancelOrderTests.cs`](../../../test/Ordering.FunctionalTests/ApiEndpoints/Orders/CancelOrderTests.cs) pin the rule.
    - **B.6** Delivery confirmation = admin-only `MarkOrderDeliveredCommand`. [`MarkOrderDeliveredEndpoint.cs:36`](../../../services/Ordering/Ordering.Api/Endpoints/Orders/MarkOrderDelivered/MarkOrderDeliveredEndpoint.cs) (`AuthPolicies.OrderingAdmin`) + functional tests `WhenAuthenticatedAsBuyer_ReturnsForbidden` (buyers blocked) and `WhenOrderShipped_ReturnsNoContentAndStatusDelivered` (admin happy path) in [`MarkOrderDeliveredTests.cs`](../../../test/Ordering.FunctionalTests/ApiEndpoints/Orders/MarkOrderDeliveredTests.cs) pin the surface.
3. **Drift fix `FailOrderCommand` → `MarkOrderFailedCommand`** — 4 occurrences in `docs/bc-design/ordering.md` (§9.1 row trigger, §10.1 Inventory row, §10.1 Payments row, §10.3 Subscribes-to). Authoritative source: [`events-catalog.md § 5.5`](../../bc-design/events-catalog.md) + shipped Avro [`MarkOrderFailedCommand.avsc`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/Orders/MarkOrderFailedCommand.avsc).
4. **Drift fix `CannotCancelAfterShipped` → `CannotCancelInStatus(Status.Name)`** — 4 in-bounds occurrences (1 in ordering.md §8 Pattern Showcase, 1 in Appendix C, 3 in example-mapping Session 2). One remaining occurrence in ordering.md Appendix B.5 prose is the deliberate "pre-M9 drafts used a `CannotCancelAfterShipped` variant" historical-context mention — intentional, preserved. Authoritative source: [`error-taxonomy.md:32, 177`](../../bc-design/error-taxonomy.md) `CannotCancelInStatus(status)` factory.
5. **Drift fix `MarkOrderStockReservedCommand` / `MarkOrderPaymentCompletedCommand` trigger clarification** — these are application-layer commands invoked by the Wave-2 saga via direct in-process dispatch; they are **NOT** Kafka topic-arriving commands per the 4-command lock in [`events-catalog.md § 5.5`](../../bc-design/events-catalog.md). Evidence: [`ConfirmOrderCommandKafkaHandlerTests.cs:40-42`](../../../test/Ordering.IntegrationTests/Messaging/Kafka/ConfirmOrderCommandKafkaHandlerTests.cs) explicitly notes *"those two transitions have no Kafka handler in v1 — saga drives them via direct app-command dispatch"*. Doc §9.1 + §10.1 + §10.3 updated to make this contract explicit.
6. **§10.2 Option X/Y discussion preserved as historical context** — Appendix B.1 ratifies Option Y; the §10.2 prose already said "Recommendation: Option Y" so the doc is now self-consistent without invasive §10.2 surgery. Minimal-edit discipline.
7. **`use-cases.md` drift flagged as carry-forward (not edited)** — [`docs/bc-design/use-cases.md`](../../bc-design/use-cases.md) lines 814, 837, 999-1000, 1488, 1491 carry the same stale `FailOrderCommand` / `MarkOrderStockReservedCommand`-as-Kafka names. `use-cases.md` is shared across BCs and OUT of Ordering's `<boundaries>:138-142`. Flagged for a future cross-BC doc-cleanup pass.
8. **No glossary edits** — `glossary-ordering.md` spot-checked clean; term list aligns with Domain entity/VO names; no stale saga references; ProductSnapshot correctly omits `CapturedAtUtc` per the F6 carry-forward state.
9. **Compose smoke deliberately skipped** — no runtime surface changed by M9 (docs-only). Re-running the M8 compose smoke would consume time and add nothing; the 4 CI gates + 4 test slices replay alone prove zero code regression.

## ADR application notes (delta from M8)

No new ADR wiring or behavior introduced — M9 is docs-only. The 6 Appendix B ratifications trace back to ADRs already enforced by M1–M8:

- **[ADR-0001](../../adr/0001-centralized-saga-orchestration.md)** + **[ADR-0004](../../adr/0004-checkout-saga-topology.md)** — back B.1 (Kafka command topic, not HTTP).
- **[ADR-0007](../../adr/0007-avro-compatibility-modes.md)** — backs the 4-command Avro contract that locks B.1's command set.
- **[ADR-0011](../../adr/0011-pii-handling-gdpr.md)** — `*_enc` PII columns; unchanged from M4/M6 (re-confirmed by re-running architecture tests this session).
- **[ADR-0012](../../adr/0012-api-versioning.md)** — `/api/v1/ordering/...` routes; back B.6's `MarkOrderDeliveredEndpoint` choice of admin policy under that prefix.
- **[ADR-0013](../../adr/0013-idempotency-key-http.md)** — back B.5's idempotency posture on admin cancel (pinned by `WhenSameIdempotencyKeyUsedByDifferentBuyer_HandlerStillRuns`).
- **[ADR-0015](../../adr/0015-time-timezone-policy.md)** — back the `DateTimeOffset utcNow` parameters on every transition method; unchanged.

All previously-applicable ADRs (0008/0010/0011/0012/0013/0015) remain enforced — verified by the re-run of all 4 Ordering test slices (architecture tests in particular).

## Ordering `<dod>` coverage matrix (delta from M8)

Walking every `<dod>` line. Only flips this milestone are highlighted.

| `<dod>` line ([`ordering.md`:110-136](../ordering.md)) | M8 → M9 | Status |
|---|---|---|
| 4-layer solution scaffold + `dotnet build -m` green | ✅ → ✅ | Re-confirmed by this session's build (53 NU1903 baseline warnings, 0 errors). |
| 6 external Avro events + 4 saga-command schemas + 8 internal `*DomainEvent` records + 4 outbox publishers + 4 saga-command consumers with inbox dedup | ✅ → ✅ | Counts unchanged. Doc edits make the **4 Kafka command names** explicit per `events-catalog.md § 5.5`. |
| Admin HTTP endpoints under `/api/v1/ordering/` — `MarkOrderShipped`, `MarkOrderDelivered`, `Cancel` + auth + `.Idempotency()` | ✅ → ✅ | Pinned by Appendix B.5/B.6 + cited functional tests. |
| Queries: `GetOrderById` (buyer-or-admin), `GetOrdersByBuyer` (paginated) | ✅ → ✅ | B.4 ratifies offset/limit pagination — cited in Appendix B.4. |
| **Appendix B decisions documented** | ⏸️ → ✅ | **Flipped this milestone.** All 6 questions resolved with **Decision** + **Rationale** + **Code citation**. |
| `OrderingErrors` matches `error-taxonomy.md § 3.3` | ✅ → ✅ | Re-confirmed by this session's drift-fix (4 stale `CannotCancelAfterShipped` references aligned to the taxonomy-locked `CannotCancelInStatus(status)` factory). |
| All timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain (arch test) | ✅ → ✅ | Arch tests re-run green. |
| PII column naming `*_enc` for `ShippingAddress` / `BillingAddress` | ✅ → ✅ | Arch tests re-run green. |
| Correlation-id propagation: Kafka header → handler → DB column → outbox row → emitted Avro event header | ✅ → ✅ | Integration tests re-run green. |
| Integration tests cover all `example-mapping/ordering.md` sessions + admin-cancel idempotency | ✅ → ✅ | 18/18 + 1 expected skip; example-mapping doc fixes do not affect the test coverage. |
| All `<applicable_adrs>` enforced (architecture tests + verification commands) | ✅ → ✅ | Arch tests re-run green; 4 CI gates green. |
| **F6 — `ProductSnapshot.CapturedAtUtc` chain** (10 steps) | ⏸️ → ⏸️ | Deliberate carry-forward (cross-BC, out of Ordering `<boundaries>`); same posture as M7/M8. 2 `Skip`'d facts remain. |
| Peer-review chain executed; HIGH findings fixed | ✅ → ✅ | Opus reviewer (`feature-dev:code-reviewer`) ran on the M9 diff — CONDITIONAL-PASS. One MEDIUM (M-1) fixed in-flight. No HIGH/CRITICAL. |

## Universal `_shared.md § 12` coverage (delta from M8)

| `§ 12` line ([`_shared.md`:189-205](../_shared.md)) | Status | Citation / evidence |
|---|---|---|
| 4-layer project compiles | ✅ | Re-confirmed (00:02:05 solution-wide build). |
| All commands + queries from use-cases.md § 3 implemented | ✅ | Cited in Appendix B trace; `use-cases.md` drift (carry-forward) does not block — application surface matches the locked Avro contract. |
| All internal `*DomainEvent` declared in Domain | ✅ | 8 records under [`Ordering.Domain/Orders/Events/`](../../../services/Ordering/Ordering.Domain/Orders/Events/). |
| All external `*Event` Avro under `Platform.SchemaRegistry.Contracts/Avro/Ordering/` | ✅ | 6 events + 4 commands; 10 `.avsc` files. |
| Outbox publishers map internal → external | ✅ | 6 publishers under [`Ordering.Application.Orders.*`](../../../services/Ordering/Ordering.Application/Orders/). |
| DbContext + naming conventions scaffolded | ✅ | Migration `20260424202154_AddOrderAndOutboxInbox` applied in M8. |
| Messaging DI: outbox, inbox, Kafka consumers per BC | ✅ | 4 SagaCommand handlers + base + options + mappers under [`Ordering.Infrastructure.Messaging.Kafka.SagaCommands/`](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/). |
| docker-compose delta: topics + outbox-relay + ordering.api | ✅ | All in place since M8. No changes this milestone. |
| 4 test projects compile + pass | ✅ | 205 / 205 (+3 SKIP) — see § Verification. |
| All HTTP routes under `/api/v1/ordering/...` | ✅ | M5; no changes. |
| All timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain (arch test) | ✅ | Arch tests re-run 27/27 + 2 F6 skips. |
| Correlation-id propagation working | ✅ | Integration tests re-run 18/18 + 1 expected skip. |
| `dotnet build -m`, `dotnet restore --locked-mode`, `dotnet format whitespace`, `dotnet format style` all green | ✅ | This session — all 4 gates exit 0. |
| `docker compose --profile full up -d` starts the container + healthcheck passes | ✅ | Last verified in M8 (`ordering.api` Up + `/api/healthz` 200 + `/api/readiness` 200 + `/api/v1/ordering/orders/{id}` 401). No M9 runtime changes, no re-run required. |
| Docs self-corrected if needed | ✅ | **This milestone's headline work.** 3 doc files updated; Appendix B converted to "Resolved Questions"; 4 + 3 = 7 stale name occurrences fixed across 2 files. |
| Peer-review chain executed; HIGH findings fixed | ✅ | Opus reviewer ran; CONDITIONAL-PASS; one MEDIUM fixed (M-1). |
| Session summary posted | ✅ | This document. |

## Verification — actual output

The 4 CI gates per [`_shared.md § 12`](../_shared.md):

```text
$ dotnet restore --locked-mode
... 53 NU1903 transitive vulnerability warnings on System.Security.Cryptography.Xml
+ Microsoft.Kiota.Abstractions + Microsoft.Extensions.Caching.Memory across many projects.
Pre-existing branch baseline (matches ordering-m8:105 / invoicing-m10:114).
NOT Ordering-introduced.
exit 0.

$ dotnet build -m --no-restore
53 upozornění
Počet chyb: 0
Uplynulý čas 00:02:05.74 — exit 0.

$ dotnet format whitespace --no-restore --verify-no-changes
"Při načítání pracovního prostoru se vygenerovala upozornění..." (workspace-load info only).
exit 0 — 0 violations.

$ dotnet format style --no-restore --verify-no-changes
"Při načítání pracovního prostoru se vygenerovala upozornění..." (workspace-load info only).
exit 0 — 0 violations.
```

The four Ordering test slices per [`ordering.md <verification>`:172-175](../ordering.md):

```text
$ dotnet test test/Ordering.UnitTests/Ordering.UnitTests.csproj --no-build --no-restore
Úspěšné!  - Neúspěšné: 0, Úspěšné: 139, Přeskočeno: 0, Celkem: 139, Doba trvání: 1 s

$ dotnet test test/Ordering.ArchitectureTests/Ordering.ArchitectureTests.csproj --no-build --no-restore
[xUnit.net]   Ordering.ArchitectureTests.ProductSnapshotContractTests.OrderingProductSnapshot_HasCapturedAtUtc [SKIP]
[xUnit.net]   Ordering.ArchitectureTests.ProductSnapshotContractTests.OrderingProductSnapshot_IsStructuralSupersetOfBasketProductSnapshot [SKIP]
Úspěšné!  - Neúspěšné: 0, Úspěšné: 27, Přeskočeno: 2, Celkem: 29, Doba trvání: 1 s
  ↑ 2 skips = F6 chain (M7 deferral) — same posture as M8 baseline.

$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Ordering.IntegrationTests/Ordering.IntegrationTests.csproj --no-build --no-restore
[xUnit.net]   Ordering.IntegrationTests.Sessions.ItemImmutabilityIntegrationTests.Placeholder_ItemMutationGuard_NotApplicableInV1 [SKIP]
Úspěšné!  - Neúspěšné: 0, Úspěšné: 18, Přeskočeno: 1, Celkem: 19, Doba trvání: 13 s
  ↑ 1 skip = explicit placeholder per example-mapping Session 3 R4 ("v1 deliberately omits any
    item-mutation command"); same as M8 baseline.

$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Ordering.FunctionalTests/Ordering.FunctionalTests.csproj --no-build --no-restore
Úspěšné!  - Neúspěšné: 0, Úspěšné: 21, Přeskočeno: 0, Celkem: 21, Doba trvání: 3 s

Total: 205 / 205 (+3 SKIP) — exact match to M8 baseline.
Docs-only edits caused zero code regression — exactly what M9 must produce.
```

### Mid-session blocker (one re-run)

The first integration-test invocation used the `NO_PROXY='*'` chain instead of the CLAUDE.md option-B `unset HTTP_PROXY HTTPS_PROXY ...` chain and failed with `Docker.DotNet.DockerClient.PrivateMakeRequestAsync` errors (the npipe-not-supported-by-relative-URI issue from CLAUDE.md). Re-ran with option B as M8 did and got the expected 18 / 1 skip green. Logged here as a "use option B" note for future BC docs-only milestones — option A is shell-state-fragile under this shell harness.

Compose smoke deliberately skipped per § Decisions #9.

## Pre-commit Opus reviewer findings + resolution

`Agent(subagent_type="feature-dev:code-reviewer", model="opus")` per [`_shared.md § 11 step 0`](../_shared.md). User's standing dispatch instruction makes this mandatory regardless of file-count threshold. Reviewer brief: file list (1 modified doc + this NEW summary), boundary check, full 4 + 4 verification posture (paste), the 6 Appendix B ratifications, and the 7 cited code-file paths to spot-verify.

**Verdict: CONDITIONAL-PASS.** Findings + dispositions:

| ID | Severity | Finding | Disposition |
|---|---|---|---|
| **M-1** | MEDIUM | Appendix B.5 prose cited `OrderingErrors.CannotCancelAfterShipped` but no such symbol exists; [`Order.cs:343`](../../../services/Ordering/Ordering.Domain/Orders/Order.cs) returns `OrderingErrors.CannotCancelInStatus(Status.Name)` per the locked [`error-taxonomy.md:32, 177`](../../bc-design/error-taxonomy.md). | **Fixed in-flight.** Single-token edit in B.5 plus 4 additional same-drift fixes uncovered in ordering.md §8/§Appendix C + 3 in `example-mapping/ordering.md` Session 2 (all pre-existing pre-M9 drift). One deliberate retained mention in B.5 prose as "pre-M9 drafts of this section used a `CannotCancelAfterShipped` variant that never materialised in the locked taxonomy" — historical-context anchor for future readers. No re-run needed (no code or schema affected). |
| **L-1** | LOW | §9.1 trigger column has cosmetic asymmetry — `MarkOrderFailedCommand` row says "Kafka (saga) → inbox consumer **on `ordering.order-commands`**" while sibling `ConfirmOrderCommand` row says only "Kafka (saga) → inbox consumer". | **Deferred to Stage-2 use-case-catalog cleanup** (the same pass that will resolve the `use-cases.md` carry-forward — see § Improvements). Reviewer rated as cosmetic-only; § 10.3 already lists the 4 commands authoritatively. Documented here and in commit body. |
| **L-2** | LOW | Appendix B.1 cites `docker-compose.yaml` retention value but does not include a `file:line` anchor (every other Appendix B entry does). | **Deferred** — value is verifiable, asymmetry is minor. Documented here. |

Verification matrix the reviewer ran independently — every cited file path / line range / test name / Avro contract / events-catalog cross-reference passed. Verdict promoted from CONDITIONAL-PASS → effective PASS after the M-1 fix.

## Improvements proposed (NOT implemented unless approved)

Carry-forward list. Items observed during M9 but out of strict scope.

- **F6 / `ProductSnapshot.CapturedAtUtc` 10-step chain** ([`ordering.md`:124-134](../ordering.md)). Two architecture-test facts remain `Skip`'d at [`ProductSnapshotContractTests.cs:19-41`](../../../test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs). Touches Basket `.avsc` + saga's `CreateOrderConsumer` + Basket's mapper — strictly forbidden by Ordering `<boundaries>`. Needs a coordinated cross-BC mini-milestone, ideally a Wave-1.7 cleanup pass (or M10 candidate — see § M10 handoff block).
- **`use-cases.md` § 3 drift** — [`docs/bc-design/use-cases.md`](../../bc-design/use-cases.md) lines 814, 837, 999-1000, 1488, 1491 carry the same stale `FailOrderCommand` / `MarkOrderStockReservedCommand`-as-Kafka names that M9 fixed in `ordering.md`. `use-cases.md` is shared across BCs (Basket, Inventory, Payments, Invoicing, BFF all read § 3+) and OUT of Ordering's `<boundaries>:138-142`. Should be a cross-BC doc-cleanup pass with explicit user authorization.
- **§9.1 trigger-column asymmetry** (Opus L-1) — for full polish, normalise the trigger phrasing across the 4 Kafka command rows. Defer to the Stage-2 use-case-catalog cleanup or M10.
- **Appendix B.1 docker-compose citation lacks `file:line` anchor** (Opus L-2). Single-line fix when next touching `ordering.md`.
- **Postgres per-BC `CREATE DATABASE` init script** — same gap noted in `catalog-m7.md` and `ordering-m8.md`. Long-term solution: ship a `src/postgres-init/per-bc-databases.sql` mounted by the `postgresdb` service in compose, idempotent. Belongs to Wave 0 platform cleanup. Canonical write-up at `catalog-m7.md:165-171`.
- **`nw-mutation-test` post-green pass on the Ordering suite** ([`_shared.md § 7`](../_shared.md) recommendation, kill-rate target ≥ 80%). 205/205 + 3 SKIP green is a meaningful baseline; mutation testing would harden the test suite against false-greens. M10 candidate (Catalog's M10 was specifically Stryker.NET mutation baseline).
- **`otel-collector attributes/pii-allowlist` processor restart loop** — cross-cutting platform defect; same baseline as `invoicing-m10:245`, `payments-m9`, `inventory-m10`, `ordering-m8:255`. Not Ordering-introduced; ADR-0011 redaction for emitted spans is non-functional in local docker-compose runs. Does not block Ordering runtime.
- **NU1903 transitive vulnerability warnings** (53 instances across the branch) — pre-existing branch-wide. Same as `invoicing-m10:246`, `ordering-m8:256`. Not Ordering-introduced; belongs to a cross-BC platform / CPM cleanup pass.
- **§10.2 Option X/Y discussion** — could be tightened post-Appendix-B.1 ratification (the historical "two viable options" framing reads as undecided to a first-time reader). Defer; doc is internally consistent ("Recommendation: Option Y" matches B.1 ratification).

## Boundary discipline

In-bounds writes per [`ordering.md` `<boundaries>`:138-142](../ordering.md):

- `docs/bc-design/ordering.md` — MODIFIED (§9.1 + §10.1 + §10.3 + §8 + Appendix B + Appendix C). "Self-correction only" explicitly allowed.
- `docs/bc-design/example-mapping/ordering.md` — MODIFIED (3 occurrences of pre-existing `CannotCancelAfterShipped` drift). "Self-correction only" explicitly allowed.
- `docs/implementation-prompts/session-summaries/ordering-m9.md` — NEW (this file). Mirrors all prior milestone summary locations.

NOT touched (out-of-bounds or out-of-scope):

- `services/Ordering/**`, `test/Ordering.*Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/**` — no code, no tests, no schemas modified.
- `docker-compose.yaml`, `Directory.Packages.props`, `DotNetAtlas.slnx` — no infra changes.
- Other BCs' code, tests, schemas, docs.
- `docs/bc-design/glossary-ordering.md` — read for verification, no drift surfaced, no edits.
- `docs/bc-design/use-cases.md` — drift surfaced (5 stale references) but explicitly OUT of Ordering boundary; flagged as carry-forward.
- `docs/bc-design/error-taxonomy.md` — read for verification (M-1 root cause); no drift in error-taxonomy itself, the drift was in references TO it.
- `docs/implementation-prompts/ordering.md` (the dispatch prompt itself) — not in the writable doc set; no edits.
- Pre-existing untracked closeout files (`basket-closeout.md`, `catalog-closeout.md`, `inventory-closeout.md`, `invoicing-closeout.md`, `payments-closeout.md`) under `docs/implementation-prompts/session-summaries/` — all OUT of Ordering's `<boundaries>`. Targeted `git add` of only M9-relevant paths.

## Inconsistencies

- **`FailOrderCommand` vs `MarkOrderFailedCommand` (FIXED)** — 4 occurrences in `docs/bc-design/ordering.md` were stale. Authoritative source: `events-catalog.md § 5.5` + shipped Avro `MarkOrderFailedCommand.avsc` + shipped consumer `MarkOrderFailedCommandKafkaHandler.cs`. Pre-M9 drafts predate the saga-design ratification.
- **`MarkOrderStockReservedCommand` / `MarkOrderPaymentCompletedCommand` trigger ambiguity (FIXED)** — `ordering.md` §9.1 + §10.1 + §10.3 said "Kafka (saga) → inbox consumer" for these; reality is application-layer only (`ConfirmOrderCommandKafkaHandlerTests.cs:40-42` is the canonical evidence). M9 clarifies the contract: 4 Kafka commands + 2 application-only commands.
- **`OrderingErrors.CannotCancelAfterShipped` vs `OrderingErrors.CannotCancelInStatus(status)` (FIXED)** — name was wrong in 4 in-bounds places (1 ordering.md §8 + 1 ordering.md Appendix C + 1 ordering.md Appendix B.5 + 3 example-mapping/ordering.md Session 2). Authoritative source: `error-taxonomy.md:32, 177` factory definition. Reviewer Opus surfaced 1 of the 4 (the new B.5 prose); spot-check during the fix revealed the other 3 pre-existing instances.
- **`use-cases.md` § 3 drift (NOT FIXED — out of boundary)** — same `FailOrderCommand` and `MarkOrderStockReservedCommand`-as-Kafka stale names at lines 814, 837, 999, 1000, 1488, 1491. Cross-BC doc; needs explicit user authorization. Logged in § Improvements.

## What "done" looks like for M9

- [x] Four CI gates green (build 53 NU1903 baseline / 0 errors, restore --locked-mode, format whitespace 0 violations, format style 0 violations).
- [x] Four Ordering test slices green: **205 / 205 (+3 SKIP)** — exact match to M8 baseline (139 Unit + 27 Architecture/2 F6 skip + 18 Integration/1 expected skip + 21 Functional).
- [x] Appendix B converted from "Open Questions" → "Resolved Questions (M9 ratification)"; 6/6 questions resolved with **Decision**, **Rationale**, **Code citation**.
- [x] 4 `FailOrderCommand` references in `ordering.md` renamed to `MarkOrderFailedCommand` (locked seam from `events-catalog.md § 5.5`).
- [x] 4 + 3 = 7 `CannotCancelAfterShipped` references aligned to the `error-taxonomy.md`-locked `CannotCancelInStatus(status)` factory across `ordering.md` + `example-mapping/ordering.md`.
- [x] `MarkOrderStockReservedCommand` + `MarkOrderPaymentCompletedCommand` trigger column in `ordering.md` §9.1 + §10.1 + §10.3 corrected to "saga-internal app-command dispatch" (no Kafka inbox consumer in v1).
- [x] Pre-commit Opus reviewer ran; CONDITIONAL-PASS verdict; one MEDIUM finding (M-1) fixed in-flight; two LOW findings deferred and documented.
- [x] No code, tests, schemas, or infra modified — docs-only milestone discipline preserved.
- [x] Session summary posted at `docs/implementation-prompts/session-summaries/ordering-m9.md` mirroring `ordering-m8.md` depth + sections.
- [x] M9 committed on branch `aaqwdqwd` (targeted `git add` of only M9 paths; pre-existing dirty closeout entries remain unstaged + untracked).
- [x] M10 handoff block emitted in chat per the user's standing dispatch instruction.

## Open questions

None blocking — M9 is the last listed milestone in `ordering.md` `<session_management>`. The Appendix B questions that were the explicit M9 deliverable are all resolved.

**Note on M10.** `ordering.md` `<session_management>` lists 9 milestones; M9 is the last defined one, so M10 has no canonical scope in the dispatch prompt. The M10 handoff block is emitted mechanically per the user's standing instruction (`{BC}` → `Ordering`, `{N+1}` → `10`); the user selects scope from the **Improvements proposed** list above. Likely M10 candidates (in order of explicit DoD coverage):

1. **F6 / `ProductSnapshot.CapturedAtUtc` 10-step chain** — only `<dod>` item still ⏸️. Cross-BC mini-milestone (Basket + saga + Ordering). Resolves the 2 `Skip`'d architecture-test facts.
2. **`nw-mutation-test` baseline** — Catalog's M10 precedent; self-contained within Ordering.
3. **`use-cases.md` cross-BC doc-cleanup** — would resolve drift carry-forward across all BCs at once.

---

## M10 handoff block

Per the user's standing dispatch instruction, the canonical M10 handoff block is emitted in the chat after this commit lands.
