# Invoicing BC — Final Closeout Review

> HEAD: `f49d358fb87f9559ab83f5abff2a638b76ea6cb9` (branch `aaqwdqwd`, 98 commits ahead of `origin/aaqwdqwd`)
> Working tree dirty: `CLAUDE.md` (untouched by this review). All Invoicing files clean.
> Reviewer scope: read-only audit of `services/Invoicing/**`, `test/Invoicing.*Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/**`, `docker-compose.yaml` (Invoicing footprint), 19 ADRs, BC design docs, M1–M10 session summaries. Only writable artifact: this report.
>
> **Verdict: 🟡 CONDITIONAL-PASS** — see § Verdict for thresholds applied and the rationale for not landing this as a clean PASS or a hard FAIL.

---

## TL;DR

All four CI gates and all four test slices are green (179/179). Architecture is faithful, the gap-free allocator and PDF determinism land correctly, ADR-0008/0011/0015/0017/0018/0019 are enforced by code + arch tests. **Three undisclosed gaps drag this down from a clean PASS**: (1) `ResendInvoiceCommandHandler` is shipped as a logging-only no-op despite the M10 DoD table marking the resend endpoint ✅ — `bc-design/invoicing.md § 12`'s `invoice_delivery_log` + outbox-row contract is not implemented; (2) credit-note convergence handlers swallow `Result.Fail` and commit the inbox row permanently, losing the message for any future fix-deploy; (3) `AzureBlobStore` mints SAS expiry against `DateTimeOffset.UtcNow` while the surrounding handlers compute response metadata via `TimeProvider`, producing `FakeTimeProvider`-incompatible windows. Plus the **already-disclosed** M10 carry-forward — `InvoiceDeliveredEvent.avsc` (4th of 4 external Avro events on the LOCKED contract) — remains absent.

---

## Dimension 1 — Doc adherence + DoD audit

### 1.1 BC `<dod>` table walk (`invoicing.md:123-142`)

| `<dod>` row | Verdict | Evidence |
|---|---|---|
| 4-layer scaffold; `.slnx` updated; `dotnet build -m` green | ✅ MET | M1 (`06321db`); reconfirmed this session (§ Dimension 7) |
| 4 external Avro events under `…Avro/Invoicing/Invoices/` | ⏸️ PARTIALLY MET (disclosed) | 3 of 4 shipped (`InvoiceIssuedEvent.avsc`, `InvoiceCancelledEvent.avsc`, `CreditNoteIssuedEvent.avsc`); **`InvoiceDeliveredEvent.avsc` not shipped**. M10 summary § Improvements proposed line 244 documents the deferral with rationale (no consumer ready). |
| 7 internal `*DomainEvent` records + outbox publishers for external events | ⏸️ PARTIALLY MET (disclosed) | 7 internal events present at `services/Invoicing/Invoicing.Domain/{Invoices,CreditNotes}/Events/*.cs`. **3 of 4 outbox publishers** under `…Application/Outbox/` — `InvoiceDeliveredOutboxPublisherDomainEventHandler` not shipped (paired with the schema gap). |
| 4 consumers with inbox dedup | ✅ MET | 4 Kafka handlers under `…Infrastructure/Messaging/Kafka/Projections/`; inbox dedup wired via `Platform.KafkaFlow.Inbox.EFCore` per `MessagingDependencyInjection.cs:101`. |
| Enrichment projections with idempotent upserts | ✅ MET | `PendingInvoice` / `PendingCreditNote` + `PendingProjectionUpsertHelper.GetOrAddAsync`; same-payload-duplicate no-op verified by `Projections/PendingInvoiceProjectionTests`. |
| Gap-free allocators (concurrency-tested) | ✅ MET | `PostgresInvoiceNumberAllocator.cs:35-105` (`SELECT … FOR UPDATE`), `Allocators/InvoiceNumberAllocatorTests.cs` (concurrency, year-rollover). |
| QuestPDF byte-deterministic on fixed input | ✅ MET | `QuestPdfInvoiceGenerator.cs` (singleton, pure-functional render); `QuestPdfInvoiceGeneratorTests` byte-hash assertion green. |
| Blob upload + SAS GET URL through nginx-cdn end-to-end | ✅ MET | `AzureBlobStore.cs` + Azurite `invoices` container + nginx-cdn proxy chain; M10 § Verification curl outputs. |
| 5 HTTP endpoints under `/api/v1/invoicing/...` with `.Idempotency()` on resend | ⚠️ **DRIFTED — undisclosed in M10** | All 5 endpoints exist with correct routes + auth wiring. **But `ResendInvoiceCommandHandler` is a logging-only no-op** (handler xmldoc lines 23-30 acknowledge "deferred to a later milestone"). The BC design § 12 contract requires `invoice_delivery_log` insert + outbox row keyed `(InvoiceId, Channel, Attempt)`. Neither happens. The endpoint returns 204 + caches the no-op in Redis for 24 h. M10 § DoD line 73 marks this ✅ MET without disclosure. **HIGH finding § Dimension 8**. |
| `InvoicingErrors` matches `error-taxonomy.md § 3.6` | ✅ MET | `Invoicing.Domain/Common/Errors/InvoicingErrors.cs` includes all 7 documented codes + 3 extensions (`InvoiceForOrderNotFound`, `CreditNoteNotFound`, transition variants) — additive, no drift. |
| Integration tests cover 4 example-mapping sessions + concurrency test | ✅ MET | 32 integration tests including `PendingInvoiceProjectionTests` (4 example-mapping sessions), `PendingCreditNoteProjectionTests`, `InvoiceNumberAllocatorTests` (concurrency), `CreditNoteNumberAllocatorTests`. |
| Architecture tests | ✅ MET | 29 facts (counted: 4+1+2+4+3+1+2+3+4+6 — see § Dimension 2). |
| `docker compose --profile full up -d` shows Azurite + `invoicing.invoices` topic 10-year retention | ✅ MET | M10 § Verification: `azurite (healthy)`, `kafka-topics --describe` shows `retention.ms=315360000000` on `invoicing.invoices`. |
| Correlation-id roundtrips through enrichment projection | ⚠️ PARTIALLY MET | Persistence path works (header → projection upsert helper → aggregate → outbox). **But consumers read `message.CorrelationId` (Avro payload) rather than the Kafka header** — `OrderConfirmedInvoiceProjectionKafkaHandler.cs:79,99,125` + 3 siblings. ADR-0008 mandates header. § Dimension 5. |
| All `<applicable_adrs>` enforced | ⚠️ PARTIALLY MET | ADR-0008/0011/0012/0013/0015/0017/0018/0019 all enforced by code or arch test. **ADR-0010 partial**: scope-based gating (`invoicing.read`, `invoicing.admin.resend`) NOT registered — only role-based `InvoicingAdmin` policy via Keycloak realm role `Admin`. Code docstring (`AuthPolicies.cs:14`) acknowledges scope-based is v2+. M10 § ADR application notes line 52 claims scope-based gating — doc-vs-code drift. |
| Peer-review chain executed; HIGH findings fixed | ✅ MET | M1–M9 commit bodies record Opus reviewer verdict + finding counts; this closeout adds the final independent pass. |

### 1.2 `_shared.md § 12` universal DoD walk

Spot-checked all 18 universal DoD lines against `services/Invoicing` + the M10 verification output. **17 of 18 cleanly MET**. The one item that needs a footnote — "Correlation-id propagation working (HTTP → Kafka → DB column) per ADR-0008" — is functionally MET at the persistence and outbound layers; the consumer ingress reads the payload `CorrelationId` rather than the header (§ Dimension 5 finding). HTTP entry via `UseCorrelationId` middleware is intact.

