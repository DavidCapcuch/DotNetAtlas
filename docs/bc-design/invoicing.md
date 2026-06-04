# Invoicing Bounded Context

>
> **Status:** Authored 2026-04-19. Greenfield BC added to the eShop reference to showcase patterns absent from Catalog / Basket / Ordering / Inventory / Payments.
> **Scope:** Invoice issuance, delivery, credit-note issuance on cancellation/refund.
> **Patterns showcased:**
> 1. **Document generation + immutable artifact storage** — PDF invoices stored as write-once blobs in Azurite (local Azure Blob Storage emulator) fronted by nginx (local CDN). Production uses Azure Blob Storage + Azure Front Door.
> 2. **Legal retention** — 10-year retention on `invoicing.invoices` topic + PDF blobs (EU VAT norm).
> 3. **Gap-free numeric sequencing** — `InvoiceNumber` uses a dedicated Postgres `SEQUENCE` with transactional allocation.
> 4. **Idempotent external re-emission** — `(InvoiceId, DeliveryChannel, DeliveryAttempt)` dedup table for "resend to customer" / "resend to tax authority".
> 5. **Async multi-source enrichment** — `Invoice = f(OrderConfirmed, PaymentCaptured, BuyerTaxProfile)` via inbox + state projection, no saga.
>
> **Storage:** PostgreSQL, schema `invoicing` + Azurite blob container `invoices` (production: Azure Blob Storage container `invoices`).
> **Folder:** `services/Invoicing/`.

---

## 1. Purpose & Role in the System

Invoicing is the **authority for fiscal records** — it produces legally-binding invoices and credit notes, stores their PDF artifacts, and delivers them to customers. It is **event-driven**, not saga-orchestrated: when an order is confirmed AND its payment is captured, Invoicing issues an invoice automatically. When an order is cancelled after payment capture (which triggers a refund in Payments), Invoicing issues a credit note.

- **Upstream (consumes):**
  - `OrderConfirmedEvent` (Ordering) — order-side half of the enrichment input
  - `PaymentCapturedEvent` (Payments) — payment-side half
  - `OrderCancelledEvent` (Ordering) + `PaymentRefundedEvent` (Payments) — credit-note triggers
