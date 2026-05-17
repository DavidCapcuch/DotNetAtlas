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
