# Master System Prompt — Implement the **Invoicing** Bounded Context

> Paste this as the first message in a fresh Claude Code session for `C:\Users\dcapc\Desktop\Git\DotNetAtlas`.

<thinking_first>
Before writing any code, do these in your **first response** — explicitly, in order:

1. **Read every file under `<reading_order>`** in order. State your understanding of what's locked vs open.
2. **Verify prerequisites.** List anything in `<prerequisites>` that isn't satisfied. STOP and ask if any.
3. **Surface contradictions** (`file:line`).
4. **Confirm applicable ADRs.** For each ADR in `<applicable_adrs>`, name what it implies for THIS BC's code.
5. **State your plan.** Group `<dod>` items into commit milestones. Confirm with the user before starting code.
6. **Acknowledge stop conditions** from this prompt and `_shared.md § 9`.
</thinking_first>

<mission>
You implement the **Invoicing** bounded context greenfield under `services/Invoicing/` (4-layer). When the session ends, issuing a confirmed+paid order produces a PDF invoice stored in Azurite (production: Azure Blob Storage), emitted as `InvoiceIssued` on `invoicing.invoices`, and retrievable via an Azure SAS URL through the local nginx-cdn. Credit notes flow on cancel-after-capture.
</mission>

<prerequisites>
- Wave 0 platform prep merged. Specifically: `azurite` + `azurite-init` + `nginx-cdn` containers running; `invoicing.invoices` topic with 10-year retention; `outbox-relay-invoicing` container; Keycloak `invoicing-service` client; `Platform.ServiceDefaults` has correlation-id + service-auth + JSON `DateTimeOffset` converter.
- Ordering + Payments BCs have published their Avro schemas (Invoicing consumes `OrderConfirmedEvent`, `OrderCancelledEvent`, `PaymentCapturedEvent`, `PaymentRefundedEvent`). Invoicing can scaffold + unit-test in parallel; integration-test the consumers after Ordering + Payments schemas land.
</prerequisites>

<role_in_system>
Invoicing is the **authority for fiscal records**. Teaching patterns (novel vs other BCs):

1. **Document generation + write-once blob** (QuestPDF → Azurite/Azure Blob → nginx-cdn / Front Door)
2. **Legal retention** (10-year topic retention for `invoicing.invoices`)
3. **Gap-free numeric sequencing** (transactional allocator — Postgres row-lock)
4. **Idempotent external re-emission** (delivery-attempt log)
5. **Async multi-source enrichment** (`pending_invoices` projection instead of a saga)

Upstream consumers: `OrderConfirmedEvent` (Ordering) + `PaymentCapturedEvent` (Payments) for invoice issuance; `OrderCancelledEvent` + `PaymentRefundedEvent` for credit notes.
Downstream publishers: `InvoiceIssued`, `InvoiceDelivered`, `InvoiceCancelled`, `CreditNoteIssued` → consumed by Notifications (email) + BFF (cache).
</role_in_system>

<contract>
LOCKED at the seams.

- Two aggregates: `Invoice` (4-state FSM: `Draft → Issued → Delivered → Archived`; `Cancelled` off-ramp) + `CreditNote` (3-state: `Issued → Delivered → Archived`)
- VO: `InvoiceNumber` format `INV-YYYY-NNNNNN`; `CreditNoteNumber` format `CN-YYYY-NNNNNN`
- 4 external Avro events on topic `invoicing.invoices` (10-year retention, partition key `BuyerId`)
- HTTP routes under `/api/v1/invoicing/...` per ADR-0012
- `InvoicingErrors` per `error-taxonomy.md § 3.6`
- PDF library: **QuestPDF** (MIT; v1 community edition) per [ADR-0019](../adr/0019-pdf-generation-questpdf.md)
- Blob storage: Azurite container `invoices`; SAS URLs (10-minute TTL); content-addressed via SHA-256; SDK `Azure.Storage.Blobs` per [ADR-0017](../adr/0017-blob-storage-cdn.md)
- Enrichment projection tables: `invoicing.pending_invoices` + `invoicing.pending_credit_notes`
- Gap-free number allocator: `invoicing.invoice_number_allocator` + `invoicing.credit_note_number_allocator` with `SELECT ... FOR UPDATE` per [ADR-0018](../adr/0018-invoice-numbering.md)
- File ownership: see `<boundaries>`
</contract>

<design_open>
You own these. Justify each in your session summary.