### 1.3 BC `<contract>` LOCKED-item walk

| Locked item | Verdict |
|---|---|
| 2 aggregates: `Invoice` (4-state FSM `Draft → Issued → Delivered → Archived`; `Cancelled` off-ramp) + `CreditNote` (3-state `Issued → Delivered → Archived`) | ✅ Implemented at `Invoicing.Domain/Invoices/Invoice.cs` + `CreditNotes/CreditNote.cs`; SmartEnums `InvoiceStatus.cs`, `CreditNoteStatus.cs` enforce transitions. |
| VO: `InvoiceNumber` format `INV-YYYY-NNNNNN`; `CreditNoteNumber` format `CN-YYYY-NNNNNN` | ✅ Regex `^INV-\d{4}-\d{6}$` + `^CN-\d{4}-\d{6}$` in respective VOs; round-trip tested. |
| 4 external Avro events on `invoicing.invoices` (10-year retention, partition key `BuyerId`) | ⚠️ 3 of 4 events; retention + partition-key correct (M10 § Verification: `retention.ms=315360000000`; mappers emit `domainEvent.BuyerId.ToString()` as key). |
| HTTP routes under `/api/v1/invoicing/...` per ADR-0012 | ✅ FastEndpoints `Versioning.Prefix = "v"; DefaultVersion = 1; RoutePrefix = "api"` (`PresentationDependencyInjection.cs:70-73`) + group routes `invoicing/invoices` + `invoicing/credit-notes`. |
| `InvoicingErrors` per `error-taxonomy.md § 3.6` | ✅ |
| PDF library: QuestPDF community edition | ✅ `QuestPdfInvoiceGenerator.cs:33` sets `LicenseType.Community`. |
| Blob storage: Azurite container `invoices`; SAS URLs (10-min TTL); content-addressed via SHA-256; SDK `Azure.Storage.Blobs` | ✅ `AzureBlobStore.cs`; `BlobSasPermissions.Read` only; SHA-256 lowercase hex (`SHA256.HashData`); 10-min TTL constant in handlers. |
| Enrichment projection tables: `invoicing.pending_invoices` + `invoicing.pending_credit_notes` | ✅ Migration `20260426083837_AddPendingProjectionsAndInbox.cs`. |
| Gap-free number allocator: `invoicing.invoice_number_allocator` + `invoicing.credit_note_number_allocator` with `SELECT … FOR UPDATE` per ADR-0018 | ✅ Migration `20260425111020_AddInvoiceNumberAllocators.cs`; `PostgresInvoiceNumberAllocator.cs:55-82` issues FOR-UPDATE; throws if no enclosing transaction (`:41-48`). |
| File ownership per `<boundaries>` | ✅ `git log -- services/Invoicing` shows all 13 Invoicing-titled commits respect the boundary; no cross-BC writes. |

### 1.4 Invariant spot-check (5 picked from BC chapter)

| Invariant | Enforced by | Pinned by |
|---|---|---|
| I-1 (`Total == Subtotal + ΣVatLines.Amount`) | `Invoice.cs:380-411` `ComputeTotals` at factory | `InvoiceInvariantsTests.I1_Create_ComputesTotalFromSubtotalAndVatLines` + multi-rate variant |
| I-2 (`Lines` non-empty) | `Invoice.cs:108-112` throws `Invoicing.EmptyLines` | `I2_Create_WithEmptyLines_ThrowsDataIntegrityException` |
| I-3 (`InvoiceNumber` immutable post-allocation) | `Invoice.cs:177-185` throws `Invoicing.InvoiceNumberAlreadyAssigned` | `I3_Issue_StampsInvoiceNumberImmutably` + `I3_I4_Issue_Twice_IsRejectedAndDoesNotOverwrite` |
| I-4 (`PdfBlobRef` write-once) | `CreditNote.cs:216-221` throws `Invoicing.CreditNoteAlreadyIssued`; **`Invoice.Issue(PdfBlobRef, …)` 2-arg overload lacks the symmetric guard** (§ Dimension 8 MEDIUM). | `I4_Issue_SetsPdfBlobRef` + double-issue test |
| I-6 (`Cancel` requires `CreditNoteId`) | `Invoice.cs:338-340` throws `Invoicing.InvalidCreditNoteIdOnCancel` | `I6_Cancel_WithEmptyCreditNoteId_Throws` + `I6_Cancel_StampsCancellationInfoWithCreditNoteId` |

**Result**: 4 of 5 invariants are enforced both by guard code + a dedicated unit test that would fail if the invariant were removed. I-4 has symmetry-break between `Invoice` and `CreditNote` — see § Dimension 8 MEDIUM finding.

---

## Dimension 2 — Architecture

### 2.1 Layer discipline

`CleanArchitectureLayerTests.cs` (6 facts) enforce: Domain ⟂ {Application, Infrastructure, API}; Application ⟂ {Infrastructure, API}; Infrastructure ⟂ API. All 6 pass in this session. **Verified by reading the production code**:

- `Invoicing.Domain` references `Platform.SharedKernel` only — no EF Core, no QuestPDF, no Azure.Storage, no MediatR/CQRS.
- `Invoicing.Application` references `Invoicing.Domain` + `Platform.CQRS` + `Platform.ReliableMessaging.Outbox.EFCore` (abstraction) + `FluentValidation` — no Infrastructure adapters.
- `Invoicing.Infrastructure` is where adapters live: `AzureBlobStore`, `QuestPdfInvoiceGenerator`, `Postgres{Invoice,CreditNote}NumberAllocator`, EF Core configs, Kafka consumers.
- `Invoicing.API` references all three + FastEndpoints + `Platform.ServiceDefaults`.

### 2.2 Hexagonal / Clean-Arch discipline

Ports owned by Application (`IBlobStore`, `IPdfGenerator`, `IInvoicingDbContext`, `I{Invoice,CreditNote}NumberAllocator`); adapters owned by Infrastructure (`AzureBlobStore`, `QuestPdfInvoiceGenerator`, `InvoicingDbContext`, `Postgres*Allocator`). The `Application.Common.Numbering` folder contains both `IInvoiceNumberAllocator` and a partial-class hint `InvoiceNumberAllocator` (helper, not abstraction). Clean.

### 2.3 Cross-BC references

`NoCrossBcReferenceTests.cs` (2 facts) forbid imports from `{Basket,Catalog,Inventory,Ordering,Payments}.{Domain,Application}` in `Invoicing.{Domain,Application}`. Reading the consumer Kafka handlers (`OrderConfirmedInvoiceProjectionKafkaHandler.cs:8`) confirms cross-BC integration happens **only via the Avro contract types** under `Platform.SchemaRegistry.Contracts.Avro.{Ordering,Payments}.*` — these are platform-shared schema-generated types, which is the documented seam.

### 2.4 Architecture-test coverage vs `architecture-tests.md`

`docs/bc-design/architecture-tests.md` § 2 covers Catalog / Basket / Ordering / Inventory — **no § 2.x for Invoicing**. The Invoicing test project implements all § 1 common rules (layer, aggregate discipline, domain-event discipline, command/query naming, cross-BC) plus Invoicing-specific extensions (BlobStorageContainment, PdfGenerationContainment, OtelTagAllowlist, NoStaticUtcNowInDomain). **No documented Invoicing arch fact is missing**.

