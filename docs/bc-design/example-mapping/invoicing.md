# Invoicing — Example Mapping Sessions

> The Sessions 1 and 3 pseudocode reflects the [ADR-0020](../../adr/0020-summary-events.md) Summary Event shape on `OrderConfirmedEvent` (Items, TotalAmount, Currency, BillingAddress travel with the event).
>
> Format: Matt Wynne's Example Mapping (Story / Rules / Examples / Questions) with BDD Given/When/Verify/Then for Examples. Each session corresponds to one non-trivial business rule or invariant in the Invoicing BC.

---

## Session 1 — Convergent enrichment: invoice issued when both halves arrive

**Story:** As the business, I need one invoice per confirmed-and-paid order, regardless of whether the order-confirmation or the payment-capture event arrives first.

### Rules

- **R1** — The `pending_invoices` projection accepts either `OrderConfirmedEvent` or `PaymentCapturedEvent` first; order of arrival does not matter.
- **R2** — `IssueInvoiceCommand` fires **only when both halves are present** AND `IssuedInvoiceId IS NULL` (idempotent guard).
- **R3** — After successful issuance, `pending_invoices.IssuedInvoiceId` is set atomically with the aggregate insert.
- **R4** — Duplicate events (same `OrderId` arriving twice) are absorbed by the inbox; the projection is idempotent.
- **R5** — The invoice `Total` must equal `OrderConfirmedEvent.Total` — this is a consistency check; a mismatch is a bug and surfaces as `DataIntegrityException`.

### Example 1.1 — Order arrives first, payment second

- **Given** no prior state for `OrderId = O1`
- **When** `OrderConfirmedEvent(orderId=O1, total=€152.00, lines=...)` is consumed
- **Then** row inserted in `pending_invoices` with `OrderId = O1`, `OrderPayload` populated, `PaymentId = NULL`
- **And** `InvoiceIssuanceReadyDomainEvent` is NOT raised (only one half present)
- **When** `PaymentCapturedEvent(paymentId=P1, orderId=O1, amount=€152.00)` is consumed
- **Then** row updated: `PaymentId = P1`, `PaymentPayload` populated, `CompletedAtUtc` set
- **And** `InvoiceIssuanceReadyDomainEvent` is raised
- **And** `IssueInvoiceCommand` runs: number allocated (`INV-2026-000142`), PDF generated + uploaded, aggregate persisted, `IssuedInvoiceId` written to `pending_invoices`
- **Verify** outbox contains one `InvoiceIssued` on topic `invoicing.invoices`
- **Verify** Azurite blob `invoices/2026/04/INV-2026-000142.pdf` exists with matching `ContentHash`
- **Verify** `InvoiceNumber` sequence advanced by 1

### Example 1.2 — Payment arrives first, order second (mirror of 1.1)

- Same outcome as 1.1; the projection is order-insensitive.

### Example 1.3 — Duplicate OrderConfirmedEvent

- **Given** `pending_invoices` row for `OrderId = O1` with both halves present and `IssuedInvoiceId = I1` (invoice already issued)
- **When** `OrderConfirmedEvent(orderId=O1, ...)` is consumed a second time
- **Then** inbox dedups at the message-id level (if `MessageId` repeats) OR the handler observes `IssuedInvoiceId IS NOT NULL` and no-ops
- **Verify** no new invoice issued; `InvoiceNumber` sequence NOT advanced
- **Verify** no new outbox row

### Example 1.4 — Total mismatch (bug)

- **Given** `OrderConfirmedEvent.Total = €152.00` written to projection
- **When** `PaymentCapturedEvent.Amount = €150.00` arrives (mismatch — this should never happen in a healthy system)
- **Then** `IssueInvoiceCommandHandler` detects the mismatch during construction of the `Invoice` aggregate
- **And** throws `DataIntegrityException` — routed to DLT by the Kafka error-handling middleware
- **Verify** the row in `pending_invoices` is NOT marked `IssuedInvoiceId`; invoice not created
- **Verify** the Invoicing BC's payments-consumer DLT (`payments.transactions.Invoicing.DLT` per [kafka-dlt-strategy.md § 3](../kafka-dlt-strategy.md)) receives the offending `PaymentCapturedEvent`
- **Verify** ops alert fires per `kafka-dlt-strategy.md § DLT cumulative` (a live production incident — orders and payments should always agree on total)

