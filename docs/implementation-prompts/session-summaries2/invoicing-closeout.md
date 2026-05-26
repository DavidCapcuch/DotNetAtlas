# Invoicing BC — Final Closeout Review

> **HEAD:** `9aad7c4` &nbsp;·&nbsp; **Branch:** `aaqwdqwd` &nbsp;·&nbsp; **Reviewed:** 2026-05-11 &nbsp;·&nbsp; **Reviewer:** independent final-state audit, read-only.

## Verdict — **CONDITIONAL-PASS**

Zero CRITICAL, zero HIGH. All four CI gates green, all four test slices green (179/179), Opus code-review pass clean (0 findings). Two DoD items ship PARTIALLY-MET with rationale already documented in the M10 session summary (`InvoiceDeliveredEvent` deferred until a consumer is ready; ADR-0010 scope-gating reframed as role-based per `AuthPolicies.cs` v2 note). The remaining MEDIUM findings are documentation-drift items in the canonical BC-design docs — the shipped code is more thorough than the spec it points at, not the other way around.

## TL;DR

- All locked contract items (aggregates, allocator pattern, blob abstraction, PDF determinism, correlation-id roundtrip, `.Idempotency()`, `/api/v1/invoicing/...` routes, PII `*_enc` columns, outbox-in-same-transaction) verified against code at file:line.
- 3-of-4 external Avro events ship; the 4th (`InvoiceDelivered`) is a documented carry-forward, paired with its outbox publisher gap.
- `architecture-tests.md § 2` has no Invoicing section yet the BC ships 29 architecture-test facts that go beyond the universal § 1 rules — implementation outstrips spec (good direction, doc owes an update).
- `use-cases.md` ends at § 5 — § 6 (referenced from `<reading_order>`) does not exist. Implementation derives its commands/queries from BC design + dispatch prompt, which is consistent — the use-cases catalog is the doc that's out of sync.

---

## Dimension 1 — Doc adherence + DoD audit

### `_shared.md § 12` (universal DoD)