- `Invoice` aggregate backing-field layout (EF owned entities? JSON column for `Lines`? — decide + justify)
- `InvoiceDocument` PDF template composition (QuestPDF fluent DSL) — logo placement, column widths, footer content
- Enrichment consumer class organization — one consumer per event type or multiplexer (recommended: one per type for clarity)
- Blob-upload retry strategy when Azurite/Azure Blob is transiently unavailable — use `Azure.Storage.Blobs`' built-in retry options (SDK-level retries, exponential backoff) rather than adding a Polly pipeline. Cross-service HTTP resilience is handled by YARP at the edge.
- Admin endpoint authorization policy names (`AuthPolicies.InvoicingAdmin`)
- Idempotency-Key integration on `POST /invoices/{id}/resend` via FastEndpoints `.Idempotency()` backed by ASP.NET Output Cache + `redis-cache` per ADR-0013
- Credit-note-on-partial-refund behavior (v1 is full-refund only; v2 hook spot)
- Additional example-mapping sessions if you surface edge cases (especially around projection timeouts)
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/invoicing.md` — the full spec
3. `docs/bc-design/glossary-invoicing.md` + `example-mapping/invoicing.md`
4. `docs/bc-design/events-catalog.md` § 2 (Invoicing events + consumption of Ordering/Payments events)
5. `docs/bc-design/error-taxonomy.md § 3.6` (`InvoicingErrors`)
6. `docs/bc-design/use-cases.md` § 6 (commands + queries)
7. `docs/bc-design/payments.md` + `docs/bc-design/ordering.md` — you consume events from both; understand their external event shapes
8. `docs/adr/0007-avro-compatibility-modes.md`
9. **All ADRs in `<applicable_adrs>` below** (including 0017, 0018, 0019 which are Invoicing-specific)
10. QuestPDF docs (if confirming fluent DSL details) — context7 `query-docs`
</reading_order>

<applicable_adrs>
Cross-cutting decisions to apply:

- [ADR-0008](../adr/0008-correlation-id-propagation.md) — every consumer reads `CorrelationId` from the inbound Kafka header; persists into `pending_invoices.correlation_id` + `invoices.correlation_id`; outbox publishers copy it into emitted events
- [ADR-0010](../adr/0010-service-to-service-auth.md) — inbound JWT validation for admin endpoints (scope `invoicing.read` / `invoicing.admin.resend`); no outbound HTTP calls to other BCs (only event consumption)
- [ADR-0011](../adr/0011-pii-handling-gdpr.md) — `BillingAddress` + buyer name are PII; columns named `*_enc` per convention (v1 plaintext, v2 encrypts); Serilog `[PII]` on `Address`; **OTEL allowlist forbids tagging spans with address fields** (`invoice.billing_address` → hashed `buyer.id.hash` only); 10-year topic retention carries this PII — acknowledge the known v1 gap
- [ADR-0012](../adr/0012-api-versioning.md) — all routes under `/api/v1/invoicing/...`
- [ADR-0013](../adr/0013-idempotency-key-http.md) — **required on `POST /invoices/{id}/resend`** (admin resend) via FastEndpoints `.Idempotency()` backed by `redis-cache`; not required on GET endpoints
- [ADR-0015](../adr/0015-time-timezone-policy.md) — `Invoice.IssueDate`, `DeliveredAt`, `CancelledAt` all `DateTimeOffset`; inject BCL `TimeProvider` and pass `GetUtcNow().Year` into the `invoice_number_allocator` for deterministic year derivation in tests (`FakeTimeProvider`); arch test forbids `DateTime.UtcNow` in `Invoicing.Domain`
- [ADR-0017](../adr/0017-blob-storage-cdn.md) — `IBlobStore` abstraction in `Invoicing.Infrastructure`; adapter uses `Azure.Storage.Blobs` against Azurite locally / real Azure Blob in production; **architecture test forbids direct `Azure.Storage.Blobs` imports in Application or Domain layers**
- [ADR-0018](../adr/0018-invoice-numbering.md) — transactional allocator with `SELECT ... FOR UPDATE`; rollback preserves gap-free sequence; nightly audit query verifies `COUNT(invoices) == next_value - 1`
- [ADR-0019](../adr/0019-pdf-generation-questpdf.md) — QuestPDF community edition; `IPdfGenerator` abstraction; deterministic output verified by byte-hash test; embed Inter + JetBrains Mono fonts via Docker image
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. Invoicing-specific:

| Phase | Skill | When |
|---|---|---|
| Designing the enrichment projection | `superpowers:brainstorming` | FIRST — async multi-source composition has subtle dedup edges; explore options before implementing |
| Projection consistency checks | `backend-development:projection-patterns` | if you're unsure about idempotency semantics in the consumer handlers |
| PDF template | `context7` (`query-docs` for QuestPDF) | the fluent DSL is non-obvious; pull recent docs |
| Blob storage integration | `backend-development:api-design-principles` | `IBlobStore` abstraction design |
| Gap-free sequencing | `superpowers:systematic-debugging` | if concurrency tests fail, debug the allocator rigorously |
</skills>

<autonomous_evolution>
Invoicing-specific triggers:

- **`Invoice.Total` vs `Order.Total` vs `Payment.Amount` consistency** — enforce in `IssueInvoiceCommandHandler`. If mismatch: throw `DataIntegrityException` → routes to DLT → alerts ops. This is `example-mapping/invoicing.md § 1.4` — implement as described.
- **Stuck projections** — if `pending_invoices` row has `FirstSeenAtUtc` > N minutes ago and `CompletedAtUtc IS NULL`, something went wrong. V1: log a warning periodically. V2: a cleanup / alerting job. Add a metric `invoicing.enrichment.lag.seconds` so future v2 work has data.
- **PDF regeneration** — if bucket is wiped in dev, regenerating a PDF must produce byte-identical output (deterministic QuestPDF usage). Verify this in a test; if not, document why (fonts? locale? ADR-0015 timestamp source?).
- **Partial credit notes** — leave the hook open in `CreditNoteReason` SmartEnum + `Amount` field on `IssueCreditNoteCommand`, but reject non-full refunds with `InvoicingErrors.PartialRefundNotSupportedV1` until v2.
- **If you surface a missing case** (e.g., "order confirmed, payment captured, then Ordering cancels before credit note issues"): add an example-mapping session.
</autonomous_evolution>

<success_criteria>
- Firing `OrderConfirmedEvent` + `PaymentCapturedEvent` (in any order) produces exactly one invoice with gap-free `InvoiceNumber` and a retrievable PDF via SAS URL through nginx-cdn.
- `Invoice.Total == Order.Total == Payment.Amount` enforced; mismatch DLTs with ops alert.
- `IssueCreditNoteCommand` on cancel-after-capture produces exactly one credit note; Invoice `Status → Cancelled` atomically.
- Byte-hash test of PDF output is green (deterministic) across two regenerations.
- PII-handling tests pass (arch test for allowlist + `[PII]` attribute usage).
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] 4-layer solution structure scaffolded under `services/Invoicing/`, `.slnx` updated, `dotnet build -m` green
- [ ] 4 external Avro events + no commands under `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/`
- [ ] 7 internal `*DomainEvent` records + outbox publishers for external events
- [ ] 4 consumers (`OrderConfirmed`, `PaymentCaptured`, `OrderCancelled`, `PaymentRefunded`) with inbox dedup
- [ ] Enrichment projections (`pending_invoices`, `pending_credit_notes`) with idempotent upserts
- [ ] Gap-free `InvoiceNumber` + `CreditNoteNumber` allocators (verified by concurrency test — two parallel issuances serialize correctly)
- [ ] QuestPDF template produces a byte-deterministic PDF for a fixed input (verified by a hash test)
- [ ] Blob upload to Azurite + Azure SAS GET URL through nginx-cdn works end-to-end
- [ ] HTTP endpoints under `/api/v1/invoicing/` — `GET /invoices/{id}`, `GET /invoices?page=…`, `GET /invoices/by-order/{orderId}`, `GET /credit-notes/{id}`, `POST /invoices/{id}/resend` (with `.Idempotency()`)
- [ ] `InvoicingErrors` implemented to match `error-taxonomy.md § 3.6`
- [ ] Integration tests cover all 4 `example-mapping/invoicing.md` sessions + concurrency test for gap-free sequencing
- [ ] Architecture tests: no `DateTime.UtcNow` in domain; aggregates private-ctor + factory; no direct `Azure.Storage.Blobs` imports in Application layer (must go through `IBlobStore`)
- [ ] `docker compose --profile full up -d` (or Aspire AppHost) shows Azurite container `invoices` initialized + `invoicing.invoices` topic with 10-year retention
- [ ] Correlation-id roundtrips through enrichment projection (consumer header → `pending_invoices.correlation_id` → aggregate → outbox → emitted event header)
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:** `services/Invoicing/**`, `test/Invoicing.*.Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/**`, `docker-compose.yaml` (touch only if topic / relay drifted from Wave 0), `DotNetAtlas.slnx`, `Directory.Packages.props` (Invoicing-specific — QuestPDF, Azure.Storage.Blobs), `docs/bc-design/invoicing.md` + glossary + example-mapping (self-correction only).

**Do not touch:** other BCs' services, other BCs' Avro schemas, Checkout saga, Notifications (you publish to it via events, not direct calls), platform libraries (only your `.avsc` files), Azurite / Azure container policies beyond `invoices`, Weather.
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- Azurite container is not running / not accessible at `http://azurite:10000` when integration tests run.
- nginx-cdn container is not running / not proxying to Azurite.
- `invoicing.invoices` topic doesn't have 10-year retention (Wave 0 should have set `retention.ms=315360000000`).
- Ordering / Payments have NOT published their Avro schemas (need at least the contracts for integration tests).
- `Platform.SharedKernel.Address` does not exist (Wave 0 prerequisite missing).
- QuestPDF community edition's license terms have changed meaningfully since ADR-0019 was written (re-evaluate swap to PDFsharp).
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Suggested commit milestones:

1. Scaffold 4 layers + project references; `dotnet build` green
2. Domain layer (`Invoice`, `CreditNote`, VOs, internal events) + unit tests + invariant tests
3. `IBlobStore` abstraction + Azurite adapter + integration test (upload + SAS URL roundtrip via nginx-cdn)
4. `IPdfGenerator` abstraction + `QuestPdfInvoiceGenerator` + deterministic hash test
5. Gap-free allocator implementation + concurrency test
6. Enrichment projection consumers + handlers + integration tests for both orderings (order-first, payment-first)
7. `IssueInvoiceCommand` + `IssueCreditNoteCommand` handlers + outbox publishers + integration tests
8. HTTP endpoints (admin + buyer) + `.Idempotency()` on resend + functional tests
9. Architecture tests (PII allowlist, no direct Azure.Storage.Blobs in Application, no DateTime.UtcNow, deterministic PDF)
10. docker-compose smoke + end-to-end invoice issuance verification + session summary
</session_management>

<verification>
```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Invoicing.UnitTests/
dotnet test test/Invoicing.ArchitectureTests/
dotnet test test/Invoicing.IntegrationTests/
dotnet test test/Invoicing.FunctionalTests/
docker compose --profile full up -d
# Verify topic retention
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic invoicing.invoices
# Verify Azurite + blob container exists
curl -s "http://localhost:10000/devstoreaccount1/invoices?restype=container&comp=list" | head
# Verify nginx-cdn proxies Azurite
curl -I http://localhost:8080/devstoreaccount1/invoices
# End-to-end smoke (through integration-test seeding):
# 1. Seed OrderConfirmedEvent + PaymentCapturedEvent with same CorrelationId
# 2. Wait for IssueInvoiceCommand
# 3. GET /api/v1/invoicing/invoices/{id} → presigned URL
# 4. GET the SAS URL through nginx-cdn → receive the PDF
# 5. Verify ContentHash matches aggregate field
```

Paste actual output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
**Question:** Enrichment consumer class organization — one consumer per event type or one multiplexer?

**Bad answer:** "One per type for isolation."

**Good answer:** "One consumer per event type (4 classes: `OrderConfirmedInvoiceProjectionConsumer`, `PaymentCapturedInvoiceProjectionConsumer`, `OrderCancelledCreditNoteProjectionConsumer`, `PaymentRefundedCreditNoteProjectionConsumer`). Reasons: (1) inbox dedup is per message-type per consumer — mixing types in one consumer complicates dedup keys; (2) KafkaFlow's `IMessageHandler<T>` is strongly typed; multiplexer would erase types at the handler boundary; (3) test isolation — each consumer fires against its own Kafka topic fixture. Trade-off accepted: 4 small classes instead of 1; extracted the common upsert-projection-row logic into `Common/PendingProjectionUpserter<TEvent>` helper. Verified by 8 integration tests (4 consumers × both projection tables)."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`. Invoicing-specific notes:

- Enrichment projection design: exact idempotency semantics + dedup keys
- Gap-free allocator test results (concurrency verification + rollback-preserves-sequence)
- PDF determinism: byte-hash comparison across two identical-input runs
- Blob storage pattern: Azurite → Azure SAS URL → nginx-cdn chain (production: Azure Blob → SAS → Front Door)
- Any `InvoicingErrors` additions beyond `error-taxonomy.md § 3.6` + why
- Cross-BC contract integrity: `Invoice.Total ≡ Order.Total ≡ Payment.Amount` verified by integration test
- Projection-lag metric: added + wired to observability dashboard?
- ADR-0011 PII — OTEL allowlist verified in-test (no address span tags leak)
- ADR-0015 time — BCL `TimeProvider` used in allocator (year derived from `GetUtcNow().Year`) for deterministic year-rollover tests via `FakeTimeProvider`
- ADR-0019 PDF determinism — hash test green across two runs

Proceed.
</session_summary>