### Questions

- *(empty)*

---

## Session 2 — Gap-free invoice number allocation

**Story:** As the tax authority, I need invoice numbers to be sequential with no missing values so my audit cannot be fooled by "deleted" invoices.

### Rules

- **R1** — `InvoiceNumber` is allocated inside the `IssueInvoiceCommand` transaction via `SELECT FOR UPDATE` on `invoice_number_allocator`.
- **R2** — If the transaction rolls back, the allocator's `next_value` is NOT incremented — row-lock released without update.
- **R3** — Concurrent issuances serialize on the allocator row; throughput ceiling is one issuance per year-row at a time.
- **R4** — Format is strictly `INV-YYYY-NNNNNN` where YYYY is the current year and NNNNNN is zero-padded 6 digits. Year rollover creates a new allocator row with `next_value = 1`.

### Example 2.1 — Normal sequential allocation

- **Given** `invoice_number_allocator` has `(year=2026, next_value=142)`
- **When** `IssueInvoiceCommand` runs successfully
- **Then** the generated invoice has `InvoiceNumber = "INV-2026-000142"`
- **And** `invoice_number_allocator.next_value = 143`
- **Verify** the next successful issuance allocates `INV-2026-000143` — no gap

### Example 2.2 — Rollback preserves the allocator

- **Given** `invoice_number_allocator` has `(year=2026, next_value=142)`
- **When** `IssueInvoiceCommand` runs and fails partway (e.g., Azurite blob upload fails after number select but before commit)
- **Then** the transaction rolls back — aggregate insert reverted, `next_value` update reverted
- **Verify** `invoice_number_allocator.next_value = 142` (unchanged)
- **Verify** the next retry allocates `INV-2026-000142` — no gap

### Example 2.3 — Year rollover

- **Given** `invoice_number_allocator` has `(year=2026, next_value=999999)` on 2026-12-31
- **When** `IssueInvoiceCommand` runs on 2027-01-01
- **Then** the handler observes `EXTRACT(YEAR FROM NOW()) = 2027` and inserts (or upserts) a new allocator row `(year=2027, next_value=1)`
- **And** issues `INV-2027-000001`
- **Verify** 2026 sequence did not exceed 999999

### Example 2.4 — Concurrent issuance serialization

- **Given** two concurrent `IssueInvoiceCommand` handlers both targeting year 2026
- **When** both call `SELECT ... FOR UPDATE` on `(year=2026)`
- **Then** the second waits until the first commits/rolls back
- **Verify** no duplicate invoice number issued
- **Verify** if both succeed, they get consecutive numbers (e.g., 142 and 143)

### Questions

- *(empty)* Should we shard the allocator by year AND month for throughput? **Decision v1:** no — reference solution throughput target ≤50 rps (see NFR ADR) is well below the allocator's capacity. V2 would shard if measured contention appears.

---

## Session 3 — Credit note on cancellation-after-capture

**Story:** As the buyer who cancels a paid order, I need a credit note so my accounting books balance.

### Rules

- **R1** — `CreditNote` is issued when both `OrderCancelledEvent` AND `PaymentRefundedEvent` have arrived for the same `OrderId` (matches a prior `Invoice` with `OrderId`).
- **R2** — `CreditNote.OriginalInvoiceId` must reference an `Invoice` in `Issued` or `Delivered` state (not `Cancelled`).
- **R3** — Issuing the CreditNote transitions the original Invoice: `Issued/Delivered → Cancelled`. `Invoice.CancellationInfo.CreditNoteId` is set atomically.
- **R4** — `CreditNote.Total` is negative (mirror of original with flipped sign).
- **R5** — No credit note can be issued against a `Cancelled` invoice (I-CN-1) — use a new invoice for the new purchase instead.

### Example 3.1 — Happy path: cancel after capture produces a credit note