| Line | Status | Citation |
|---|---|---|
| 4-layer project compiles | ✅ MET | Build exit 0 (see § Dimension 7). Layers at `services/Invoicing/Invoicing.{API,Application,Domain,Infrastructure}/`. |
| Commands + queries from `use-cases.md § {bc}` | ⚠️ PARTIALLY MET | The cited § 6 of `use-cases.md` does **not exist** (the doc ends at § 5 — [use-cases.md:1509](../../bc-design/use-cases.md)). Commands shipped (3: `IssueInvoiceCommand`, `IssueCreditNoteCommand`, `ResendInvoiceCommand`) and queries shipped (4: `GetInvoiceByIdQuery`, `GetInvoicesByBuyerQuery`, `GetInvoiceByOrderIdQuery`, `GetCreditNoteByIdQuery`) match `docs/bc-design/invoicing.md § 7` and the M8 admin/buyer split. Doc-side drift, not implementation-side. |
| All internal `*DomainEvent` in Domain | ✅ MET | 7 events under [`Invoicing.Domain/Invoices/Events/`](../../../services/Invoicing/Invoicing.Domain/Invoices/Events/) (5) + [`Invoicing.Domain/CreditNotes/Events/`](../../../services/Invoicing/Invoicing.Domain/CreditNotes/Events/) (2). Names match `invoicing.md § 5` exactly. |
| External `*Event` Avro under `Platform.SchemaRegistry.Contracts/Avro/Invoicing/` | ⚠️ PARTIALLY MET | **3/4 shipped:** [`InvoiceIssuedEvent.avsc`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceIssuedEvent.avsc), [`InvoiceCancelledEvent.avsc`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceCancelledEvent.avsc), [`CreditNoteIssuedEvent.avsc`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/CreditNotes/CreditNoteIssuedEvent.avsc). Missing `InvoiceDeliveredEvent.avsc` — paired with the deferred outbox publisher (next row). Rationale documented in [invoicing-m10.md:244](invoicing-m10.md) — no downstream consumer ready in v1. |
| Outbox publishers map internal → external | ⚠️ PARTIALLY MET | 3/4 shipped in [`Invoicing.Application/Outbox/`](../../../services/Invoicing/Invoicing.Application/Outbox/): `InvoiceIssuedOutboxPublisherDomainEventHandler`, `CreditNoteIssuedOutboxPublisherDomainEventHandler`, `InvoiceCancelledOutboxPublisherDomainEventHandler` + 3 corresponding mappers. `InvoiceDeliveredOutboxPublisher` deferred with its schema. |
| DbContext + naming conventions | ✅ MET | [`InvoicingDbContext`](../../../services/Invoicing/Invoicing.Infrastructure/Persistence/Database/InvoicingDbContext.cs) with `EFCore.NamingConventions` snake_case; 3 user-generated migrations under [`Migrations/`](../../../services/Invoicing/Invoicing.Infrastructure/Persistence/Database/Migrations/). |
| Messaging DI: outbox, inbox, Kafka consumers | ✅ MET | [`MessagingDependencyInjection.AddKafkaMessaging`](../../../services/Invoicing/Invoicing.Infrastructure/Common/MessagingDependencyInjection.cs:43) wires 4 Kafka consumers via 2 consumer groups (ordering, payments) + middleware chain `AddSchemaRegistryAvroDeserializer → AddCorrelationIdConsumerMiddleware → AddDeadLetter → RetryForever → AddInbox → AddTypedHandlers`. `services.AddInbox<InvoicingDbContext>()` at line 144; `services.AddOutbox(...)` at line 151. |
| docker-compose delta: topics + outbox-relay | ✅ MET | `invoicing.invoices` topic with `retention.ms=315360000000` (10y) at [`docker-compose.yaml:288`](../../../docker-compose.yaml); `outbox-relay-invoicing` container at lines 526-553; `azurite` + `azurite-init` + `nginx-cdn` at lines 665-732. **Doc gap (LOW):** `events-catalog.md § 4 Docker-compose Delta` (lines 156-190) does not list `invoicing.invoices` (it only enumerates Catalog/Basket/Ordering/Inventory). Topic was correctly added to compose; canonical delta-doc is stale. |
| 4 test projects compile + pass; arch tests enforce architecture-tests.md § Invoicing | ⚠️ PARTIALLY MET | 4 projects green (96 + 29 + 32 + 22 = 179/179 — see § Dimension 7). **Doc gap (MEDIUM):** [`architecture-tests.md`](../../bc-design/architecture-tests.md) has Catalog (§ 2.1), Basket (§ 2.2), Ordering (§ 2.3), Inventory (§ 2.4) BC-specific sections but **no Invoicing § 2.x section**. The 29 shipped arch facts (PII allowlist, blob containment, PDF containment, no-static-utc-now, layered Clean-Arch, etc.) exceed what § 2.1–2.4 each prescribe for their BC — implementation outstrips spec. Self-correction was missed during M9. |
| All HTTP routes under `/api/v1/invoicing/...` per ADR-0012 | ✅ MET | 4 GET + 1 POST under [`Invoicing.Api/Endpoints/`](../../../services/Invoicing/Invoicing.Api/Endpoints/). FastEndpoints `Version(1)` + `InvoicesGroup`/`CreditNotesGroup` route prefix combined with platform `api/v` versioning. Verified by 22/22 functional tests. |
| Timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain | ✅ MET | Arch test [`Domain/NoStaticUtcNowInDomainTests.cs`](../../../test/Invoicing.ArchitectureTests/Domain/NoStaticUtcNowInDomainTests.cs) green; rule applied to `Invoicing.Domain` + `Invoicing.Infrastructure.Pdf.*`. Spot-check: [Invoice.cs:91](../../../services/Invoicing/Invoicing.Domain/Invoices/Invoice.cs) takes `DateTimeOffset utcNow` parameter on every mutating method; `TimeProvider` injected at every Application/Infrastructure call-site. Single `DateTimeOffset.UtcNow` lives outside the prohibition zone at [AzureBlobStore.cs:140](../../../services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs) — see Dimension 6 LOW finding. |
| Correlation-id propagation working (HTTP → Kafka → DB) | ✅ MET | `AddCorrelationIdConsumerMiddleware()` in both Kafka consumer pipelines ([MessagingDependencyInjection.cs:101, 124](../../../services/Invoicing/Invoicing.Infrastructure/Common/MessagingDependencyInjection.cs)). `pending_invoices.correlation_id` PK; promoted to `invoices.correlation_id` (unique index, defence-in-depth per the M7 handler comment). Outbox copies via `_outbox.AddOutboxMessage(...)` + integration-event mapper (`InvoiceIssuedMapper`). Pinned by `PendingInvoiceProjectionTests` (32/32 green). |
| All four CI gates green | ✅ MET | See § Dimension 7 verbatim output. |
| `docker compose --profile full up -d` Healthy | ✅ MET | M10 smoke output in [invoicing-m10.md:155-194](invoicing-m10.md). Single observed `otel-collector` restart loop is pre-existing platform-level defect (carry-forward), not Invoicing-side. |
| Docs self-corrected if needed | ⚠️ PARTIALLY MET | M10 explicitly claims "no drift surfaced against bc-design docs". This audit surfaces drift the M10 sweep missed: `use-cases.md § 6` non-existence; `architecture-tests.md § Invoicing` non-existence; `events-catalog.md § 4` missing `invoicing.invoices` topic line; `events-catalog.md § 5.x` missing dedicated Invoicing-events schema enumeration (the 4 events are only listed in § 2/§ 3 master tables); `error-taxonomy.md:49` says `BlobUploadFailed` "Retried via Polly (3 attempts)" but implementation uses Azure SDK retries per the more recent ADR-0017 design_open — doc says Polly, code says SDK (code is correct; doc is stale). |
| Peer-review chain executed; HIGH findings fixed | ✅ MET | 9 prior Opus reviewer passes documented in M1–M9 commit bodies. This closeout's parallel Opus reviewer pass returned **PASS, 0 findings** — see § Dimension 8. |
| Session summary posted | ✅ MET | [`invoicing-m10.md`](invoicing-m10.md) — comprehensive (310 lines), follows `_template.md` shape, captures the verify+document interpretation explicitly. |

### `invoicing.md <dod>` (BC-specific DoD)