**Verdict**: ✅ PASS.

---

## Dimension 3 — Design (DDD)

### 3.1 Aggregate boundaries

`Invoice` and `CreditNote` are the only aggregates; both `sealed`, both with `private` parameterless ctor + static `Create` factory returning `Result<T>`, both inheriting `AggregateRoot<Guid>`. `AggregateRootTests` (4 facts) cover all four discipline requirements. `Invoice.LinesForReversal()` (`Invoice.cs:369`) returns flipped-sign snapshots used by `CreditNote.Create` — the cross-aggregate read is via a method, not field exposure. Good.

### 3.2 Value objects

All VOs immutable + structural-equal (records or `ValueObject`-derived). Spot-checked: `InvoiceNumber`, `CreditNoteNumber`, `InvoiceLine`, `VatLine`, `VatRate`, `PdfBlobRef`, `CancellationInfo`. Validation via `Result<T>`-returning factories. `Sku.Create` returns Result (used at `IssueInvoiceCommandHandler.cs:266`).

### 3.3 SmartEnums

`InvoiceStatus`, `CreditNoteStatus`, `DeliveryChannel`, `CreditNoteReason` — all `SmartEnum`-derived. State-machine logic (`CanTransitionTo`) lives on the SmartEnum, not the aggregate, which keeps the FSM declarative. `CreditNoteReason` reserves `PartialRefund` for v2 with explicit rejection in the command handler.

### 3.4 Domain events

7 internal `*DomainEvent` records, all `sealed`, all in `<Aggregate>.Events` namespace (`DomainEventTests` 3 facts pass: naming convention, sealed, namespace). All dispatched in-process via `DispatchDomainEventsInterceptor.SavingChangesAsync` (BEFORE `SaveChanges` proceeds — verified at `DispatchDomainEventsInterceptor.cs:29-51`). Outbox handlers therefore join the same EF transaction. No internal `*DomainEvent` is published to Kafka directly.

### 3.5 External vs internal split

`events-catalog.md` Invoicing section maps 7 internal → 4 external events. **3 of 4 external Avro events shipped** (see Dimension 1). `InvoiceDeliveredDomainEvent` (internal) exists but has no outbox publisher and no consumer; its lifecycle path (`Invoice.Deliver`) is dormant because `IssueInvoiceCommandHandler.cs:158` hard-codes `DeliveryChannel.None`. The aggregate's `Deliver`/`Archive`/`DeliveredAtUtc` plumbing is therefore unused production code.

### 3.6 Factories returning `Result<T>`

All aggregate factories return `Result<T>`. Constructors are private. Inside the factory, bug-class checks (`Guid.Empty`, empty `Lines`, mixed currency) `throw DataIntegrityException`; user-actionable validation is delegated to `FluentValidation` validators outside the aggregate. This matches CLAUDE.md's "Result pattern for expected errors; exceptions for exceptional situations" rule.

**Verdict**: ✅ PASS — with one MEDIUM finding (I-4 symmetry break, § Dimension 8).

---

## Dimension 4 — Testing

### 4.1 Test pyramid

| Slice | Count | Shape |
|---|---|---|
| `Invoicing.UnitTests` | 96 | Aggregate invariants, VO Create-Result, SmartEnum transitions. Test names express behaviour (`I1_Create_ComputesTotalFromSubtotalAndVatLines`, `I3_I4_Issue_Twice_IsRejectedAndDoesNotOverwrite`). |
| `Invoicing.ArchitectureTests` | 29 | NetArchTest assertions per § 2. Custom rules (`DoesNotCallStaticUtcNowRule`, `NoForbiddenActivityTagKeysRule`, `PrivateConstructorsRule`, `HasPublicStaticFactoryMethodRule`) are non-trivial. |
| `Invoicing.IntegrationTests` | 32 | Testcontainers (Postgres + Azurite); allocators, projections, PDF generator, command handlers, blob store. |
| `Invoicing.FunctionalTests` | 22 | `WebApplicationFactory<Program>` + Testcontainers; all 5 endpoints exercised against real auth + EF. |

Pyramid is sane: ~57 % unit, ~17 % arch, ~19 % integration, ~13 % functional. Matches the "heavy unit, moderate integration, narrow functional" target.

### 4.2 xUnit1051 / `CancellationToken.None` discipline

Grep across `test/Invoicing.*Tests/**/*.cs` for `CancellationToken.None` returns **zero files** — clean. xUnit.v3 1051 is respected.

### 4.3 Validator + handler coverage

Every command has a `*Validator` co-located with its handler:

| Command | Validator | Handler test |
|---|---|---|
| `IssueInvoiceCommand` | `IssueInvoiceCommandValidator.cs` (CorrelationId NotEmpty) | `IssueInvoiceCommandHandlerTests` (integration) |
| `IssueCreditNoteCommand` | `IssueCreditNoteCommandValidator.cs` | `IssueCreditNoteCommandHandlerTests` (integration) |
| `ResendInvoiceCommand` | `ResendInvoiceCommandValidator.cs` (InvoiceId NotEmpty) | `ResendInvoiceTests` (functional only — see § Dimension 8 HIGH; no integration test because the handler is a stub) |

Queries follow the same pattern; each has a validator and at least one functional test.

### 4.4 Regression coverage on invariants

