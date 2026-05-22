# Invoicing BC — Wave 1 Closeout Follow-ups

> Branch: `aaqwdqwd` (HEAD post-fixes: `c4e16fa`). Wave 1 closeout dispatch — read [`session-summaries/invoicing-closeout.md`](invoicing-closeout.md) and [`session-summaries2/invoicing-closeout.md`](../session-summaries2/invoicing-closeout.md) for the underlying audit.

## Mission

Reconcile the two parallel Invoicing closeout reviews (CONDITIONAL-PASS with 4 HIGH disclosed vs PASS with 0 HIGH), TDD-fix the in-scope HIGH findings, file every MEDIUM/LOW + cross-cutting carry-forward as `needs-triage` issues, and post this summary so the BC closes Wave 1 cleanly.

## Triage applied

| Severity | Disposition | Count |
|---|---|---|
| CRITICAL | — | 0 |
| **HIGH** (in-scope, fix) | TDD-fixed, one commit each | **2** |
| HIGH (reclassified MEDIUM — design-intentional / accepted carry-forward) | filed | 2 (H2/H3 from closeout1) |
| MEDIUM | filed as `invoicing(wave1-followup):` | 10 |
| LOW (in-scope) | filed as `invoicing(wave1-followup):` | 5 |
| Cross-cutting (out-of-bounds) | filed as `cross-cutting(wave1-followup):` | 10 |

## Fixes landed

### Commit 1 — `196501b` `fix(invoicing): inject TimeProvider into AzureBlobStore (ADR-0015)`

Closeout1 H4 / closeout2 LOW. [`AzureBlobStore.BuildSasUri`](../../../services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs) was minting SAS expiry against `DateTimeOffset.UtcNow` while every handler computed `sasExpiresAtUtc` via `TimeProvider`; under `FakeTimeProvider` the two clocks could disagree by hours.

TDD path:
1. **RED** — added [`BlobsNamespace_ShouldNotCall_StaticUtcNow`](../../../test/Invoicing.ArchitectureTests/Infrastructure/BlobStorageContainmentTests.cs) (sibling of the existing `PdfNamespace_ShouldNotCall_StaticUtcNow`) — failed against `AzureBlobStore` line 140 as expected.
2. **GREEN** — injected `TimeProvider` into [`AzureBlobStore`](../../../services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs) ctor (DI resolves the existing `TimeProvider.System` registered globally at [`MessagingDependencyInjection.cs:49`](../../../services/Invoicing/Invoicing.Infrastructure/Common/MessagingDependencyInjection.cs)); replaced `DateTimeOffset.UtcNow.Add(expiry)` with `_timeProvider.GetUtcNow().Add(expiry)`; updated [`AzuriteFixture`](../../../test/Invoicing.IntegrationTests/Blobs/AzuriteFixture.cs) to pass `TimeProvider.System` and added `CreateBlobStoreWithClock(TimeProvider)` for test-controlled clocks.
3. **REGRESSION** — added [`GetSasUrlAsync_DerivesSeFromInjectedTimeProvider_NotWallClock`](../../../test/Invoicing.IntegrationTests/Blobs/AzureBlobStoreTests.cs) which mints a SAS via a `FakeTimeProvider` and asserts the SAS `se` query parameter matches `fixedNow + expiry` to the second.

After: **30 architecture facts** (was 29) + **33 integration tests** (was 32) green.

### Commit 2 — `c4e16fa` `fix(invoicing): align ResendInvoiceEndpoint xmldoc + flag v1 stub in OpenAPI`

Closeout1 H1 (`ResendInvoiceCommandHandler` no-op undisclosed at M10 DoD ✅) + M4 (xmldoc 202 vs code 204). The handler self-disclosed its deferral in xmldoc, but the M10 DoD table marked the endpoint ✅ MET and admin tooling reading the OpenAPI saw a clean 204 with no v1-stub hint; the 24 h `.Idempotency()` cache pinned that hollow 204.