| Line | Status | Citation |
|---|---|---|
| 4-layer solution scaffolded; `dotnet build -m` green | ✅ MET | Build exit 0 (see Dimension 7). |
| **4 external Avro events** + no commands under `Avro/Invoicing/Invoices/` | ⚠️ PARTIALLY MET | 3 of 4 (see § Dimension 1 universal table above). Rationale documented as carry-forward. |
| 7 internal `*DomainEvent` + outbox publishers | ⚠️ PARTIALLY MET | 7 internal events ✅; 3 of 4 publishers (matched to the 3 external schemas above). |
| 4 consumers with inbox dedup | ✅ MET | All 4 Kafka projection handlers under [`Invoicing.Infrastructure/Messaging/Kafka/Projections/`](../../../services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Projections/) wired through `AddInbox(typeof(AvroOrderConfirmedEvent), typeof(AvroOrderCancelledEvent))` + `AddInbox(typeof(AvroPaymentCapturedEvent), typeof(AvroPaymentRefundedEvent))` ([MessagingDependencyInjection.cs:112, 135](../../../services/Invoicing/Invoicing.Infrastructure/Common/MessagingDependencyInjection.cs)). |
| Enrichment projections (`pending_invoices`, `pending_credit_notes`) idempotent upserts | ✅ MET | `PendingProjectionUpsertHelper.GetOrAddAsync` + per-handler "half already populated → no-op" branch (e.g. [OrderConfirmedInvoiceProjectionKafkaHandler.cs:88-97](../../../services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Projections/OrderConfirmedInvoiceProjectionKafkaHandler.cs)). |
| Gap-free `InvoiceNumber` + `CreditNoteNumber` allocators (concurrency-tested) | ✅ MET | [PostgresInvoiceNumberAllocator.cs:41-87](../../../services/Invoicing/Invoicing.Infrastructure/Persistence/Numbering/PostgresInvoiceNumberAllocator.cs) — `if (CurrentTransaction is null) throw` + `FromSqlInterpolated("SELECT * FROM ... FOR UPDATE")` + year-rollover via `INSERT ... ON CONFLICT (year) DO NOTHING` + re-select. Same shape in `PostgresCreditNoteNumberAllocator`. Pinned by [`InvoiceNumberAllocatorTests`](../../../test/Invoicing.IntegrationTests/Allocators/InvoiceNumberAllocatorTests.cs) + `CreditNoteNumberAllocatorTests`. |
| QuestPDF byte-deterministic PDF (hash-tested) | ✅ MET | [`QuestPdfInvoiceGenerator.cs`](../../../services/Invoicing/Invoicing.Infrastructure/Pdf/QuestPdfInvoiceGenerator.cs) — community license set in static ctor; `CreationDate`/`ModifiedDate` derived from `Invoice.IssueDate.UtcDateTime`; `CultureInfo.InvariantCulture`. Pinned by [`QuestPdfInvoiceGeneratorTests`](../../../test/Invoicing.IntegrationTests/Pdf/QuestPdfInvoiceGeneratorTests.cs) hash test. Arch test [`PdfGenerationContainmentTests`](../../../test/Invoicing.ArchitectureTests/Infrastructure/PdfGenerationContainmentTests.cs) blocks QuestPDF leakage. |
| Blob upload to Azurite + SAS GET via nginx-cdn end-to-end | ✅ MET | [`AzureBlobStore.cs`](../../../services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs) — `SHA-256` content hash, SAS via `BlobSasBuilder`, optional CDN rewrite via `BlobStorageOptions.PublicBaseUri`. M10 docker smoke captured `azurite (healthy)` + `nginx-cdn Up` + reachability proof. Pinned by [`AzureBlobStoreTests`](../../../test/Invoicing.IntegrationTests/Blobs/AzureBlobStoreTests.cs). |
| HTTP endpoints under `/api/v1/invoicing/` — 4 GET + 1 POST (resend with `.Idempotency()`) | ✅ MET | 5 endpoints shipped. [`ResendInvoiceEndpoint.cs:40-48`](../../../services/Invoicing/Invoicing.Api/Endpoints/Invoices/ResendInvoice/ResendInvoiceEndpoint.cs) wires `Idempotency(opts => { opts.HeaderName = "Idempotency-Key"; opts.CacheDuration = TimeSpan.FromHours(24); })` per ADR-0013. Group routing on `InvoicesGroup`/`CreditNotesGroup`. Verified by 22/22 functional tests. |
| `InvoicingErrors` matches `error-taxonomy.md § 3.6` | ✅ MET (with NOTE) | [InvoicingErrors.cs](../../../services/Invoicing/Invoicing.Domain/Common/Errors/InvoicingErrors.cs) ships all 4 ValidationError factories from § 3.6 + 2 typed `IError` records (`TotalMismatchError`, `PdfGenerationFailedError`). Plus 4 extensions not in § 3.6: `InvoiceForOrderNotFound`, `CreditNoteNotFound`, `CreditNoteRefersToCancelledInvoice`, `InvalidInvoiceTransition`, `InvalidCreditNoteTransition`. These extensions are well-motivated (e.g., the by-order lookup is in `<dod>`) and consistent with the error-code naming scheme — a defensible auto-evolution per `_shared.md § 8`. **Doc gap (LOW):** the spec code block in `error-taxonomy.md:243-276` does not enumerate the 4 extensions; the table at lines 45-51 implicitly covers them. |
| Integration tests cover all 4 example-mapping sessions + concurrency | ✅ MET | [`PendingInvoiceProjectionTests`](../../../test/Invoicing.IntegrationTests/Projections/PendingInvoiceProjectionTests.cs) + `PendingCreditNoteProjectionTests` cover Sessions 1 + 3 (convergent enrichment + cancel-after-capture). [`InvoiceNumberAllocatorTests`](../../../test/Invoicing.IntegrationTests/Allocators/InvoiceNumberAllocatorTests.cs) covers Session 2 (gap-free + rollback + year-rollover + concurrency). [`ResendInvoiceTests`](../../../test/Invoicing.FunctionalTests/ApiEndpoints/Invoices/ResendInvoiceTests.cs) covers Session 4 (resend idempotency). 32/32 integration + 22/22 functional. |
| Arch tests: no `DateTime.UtcNow` in domain; private-ctor + factory; no `Azure.Storage.Blobs` in Application | ✅ MET | [`Domain/NoStaticUtcNowInDomainTests.cs`](../../../test/Invoicing.ArchitectureTests/Domain/NoStaticUtcNowInDomainTests.cs), [`Domain/AggregateRootTests.cs`](../../../test/Invoicing.ArchitectureTests/Domain/AggregateRootTests.cs), [`Infrastructure/BlobStorageContainmentTests.cs`](../../../test/Invoicing.ArchitectureTests/Infrastructure/BlobStorageContainmentTests.cs) (Domain + Application both forbid `Azure.Storage` per [BlobStorageContainmentTests.cs:14-37](../../../test/Invoicing.ArchitectureTests/Infrastructure/BlobStorageContainmentTests.cs)). |
| docker-compose Azurite + `invoicing.invoices` topic with 10y retention | ✅ MET | [docker-compose.yaml:288](../../../docker-compose.yaml) `retention.ms=315360000000` = 10y. M10 `kafka-topics --describe` confirms `PartitionCount: 3 ReplicationFactor: 1 ... retention.ms=315360000000` ([invoicing-m10.md:188](invoicing-m10.md)). |
| Correlation-id roundtrips through enrichment projection | ✅ MET | See § Dimension 1 universal table. Spot-check confirmed by reading the M7 + M6 handler code. |
| All `<applicable_adrs>` enforced | ⚠️ PARTIALLY MET | Mostly enforced; **ADR-0010 scope-based gating reframed as role-based**. The dispatch prompt `<applicable_adrs>:83` cites scopes `invoicing.read` / `invoicing.admin.resend`. The implementation in [`AuthDependencyInjection.cs:54-59`](../../../services/Invoicing/Invoicing.Infrastructure/Common/AuthDependencyInjection.cs) and [`AuthPolicies.cs:11-22`](../../../services/Invoicing/Invoicing.Infrastructure/Common/Authorization/AuthPolicies.cs) ships **`RequireRole(Roles.Admin)`** for `InvoicingAdmin` (no scope claim) and **no `invoicing.read` scope check at all on buyer GETs** — instead the GET endpoints accept any authenticated user and enforce per-buyer authorization inside the query handler (see [GetInvoiceByIdEndpoint.cs:47-65](../../../services/Invoicing/Invoicing.Api/Endpoints/Invoices/GetInvoiceById/GetInvoiceByIdEndpoint.cs)). `AuthPolicies.cs:11-15` explicitly acknowledges this and defers scope-based gating to v2. **Security posture is sound** (admin role required; buyers see only their own invoices, with non-existence-leaking 404 on cross-buyer reads). **Doc drift:** M10 session summary line 52 overstates posture by citing scope names that are not in the code. |
| Peer-review chain executed; HIGH findings fixed | ✅ MET | See § Dimension 8. |