- **Downstream (publishes):**
  - `InvoiceIssuedEvent` — Invoicing-internal trigger for `InvoiceDeliveryRequestedDomainEventHandler` to emit `NotifyUserCommand` on `notifications.notify-commands` (the command-driven Notifications pattern in [notifications.md § 2](notifications.md); v2 — [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md), migrated in #312). Notifications does NOT subscribe to `invoicing.invoices`.
  - `InvoiceDeliveredEvent` — published after Invoicing consumes `NotificationDeliveryStatusChangedEvent` (`Channel==email && Status==Dispatched`, correlated on `NotificationId`) back from Notifications and transitions `Issued → Delivered`. Consumer-less by default; available for analytics.
  - `InvoiceCancelledEvent` + `CreditNoteIssuedEvent` — no Notifications wiring in v1 (would follow the same command-driven pattern as `InvoiceIssuedEvent` if buyer emails for cancellations/credit notes are added).

**Why event-driven, not saga-orchestrated?** Invoice issuance is a **convergent enrichment** — two independent events (order + payment) must both arrive before the invoice can be issued. This is a natural fit for an inbox-backed projection that buffers partial state. Invoicing has no multi-step distributed transaction of its own; a saga would be over-engineered.

---

## 2. Aggregates

Two aggregates: `Invoice` and `CreditNote`. A `CreditNote` references the `Invoice` it reverses.

### 2.1 `Invoice` aggregate

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` (UUID v7) | Internal identity |
| `InvoiceNumber` | `InvoiceNumber` (VO) | Format `INV-YYYY-NNNNNN` (e.g., `INV-2026-000142`). Gap-free, sequential. |
| `BuyerId` | `Guid` | From `OrderConfirmedEvent`; frozen |
| `OrderId` | `Guid` | The order this invoices |
| `PaymentId` | `Guid` | The payment transaction |
| `IssueDate` | `DateTimeOffset` | When the aggregate moved to `Issued` |
| `BillingAddress` | `Address` | Snapshotted from `OrderConfirmedEvent.BillingAddress` (Summary Event per [ADR-0020](../adr/0020-summary-events.md)) |
| `Lines` | `IReadOnlyList<InvoiceLine>` | Snapshotted from `OrderConfirmedEvent.Items`, frozen at issuance, immutable thereafter |
| `Subtotal` | `Money` | Sum of `Lines[i].LineTotal` |
| `VatLines` | `IReadOnlyList<VatLine>` | Per-rate breakdown (e.g., `21% → €42.00`, `0% → €10.00`) |
| `Total` | `Money` | `Subtotal + sum(VatLines.Amount)` |
| `PdfBlobRef` | `PdfBlobRef?` | Populated on `Issued`; null while `Draft` |
| `DeliveryChannel` | `DeliveryChannel` (SmartEnum) | `Email`, `None`. Additional channels (tax-authority webhook, postal mail) are planned scope — see [roadmap.md § 2.3 Invoicing](../roadmap.md). |
| `Status` | `InvoiceStatus` (SmartEnum) | `Draft → Issued → Delivered → Archived`; `Cancelled` off-ramp |
| `CancellationInfo` | `CancellationInfo?` | Populated when moving to `Cancelled`; references the `CreditNote.Id` that reverses this invoice |
| `RowVersion` | `uint` | Optimistic concurrency |

**Invariants:**
- **I-1** `Total == Subtotal + sum(VatLines)` — enforced at factory time; re-asserted on any read (defensive).
- **I-2** `Lines` is non-empty.
- **I-3** `InvoiceNumber` is immutable post-allocation.
- **I-4** `PdfBlobRef` is immutable once set (write-once blob).
- **I-5** Transitions gated by `InvoiceStatus.CanTransitionTo(target)`; invalid → `DataIntegrityException`.
- **I-6** `Cancelled` requires a `CreditNoteId` reference in `CancellationInfo`.

### 2.2 `CreditNote` aggregate

Mirrors `Invoice` shape with negative amounts:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 |
| `CreditNoteNumber` | `CreditNoteNumber` (VO) | Format `CN-YYYY-NNNNNN`; gap-free, separate sequence from invoices |
| `OriginalInvoiceId` | `Guid` | FK to the invoice being reversed |
| `OriginalInvoiceNumber` | `InvoiceNumber` | Snapshotted for PDF |
| `IssueDate` | `DateTimeOffset` | |
| `Lines` | `IReadOnlyList<InvoiceLine>` | Copy of original with flipped sign |
| `Total` | `Money` | Negative (e.g., `-€152.00`) |
| `Reason` | `CreditNoteReason` (SmartEnum) | `OrderCancelled`. `PartialRefund` and `Adjustment` are planned scope — see [roadmap.md § 2.3 Invoicing](../roadmap.md). |
| `PdfBlobRef` | `PdfBlobRef` | Required at creation; credit notes are immediately issued |
| `Status` | `CreditNoteStatus` | `Issued → Delivered → Archived` — no cancellation of a credit note |

**Invariants:**
- **I-CN-1** `OriginalInvoiceId` references an `Invoice` aggregate in `Issued` or `Delivered` state (not `Cancelled`).
- **I-CN-2** `Total` is negative.
- **I-CN-3** `CreditNoteNumber` is immutable post-allocation.

---

## 3. Value Objects

`Payments.Domain.ValueObjects` equivalents, plus:

| VO | Fields | Notes |
|---|---|---|
| `InvoiceNumber` | `Value : string` | Format `INV-YYYY-NNNNNN`; regex `^INV-\d{4}-\d{6}$` |
| `CreditNoteNumber` | `Value : string` | Format `CN-YYYY-NNNNNN`; regex `^CN-\d{4}-\d{6}$` |
| `InvoiceLine` | `LineNumber : int`, `Sku : Sku`, `Description : string`, `Quantity : int`, `UnitPrice : Money`, `LineTotal : Money`, `VatRate : VatRate` | Immutable |
| `VatLine` | `Rate : VatRate`, `Base : Money`, `Amount : Money` | e.g., rate 21% on a base of €200 → amount €42 |
| `VatRate` | `Percentage : decimal` | `0..100`; ISO-compliant (e.g., 0, 10, 15, 21) |
| `PdfBlobRef` | `BlobUri : Uri`, `ContentHash : string`, `SizeBytes : long` | Content-addressed; `BlobUri` is a presigned URL |
| `CancellationInfo` | `CancelledAtUtc : DateTimeOffset`, `Reason : CreditNoteReason`, `CreditNoteId : Guid` | |

---

## 4. `InvoiceStatus` SmartEnum

```
Draft ──issue──▶ Issued ──deliver──▶ Delivered ──archive──▶ Archived
  │                │                       │
  │                │                       │
  │                └──cancel──▶ Cancelled (off-ramp; requires CreditNote)
  │                                        
  │                                        
  └──(only transition valid from Draft is to Issued)
```

`Draft` exists for the brief window between "enrichment complete" and "PDF generated + blob uploaded + number allocated + saved". Most of the time the aggregate skips straight to `Issued` within a single command handler. `Draft` is retained as a state so that partial failures (e.g., PDF generation OK but blob upload fails) are resumable.

---

## 5. Domain Events (internal, 7)

- `InvoiceCreatedDomainEvent` — aggregate created in `Draft`
- `InvoiceIssuedDomainEvent` — aggregate → `Issued` (number allocated, PDF stored)
- `InvoiceDeliveryRequestedDomainEvent` — used internally to trigger the delivery side-effect via outbox (`NotifyUserCommand` to `notifications.notify-commands`, carrying the producer-assigned `NotificationId`; v2 — [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md); receiving end documented in [notifications.md](notifications.md))
- `InvoiceDeliveredDomainEvent` — delivery confirmed
- `InvoiceCancelledDomainEvent` — aggregate → `Cancelled`
- `CreditNoteCreatedDomainEvent` — a new credit note was created against an invoice
- `CreditNoteIssuedDomainEvent` — credit note's PDF is stored and number allocated

---

## 6. External Events (Avro) + Topic

**Topic:** `invoicing.invoices` — per-topic topology (partitions / retention / class) in [kafka-topology.md](../kafka-topology.md); its **10-year retention** reflects the EU VAT norm (see [ADR-0018 Invoice numbering](../adr/0018-invoice-numbering.md)). Partition key `BuyerId` ([events-catalog.md § 2](events-catalog.md)) keeps all of a buyer's invoices on one partition for efficient per-buyer consumer reads.

| External event | Triggered by | Consumer(s) |
|---|---|---|
| `InvoiceIssuedEvent` | `InvoiceIssuedDomainEvent` | **No v1 consumer** — a BFF invoice cache is planned-not-v1 (the v1 BFF defines no invoice endpoint/cache; see [events-catalog.md § 5.8](events-catalog.md)) and would consume this topic if added. Invoice-delivery email flows via the command-driven pattern (Invoicing → `NotifyUserCommand` → Notifications; v2 ADR-0031), NOT a Notifications subscription to this topic — see [notifications.md § 2](notifications.md). |
| `InvoiceDeliveredEvent` | `InvoiceDeliveredDomainEvent` | **No v1 consumer** (a BFF "my invoices" cache is planned-not-v1). |
| `InvoiceCancelledEvent` | `InvoiceCancelledDomainEvent` | **No v1 consumer** (BFF invoice cache is planned-not-v1). Buyer email deferred (would route via `NotifyUserCommand` per [notifications.md § 2](notifications.md)). |
| `CreditNoteIssuedEvent` | `CreditNoteIssuedDomainEvent` | **No v1 consumer** (BFF invoice cache is planned-not-v1). Buyer email deferred (would route via `NotifyUserCommand` per [notifications.md § 2](notifications.md)). |

**Consumers** are canonical in [events-catalog.md § 2](events-catalog.md). **Schema compatibility** is *derived* from topic class — event-log → `FORWARD_TRANSITIVE` — see [ADR-0007](../adr/0007-avro-compatibility-modes.md).

**Payload:** enriched — carries buyer address, total, VAT breakdown, PDF URL. Consumers can render or forward without a callback.

**PII note:** these events carry `BillingAddress` + buyer name + `BuyerId`. 10-year retention + PII → handled per ADR-0011 (PII + GDPR Article 17 path, when authored). Until that ADR lands, the reference implementation accepts retention as-is and documents the gap.

---

## 7. Use Cases (summary)

Full use-case catalog in [`use-cases.md § 6`](use-cases.md) (new § added alongside Payments).

### 7.1 Commands (event-triggered, internal)

- `IssueInvoiceCommand(OrderId)` — triggered when both `OrderConfirmedEvent` AND `PaymentCapturedEvent` have arrived for the same `OrderId` (the `pending_invoices` key + idempotency key per [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)). The handler loads both payloads from the converged projection row. Allocates `InvoiceNumber` from the Postgres sequence in the same transaction. Generates PDF. Uploads to Azurite (production: Azure Blob Storage). Writes aggregate + outbox atomically.
- `IssueCreditNoteCommand(OrderId)` — triggered when both `OrderCancelledEvent` AND `PaymentRefundedEvent` have arrived for the order matching a prior invoice; idempotent on `OrderId` (the `pending_credit_notes` key). The original invoice and reason are resolved from the converged projection row. Allocates `CreditNoteNumber`. Generates PDF. Uploads. Persists.

### 7.2 Commands (admin HTTP)

- `ResendInvoiceCommand(InvoiceId)` — admin-triggered re-delivery. Idempotent on `(InvoiceId, DeliveryChannel, DeliveryAttempt)` via the `invoice_delivery_log` table.

### 7.3 Queries (HTTP)

- `GetInvoiceByIdQuery(InvoiceId, BuyerId, IsAdmin)` → presigned PDF URL (10-minute expiry) + invoice metadata. Buyer-scoped authorization.
- `GetInvoicesByBuyerQuery(BuyerId, Page, Limit)` → paginated list.

All HTTP routes under `/api/v1/invoicing/`.

---

## 8. The enrichment projection — how `IssueInvoiceCommand` is triggered

**The teaching problem:** Invoicing needs `OrderConfirmedEvent` + `PaymentCapturedEvent` for the same `OrderId`. They arrive in arbitrary order; either can be delayed minutes. Invoicing needs a **state projection** that buffers each half until the other arrives.

### 8.1 Projection table: `invoicing.pending_invoices`

| Column | Type | Purpose |
|---|---|---|
| `OrderId` | `uuid` | Primary key — the saga / business key, present on **both** halves ([ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)). |
| `PaymentId` | `uuid?` | Populated when `PaymentCapturedEvent` arrives |
| `OrderPayload` | `jsonb?` | Full Avro → JSON when `OrderConfirmedEvent` arrives; its non-null state is the "order half present" sentinel. |
| `PaymentPayload` | `jsonb?` | Same |
| `FirstSeenAtUtc` | `timestamptz` | |
| `CompletedAtUtc` | `timestamptz?` | Set when both halves present |
| `IssuedInvoiceId` | `uuid?` | Set after `IssueInvoiceCommand` succeeds |

### 8.2 Flow

1. `OrderConfirmedConsumer` handles `OrderConfirmedEvent`:
   - Upsert `pending_invoices` row (key `OrderId`), populate `OrderPayload`.
   - If `PaymentId` is already non-null, publish `InvoiceIssuanceReadyDomainEvent` → triggers `IssueInvoiceCommand`.
2. `PaymentCapturedConsumer` handles `PaymentCapturedEvent`:
   - Upsert `pending_invoices` row (key `OrderId`), populate `PaymentId` + `PaymentPayload`.
   - If `OrderPayload` is already non-null, publish `InvoiceIssuanceReadyDomainEvent`.
3. `IssueInvoiceCommandHandler`:
   - Load `pending_invoices` row by `OrderId`.
   - If `CompletedAtUtc IS NOT NULL AND IssuedInvoiceId IS NOT NULL` → idempotent no-op (already issued).
   - Otherwise: create `Invoice` aggregate from both payloads (`OrderPayload` carries the order summary — `Items`, `TotalAmount`, `Currency`, `BillingAddress` — per the [ADR-0020](../adr/0020-summary-events.md) Summary Event contract on `OrderConfirmedEvent`; `PaymentPayload` carries `Amount`, `Currency`, `PaymentTransactionId`), allocate number, generate PDF, upload blob, persist aggregate + outbox + update `pending_invoices.IssuedInvoiceId` + `CompletedAtUtc` in one transaction.

### 8.3 Credit-note counterpart

Same pattern with table `invoicing.pending_credit_notes`, consumers for `OrderCancelledEvent` + `PaymentRefundedEvent`, and `IssueCreditNoteCommand`.

---

## 9. Gap-free InvoiceNumber allocation

**The teaching problem:** VAT law typically requires invoice numbers to be sequential with no gaps. Naive `SERIAL` columns leak gaps on rollback (the sequence increments even when the insert rolls back). The solution is a **transactional allocator** that holds the number until commit.

**Approach:** Postgres `SEQUENCE` with `nextval()` called **inside** the `IssueInvoiceCommand` transaction. If the transaction commits, the number is used; if it rolls back, the sequence value is *still consumed* (sequences don't participate in rollbacks). This leaves gaps.

**Gap-free variant (implemented in v1):** a separate `invoicing.invoice_number_allocator` table with an exclusive-lock protocol:

```sql
-- Inside the IssueInvoiceCommand transaction:
SELECT next_value FROM invoicing.invoice_number_allocator
  WHERE year = EXTRACT(YEAR FROM NOW())
  FOR UPDATE;
-- use next_value to format INV-2026-000142
UPDATE invoicing.invoice_number_allocator
  SET next_value = next_value + 1
  WHERE year = EXTRACT(YEAR FROM NOW());
-- INSERT INTO invoices (invoice_number, ...) ...;
-- COMMIT;
```

Rollback releases the `FOR UPDATE` lock without incrementing — no gap. Throughput ceiling: one issuance per transaction round-trip per year-row. For a reference solution this is fine; production with high throughput would shard by year or use a Hi/Lo pattern.

Documented fully in ADR-0018 (Invoice numbering strategy; to be authored in chunk 3).

---

## 10. PDF Generation

Library: **QuestPDF** (MIT, fluent DSL). Picked per § F answer in the plan; ADR-0019 captures the rationale.

**Template location:** `services/Invoicing/Invoicing.Infrastructure/Pdf/InvoiceDocument.cs` — a `IDocument` implementation composing header, buyer block, line-item table, VAT summary, total, footer.

**Content:**
- Invoice number, issue date, buyer name + address
- Line items (SKU, description, quantity, unit price, VAT rate, line total)
- VAT summary per rate
- Subtotal, VAT total, Total
- Legal footer (tax-ID placeholder — configured per deployment)

**Blob storage:** Azurite container `invoices` (production: Azure Blob Storage container `invoices`). Blob name pattern: `{YYYY}/{MM}/{InvoiceNumber}.pdf` (e.g., `2026/04/INV-2026-000142.pdf`). Write-once via Azure immutable-blob policy (10-year time-based retention). Content hashed (SHA-256) and stored on `PdfBlobRef.ContentHash` so callers can verify integrity. SDK: `Azure.Storage.Blobs`. Connection string injected by Aspire AppHost (`AddAzureStorage("storage").RunAsEmulator()`) or read from `ConnectionStrings:AzureStorage` in `appsettings.json` for raw `docker-compose` flows. See [ADR-0017](../adr/0017-blob-storage-cdn.md).

**Delivery via presigned URL:** `GetInvoiceByIdQuery` returns a 10-minute presigned GET URL. The URL is fetched through nginx-cdn (simulating a real CDN edge cache).

---

## 11. HTTP API

All under `/api/v1/invoicing/`. Buyer-scoped authorization using JWT `sub`.

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/v1/invoicing/invoices/{id}` | Get invoice metadata + presigned PDF URL. Buyer or admin. |
| `GET` | `/api/v1/invoicing/invoices?page=…&limit=…` | Paginated list for authenticated buyer |
| `GET` | `/api/v1/invoicing/invoices/by-order/{orderId}` | Find the invoice for a given order (buyer-scoped) |
| `GET` | `/api/v1/invoicing/credit-notes/{id}` | Get credit note metadata + presigned PDF URL |
| `POST` | `/api/v1/invoicing/invoices/{id}/resend` | Admin: re-deliver invoice (idempotent on `Idempotency-Key` header) |

Rate limits per `rate-limiting.md`.

---

## 12. Idempotent delivery log

Table `invoicing.invoice_delivery_log`:

| Column | Type | |
|---|---|---|
| `InvoiceId` | `uuid` | Composite PK |
| `Channel` | `text` | `email` (additional channels are planned scope — see [roadmap.md § 2.3 Invoicing](../roadmap.md)) |
| `Attempt` | `int` | 1..N |
| `AttemptedAtUtc` | `timestamptz` | |
| `Outcome` | `text` | `delivered`, `bounced`, `failed` |
| `Detail` | `text` | Free-form (bounce reason, etc.) |

`ResendInvoiceCommandHandler` selects `MAX(Attempt)` for `(InvoiceId, Channel)` and inserts `Attempt = max + 1`. In v2 each resend mints a **fresh `NotificationId`** on the `NotifyUserCommand` (producer-assigned, [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)); dedup is the `message.id` header at the Notifications inbox plus the per-channel ledger — **not** a payload key. (`ResendInvoice` is a documented future seam — a no-op stub today.)

---

## 13. Error Classes

Single source of truth: [`error-taxonomy.md § 3.6`](error-taxonomy.md) (`InvoicingErrors`). Do not duplicate.

Key user-actionable errors (factory methods on `InvoicingErrors` returning typed `DomainError` subclasses):
- `InvoicingErrors.InvoiceNotFound(Guid invoiceId)` — `NotFoundError`, 404
- `InvoicingErrors.InvoiceAlreadyIssued(Guid orderId)` — `ConflictError`, 409 (idempotent re-issue attempt)
- `InvoicingErrors.CreditNoteRefersToCancelledInvoice(Guid invoiceId)` — `ConflictError`, 409 (I-CN-1 violation)
- `InvoicingErrors.BlobUploadFailed()` — `ServiceUnavailableError`, 503 (Azure Blob SDK retries exhausted)
- `InvoicingErrors.PartialRefundNotSupportedV1()` — `NotImplementedError`, 501

Bug-class typed exceptions (live in `Invoicing.Application.Common.Exceptions`, inherit `DataIntegrityException` → consumer middleware DLTs them):
- `InvoiceTotalMismatchException(decimal orderTotal, decimal paymentAmount, Guid orderId)` — raised by `IssueInvoiceCommandHandler` when `OrderConfirmedEvent.TotalAmount ≠ PaymentCapturedEvent.Amount` (example-mapping 1.4).
- `PdfGenerationFailedException(string detail, Exception innerException)` — raised by `QuestPdfInvoiceGenerator` wrapping `QuestPDF.Drawing.Exceptions.DocumentLayoutException`.

---

## 14. Storage

- Schema `invoicing` (Postgres):
  - `invoices` (one per `Invoice` aggregate)
  - `invoice_lines` (owned by `Invoice`; FK cascade)
  - `credit_notes`, `credit_note_lines`
  - `invoice_number_allocator`, `credit_note_number_allocator`
  - `pending_invoices`, `pending_credit_notes` (enrichment projection)
  - `invoice_delivery_log`
  - `outbox_messages`, `inbox_messages` (standard)
- Azurite blob container `invoices` (blob names `{YYYY}/{MM}/{InvoiceNumber}.pdf`); production: Azure Blob Storage
- All timestamps `timestamptz`; domain `DateTimeOffset`.
- Concurrency: explicit `RowVersion : uint`.

---

## 15. Testing Strategy

- **Unit tests** — aggregate invariants, factory validation, SmartEnum transitions, VO construction, invoice-number format regex, total calculation.
- **Architecture tests** — no cross-BC references; aggregates have private ctor + static factory; `PdfBlobRef` only constructed via factory.
- **Integration tests** (Testcontainers: Postgres + Kafka + Azurite):
  - **Happy path**: fire `OrderConfirmedEvent` + `PaymentCapturedEvent` → `pending_invoices` populates → `IssueInvoiceCommand` triggers → aggregate persists → PDF lands in Azurite → outbox row → Kafka event published.
  - **Out-of-order**: fire payment first, then order — same outcome.
  - **Duplicate events**: fire `OrderConfirmedEvent` twice — projection is idempotent, only one invoice issued.
  - **Gap-free number**: simulate a rollback between allocator-select and insert; verify no gap.
  - **Credit note**: issue invoice, then fire `OrderCancelledEvent` + `PaymentRefundedEvent` → credit note issued.
- **Functional tests** — HTTP endpoints with Testcontainers (Postgres + Azurite); verify Azure SAS URLs are retrievable via nginx-cdn.

Full example-mapping sessions in [`example-mapping/invoicing.md`](example-mapping/invoicing.md).

---

## 16. Observability

- `InvoicingActivitySource` for tracing.
- Metrics:
  - `invoicing.invoices.issued.count` (counter) tagged `vat_rate_primary`
  - `invoicing.credit_notes.issued.count`
  - `invoicing.pdf.generation.duration.seconds` (histogram)
  - `invoicing.blob.upload.duration.seconds` (histogram)
  - `invoicing.enrichment.lag.seconds` (histogram) — time between first half arriving and both halves present
- **PII rule:** never tag spans with `BillingAddress` or buyer name. Tag with `InvoiceId` (Guid) and `BuyerId` hash only.

---

## 17. Out of scope for current scope

Planned scope is catalogued in [roadmap.md § 2.3 Invoicing](../roadmap.md):

- **Multi-currency invoices** — single currency per invoice (matches Order constraint).
- **Partial refunds / partial credit notes** — today issues one credit note for the full refund.
- **Legal numbering per country** — today uses `INV-YYYY-NNNNNN` globally. Real deployments need per-country sequences (Czech Republic, Germany, France each have different formal requirements).
- **E-invoicing webhooks (tax authority delivery)** — `DeliveryChannel` leaves the slot open but only `Email` is implemented. Webhook to a stub SII/XRechnung endpoint is planned scope.
- **Tax-rate calculation** — today accepts VAT rates as inputs from `OrderConfirmedEvent` (computed by Ordering at checkout). Invoicing does not compute tax; it just records.
- **Archival rotation** — `Archived` status is defined but no job moves old invoices to cold storage.

---

## 18. Integration map (summary)

```
Ordering                 Payments
   │                        │
   │ OrderConfirmedEvent    │ PaymentCapturedEvent
   ▼                        ▼
┌─────────────────────────────┐
│ Invoicing.pending_invoices  │  (enrichment projection)
└───────────┬─────────────────┘
            │
            ▼
┌──────────────────────────────┐
│ IssueInvoiceCommand          │  (both halves present → atomic issue)
│  1. Allocate InvoiceNumber   │
│  2. Generate PDF (QuestPDF)  │
│  3. Upload to Azurite/Azure  │
│  4. Persist Invoice + outbox │
└───────────┬──────────────────┘
            │
            ▼
   InvoiceIssuedEvent  (emitted to invoicing.invoices — no v1 consumer)
   InvoiceDeliveryRequestedDomainEvent
        ──▶  NotifyUserCommand  ──▶  Notifications  ──▶  NotificationDeliveryStatusChangedEvent
                                                                          │
                                                                          ▼
                                                                   Invoicing transitions
                                                                   Issued → Delivered
```

Cancellation:

```
Ordering (OrderCancelled) + Payments (PaymentRefunded)
                ▼
      pending_credit_notes
                ▼
      IssueCreditNoteCommand
                ▼
      CreditNoteIssuedEvent (emitted to invoicing.invoices — no v1 consumer)
                            (no Notifications wiring in v1)
```

---

*End of Invoicing BC design.*