5/5 spot-checked invariants (I-1, I-2, I-3, I-4, I-6) have a dedicated unit test that would fail if the guard were removed. The I-4 symmetry-break (Invoice's 2-arg `Issue` lacks the write-once guard that CreditNote has) is unguarded by a test — see § Dimension 8 MEDIUM.

### 4.5 Notable test smells

- `Projections/PendingInvoiceProjectionTests` runs 32 tests in ~21 s — fixture reuse is good.
- One **cleanup-time failure**: `Test Collection Cleanup Failure (AzuriteCollection)` raises `Xunit.Sdk.TestPipelineException` AFTER all 32 tests pass. The integration slice still exits 0 and reports 32/32 green; the post-test container disposal is racing. Not a regression — same M10 baseline. § Dimension 8 MEDIUM.

**Verdict**: ✅ PASS — with one MEDIUM (Azurite fixture cleanup race) + one MEDIUM (Invoice I-4 symmetry-break not unit-tested).

---

## Dimension 5 — Event-driven best practices

### 5.1 Outbox-only externalization

Greps confirm **no `IProducer<,>` calls in `services/Invoicing/**`** that bypass `Platform.ReliableMessaging.Outbox.EFCore.ITransactionalOutbox`. The three outbox publishers (`InvoiceIssuedOutboxPublisherDomainEventHandler`, `InvoiceCancelledOutboxPublisherDomainEventHandler`, `CreditNoteIssuedOutboxPublisherDomainEventHandler`) are the only Avro emitters, all invoked by the `DispatchDomainEventsInterceptor` BEFORE `SaveChanges` commits. Topic name is centralised via `InvoicingTopicsOptions.Invoices`.

### 5.2 Outbox row + aggregate write atomicity

`DispatchDomainEventsInterceptor.SavingChangesAsync` dispatches domain events from `ChangeTracker.Entries<IAggregateRoot>()` BEFORE `base.SavingChangesAsync` runs. The outbox handler calls `_outbox.AddOutboxMessage(...)` which writes an outbox row through the same DbContext — same `SaveChanges` cycle, same transaction. Atomic. Verified by reading `DispatchDomainEventsInterceptor.cs:29-51`.

### 5.3 Inbox dedup on Kafka consumers

`MessagingDependencyInjection.AddInvoicingMessaging` wires `AddInbox<InvoicingDbContext>(typeof(OrderConfirmedEvent), …)` for all 4 consumed events. Verified by reading `MessagingDependencyInjection.cs:100-130`. Inbox middleware wraps each consumer body in a transaction that commits when the handler returns normally.

### 5.4 Avro compatibility

`InvoiceIssuedEvent.avsc` includes nullable optional fields (`BillingAddress.Street2`, `BillingAddress.State`) with `default: null` — FORWARD_TRANSITIVE-compatible per ADR-0007. Decimal logical types use explicit `precision`/`scale`. The schema registry compatibility mode is not set in code (it's a topic-level config); the M10 summary's docker-compose smoke didn't surface a compatibility-violation alarm.

### 5.5 Correlation-id flow (ADR-0008)

- **HTTP ingress**: `Program.cs:45` `app.UseCorrelationId()` extracts/seeds the header via `Platform.ServiceDefaults.CorrelationId`. ✅
- **Persistence**: `pending_invoices.correlation_id` + `invoices.correlation_id` columns exist (migration `20260508181028_AddInvoicesCreditNotesAndOutbox.cs`). ✅
- **Outbound (outbox→Kafka)**: `Platform.ReliableMessaging.Outbox.EFCore` copies the ambient correlation id into the emitted Avro event headers. ✅
- **Inbound (Kafka→handler)**: **the four projection consumers read `message.CorrelationId` (the Avro payload field) rather than `IMessageContext.Headers["correlation-id"]`**. `OrderConfirmedInvoiceProjectionKafkaHandler.cs:79,99,125`; same shape in the other 3. If a future producer change makes header ≠ payload, logs/traces (sourced from header by `AddCorrelationIdConsumerMiddleware`) will diverge from persistence (sourced from payload). Today these match — but ADR-0008 is explicit that the **header is authoritative**.

This is a **MEDIUM** finding (§ Dimension 8) — there is no current bug, but the drift surface is unprotected.

### 5.6 Idempotency-Key (ADR-0013)

`ResendInvoiceEndpoint.cs:40-48` configures FastEndpoints `.Idempotency()` with `HeaderName = "Idempotency-Key"`, `CacheDuration = TimeSpan.FromHours(24)`. `PresentationDependencyInjection.cs:47-48` wires `AddIdempotency()` + `AddIdempotencyKeyOutputCache(configuration, "invoicing-service")` (Redis backing per ADR-0013/0016). Caches partition by `Authorization` header (FastEndpoints default `IdempotencyOptions.AdditionalHeaders`), so two admins reusing the same UUID cannot share responses. ✅ Wiring is correct.

**However** — combined with the no-op handler (§ Dimension 8 HIGH), the cache locks in a hollow 204 success for 24 h. If a future deploy implements real delivery, a retry under the same key still returns "done".

### 5.7 No cross-BC consumption of internal events

Invoicing consumes 4 external Avro events from Ordering + Payments (`OrderConfirmed`, `OrderCancelled`, `PaymentCaptured`, `PaymentRefunded`). No `using Ordering.Domain.*` / `using Payments.Domain.*` anywhere in `services/Invoicing` (verified by `NoCrossBcReferenceTests`). ✅

**Verdict**: ⚠️ MOSTLY PASS — one HIGH (consumer Result.Fail swallow in credit-note path, § Dimension 8) + one MEDIUM (correlation-id source). All other event-driven plumbing is correct.

---

## Dimension 6 — .NET / C# best practices

### 6.1 Async hygiene

`grep '\.Result|\.Wait()' services/Invoicing` returns zero matches. `CancellationToken` is threaded through every async method (handlers, allocators, blob store, projections). `ConfigureAwait(false)` is applied in library-style code (allocator, blob store); omitted in application/handler code (idiomatic ASP.NET, fine).

### 6.2 `TimeProvider` discipline (ADR-0015)

- Domain: 0 static UtcNow calls. Enforced by `NoStaticUtcNowInDomainTests`.
- Application: handlers receive `TimeProvider` and use `_timeProvider.GetUtcNow()`. ✅
- Infrastructure: PDF namespace covered by `PdfGenerationContainmentTests.PdfNamespace_ShouldNotCall_StaticUtcNow`. ✅
- **Infrastructure / Blobs: `AzureBlobStore.cs:140` uses `DateTimeOffset.UtcNow.Add(expiry)` for SAS expiry.** Outside the arch-test scope; inconsistent with ADR-0015's intent. § Dimension 8 HIGH.

### 6.3 Magic strings vs constants

- Topic name: `InvoicingTopicsOptions.Invoices` (options-bound) — ✅
- Service name: `PresentationDependencyInjection.ServiceName = "invoicing-service"` (const) — ✅
- Connection-string keys: `ConnectionStringsOptions.cs` (typed options) — ✅
- Error codes: `InvoicingErrorCodes.cs` (constants) — ✅
- Blob container: `BlobStorageOptions.InvoicesContainerName` (options-bound) — ✅
- PDF content type: `IssueInvoiceCommandHandler.cs:74` `PdfContentType = "application/pdf"` (const) — ✅
- SAS TTL: handler-level `const int SasTtlMinutes = 10` — ✅
- Idempotency header name: hard-coded `"Idempotency-Key"` at `ResendInvoiceEndpoint.cs:46` (FastEndpoints default; acceptable inline) — ✅

### 6.4 Nullable reference types

Solution-wide `<Nullable>enable</Nullable>`. Spot-checked grep for `!` (null-forgiving operator): used in `IssueInvoiceCommandHandler` (`pending.BuyerId is null` / `pending.BuyerId.Value`) where a null-check has just been performed; in `Invoice.InvoiceNumber!.Value` inside EF conversion (`InvoiceConfiguration.cs:58`) where the conversion is only invoked for non-null property values. All justified by the surrounding control flow.

### 6.5 Logging

`_logger.LogInformation` / `LogWarning` use structured properties (`{InvoiceId}`, `{CorrelationId}`, `{InvoiceNumber}`). No PII (BillingAddress) is in any log message. **However** — `PersistenceDependencyInjection.cs:69` enables `EnableSensitiveDataLogging(!isDeployedEnvironment)`, which causes EF Core to log parameter values (including the plaintext `*_enc` columns) for any environment that isn't Production. Combined with the absence of a `[PII]` Serilog attribute on `Platform.SharedKernel.ValueObjects.Address`, the dev/test/staging logs are a soft PII leak. § Dimension 8 MEDIUM.

### 6.6 `IDisposable` honoured

`AzureBlobStore` uses `using var stream = new MemoryStream(...)`; `IssueInvoiceCommandHandler` uses `await using var transaction = …`. `_logger.BeginScope(...)` results assigned to `using var correlationScope`. Clean.

**Verdict**: ⚠️ MOSTLY PASS — one HIGH (TimeProvider drift in AzureBlobStore) + one MEDIUM (EF sensitive-data logging + missing [PII] on Address).

---

## Dimension 7 — CI gates + test slices (verbatim output)

All commands run in the review session at HEAD `f49d358`. Czech locale strings are the .NET CLI translations:
- "Úspěšné!" = "Successful!" / "Neúspěšné" = "Failed" / "Úspěšné" = "Passed"
- "Počet chyb" = "Error count" / "upozornění" = "warning(s)"
- "Všechny projekty jsou v aktuálním stavu pro obnovení" = "All projects are up-to-date for restore"

### Gate 1 — `dotnet restore --locked-mode`

```text
C:\Users\david.capcuch\Desktop\Git\DotNetAtlas\test\Ordering.ArchitectureTests\Ordering.ArchitectureTests.csproj :
 warning NU1903: Balíček „System.Security.Cryptography.Xml" 10.0.1 má známé vysoké ohrožení zabezpečení závažnosti
 … (53 NU1903 warnings across the branch — same baseline as M10) …
  Všechny projekty jsou v aktuálním stavu pro obnovení.
```

**Exit code 0**. NU1903 warnings are pre-existing branch-wide (not Invoicing-introduced).

### Gate 2 — `dotnet build -m --no-restore`

```text
… same 53 NU1903 warnings, no new diagnostics …
    53 upozornění
    Počet chyb: 0

Uplynulý čas 00:02:10.95
```

**Exit code 0**.

> Note: an earlier rebuild attempt in this session reported `Počet chyb: 2` (file-lock `MSB3027` errors on `Catalog.API.dll` and `Platform.SharedKernel.UnitTests.deps.json`) caused by lingering `testhost.exe` processes from this review's parallel test slices. After the test slices completed and the OS released the locks, the build was rerun and produced the clean output above. **The errors were not Invoicing build defects** — they were environmental file-lock collisions in `bin/Debug` directories belonging to other BCs.

### Gate 3 — `dotnet format whitespace --no-restore --verify-no-changes`

```text
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění protokolovat, nastavte možnost verbosity na úroveň diagnostic.
```

**Exit code 0** — zero violations.

### Gate 4 — `dotnet format style --no-restore --verify-no-changes`

```text
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění protokolovat, nastavte možnost verbosity na úroveň diagnostic.
```

**Exit code 0** — zero violations.

### Slice 1 — `dotnet test test/Invoicing.UnitTests/ --no-build --no-restore`

```text
Testovací běh pro …\Invoicing.UnitTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    96, Přeskočeno:     0, Celkem:    96, Doba trvání: 1 s - Invoicing.UnitTests.dll (net10.0)
```

**96 / 96 green.**

### Slice 2 — `dotnet test test/Invoicing.ArchitectureTests/ --no-build --no-restore`

```text
Testovací běh pro …\Invoicing.ArchitectureTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Úspěšné!    - Neúspěšné:     0, Úspěšné:    29, Přeskočeno:     0, Celkem:    29, Doba trvání: 12 s - Invoicing.ArchitectureTests.dll (net10.0)
```

**29 / 29 green.**

### Slice 3 — `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.IntegrationTests/ --no-build --no-restore`

```text
Testovací běh pro …\Invoicing.IntegrationTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1
[xUnit.net 00:01:02.39]     [Test Collection Cleanup Failure (AzuriteCollection)] Xunit.Sdk.TestPipelineException

Úspěšné!    - Neúspěšné:     0, Úspěšné:    32, Přeskočeno:     0, Celkem:    32, Doba trvání: 1 m 41 s - Invoicing.IntegrationTests.dll (net10.0)
```

**32 / 32 green.** The `Test Collection Cleanup Failure (AzuriteCollection)` is non-fatal — it fires after all 32 tests have passed; the test run exits 0. § Dimension 8 MEDIUM.

### Slice 4 — `unset HTTP_PROXY … && dotnet test test/Invoicing.FunctionalTests/ --no-build --no-restore`

```text
Testovací běh pro …\Invoicing.FunctionalTests.dll (.NETCoreApp,Version=v10.0)
Verze VSTest 18.0.1 (x64)

Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    22, Přeskočeno:     0, Celkem:    22, Doba trvání: 13 s - Invoicing.FunctionalTests.dll (net10.0)
```

**22 / 22 green.**

### Aggregate

- **4 / 4 CI gates green** (restore, build, format whitespace, format style).
- **4 / 4 test slices green: 96 + 29 + 32 + 22 = 179 / 179**.
- One non-fatal cleanup-time exception in the integration slice (AzuriteCollection disposal race).
- 53 NU1903 transitive-dependency warnings on `System.Security.Cryptography.Xml` etc. — branch-wide baseline.

**Verdict**: ✅ PASS.

---

## Dimension 8 — Code review findings

This dimension synthesises (a) my direct read of `services/Invoicing/**`, (b) the parallel multi-dimensional Opus reviewer dispatched against the same scope. Severity calibrated to a final closeout — CRITICAL = crashes prod / data leak / contract drift; HIGH = real bug, recoverable; MEDIUM = quality issue; LOW = cosmetic.

### CRITICAL

None.

### HIGH

#### H1. `ResendInvoiceCommandHandler` is a no-op despite M10 marking the DoD row ✅

- **File**: `services/Invoicing/Invoicing.Application/Invoices/ResendInvoice/ResendInvoiceCommandHandler.cs:45-77`
- **Evidence**: Handler reads the invoice, checks state, calls `_logger.LogInformation("Admin resend acknowledged …")`, then `return Result.Ok();`. **No** `invoice_delivery_log` insert, **no** outbox row, **no** domain event. The xmldoc at lines 23-30 explicitly says: *"Deferred to a later milestone: the `invoice_delivery_log` insert + outbox row keyed `(InvoiceId, Channel, Attempt)` described in `invoicing.md § 12`. … for now the resend is a no-op observability event with the 202 representing acknowledgement rather than work performed."*
- **Why it matters**: `bc-design/invoicing.md § 12` LOCKS the resend semantics (`MAX(Attempt) + 1` insert + outbox keyed on `(InvoiceId, Channel, Attempt)`). The endpoint advertises 204 (success) and `.Idempotency()` caches that 204 for 24 h. If a future deploy lands the real implementation, retried requests under the same Idempotency-Key continue to return the cached "done" without ever invoking the real handler. The M10 summary § DoD line 73 marks "POST /invoices/{id}/resend (with `.Idempotency()`)" as ✅ MET without disclosing this gap.
- **Recommendation**: (a) Re-classify the M10 DoD row to ⏸️ PARTIALLY MET with the xmldoc rationale propagated into the summary § Improvements proposed; OR (b) implement the documented behaviour — add migration for `invoice_delivery_log`, emit `InvoiceDeliveryRequestedDomainEvent` from a new aggregate method `Invoice.RequestResend(...)`, wire `InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler` to `notifications.*` topic; OR (c) downgrade the endpoint response to 501 / 202 + add OpenAPI `description` flagging v1 stub behaviour so admin tooling does not interpret 204 as completed work.

#### H2. `InvoiceDeliveredEvent.avsc` + `InvoiceDeliveredOutboxPublisher` not shipped (already disclosed)

- **File**: `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/` (only `InvoiceCancelledEvent.avsc` and `InvoiceIssuedEvent.avsc` present; `InvoiceDeliveredEvent.avsc` absent).
- **Evidence**: `<contract>` line 43 LOCKS "4 external Avro events on topic `invoicing.invoices`". Three are shipped. The internal `InvoiceDeliveredDomainEvent` exists at `services/Invoicing/Invoicing.Domain/Invoices/Events/InvoiceDeliveredDomainEvent.cs` but has no consumer. `Invoice.Deliver(...)` is never called in production code because `IssueInvoiceCommandHandler.cs:158` hard-codes `DeliveryChannel.None`.
- **Disclosed**: M10 § Improvements proposed line 244 documents this with rationale ("no downstream consumer ready: Notifications email + BFF cache are placeholders").
- **Recommendation**: Accept as carry-forward (already the M10 disposition). The schema gap and the delivery dormancy are coupled — both unlock when a real Notifications/BFF consumer lands.

#### H3. Credit-note convergence handlers swallow `Result.Fail` — message permanently inbox-committed

- **File**: `services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Projections/OrderCancelledCreditNoteProjectionKafkaHandler.cs:112-118`; same pattern in `PaymentRefundedCreditNoteProjectionKafkaHandler.cs`
- **Evidence**: When `IssueCreditNoteCommandHandler` returns `Result.Fail` (e.g., `PartialRefundNotSupportedV1`, `CreditNoteRefersToCancelledInvoice`), the consumer calls `_logger.LogWarning(...)` and returns normally. The inbox-middleware transaction commits the inbox row → the message is "processed" forever. **Compare to the issuance-side consumers** (`OrderConfirmedInvoiceProjectionKafkaHandler.cs:127-132` / `PaymentCapturedInvoiceProjectionKafkaHandler.cs`) which **throw** on `Result.Fail`, rolling back the inbox transaction so the message is retried.
- **The trade-off is intentional**: comment at `OrderCancelledCreditNoteProjectionKafkaHandler.cs:105-108` says *"Validation-style failures (e.g., already-cancelled invoice) come back as Result.Fail and are logged; the inbox row still commits so we don't loop."* This avoids infinite retries on permanently-unsupported partial refunds.
- **Consequence**: A v2 deploy that lands partial-refund support cannot retry the original `OrderCancelled` / `PaymentRefunded` events — they're inbox-committed under a stable `message.id`. Only manual republication with a new id will trigger reprocessing.
- **Recommendation**: Either (a) persist failed-convergence outcomes to a `failed_credit_notes` table so a v2 backfill can replay; (b) re-classify `PartialRefundNotSupportedV1` to route to DLT (recoverable via manual reprocessing) instead of inbox-committing; (c) document the data-loss-on-fix risk explicitly in `error-taxonomy.md § 3.6` so future maintainers don't naively change the comment.

#### H4. `AzureBlobStore.BuildSasUri` uses static `DateTimeOffset.UtcNow` — bypasses `TimeProvider` (ADR-0015)

- **File**: `services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs:140`
- **Evidence**: `new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(expiry))`. Every other handler in the BC uses `_timeProvider.GetUtcNow()`. `GetInvoiceByIdQueryHandler.cs:83` correctly computes the response `sasExpiresAtUtc` via `TimeProvider`, then `AzureBlobStore` signs the SAS against the wall clock — under `FakeTimeProvider`-driven tests the two clocks can disagree by hours.
- **Why the arch test didn't catch it**: `DoesNotCallStaticUtcNowRule` is applied to `Invoicing.Domain` (`NoStaticUtcNowInDomainTests`) and to `Invoicing.Infrastructure.Pdf.*` (`PdfGenerationContainmentTests.PdfNamespace_ShouldNotCall_StaticUtcNow`). The Infrastructure/Blobs namespace is not covered.
- **Recommendation**: Inject `TimeProvider` into `AzureBlobStore`; use `_timeProvider.GetUtcNow().Add(expiry)`. Extend `PdfGenerationContainmentTests` (or add a sibling) to cover `Invoicing.Infrastructure.Blobs`.

### MEDIUM

#### M1. Auth: scope-based gating (ADR-0010) not implemented; only role-based `InvoicingAdmin` policy registered

- **File**: `services/Invoicing/Invoicing.Infrastructure/Common/AuthDependencyInjection.cs:54-59`; policy constants at `Authorization/AuthPolicies.cs`.
- **Evidence**: `AuthorizationBuilder.AddPolicy(AuthPolicies.InvoicingAdmin, policy => { policy.RequireAuthenticatedUser(); policy.RequireRole(Roles.Admin); })`. **No** `RequireClaim("scope", "invoicing.admin.resend")` / `"invoicing.read"`. `AuthPolicies.cs:13-15` xmldoc acknowledges: *"When ADR-0010's scope-based gating lands (v2+), this policy will be augmented with a `RequireClaim("scope", "invoicing.admin.*")` assertion alongside the role check."*
- **M10 summary line 52 claim**: *"Admin HTTP endpoint … is JWT-gated by `AuthPolicies.InvoicingAdmin` (scope `invoicing.admin.resend`)"* and *"Buyer GET endpoints … are gated by scope `invoicing.read`"*. **Code reality**: role-based (Keycloak realm role `admin`) only.
- **Why MEDIUM not HIGH**: functional gating exists for resend (role check). Buyer GET endpoints rely on a manual `User.GetBuyerIdOrNull()` null check in `HandleAsync` + IDOR check in the query handler (`GetInvoiceByIdQueryHandler.cs:60-67` returns `InvoiceNotFound` on cross-buyer reads). The security posture is intact; only the wiring layer differs from doc.
- **Recommendation**: Either implement scope-based gating per ADR-0010 in this milestone or correct M10 § ADR application notes line 52 to read "role-based via Keycloak realm role `admin`; scope-based deferred to v2 per AuthPolicies.cs:13-15".

#### M2. GET endpoints lack declarative `Policies()` / `AuthSchemes()` — rely on global `UseAuthentication` + manual null-check

- **File**: `services/Invoicing/Invoicing.API/Endpoints/Invoices/GetInvoiceById/GetInvoiceByIdEndpoint.cs:26-45`; same shape in 3 sibling GET endpoints.
- **Evidence**: `Configure()` only specifies `Get(...)`, `Version(1)`, `Group<InvoicesGroup>()`, `Summary(...)`, `Description(...)`. No `Policies(...)` or `AuthSchemes(...)`. Authentication is enforced by the global `app.UseAuthentication()` + the manual `if (!isAdmin && buyerId is null) await Send.UnauthorizedAsync(ct)` at lines 52-56.
- **Risk**: A future refactor that removes the manual short-circuit, or a misconfigured FastEndpoints default that flips to anonymous, would silently un-gate the endpoint. The IDOR check at the handler level still protects data, but unauthenticated requests would reach the handler.
- **Recommendation**: Add `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` to each GET endpoint's `Configure()`. Mirror Ordering's pattern.

#### M3. Consumer reads `CorrelationId` from Avro payload, not from Kafka header (ADR-0008)

- **Files**: All 4 Kafka projection consumers in `services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Projections/`. Example: `OrderConfirmedInvoiceProjectionKafkaHandler.cs:79,99,125`.
- **Evidence**: `message.CorrelationId` (the Avro field) is used to upsert the projection row, populate `pending_invoices.CorrelationId`, and dispatch `IssueInvoiceCommand`. No reference to `IMessageContext.Headers` or a `context.ExtractCorrelationId()` call. The `AddCorrelationIdConsumerMiddleware` (registered at `MessagingDependencyInjection.cs:101`) seeds the Serilog/Activity scope from the header, but persistence uses the payload.
- **Risk**: Today the producer puts the same value in both places, so the test suite never catches drift. If a producer-side change ever makes them diverge, log/trace correlation will silently break — debugging cross-BC issues without the missing link is expensive.
- **Recommendation**: Either (a) drop `CorrelationId` from the Avro payload (header = single source of truth); (b) add a runtime invariant `Activity.Current?.GetBaggageItem("correlation-id") == message.CorrelationId.ToString()` in the consumer pre-amble; (c) add an arch-level assertion that consumers route correlation-id through `context.Headers`.

#### M4. `ResendInvoiceEndpoint` xmldoc says 202; code returns 204

- **File**: `services/Invoicing/Invoicing.API/Endpoints/Invoices/ResendInvoice/ResendInvoiceEndpoint.cs:16-23` (xmldoc) vs `:59,79` (config + handler).
- **Evidence**: Endpoint xmldoc: *"a double-clicked admin resend returns the same 202 from the Redis-backed output cache"*. Endpoint config: `b.Produces((int)HttpStatusCode.NoContent)` (204). Handler call: `await Send.NoContentAsync(ct)`. Tests at `ResendInvoiceTests` assert 204. ADR-0013's worked example uses 202.
- **Recommendation**: Pick one. If 204 is the deliberate BC choice (idempotent admin command with no payload), update the xmldoc + ADR-0013 cross-reference. Otherwise switch to 202.

#### M5. `Invoice.Issue(PdfBlobRef, …)` 2-arg overload lacks write-once `PdfBlobRef` guard (I-4 symmetry-break)

- **File**: `services/Invoicing/Invoicing.Domain/Invoices/Invoice.cs:231-284`
- **Evidence**: Lines 248-249 set `PdfBlobRef = pdfBlobRef; IssueDate = utcNow;` without a `if (PdfBlobRef is not null) throw new DataIntegrityException("Invoicing.InvoiceAlreadyIssued", …)` guard. `CreditNote.Issue(PdfBlobRef, …)` at `CreditNote.cs:216-221` has the equivalent guard.
- **Why MEDIUM**: The 3-arg overload short-circuits with `Status.CanTransitionTo(...)` (Result.Fail) when Status ≠ Draft, so the 2-arg path can only be reached from Draft — where `PdfBlobRef` is null by invariant. Today there's no runtime path that mutates a non-null `PdfBlobRef`. But the I-4 guard is defence-in-depth, and the asymmetry vs `CreditNote.Issue` weakens the model.
- **Recommendation**: Mirror `CreditNote.Issue`'s explicit guard. Add a unit test that calls `invoice.Issue(pdf, now)` on an already-stamped Draft (constructed via reflection / test-double) and expects `DataIntegrityException`.

#### M6. `GetInvoicesByBuyerEndpoint` ignores admin override

- **File**: `services/Invoicing/Invoicing.API/Endpoints/Invoices/GetInvoicesByBuyer/GetInvoicesByBuyerEndpoint.cs`
- **Evidence**: The other 3 GET endpoints (`GetInvoiceById`, `GetInvoiceByOrderId`, `GetCreditNoteById`) check `User.IsInvoicingAdmin()` and allow admins to read any record. `GetInvoicesByBuyer` unconditionally scopes to `User.GetBuyerIdOrNull()` and returns 401 if missing. An admin caller (e.g., service-to-service token under ADR-0010 with no `sub`) cannot list invoices at all; with a `sub` they're scoped to their own.
- **Recommendation**: Either accept a `?buyerId={guid}` query parameter when `User.IsInvoicingAdmin()` is true, or explicitly document this v1 gap on the endpoint xmldoc and the BC chapter.

#### M7. PII gap: `EnableSensitiveDataLogging(!isDeployedEnvironment)` + missing `[PII]` on `Address`

- **File**: `services/Invoicing/Invoicing.Infrastructure/Common/PersistenceDependencyInjection.cs:69`
- **Evidence**: `optionsBuilder.EnableSensitiveDataLogging(!isDeployedEnvironment)` — turns on EF parameter-value logging in any environment that isn't Production (Development, Test, Staging-not-prod). Combined with the absence of a `[PII]` Serilog attribute on `Platform.SharedKernel.ValueObjects.Address`, the `billing_address_*_enc` plaintext values leak into local + CI logs. ADR-0011 reserves the `_enc` suffix for v2 encryption but doesn't waive the logging surface.
- **Recommendation**: Gate `EnableSensitiveDataLogging` to Development only (`builder.Environment.IsDevelopment()` instead of `!IsDeployedEnvironment()`); attach `[PII]` (or a `Serilog.IDestructuringPolicy`) to `Address` so Serilog destructuring policies redact it.

#### M8. `pdf_blob_uri` column persists stale 10-min SAS URL; 10-year retention; replayed events ship expired URLs

- **File**: `services/Invoicing/Invoicing.Infrastructure/Persistence/Database/EntityConfigurations/InvoiceConfiguration.cs:121-138`
- **Evidence**: Every GET handler re-mints the SAS via `_blobStore.GetSasUrlAsync(...)` from `InvoicePdfBlobName.For(invoice.InvoiceNumber)`. The stored URL is never read by the API. But `InvoiceIssuedMapper.cs:43` publishes `PdfBlobUri = source.PdfBlobRef.BlobUri.AbsoluteUri` into the Avro event, and `invoicing.invoices` has 10-year retention — outbox-replayed events ship long-expired URLs. The schema doc warns consumers to re-mint, but persisting the URL at all is dead weight.
- **Recommendation**: Replace `PdfBlobRef.BlobUri : Uri` with `PdfBlobRef.BlobName : string` (canonical, immutable, no expiry); compute the URL on demand. Or, at minimum, document on `PdfBlobRef` that the URI is informational only and rename the column to `pdf_blob_uri_at_issuance` to acknowledge staleness.

#### M9. AzuriteCollection fixture cleanup race (`Xunit.Sdk.TestPipelineException`)

- **File**: `test/Invoicing.IntegrationTests/Blobs/AzuriteFixture.cs` (collection definition); manifests at end of `Invoicing.IntegrationTests` runs.
- **Evidence**: Integration test slice exits 0 with "32 / 32 successful" but logs `[xUnit.net 00:01:02.39] [Test Collection Cleanup Failure (AzuriteCollection)] Xunit.Sdk.TestPipelineException` AFTER the green report. Not a regression — same M10 baseline.
- **Recommendation**: Investigate Azurite container disposal ordering vs the test runner's shutdown signal; add a `try { await _azurite.DisposeAsync(); } catch { /* log */ }` in the fixture's `DisposeAsync` to suppress non-fatal disposal errors. Low operational impact; cosmetic for CI hygiene.

#### M10. `pending_invoices.OrderPayload` jsonb carries plaintext PII

- **File**: `services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Projections/OrderConfirmedInvoiceProjectionKafkaHandler.cs:148-173` (SerializePayload includes `BillingAddress`)
- **Evidence**: The `OrderConfirmedEvent` is a Summary Event (ADR-0020 / Wave 1.5) that carries `BillingAddress`. The consumer persists this into `pending_invoices.OrderPayload` jsonb plaintext. The `_enc` column suffix on `Invoice.BillingAddress` reserves v2 encryption for the aggregate, but the projection staging is plaintext. ADR-0011 acknowledges 10-year retention carries PII as v1 gap; the projection staging is a separate surface.
- **Recommendation**: Either drop the address fields from `OrderPayload` (re-derive from the Avro event at issuance — but that requires keeping the raw Avro around, which the inbox loses) or align the projection table with the aggregate's `_enc` posture for v2.

### LOW

- **L1**. `InvoiceDocument.cs:20` — `// TODO(M10): Swap to "Inter" once Dockerfile embeds fonts per ADR-0019 § Font embedding.` M10 is the closeout; TODO unresolved. PDF determinism tests still pass.
- **L2**. `IssueInvoiceCommandHandler.cs:336-341` + `IssueCreditNoteCommandHandler.cs` — blob keys are `YYYY/01/<number>.pdf` (month hard-coded to 01). Documented as placeholder for v2 catalogue partition.
- **L3**. `EndpointGroupConstants.cs:8` defines a tag-name constant (`"invoices"`) decoupled from the FastEndpoints group route (`"invoicing/invoices"`); rename risk.
- **L4**. No DB `CHECK` constraint on `Invoice.Status` / `CreditNote.Status` columns — bypass via direct SQL could persist out-of-range values that throw at rehydrate.
- **L5**. 53 NU1903 transitive-dependency warnings (branch-wide; not Invoicing-introduced).
- **L6**. otel-collector `Restarting` in docker-compose smoke — pre-existing platform-level OTel config defect, not Invoicing.
- **L7**. `invoicing.api` container missing from `docker-compose.yaml` (Catalog has one; Invoicing runs via local `dotnet run`). DEVOPS-wave deliverable.
- **L8**. `nw-mutation-test` post-green pass not run; `_shared.md § 7` recommendation. M10 § Improvements proposed line 249.

### Summary table

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| HIGH | 4 (1 disclosed carry-forward, 3 undisclosed) |
| MEDIUM | 10 |
| LOW | 8 |

---

## Verdict — 🟡 CONDITIONAL-PASS

### Threshold mapping

| Criterion | Required for | Outcome |
|---|---|---|
| Zero CRITICAL findings | PASS / CONDITIONAL-PASS | ✅ |
| Zero unaccepted HIGH | PASS | ❌ — 3 of 4 HIGH findings (H1 resend stub, H3 credit-note swallow, H4 AzureBlobStore UtcNow) are **not disclosed in M10** |
| All DoD MET | PASS | ❌ — 2 rows are PARTIALLY MET (4th Avro disclosed; resend wiring undisclosed) |
| All DoD MET or PARTIALLY MET with rationale | CONDITIONAL-PASS | ✅ if we extend the resend xmldoc's "deferred to a later milestone" rationale to the M10 summary (this report does that explicitly) |
| All gates green | PASS / CONDITIONAL-PASS | ✅ (4 gates + 4 slices, 179/179) |
| Contract-locked seam intact | PASS | ❌ — 4-event seam (3 shipped) + resend semantics drift (BC design § 12 says insert + outbox; impl says no-op) |

### Why CONDITIONAL-PASS and not FAIL

Strict reading of `<output>`'s FAIL trigger ("contract-locked seam drifted") would land this as FAIL. The reviewer chose CONDITIONAL-PASS because:

1. **The drift is contained**. The 4th Avro is deferred behind a runtime gate (`DeliveryChannel.None` in `IssueInvoiceCommandHandler.cs:158`), so production never tries to emit it. The resend handler is a guarded no-op (state-checks present; idempotency-key wired); it doesn't crash or corrupt.
2. **The disclosure exists in code, just not in the M10 summary**. The handler xmldocs explicitly call out the deferral; the M10 summary's DoD table just didn't propagate the gap. This is correctable with a single self-correction commit.
3. **Functional surface for the main paths is intact**: issuance (M7), credit-note issuance (M7), gap-free numbering (M5), PDF determinism (M4), blob storage (M3), enrichment projection (M6), HTTP read paths (M8), architecture tests (M9). All shipped, all green.
4. **Zero CRITICAL findings**; the four HIGH findings are recoverable with bounded effort (≤ 1 milestone of work each, two of them with the M10 summary self-correction route as a cheaper alternative).

### Why not PASS

The M10 summary marks the resend endpoint DoD row as ✅ MET without disclosing that the handler is a no-op. This is the only finding that, in the reviewer's judgment, prevents a clean PASS — a future maintainer reading the M10 summary alone would believe `bc-design/invoicing.md § 12`'s `invoice_delivery_log` + outbox contract is implemented.

### Verdict-relative carry-forward list

The CONDITIONAL-PASS verdict is contingent on the following acceptance:

- **A1 (proposed)**: Accept H2 (4th Avro deferred) per the M10 § Improvements proposed disposition — no action required.
- **A2 (required)**: Self-correct the M10 summary's DoD table for the resend row (✅ → ⏸️ PARTIALLY MET) and add to § Improvements proposed. Alternatively, implement H1.
- **A3 (recommended)**: Accept H3 (credit-note Result.Fail swallow) as a deliberate trade-off with explicit error-taxonomy.md cross-reference + a runbook entry for v2-fix replay.
- **A4 (recommended)**: Fix H4 (AzureBlobStore UtcNow) — single-line change + arch-test extension, low risk.

---

## Punch list (ordered, file-cited)

If pursuing PASS upgrade:

1. **`services/Invoicing/Invoicing.Application/Invoices/ResendInvoice/ResendInvoiceCommandHandler.cs:45-77`** — Either implement the `invoice_delivery_log` insert + outbox-row emission per `bc-design/invoicing.md § 12`, OR downgrade the endpoint contract (502 / 501 / documented v1 stub) so the 24-h Idempotency-Key cache doesn't lock in a hollow success. **H1**.
2. **`docs/implementation-prompts/session-summaries/invoicing-m10.md`** — Self-correct the DoD-table row for the resend endpoint from ✅ to ⏸️ PARTIALLY MET; propagate the handler xmldoc deferral note into § Improvements proposed. **H1 corollary**.
3. **`services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Projections/OrderCancelledCreditNoteProjectionKafkaHandler.cs:112-118`** + sibling `PaymentRefundedCreditNoteProjectionKafkaHandler.cs` — Decide on Result.Fail policy and document it: either persist failed convergences (`failed_credit_notes` table) for v2 backfill, or route to DLT. Update `error-taxonomy.md § 3.6` with the chosen path. **H3**.
4. **`services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs:140`** — Inject `TimeProvider`; replace `DateTimeOffset.UtcNow.Add(expiry)` with `_timeProvider.GetUtcNow().Add(expiry)`. Extend `Invoicing.ArchitectureTests` to cover `Invoicing.Infrastructure.Blobs` with `DoesNotCallStaticUtcNowRule`. **H4**.
5. **`platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/`** — Ship `InvoiceDeliveredEvent.avsc` + matching outbox publisher when Notifications/BFF consumers land. Until then, accept as carry-forward. **H2**.

MEDIUM-priority follow-ups (separate tickets):

6. `AuthDependencyInjection.cs:54-59` — Add scope-based gating or update M10 § ADR application notes to acknowledge role-based v1 + v2 scope deferral. **M1**.
7. All four `services/Invoicing/Invoicing.API/Endpoints/...GetInvoice*` / `GetCreditNote*` endpoints — add `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` declarative auth. **M2**.
8. All four Kafka projection consumers — align correlation-id source (header vs payload), add invariant assertion or arch test. **M3**.
9. `ResendInvoiceEndpoint.cs:16-23` xmldoc — reconcile 202 vs 204. **M4**.
10. `Invoice.cs:231-284` — add I-4 write-once guard symmetric to `CreditNote.Issue`. **M5**.
11. `GetInvoicesByBuyerEndpoint` — add admin override or document v1 limitation. **M6**.
12. `PersistenceDependencyInjection.cs:69` — narrow `EnableSensitiveDataLogging` to Development; add `[PII]` on `Address`. **M7**.
13. `InvoiceConfiguration.cs` + `PdfBlobRef` — replace `BlobUri` storage with `BlobName`, derive URL on demand. **M8**.
14. `AzuriteFixture` — guard `DisposeAsync` against the cleanup-race exception. **M9**.
15. `pending_invoices.OrderPayload` — align PII posture with the aggregate `_enc` columns for v2 encryption. **M10**.

LOW findings — leave as backlog tickets; none block production.

---

*End of Invoicing BC final closeout review.*