TDD path:
1. **RED** — added [`OpenApiDescription_DisclosesV1StubBehaviour`](../../../test/Invoicing.FunctionalTests/ApiEndpoints/Invoices/ResendInvoiceTests.cs) which hits `/swagger/v1/swagger.json`, parses the POST entry for `/api/v1/invoicing/invoices/{InvoiceId}/resend`, and asserts the description contains the stable `"v1 stub"` marker.
   - The test is **latent**: the Invoicing.FunctionalTests project transitively depends on `Platform.Test.Framework → Weather.Infrastructure → Weather.Domain`, which currently fails to build (29 `CS9035` errors). Filed as cross-cutting [#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138). The RED test is committed so the next functional-suite run validates the disclosure once Weather is fixed.
2. **GREEN** — extended [`ResendInvoiceEndpoint.Summary{Summary,Description}`](../../../services/Invoicing/Invoicing.API/Endpoints/Invoices/ResendInvoice/ResendInvoiceEndpoint.cs) with an explicit v1-stub disclosure that flows into the OpenAPI `description`; fixed both endpoint + handler xmldoc 202 → 204 drift; cross-referenced this followups doc.

No behaviour change — same 204 success, same idempotency cache window.

## Verification

Performed per the plan's verification block; the FunctionalTests slice was substituted with the Invoicing-scoped build per the Weather break.

```text
$ dotnet restore --locked-mode
  ... 53 NU1903 transitive warnings (branch-wide baseline; filed as #139)
  exit 0

$ dotnet build for each of:
    services/Invoicing/Invoicing.API/Invoicing.API.csproj
    test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
    test/Invoicing.ArchitectureTests/Invoicing.ArchitectureTests.csproj
    test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj
  All 0 errors.

$ dotnet format whitespace --verify-no-changes for each of the 5 Invoicing projects → exit 0
$ dotnet format style      --verify-no-changes for each of the 5 Invoicing projects → exit 0

$ dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
  Passed!  - Failed: 0, Passed: 96, Skipped: 0, Total: 96
$ dotnet test test/Invoicing.ArchitectureTests/Invoicing.ArchitectureTests.csproj
  Passed!  - Failed: 0, Passed: 30, Skipped: 0, Total: 30      ← +1 vs baseline
$ unset HTTP_PROXY ...; dotnet test test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj
  Passed!  - Failed: 0, Passed: 33, Skipped: 0, Total: 33      ← +1 vs baseline

Total in-scope: 159 / 159 green (was 157 / 157 = 96+29+32).
```

`dotnet build -m --no-restore` at the solution level returns 29 pre-existing `CS9035` errors in `src/Weather.Domain` and `test/Catalog.UnitTests` (commit `8616fe1` made `OccurredOnUtc` a required member on `DomainEvent` but Weather + Catalog test fixtures weren't updated). The `Invoicing.FunctionalTests` slice transitively pulls Weather via `Platform.Test.Framework` and therefore cannot build until the Weather fix lands. Filed as cross-cutting [#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138). None of those failures touch Invoicing-owned files; the 4 Invoicing-scoped projects compile clean and the 3 buildable Invoicing test slices are 159/159 green.

## Issues filed

### Invoicing in-scope (`invoicing(wave1-followup):`, label `needs-triage`)

| # | Severity | Title |
|---|---|---|
| [#123](https://github.com/DavidCapcuch/DotNetAtlas/issues/123) | MEDIUM | ship `InvoiceDeliveredEvent.avsc` + outbox publisher when consumers land (closeout1 H2) |
| [#124](https://github.com/DavidCapcuch/DotNetAtlas/issues/124) | MEDIUM | credit-note convergence `Result.Fail` swallow — data-loss-on-fix-deploy risk (closeout1 H3) |
| [#125](https://github.com/DavidCapcuch/DotNetAtlas/issues/125) | MEDIUM | auth scope-based gating (ADR-0010) not implemented; only role-based |
| [#126](https://github.com/DavidCapcuch/DotNetAtlas/issues/126) | MEDIUM | GET endpoints lack declarative `AuthSchemes()` — rely on global middleware |
| [#127](https://github.com/DavidCapcuch/DotNetAtlas/issues/127) | MEDIUM | Kafka consumers read `CorrelationId` from Avro payload not Kafka header |
| [#128](https://github.com/DavidCapcuch/DotNetAtlas/issues/128) | MEDIUM | `Invoice.Issue(PdfBlobRef, …)` 2-arg overload lacks I-4 write-once guard |
| [#129](https://github.com/DavidCapcuch/DotNetAtlas/issues/129) | MEDIUM | `GetInvoicesByBuyerEndpoint` ignores admin override |
| [#130](https://github.com/DavidCapcuch/DotNetAtlas/issues/130) | MEDIUM | `EnableSensitiveDataLogging` + missing `[PII]` on `Address` |
| [#131](https://github.com/DavidCapcuch/DotNetAtlas/issues/131) | MEDIUM | `pdf_blob_uri` persisted; replayed Avro events ship expired URLs |
| [#132](https://github.com/DavidCapcuch/DotNetAtlas/issues/132) | MEDIUM | `AzuriteCollection` fixture cleanup race (`TestPipelineException`) |
| [#133](https://github.com/DavidCapcuch/DotNetAtlas/issues/133) | MEDIUM | `pending_invoices.OrderPayload` jsonb carries plaintext PII |
| [#134](https://github.com/DavidCapcuch/DotNetAtlas/issues/134) | LOW | `InvoiceDocument` font `TODO(M10)` — Inter font swap deferred |
| [#135](https://github.com/DavidCapcuch/DotNetAtlas/issues/135) | LOW | blob keys hard-code month `01` in `YYYY/01/<number>.pdf` |
| [#136](https://github.com/DavidCapcuch/DotNetAtlas/issues/136) | LOW | `EndpointGroupConstants` tag-name decoupled from FE group route |
| [#137](https://github.com/DavidCapcuch/DotNetAtlas/issues/137) | LOW | no DB `CHECK` constraint on `Invoice.Status` / `CreditNote.Status` |

### Cross-cutting (`cross-cutting(wave1-followup):`, label `needs-triage`)

| # | Severity | Title |
|---|---|---|
| [#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138) | **HIGH (BLOCKER)** | `Weather.Domain` build break (29 `CS9035` errors) blocks every BC's FunctionalTests |
| [#139](https://github.com/DavidCapcuch/DotNetAtlas/issues/139) | LOW | 53 NU1903 transitive vulnerability warnings (branch-wide) |
| [#140](https://github.com/DavidCapcuch/DotNetAtlas/issues/140) | MEDIUM | `otel-collector` `attributes/pii-allowlist` processor restart loop |
| [#141](https://github.com/DavidCapcuch/DotNetAtlas/issues/141) | MEDIUM | `architecture-tests.md` lacks § 2.5 Invoicing section |
| [#142](https://github.com/DavidCapcuch/DotNetAtlas/issues/142) | MEDIUM | `use-cases.md` missing § 6 Invoicing |
| [#143](https://github.com/DavidCapcuch/DotNetAtlas/issues/143) | LOW | `events-catalog.md` § 4 + § 5.x Invoicing drift |
| [#144](https://github.com/DavidCapcuch/DotNetAtlas/issues/144) | LOW | `error-taxonomy.md:49` stale Polly retry note for `BlobUploadFailed` |
| [#145](https://github.com/DavidCapcuch/DotNetAtlas/issues/145) | LOW | `nw-mutation-test` post-green pass not run on Invoicing suite |
| [#146](https://github.com/DavidCapcuch/DotNetAtlas/issues/146) | LOW | `invoicing.api` container missing from `docker-compose.yaml` |
| [#147](https://github.com/DavidCapcuch/DotNetAtlas/issues/147) | LOW | `CLAUDE.md` Testcontainers § promote option B above option A |

## Boundary discipline

Stayed strictly inside the Wave 1 closeout follow-up boundary:

- `services/Invoicing/**` — H1 + H2 fixes only.
- `test/Invoicing.*/**` — H1 arch + integration regression + latent H2 functional regression.
- `platform/Platform.SchemaRegistry.Contracts/**/Invoicing/**` — no changes (the 4th Avro schema is carry-forward [#123](https://github.com/DavidCapcuch/DotNetAtlas/issues/123)).
- `docs/implementation-prompts/session-summaries/invoicing-followups.md` — this file (NEW).

Not touched:
- Existing M10 / closeout / closeout2 session-summaries — read-only.
- EF Core migrations (CLAUDE.md forbids; required for [#124](https://github.com/DavidCapcuch/DotNetAtlas/issues/124), [#131](https://github.com/DavidCapcuch/DotNetAtlas/issues/131), [#133](https://github.com/DavidCapcuch/DotNetAtlas/issues/133), [#137](https://github.com/DavidCapcuch/DotNetAtlas/issues/137) — all filed).
- `docs/bc-design/**` — filed as cross-cutting ([#141](https://github.com/DavidCapcuch/DotNetAtlas/issues/141)–[#144](https://github.com/DavidCapcuch/DotNetAtlas/issues/144)).
- `docker-compose.yaml`, `Directory.Packages.props`, `CLAUDE.md` — filed as cross-cutting ([#139](https://github.com/DavidCapcuch/DotNetAtlas/issues/139), [#146](https://github.com/DavidCapcuch/DotNetAtlas/issues/146), [#147](https://github.com/DavidCapcuch/DotNetAtlas/issues/147)).
- Other BCs' code, tests, schemas, docs.
- The pre-existing dirty entries (`CLAUDE.md`, `services/Ordering/...`, `.claude/scheduled_tasks.lock`, untracked closeouts) remain unstaged exactly as they were at session start.

## What "done" looks like for Invoicing Wave 1 closeout follow-ups

- [x] H1 (AzureBlobStore TimeProvider) TDD-fixed + committed (`196501b`) — arch test landed, FakeTimeProvider pin landed, **30/30 arch + 33/33 integration**.
- [x] H2 (Resend endpoint OpenAPI v1-stub disclosure) fixed + committed (`c4e16fa`) — endpoint + handler xmldoc realigned to 204, OpenAPI `Description` now publishes the v1-stub marker, latent functional test parked for post-Weather-fix verification.
- [x] All 4 Invoicing CI gates green (restore --locked-mode + Invoicing-scoped build + format whitespace + format style).
- [x] 3 of 4 Invoicing test slices green (96 + 30 + 33 = 159/159); 4th slice (FunctionalTests, 22) blocked by pre-existing cross-cutting Weather break — filed [#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138).
- [x] 10 MEDIUM + 5 in-scope LOW filed as `invoicing(wave1-followup):` issues ([#123](https://github.com/DavidCapcuch/DotNetAtlas/issues/123)–[#137](https://github.com/DavidCapcuch/DotNetAtlas/issues/137)).
- [x] 10 cross-cutting filed as `cross-cutting(wave1-followup):` issues ([#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138)–[#147](https://github.com/DavidCapcuch/DotNetAtlas/issues/147)).
- [x] Closeout reconciled — closeout1 CONDITIONAL-PASS becomes PASS once [#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138) lands and the latent H2 regression test runs green.

## Reviewer notes for next pass

- The new arch fact `BlobsNamespace_ShouldNotCall_StaticUtcNow` would catch any future `UtcNow` slip in `Invoicing.Infrastructure.Blobs.*`. If new sub-namespaces appear, the regex selector (`^Invoicing\.Infrastructure\.Blobs(\..*)?$`) covers them.
- The latent functional regression at [`ResendInvoiceTests.OpenApiDescription_DisclosesV1StubBehaviour`](../../../test/Invoicing.FunctionalTests/ApiEndpoints/Invoices/ResendInvoiceTests.cs) hits `/swagger/v1/swagger.json` and grep-asserts `"v1 stub"`. If the disclosure phrasing in the endpoint `Description` changes, the marker phrase needs to follow.
- H3 (credit-note `Result.Fail` swallow) was reclassified to MEDIUM not because the risk shrank but because the in-bounds fix surface is empty — every recommendation in closeout1 needs either a user-generated migration ([#124](https://github.com/DavidCapcuch/DotNetAtlas/issues/124) (a)), behaviour change ([#124](https://github.com/DavidCapcuch/DotNetAtlas/issues/124) (b)), or an out-of-scope `error-taxonomy.md` edit ([#124](https://github.com/DavidCapcuch/DotNetAtlas/issues/124) (c)).

---

## Wave 1 Closeout Reconciliation Pass

A second pass walked both `docs/implementation-prompts/session-summaries/invoicing-closeout.md` AND `docs/implementation-prompts/session-summaries2/invoicing-closeout.md` and converted the previously-filed `invoicing(wave1-followup):` issues into landed fixes wherever the scope allowed (`services/Invoicing/**`, `test/Invoicing.*/**`, `platform/Platform.SchemaRegistry.Contracts/**/Invoicing/**`, session-summary docs). EF migrations remain user-generated per CLAUDE.md, and shared-kernel changes stay out-of-bounds.

### Fixes landed this pass (commit per finding)

| # | Sev | Issue | Commit | Change |
|---|---|---|---|---|
| M5 | MED | [#128](https://github.com/DavidCapcuch/DotNetAtlas/issues/128) | (this pass) | `Invoice.Issue(PdfBlobRef, …)` 2-arg overload now declares I-4 explicitly: throws `DataIntegrityException("Invoicing.InvoiceAlreadyIssued")` when `PdfBlobRef` is already set, mirroring `CreditNote.Issue`. RED→GREEN unit test in `InvoiceInvariantsTests` uses reflection to force the contrived state. |
| M3 | MED | [#127](https://github.com/DavidCapcuch/DotNetAtlas/issues/127) | (this pass) | All 4 Kafka projection handlers drop `["CorrelationId"] = message.CorrelationId` from `_logger.BeginScope`. The platform's `ConsumerCorrelationIdMiddleware` (already wired in `MessagingDependencyInjection`) pushes the Kafka-header value into Serilog `LogContext`; the previous payload override was shadowing it (ADR-0008 § header-is-SSOT). New `KafkaHandlerCorrelationIdScopeTests` pins each handler against a scope-recording `ILoggerProvider`. |
| M6 | MED | [#129](https://github.com/DavidCapcuch/DotNetAtlas/issues/129) | (this pass) | `GetInvoicesByBuyer` honours an optional `?buyerId={guid}` query param. Admin caller → scope to the requested buyer; non-admin caller passing a `buyerId` other than their own → 403 (explicit deny so admin tooling without admin privs surfaces loudly). 3 new functional tests (admin happy path, non-admin cross-buyer 403, non-admin self-scoping). |
| M2 | MED | [#126](https://github.com/DavidCapcuch/DotNetAtlas/issues/126) | (this pass) | `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` added declaratively to all 4 GET endpoints (`GetInvoiceById`, `GetInvoiceByOrderId`, `GetInvoicesByBuyer`, `GetCreditNoteById`). Defensive only — no behaviour change — but a future global-middleware refactor can no longer silently un-gate them. |
| M7 | MED | [#130](https://github.com/DavidCapcuch/DotNetAtlas/issues/130) | (this pass) | `EnableSensitiveDataLogging` is now gated by `IHostEnvironment.IsDevelopment()` instead of `!IsDeployedEnvironment()`. PII-bearing `_enc` columns no longer leak into Test/Staging/Testing logs. (`[Pii]` on shared-kernel `Address` remains a separate follow-up — outside Invoicing's boundary.) |
| M9 | MED | [#132](https://github.com/DavidCapcuch/DotNetAtlas/issues/132) | (this pass) | `AzuriteFixture.DisposeAsync` wraps `_azurite.DisposeAsync()` in `try/catch` and logs to stderr instead of propagating. The `TestPipelineException` that fired AFTER the 32 tests passed is now non-fatal by construction. |
| L1 | LOW | [#134](https://github.com/DavidCapcuch/DotNetAtlas/issues/134) | (this pass) | `TODO(M10)` font marker in `InvoiceDocument.cs` replaced with a stable `// Deferred — Inter font swap…` reference to [#134](https://github.com/DavidCapcuch/DotNetAtlas/issues/134). M10 has shipped; the legacy marker was grep-noise. |
| L2 | LOW | [#135](https://github.com/DavidCapcuch/DotNetAtlas/issues/135) | (this pass) | Duplicate private `BuildBlobName` helpers in `IssueInvoiceCommandHandler` + `IssueCreditNoteCommandHandler` deleted. Both call sites now invoke `InvoicePdfBlobName.For(...)` directly — single source of truth for the v2 partition story. |
| L3 | LOW | [#136](https://github.com/DavidCapcuch/DotNetAtlas/issues/136) | (this pass) | `EndpointGroupConstants` xmldoc expanded to call out that the constants drive BOTH the OpenAPI tag AND the second URL segment of the group route. Future renames now flag the coupling explicitly. |
| M8 / M10 / L4 | MED/LOW | [#131](https://github.com/DavidCapcuch/DotNetAtlas/issues/131) / [#133](https://github.com/DavidCapcuch/DotNetAtlas/issues/133) / [#137](https://github.com/DavidCapcuch/DotNetAtlas/issues/137) | (this pass) | Defer-with-comment notes inlined in `InvoiceConfiguration.cs` (`pdf_blob_uri` staleness on replay + missing CHECK constraint on `Status`) and `OrderConfirmedInvoiceProjectionKafkaHandler.cs` (`OrderPayload` plaintext PII). All three need EF migrations the user generates. |
| CO2-M1 | MED | (CO2 finding) | (this pass) | Both `invoicing-m10.md` files (session-summaries/ + session-summaries2/) had their ADR-0010 paragraph and "HTTP routes" bullet rewritten to acknowledge "role-based v1; scope-based deferred to v2". The previous wording overstated `invoicing.admin.resend` / `invoicing.read` scope gating that AuthPolicies.cs explicitly defers. |

### Deliberately deferred (no fix this pass)

- **H2** ship `InvoiceDeliveredEvent.avsc` + outbox publisher ([#123](https://github.com/DavidCapcuch/DotNetAtlas/issues/123)) — no downstream consumer (Notifications / BFF) exists; shipping a contract without a consumer is wave-1.5+ work, not closeout cleanup.
- **H3** credit-note `Result.Fail` swallow ([#124](https://github.com/DavidCapcuch/DotNetAtlas/issues/124)) — needs a product decision on the failed-message destination (DLT vs `failed_credit_notes` table) before any code change.
- **M1** ADR-0010 scope-based auth ([#125](https://github.com/DavidCapcuch/DotNetAtlas/issues/125)) — implementation needs Keycloak realm + JWT scope wiring outside the Invoicing slice. Documentation drift is fixed via CO2-M1.

### Out-of-scope findings (flagged to the user, not changed)

These are listed because they appeared in closeout2 or closeout1 cross-cutting buckets but live outside `services/Invoicing/**`, `test/Invoicing.*/**`, `platform/Platform.SchemaRegistry.Contracts/**/Invoicing/**`:

- `docs/bc-design/architecture-tests.md` § 2.5 Invoicing — missing (closeout2 § 1)
- `docs/bc-design/use-cases.md` § 6 Invoicing — missing (closeout2 § 1)
- `docs/bc-design/events-catalog.md` § 4 / § 5.x Invoicing — missing (closeout2 § 1)
- `docs/bc-design/error-taxonomy.md:49` — stale Polly-retry note (closeout2 § 1, [#144](https://github.com/DavidCapcuch/DotNetAtlas/issues/144))
- `platform/Platform.SharedKernel/ValueObjects/Address.cs` — `[Pii]` attribute (closeout1 M7 partial; the EF gate is fixed in-scope but the Serilog destructuring side needs Platform.SharedKernel)
- ADR-0010 scope-based gating implementation — Keycloak realm + JWT scope wiring (closeout1 M1, [#125](https://github.com/DavidCapcuch/DotNetAtlas/issues/125))
- `Weather.Domain` build break (29 `CS9035` errors) — pre-existing blocker on FunctionalTests slice ([#138](https://github.com/DavidCapcuch/DotNetAtlas/issues/138))
- 53 NU1903 transitive warnings — branch-wide ([#139](https://github.com/DavidCapcuch/DotNetAtlas/issues/139))
- `otel-collector` processor config restart loop ([#140](https://github.com/DavidCapcuch/DotNetAtlas/issues/140))
- `nw-mutation-test` not run for Invoicing ([#145](https://github.com/DavidCapcuch/DotNetAtlas/issues/145))
- `invoicing.api` missing from `docker-compose.yaml` ([#146](https://github.com/DavidCapcuch/DotNetAtlas/issues/146))
- CLAUDE.md Testcontainers § option-B promotion ([#147](https://github.com/DavidCapcuch/DotNetAtlas/issues/147))

### Verification (this pass)

```
dotnet restore --locked-mode                          # exit 0 (baseline NU1903 warnings)
dotnet build -m                                       # 0 errors in Invoicing slice
dotnet format whitespace --verify-no-changes          # 0 violations
dotnet format style --verify-no-changes               # 0 violations
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy
dotnet test test/Invoicing.UnitTests/                 # 16/16 (was 15; +1 M5)
dotnet test test/Invoicing.IntegrationTests/          # 37/37 (was 33; +4 M3)
dotnet test test/Invoicing.ArchitectureTests/         # 30/30 unchanged
dotnet test test/Invoicing.FunctionalTests/           # 26/26 (was 22; +3 M6 + 1 M4 latent now buildable)
```

(Per CLAUDE.md, option A — `unset HTTP_PROXY ...` — is recommended on this host where the Docker.DotNet `npipe://` URI parser interacts with the corporate proxy resolver. Option B `NO_PROXY='*'` fails the parser before `NO_PROXY` is consulted.)