### Locked-contract spot-checks (5 invariants from `invoicing.md`)

| Invariant | Status | Citation |
|---|---|---|
| **I-1** `Total == Subtotal + sum(VatLines)` | ✅ enforced | [Invoice.cs:380-411](../../../services/Invoicing/Invoicing.Domain/Invoices/Invoice.cs) `ComputeTotals` computes both from inputs; `Invoice.Create` calls it at factory time. M7 handler additionally re-checks `invoice.Total.Amount != orderTotal` and throws `DataIntegrityException("Invoicing.InvoiceTotalDriftFromOrder")` ([IssueInvoiceCommandHandler.cs:200-206](../../../services/Invoicing/Invoicing.Application/Invoices/IssueInvoice/IssueInvoiceCommandHandler.cs)). |
| **I-2** `Lines` non-empty | ✅ enforced | [Invoice.cs:108-112](../../../services/Invoicing/Invoicing.Domain/Invoices/Invoice.cs) `throws DataIntegrityException("Invoicing.EmptyLines")` if `lines.Count == 0`. |
| **I-3** `InvoiceNumber` immutable post-allocation | ✅ enforced | [Invoice.cs:177-184](../../../services/Invoicing/Invoicing.Domain/Invoices/Invoice.cs) — second assignment throws `DataIntegrityException("Invoicing.InvoiceNumberAlreadyAssigned")`. M7 handler uses split `AssignInvoiceNumber` + `Issue(pdfBlobRef)` flow ([IssueInvoiceCommandHandler.cs:211, 225](../../../services/Invoicing/Invoicing.Application/Invoices/IssueInvoice/IssueInvoiceCommandHandler.cs)). |
| **I-4** `PdfBlobRef` write-once | ✅ enforced | `Issue(pdfBlobRef, utcNow)` runs `Status.CanTransitionTo(Issued)` first; re-issue rejected by FSM ([Invoice.cs:242-246](../../../services/Invoicing/Invoicing.Domain/Invoices/Invoice.cs)). CreditNote.Issue additionally throws `DataIntegrityException("Invoicing.CreditNoteAlreadyIssued")` on `PdfBlobRef is not null` ([CreditNote.cs:216-221](../../../services/Invoicing/Invoicing.Domain/CreditNotes/CreditNote.cs)). |
| **I-CN-1** Credit-note against `Cancelled` invoice forbidden | ✅ enforced | [IssueCreditNoteCommandHandler.cs:117-121](../../../services/Invoicing/Invoicing.Application/CreditNotes/IssueCreditNote/IssueCreditNoteCommandHandler.cs) returns `Result.Fail(InvoicingErrors.CreditNoteRefersToCancelledInvoice(...))`. Defence-in-depth at [CreditNote.cs:86-91](../../../services/Invoicing/Invoicing.Domain/CreditNotes/CreditNote.cs) `throws DataIntegrityException` if it slipped past the handler. |

### Dimension 1 findings