- **Given** `Invoice I1` in `Issued` with `Total = €152.00`, `OrderId = O1`
- **When** `OrderCancelledEvent(orderId=O1)` + `PaymentRefundedEvent(orderId=O1)` both arrive (in either order)
- **Then** `pending_credit_notes` row populates both halves; `IssueCreditNoteCommand` fires
- **And** `CreditNote CN1` created with `CreditNoteNumber = "CN-2026-000008"`, `Total = -€152.00`, `OriginalInvoiceId = I1`
- **And** `Invoice I1.Status → Cancelled`, `I1.CancellationInfo.CreditNoteId = CN1.Id`
- **Verify** outbox contains `CreditNoteIssued` + `InvoiceCancelled` (both, ordered)
- **Verify** Azurite has blob `credit-notes/2026/04/CN-2026-000008.pdf`
- **Verify** `I1` cannot be cancelled again (terminal)

### Example 3.2 — Refund without order-cancel is invalid

- **Given** `OrderConfirmedEvent` + `PaymentCapturedEvent` + `Invoice I1` issued normally
- **When** `PaymentRefundedEvent(orderId=O1)` arrives WITHOUT a corresponding `OrderCancelledEvent`
- **Then** `pending_credit_notes` row has one half only
- **And** `IssueCreditNoteCommand` does NOT fire (waiting for both halves)
- **Verify** no credit note issued; projection buffers indefinitely (alarms after configurable timeout — planned scope, see [roadmap.md § 2.3 Invoicing](../../roadmap.md))

### Example 3.3 — Credit note against already-cancelled invoice is forbidden

- **Given** `Invoice I1.Status = Cancelled` (already has `CreditNote CN1`)
- **When** somehow `IssueCreditNoteCommand` fires for `I1` again
- **Then** handler returns `Result.Fail(InvoicingErrors.CreditNoteRefersToCancelledInvoice)`
- **Verify** no new credit note; no outbox row

### Questions

- *(empty)* What happens if the buyer cancels pre-capture (void path)? **Decision:** no invoice is issued (no `PaymentCapturedEvent`), so no credit note is needed. The pending_invoices row is left dangling; a planned cleanup job sweeps these — see [roadmap.md § 2.3 Invoicing](../../roadmap.md).

---

## Session 4 — PDF delivery idempotency

**Story:** As ops, I need `ResendInvoice` to be safe to call multiple times (e.g., after SMTP bounce recovery) without confusing the buyer with duplicate emails.

### Rules

- **R1** — `ResendInvoiceCommand` uses the `Idempotency-Key` HTTP header to dedup HTTP-level retries.
- **R2** — Each resend increments `invoice_delivery_log.Attempt` for `(InvoiceId, Channel)`.
- **R3** — Each resend mints a fresh producer-assigned `NotificationId` (GUID v7) on the `NotifyUserCommand` ([ADR-0031](../../adr/0031-notify-user-command-and-notification-id.md)); Notifications dedups durably on its `notification_deliveries` ledger keyed `(NotificationId, Channel)` (transport-level redelivery is already deduped by the `message.id` inbox).
- **R4** — Delivery attempt 1 is emitted automatically by `InvoiceIssuedDomainEvent` handler (no admin action needed).

### Example 4.1 — Admin resends invoice after SMTP bounce

- **Given** `Invoice I1`, `invoice_delivery_log` has row `(I1, email, attempt=1, outcome=bounced)`
- **When** admin POSTs `/api/v1/invoicing/invoices/{I1}/resend` with `Idempotency-Key: K1`
- **Then** handler writes `invoice_delivery_log(I1, email, attempt=2, outcome=pending)`
- **And** enqueues a `NotifyUserCommand` outbox row carrying a fresh producer-assigned `NotificationId` (persisted on the invoice as `delivery_notification_id`)
- **Verify** Notifications receives one `NotifyUserCommand`; dedups on `(NotificationId, email)`; sends email
- **When** the admin (or a network-glitch retry) POSTs again with the SAME `Idempotency-Key: K1`
- **Then** FastEndpoints' `.Idempotency()` filter (backed by ASP.NET Output Cache on `redis-cache`, per ADR-0013) returns the cached response
- **Verify** `invoice_delivery_log.Attempt` still = 2 (not 3)
- **Verify** no new Notifications message

### Example 4.2 — Admin resends with DIFFERENT Idempotency-Key (intentional re-re-send)

- **Given** same starting state as 4.1 after a successful attempt=2
- **When** admin POSTs with `Idempotency-Key: K2` (different key = intentional new attempt)
- **Then** handler writes attempt=3; new outbox row; new email sent

### Questions

- *(empty)*

---

*End of Invoicing example mapping — 4 sessions.*