| Sev | file:line | Description | Recommendation |
|---|---|---|---|
| MEDIUM | [invoicing-m10.md:52](invoicing-m10.md) | M10 session summary claims admin endpoint is "JWT-gated by `AuthPolicies.InvoicingAdmin` (scope `invoicing.admin.resend`)" and buyer GETs "gated by scope `invoicing.read`". Code ships role-based (`RequireRole(Roles.Admin)`) with no scope claim check on buyer endpoints. AuthPolicies.cs:11-15 acknowledges scope-based is v2. Implementation is safe; doc overstates. | Either tighten the closeout document to match shipped posture (role-based, scopes deferred to v2) OR open a follow-up task to add scope-claim checks per ADR-0010. |
| MEDIUM | [architecture-tests.md](../../bc-design/architecture-tests.md) (no Invoicing § 2) | `architecture-tests.md § 2 Per-BC Specific Rules` has sections for Catalog (2.1), Basket (2.2), Ordering (2.3), Inventory (2.4) but **no Invoicing section**. The 29 shipped arch facts (PII allowlist, blob containment, PDF containment, no-static-utc-now, etc.) outstrip the universal § 1 rules. Doc drift the M9 + M10 sweeps both missed. | Add an Invoicing § 2.5 to `architecture-tests.md` enumerating the 29 facts — particularly the BC-specific Pii/Blob/Pdf rules that go beyond § 1. |
| MEDIUM | [use-cases.md:1509](../../bc-design/use-cases.md) (no § 6) | `<reading_order>` step 6 (and `invoicing.md` `<reading_order>:72`) points to "`use-cases.md` § 6 (commands + queries)". Doc ends at § 5. | Add § 6 Invoicing to `use-cases.md`, mirroring the Catalog/Basket/Ordering/Inventory shape; or update the reading-order to point at `invoicing.md § 7` as the canonical source. |
| LOW | [events-catalog.md § 5.x](../../bc-design/events-catalog.md) | `events-catalog.md` enumerates Avro-schema files for Catalog/Basket/Ordering/Inventory (§ 5.1–5.4) and the saga commands (§ 5.5–5.6) but **no dedicated § 5.x for the 4 Invoicing events**. The 4 events appear only in the § 2 master table + § 3 topic table. Schemas live correctly under `Avro/Invoicing/`. | Optional but useful — add § 5.7 Invoicing External Events with the 3 shipped schemas + the deferred 4th. |
| LOW | [events-catalog.md § 4 (lines 156-190)](../../bc-design/events-catalog.md) | Docker-compose Delta block does not include the `invoicing.invoices` topic. Topic was correctly added to `docker-compose.yaml:288` (with the 10y retention) — canonical delta-doc is stale. | Add the missing line to § 4 for completeness. |
| LOW | [error-taxonomy.md:49](../../bc-design/error-taxonomy.md) | Table row says `BlobUploadFailed` is "Retried via Polly (3 attempts); DLT after exhaustion". Implementation uses `Azure.Storage.Blobs` SDK-level retries per ADR-0017's `<design_open>` resolution (correctly — the dispatch prompt explicitly forbids adding a Polly pipeline). Doc says Polly, code does SDK. | Update the table row to reflect SDK-level retries per ADR-0017. |

---

## Dimension 2 — Architecture

**PASS.** Clean-Architecture layer boundaries enforced by NetArchTest. Layer-test bodies are non-trivial (6 facts in [CleanArchitectureLayerTests.cs](../../../test/Invoicing.ArchitectureTests/CleanArchitecture/CleanArchitectureLayerTests.cs) — every direction: Domain ⟂ {App, Infra, Api}, App ⟂ {Infra, Api}, Infra ⟂ Api). Cross-BC reference rules covered by [`CrossBoundedContext/NoCrossBcReferenceTests.cs`](../../../test/Invoicing.ArchitectureTests/CrossBoundedContext/NoCrossBcReferenceTests.cs). DDD layout (Domain owns aggregates + VOs + events; Application owns ports `IBlobStore`, `IPdfGenerator`, `IInvoiceNumberAllocator`, `IInvoicingDbContext`; Infrastructure owns adapters `AzureBlobStore`, `QuestPdfInvoiceGenerator`, `PostgresInvoiceNumberAllocator`, `InvoicingDbContext`) holds end-to-end. Outbox publishers live in Application — correct (they're domain-event handlers, not infrastructure).

29/29 architecture tests pass (see § Dimension 7 output). Re-read the actual assertions in [`Pii/OtelTagAllowlistTests.cs`](../../../test/Invoicing.ArchitectureTests/Pii/OtelTagAllowlistTests.cs) + [`Rules/NoForbiddenActivityTagKeysRule.cs`](../../../test/Invoicing.ArchitectureTests/Rules/NoForbiddenActivityTagKeysRule.cs) — the rule is a real Mono.Cecil IL walk that detects `Activity.SetTag/AddTag/ActivityTagsCollection.Add` calls and rejects forbidden literal keys (exact + suffix + prefix) — substantive guard, not a trivial "always passes" assertion. Similar quality in `BlobStorageContainmentTests` and `PdfGenerationContainmentTests`.

---

## Dimension 3 — Design (DDD)

**PASS.** Aggregate boundaries match `invoicing.md § 2`: `Invoice` and `CreditNote` are separate aggregates; `CreditNote.OriginalInvoiceId` references by Guid only (no aggregate-to-aggregate object reference). Invariants enforced inside the aggregate, not in handlers: I-1 (`ComputeTotals` + post-create assertion), I-2 (empty-lines reject), I-3 (`AssignInvoiceNumber` double-write reject), I-4 (FSM-gated transition), I-5 (`InvoiceStatus.CanTransitionTo`), I-6 (`Cancel` requires non-empty `creditNoteId`). Aggregate factories return `Result<T>` (or throw `DataIntegrityException` for bug-class — well-distinguished per the result-vs-exception convention in the BC's CLAUDE.md). Private constructors (default-empty for EF only) + static factories. Domain events sealed records inheriting `Platform.SharedKernel.Base.DomainEvents.DomainEvent`. Internal vs external event split honored — internal `*DomainEvent` records never serialized to Kafka; the outbox publishers map to dedicated external Avro types ([InvoiceIssuedMapper.cs](../../../services/Invoicing/Invoicing.Application/Outbox/InvoiceIssuedMapper.cs) etc.).

Value objects: `InvoiceNumber` + `CreditNoteNumber` immutable records with regex validation; `InvoiceLine`/`VatLine` immutable; `PdfBlobRef` content-addressed; `Address` reused from `Platform.SharedKernel`. `InvoiceStatus`/`CreditNoteStatus`/`DeliveryChannel`/`CreditNoteReason` are `Ardalis.SmartEnum` — correct choice for bounded state machines.

---

## Dimension 4 — Testing

**PASS.** Pyramid sane: 96 unit + 29 architecture + 32 integration + 22 functional = **179/179**. Unit tests target invariants, FSM transitions, VO construction, and number-format regex — exactly what a unit slice should own. Integration tests own everything that needs a real DB / Kafka / Azurite — allocator concurrency, projection convergence (both orderings + duplicate + total-mismatch), credit-note flow. Functional tests own HTTP shape + auth + idempotency-key behavior.

Test names express behavior (e.g., `Example_1_1_OrderConfirmed_Then_PaymentCaptured_…`). Each command has a handler test (integration slice) AND a validator test (unit slice). Each external event has an outbox-publisher test (M7 commit set). Architecture tests are substantive (Mono.Cecil IL walks where needed, not just naming-convention assertions).

Spot-checks for common antipatterns:
- **`CancellationToken.None` in xunit.v3 tests** — Grep found 0 occurrences in `test/Invoicing.*Tests/**` using `CancellationToken.None` in test bodies. Tests use `TestContext.Current.CancellationToken` per the xUnit1051 fix-up captured during M9.
- **Brittle string matching on error messages** — handlers return typed `ValidationError`/`IError` records with stable `ErrorCode` metadata (e.g., `"Invoicing.InvoiceAlreadyIssued"`); functional tests can assert on the code rather than the message.
- **Testcontainers used for integration** — confirmed by [`AzuriteFixture.cs`](../../../test/Invoicing.IntegrationTests/Blobs/AzuriteFixture.cs) and [`IntegrationTestFixture.cs`](../../../test/Invoicing.IntegrationTests/Common/IntegrationTestFixture.cs).
- **Mocks-where-real-wiring-matters** — none found; integration suite uses real Postgres + Kafka + Azurite via Testcontainers.

---

## Dimension 5 — Event-driven best practices

**PASS.** Outbox is the only path producing external events. The 3 outbox publishers ([`InvoiceIssuedOutboxPublisherDomainEventHandler`](../../../services/Invoicing/Invoicing.Application/Outbox/InvoiceIssuedOutboxPublisherDomainEventHandler.cs), `CreditNoteIssuedOutboxPublisherDomainEventHandler`, `InvoiceCancelledOutboxPublisherDomainEventHandler`) call `_outbox.AddOutboxMessage(...)` from inside the EF transaction (they're `IDomainEventHandler<TDomainEvent>` instances dispatched by [`DispatchDomainEventsInterceptor`](../../../services/Invoicing/Invoicing.Infrastructure/Persistence/Database/Interceptors/DispatchDomainEventsInterceptor.cs) immediately before `SavingChangesAsync`). No direct `IProducer<...>` references anywhere in the BC — the only place Kafka is "produced" is via the outbox writer.

Inbox dedup wired on **both** Kafka consumers (`AddInbox(typeof(...), typeof(...))` at [MessagingDependencyInjection.cs:112, 135](../../../services/Invoicing/Invoicing.Infrastructure/Common/MessagingDependencyInjection.cs)). The 4 projection handlers themselves also short-circuit on "this half already populated" — defence-in-depth.

Avro schemas conform to ADR-0007 FORWARD_TRANSITIVE compatibility: all fields with non-trivial defaults / nullable unions or new fields added with `default: null`. Field names + types in the 3 `.avsc` files match the dispatch-prompt `<contract>` (`BuyerId` partition key, decimal-19-4 totals, timestamp-millis dates, uuid logical types). The `Subtotal`/`Total` fields are `bytes` + `decimal logicalType` — correct for money.

Correlation-id flow verified end-to-end: HTTP → consumer middleware (`AddCorrelationIdConsumerMiddleware`) → `pending_invoices.correlation_id` → `invoices.correlation_id` (defence-in-depth unique-index) → outbox row → emitted Avro event header (via platform `Platform.ReliableMessaging.Outbox.EFCore`'s tracing propagation). ADR-0008 honored.

No internal `*DomainEvent` types are serialized to Kafka — outbox publishers map to dedicated Avro records. No cross-BC consumption of another BC's internal events — Invoicing only consumes Ordering's `OrderConfirmedEvent`/`OrderCancelledEvent` and Payments' `PaymentCapturedEvent`/`PaymentRefundedEvent`, which are all public external events on their respective topics.

ADR-0013 `.Idempotency()` wired on the one state-changing endpoint ([ResendInvoiceEndpoint.cs:40](../../../services/Invoicing/Invoicing.Api/Endpoints/Invoices/ResendInvoice/ResendInvoiceEndpoint.cs)).

---

## Dimension 6 — .NET / C# best practices

**PASS** with one LOW. Async-all-the-way-down — every `Task`/`Task<T>` returning method takes a `CancellationToken` parameter and forwards it. Spot-checked: command handlers, query handlers, Kafka handlers, blob store, allocator. No `.Result` / `.Wait()` in production code. `ArgumentNullException.ThrowIfNull` + `ArgumentException.ThrowIfNullOrWhiteSpace` used consistently. Generic Host registers `TimeProvider` (M9 captures this) — no `DateTime.UtcNow` slipping into Domain or Pdf surfaces.

Connection-string keys + topic names live in typed options classes ([`ConnectionStringsOptions.cs`](../../../services/Invoicing/Invoicing.Infrastructure/Common/Config/ConnectionStringsOptions.cs), [`TopicsOptions.cs`](../../../services/Invoicing/Invoicing.Application/Common/Messaging/TopicsOptions.cs)) — no magic strings. Logging uses scope-pushed `LogContext.PushProperty("CorrelationId", ...)` / `_logger.BeginScope` with structured properties — no PII (only `BuyerId`/`OrderId`/`InvoiceId`/`CorrelationId`/`IsAdmin`). Nullable reference types enabled across the BC.

### Dimension 6 findings

| Sev | file:line | Description | Recommendation |
|---|---|---|---|
| LOW | [AzureBlobStore.cs:140](../../../services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs) | `BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(expiry))` uses `DateTimeOffset.UtcNow` instead of an injected `TimeProvider`. Outside the strict-prohibition zone (the arch test [`NoStaticUtcNowInDomainTests`](../../../test/Invoicing.ArchitectureTests/Domain/NoStaticUtcNowInDomainTests.cs) only enforces Domain + `Infrastructure.Pdf.*`), so build is green. Functional impact is zero — SAS expiry is wall-clock-bound by design. Stylistic inconsistency only. | Optional: thread `TimeProvider` into `AzureBlobStore` for consistency. The current arch test could also be extended to forbid `*.UtcNow` in `Infrastructure.Blobs.*` if the project wants TimeProvider universally. |

---

## Dimension 7 — CI gates + test slices (verbatim)

All commands run with `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy` per [CLAUDE.md](../../../CLAUDE.md) option B (corporate-proxy host).

### Gate 1 — `dotnet restore --locked-mode`

```text
... 53 NU1903 transitive vulnerability warnings (System.Security.Cryptography.Xml 10.0.1,
    Microsoft.Kiota.Abstractions 1.19.0) across Weather, Catalog, Inventory, Ordering,
    Invoicing, Payments, saga, platform projects. Pre-existing baseline — same as
    basket-m9 / catalog-m8 / payments-m9 / inventory-m10. NOT Invoicing-introduced.
  Všechny projekty jsou v aktuálním stavu pro obnovení.
exit: 0
```

### Gate 2 — `dotnet build -m --no-restore`

```text
... same 53 NU1903 warnings, no new diagnostics.
    53 upozornění
    Počet chyb: 0
Uplynulý čas 00:02:43.85
exit: 0
```

### Gate 3 — `dotnet format whitespace --no-restore --verify-no-changes`

```text
Při načítání pracovního prostoru se vygenerovala upozornění... (workspace-load info only).
exit: 0
```

### Gate 4 — `dotnet format style --no-restore --verify-no-changes`

```text
Při načítání pracovního prostoru se vygenerovala upozornění... (workspace-load info only).
exit: 0
```

### Test slice 1 — `dotnet test test/Invoicing.UnitTests/`

```text
Verze VSTest 18.0.1 (x64)
Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1
Úspěšné!    - Neúspěšné:     0, Úspěšné:    96, Přeskočeno:     0, Celkem:    96, Doba trvání: 115 ms - Invoicing.UnitTests.dll (net10.0)
exit: 0
```

### Test slice 2 — `dotnet test test/Invoicing.ArchitectureTests/`

```text
Verze VSTest 18.0.1 (x64)
Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1
Úspěšné!    - Neúspěšné:     0, Úspěšné:    29, Přeskočeno:     0, Celkem:    29, Doba trvání: 681 ms - Invoicing.ArchitectureTests.dll (net10.0)
exit: 0
```

### Test slice 3 — `dotnet test test/Invoicing.IntegrationTests/` (Testcontainers)

```text
Verze VSTest 18.0.1 (x64)
Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1
Úspěšné!    - Neúspěšné:     0, Úspěšné:    32, Přeskočeno:     0, Celkem:    32, Doba trvání: 12 s - Invoicing.IntegrationTests.dll (net10.0)
exit: 0
```

### Test slice 4 — `dotnet test test/Invoicing.FunctionalTests/` (Testcontainers)

```text
Verze VSTest 18.0.1 (x64)
Začínají se provádět testy, počkejte prosím...
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1
Úspěšné!    - Neúspěšné:     0, Úspěšné:    22, Přeskočeno:     0, Celkem:    22, Doba trvání: 3 s - Invoicing.FunctionalTests.dll (net10.0)
exit: 0
```

**Total: 179 / 179 — matches M10 baseline verbatim; zero regression.**

---

## Dimension 8 — Code review (parallel dispatch)

Independent Opus reviewer (`feature-dev:code-reviewer`, `model="opus"`) dispatched on the final state of `services/Invoicing/**` + Avro schemas. Reviewer walked all 4 layers (Domain aggregates, M7 issuance handlers, gap-free allocators, 3 outbox publisher/mapper pairs, `DispatchDomainEventsInterceptor`, 4 enrichment Kafka consumers with `AddInbox(...)`, resend endpoint's `.Idempotency()` + `Policies(InvoicingAdmin)`, buyer-scoped GETs, EF configurations including `_enc` PII suffixes + `xmin` row-version + unique indexes, QuestPDF generators + `IDocument`s, `AzureBlobStore` adapter behind the `IBlobStore` port, the 3 Avro schemas, and DI composition).

### Reviewer verdict

```
## Verdict: PASS
## Counts: CRITICAL=0, HIGH=0, MEDIUM=0, LOW=0
```

Reviewer confirmed:

- 4-state Invoice FSM + 3-state CreditNote FSM with cancellation off-ramp and I-6 enforcement (`Result.Fail` on transition errors, `DataIntegrityException` on bug-class).
- 3 of 4 external Avro events ship — `InvoiceDelivered` is the documented carry-forward.
- All routes under `/api/v1/invoicing/...`; the single POST is `/invoices/{invoiceId}/resend` with `.Idempotency()` (Idempotency-Key header + Redis output-cache).
- Numbering: both allocators enforce enclosing-transaction (`Database.CurrentTransaction is null` hard-fail), use `SELECT … FOR UPDATE`, and `INSERT … ON CONFLICT DO NOTHING` + re-select for year-rollover race. Defence-in-depth unique partial indexes on both number columns.
- ADR-0008 correlation-id roundtrip end-to-end.
- ADR-0011 PII: `billing_address_*_enc` columns on Invoice; no PII keys in any logged scope.
- ADR-0015 time: no `DateTime.UtcNow` / `DateTimeOffset.UtcNow` in Domain or `Infrastructure.Pdf.*`. Single `DateTimeOffset.UtcNow` is in `AzureBlobStore.BuildSasUri:140` — outside the strict-prohibition zone (matched independently above as a LOW under Dimension 6).
- ADR-0017 blob storage: `IBlobStore` lives in Application; only `Invoicing.Infrastructure.Blobs` references `Azure.Storage.Blobs.*`. SAS TTL is 10 minutes; content-addressed SHA-256 hash on `PdfBlobRef`.
- ADR-0018 gap-free numbering: allocator runs inside the same EF transaction as `_db.Invoices.Add(invoice)` + `pending.IssuedInvoiceId = invoice.Id` + outbox row write. A blob-upload failure between allocation and `SaveChanges` rolls back the transaction → number is NOT consumed.
- ADR-0019 PDF determinism: QuestPDF Community license set in static ctor; `CreationDate`/`ModifiedDate` derived from `Invoice.IssueDate.UtcDateTime`; constant `Creator`/`Producer`; `CultureInfo.InvariantCulture` everywhere.
- ADR-0020 summary events: `OrderPayload` jsonb persists `Items`/`TotalAmount`/`Currency`/`BillingAddress` from `OrderConfirmedEvent` so M7 rehydrates without HTTP round-trip.
- Cross-aggregate consistency (`Order.Total ≡ Payment.Amount ≡ Invoice.Total`): `IssueInvoiceCommandHandler.cs:140` throws `DataIntegrityException("Invoicing.TotalMismatch")` for `orderTotal != paymentPayload.Amount` or currency mismatch BEFORE allocation; line 200 additionally re-checks `invoice.Total.Amount != orderTotal` AFTER `Invoice.Create` so any v1 simplification drift fails loud (DLT).

The reviewer's bottom-line: "The codebase has been reviewed milestone-by-milestone and shows it. No actionable issues at the ≥80 confidence bar."

---

## Verdict & thresholds

- **PASS** would require: zero CRITICAL, zero unaccepted HIGH, all DoD MET, all gates green.
- **CONDITIONAL-PASS** allows: zero CRITICAL, ≤ N HIGH documented as accepted carry-forwards, DoD MET or PARTIALLY MET with rationale.
- **FAIL** requires: any CRITICAL, OR any DoD NOT MET without acceptance, OR any test red, OR contract-locked seam drifted.

Counts: **CRITICAL = 0, HIGH = 0, MEDIUM = 3, LOW = 4**.

Two DoD lines ship PARTIALLY MET (the 4 external Avro events / 4 outbox publishers — both pegged to `InvoiceDelivered` which is documented as carry-forward; ADR-0010 enforcement scope-vs-role with `AuthPolicies.cs` openly noting the v2 deferral). No CRITICAL findings, no HIGH findings, zero failing tests, zero failing gates, no contract-locked seam drift.

→ **Verdict: CONDITIONAL-PASS.**

---

## Punch list (ordered, actionable)

These are out-of-`<boundaries>` doc edits + one consistency tweak — none block declaring the BC complete; all carry-forwards belong on the v2 / DEVOPS backlog.

1. **[MEDIUM]** Update [invoicing-m10.md:52](invoicing-m10.md) to match shipped ADR-0010 posture: role-based `RequireRole(Roles.Admin)` for admin, handler-side per-buyer authorization on GETs; explicitly cite the v2 deferral that `AuthPolicies.cs:11-15` already owns.
2. **[MEDIUM]** Add an Invoicing § 2.5 to [`docs/bc-design/architecture-tests.md`](../../bc-design/architecture-tests.md) documenting the 29 shipped facts (Pii allowlist, BlobStorage containment, PdfGeneration containment, NoStaticUtcNowInDomain, CleanArchitecture layer rules, CommandHandler/QueryHandler discipline, etc.).
3. **[MEDIUM]** Either add a § 6 Invoicing block to [`docs/bc-design/use-cases.md`](../../bc-design/use-cases.md) (preferred — mirror Ordering / Inventory shape) or update both `_shared.md § 2` and `invoicing.md <reading_order>:72` to retire the broken pointer.
4. **[LOW]** Add a § 5.7 Invoicing External Events block to [`docs/bc-design/events-catalog.md`](../../bc-design/events-catalog.md) enumerating the 3 shipped + 1 deferred schema.
5. **[LOW]** Add the `invoicing.invoices` topic line to [`docs/bc-design/events-catalog.md § 4`](../../bc-design/events-catalog.md) Docker-compose Delta block (already correctly in `docker-compose.yaml:288`).
6. **[LOW]** Update [`docs/bc-design/error-taxonomy.md:49`](../../bc-design/error-taxonomy.md) to say "Azure.Storage.Blobs SDK retries (exponential backoff); DLT after exhaustion" per ADR-0017 `<design_open>` — current text says "Polly (3 attempts)" which is contradicted by the dispatch prompt and the shipped code.
7. **[LOW]** Consider extending [`NoStaticUtcNowInDomainTests`](../../../test/Invoicing.ArchitectureTests/Domain/NoStaticUtcNowInDomainTests.cs) (or adding a new rule) to forbid `*.UtcNow` in `Invoicing.Infrastructure.Blobs.*` for consistency with the existing `Infrastructure.Pdf.*` rule. Cost: ~1 line of code in [AzureBlobStore.cs:140](../../../services/Invoicing/Invoicing.Infrastructure/Blobs/AzureBlobStore.cs) to thread `TimeProvider` through; arch test then guards.

Out-of-scope-but-noteworthy carry-forwards (already captured in [invoicing-m10.md § Improvements proposed](invoicing-m10.md)): ship the 4th `InvoiceDeliveredEvent` + publisher when a downstream consumer is ready; resolve `otel-collector` `attributes/pii-allowlist` processor (platform); NU1903 baseline cleanup; promote CLAUDE.md Testcontainers § option B; `invoicing.api` compose service (DEVOPS parity); `nw-mutation-test` post-green pass; `invoicing.enrichment.lag.seconds` metric; partial-refund credit notes (v2).

---

**Invoicing BC ships clean.** The 10-milestone arc on branch `aaqwdqwd` has produced a fiscal-records service that honors gap-free numbering, write-once PDF blobs, total-mismatch DLT discipline, 10-year retention semantics, ADR-0011 PII posture, and a transactional outbox-only external-event path. Carry-forward items belong to v2 work — not to closing out v1.
