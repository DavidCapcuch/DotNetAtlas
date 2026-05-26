# Invoicing — Phase 1 delivery flow (session summary)

**Branch:** `aaqwdqwd`
**Session date:** 2026-05-22
**Inputs:** [spec](../../superpowers/specs/2026-05-22-invoice-delivery-flow-design.md), [plan](../../superpowers/plans/2026-05-22-invoice-delivery-flow.md).
**Outcome:** Closes [#123](https://github.com/DavidCapcuch/DotNetAtlas/issues/123) (InvoiceDeliveredEvent + Notifications delivery consumer + DeliveryChannel.Email flip) and [#131](https://github.com/DavidCapcuch/DotNetAtlas/issues/131) (PdfBlobRef stale-SAS staleness). All 6 test slices green.

---

## TL;DR

| Phase | Commits | What changed |
|---|---|---|
| **A — Foundation** | `b7e4879`, `45e5c27`, `d93337a`, `58919a7`, `54e6614`, `6eb7da7` | New Avro events (InvoiceDeliveredEvent, EmailNotificationSentEvent), `PdfBlobRef.BlobUri → BlobName` refactor, breaking Avro field renames on InvoiceIssued + CreditNoteIssued, EF migration (`pdf_blob_uri → pdf_blob_name` with RenameColumn + AlterColumn to preserve data) |
| **B — Notifications BC** | `c7e7119`, `8e1f3ce`, `44c8996`, `4a9cccf`, `437156a`, `e281586`, `a90a596`, `6a99318` | New `Notifications.UnitTests` + `Notifications.IntegrationTests` projects, `IEmailGateway` + `MockEmailGateway`, `EmailTemplateRenderer` (with `invoicing.invoice-delivered`), `SendEmailNotificationCommandKafkaHandler`, KafkaFlow consumer wiring, Testcontainers fixture, end-to-end integration test |
| **C — Invoicing publishers** | `cfc58da`, `37294e6`, `18c8667`, `577f3a0`, `e562cbc` | `BuyerPortalOptions`, `TopicsOptions` extended with `NotificationsEmailCommands`/`Events`, `InvoiceDeliveryRequestedOutboxPublisher`, `InvoiceDeliveredMapper`, `InvoiceDeliveredOutboxPublisher` |
| **D — Reciprocal consumer + flip** | `5f08c51`, `cd3fe17` | `EmailNotificationSentEventKafkaHandler` (Invoicing-side) + KafkaFlow subscription, flipped `DeliveryChannel.None → Email` at `IssueInvoiceCommandHandler.cs:159` |
| **E — Closeout** | `11d434d`, `acc343e` | Extended `IssueInvoiceCommandHandlerTests` to assert `SendEmailNotificationCommand` outbox row, added end-to-end `InvoiceDeliveryFlowTests` covering Issue → Notifications ack → Delivered |

**Total: 23 commits.**

---

## Architecture (as shipped)

```
Invoicing                                       Notifications
─────────                                       ─────────────
IssueInvoiceCommandHandler
  deliveryChannel = DeliveryChannel.Email
  invoice.Issue(pdfBlobRef, utcNow) →
    raises InvoiceIssuedDomainEvent
    raises InvoiceDeliveryRequestedDomainEvent (in-process, no Avro)
                                                  ↓
  InvoiceIssuedOutboxPublisher
    → InvoiceIssuedEvent.avsc on invoicing.invoices
       (now carries PdfBlobName — canonical, immutable)

  InvoiceDeliveryRequestedOutboxPublisher
    (sync, no IBlobStore — reads InvoiceNumber/Total from the event itself)
    → SendEmailNotificationCommand on notifications.email-commands
       TemplateData: { InvoiceNumber, TotalAmount, Currency, ViewInvoiceUrl }
       ViewInvoiceUrl = BuyerPortal.BaseUrl + "/invoices/" + InvoiceId
       IdempotencyKey = "invoice-delivered-{InvoiceId}-{Attempt}"
  ─────────────────────────────────────────────→
                                                  SendEmailNotificationCommandKafkaHandler
                                                    EmailTemplateRenderer.Render(TemplateId, TemplateData) → EmailMessage
                                                    IEmailGateway.SendAsync(...) → Result
                                                    on success: outbox EmailNotificationSentEvent
                                                       on notifications.email-events
                                                  ←─────────────────────────────────────────────

  EmailNotificationSentEventKafkaHandler
    filter: TemplateId == "invoicing.invoice-delivered"
    parse InvoiceId from IdempotencyKey
    load Invoice; invoice.Deliver(_clock.GetUtcNow())
      → raises InvoiceDeliveredDomainEvent

  InvoiceDeliveredOutboxPublisher
    → InvoiceDeliveredEvent.avsc on invoicing.invoices
```

Key invariants:
- **No SAS URLs in Kafka payloads or email bodies.** Only `BlobName` on long-retention events; ViewInvoiceUrl in email is a portal URL (non-credential).
- **Only Invoicing mints SAS URLs**, exclusively in `GET /api/v1/invoices/{id}` (the existing endpoint).
- **In-process domain event stays in-process.** `InvoiceDeliveryRequestedDomainEvent` has no Avro counterpart.

---

## Notable deviations / discoveries during implementation

### A3 + A4 — scope expansion driven by compile chain
The plan scoped A3 (domain VO refactor) to Domain + UnitTests only, with downstream tasks A5–A6 fixing Application/Infrastructure compile breakage. In practice, `Invoicing.UnitTests.csproj` transitively builds Application + Infrastructure, so A3 had to absorb the minimum mapper + AzureBlobStore + EF-config edits to land a green test build. A5 and A6 became no-ops. A4 separately picked up 6 leftover test-fixture compile fixes from the `Uri → string` cascade.

### A4-extension — symmetric Avro rename on CreditNoteIssuedEvent (spec-reviewer recommendation)
The original plan only renamed `InvoiceIssuedEvent.PdfBlobUri → PdfBlobName`. The spec-compliance reviewer flagged that `CreditNoteIssuedEvent.avsc` had the same field with a now-dangling cross-reference (`"Same opacity rules as InvoiceIssuedEvent.PdfBlobUri"`), and that issue #131's staleness concern applied symmetrically. Committed as `54e6614` — same breaking-change pattern + accepted ADR-0007 deviation.

### A7 — CLAUDE.md policy amendment + manually-corrected migration
The original CLAUDE.md forbade generating EF migrations; the user authorised generation for this session and asked for the policy to be updated. New policy (commit `6eb7da7`):
> EF Core migrations: generate via `dotnet ef migrations add` (never hand-write the `.cs` migration from scratch). After generation, inspect the `Up()` / `Down()` and fix EF's choices where they would destroy data — typically swap `DropColumn` + `AddColumn` for `RenameColumn` on column renames.

EF auto-scaffolded `Drop + Add` (data-loss path). The migration was manually rewritten to `RenameColumn + AlterColumn` so existing fixture rows survive the rename. Down-migration symmetrically restores the legacy `varchar(2048)` + comments.

### C1 + C2 — fixture follow-up commit
Adding two `required` properties to `TopicsOptions` regressed 5 integration tests because the in-memory configuration in `IntegrationTestFixture` didn't include the new keys. Commit `37294e6` added them inline.

### D1 — handler placement deviation
The plan put the reciprocal consumer in `Invoicing.Application/Messaging/`. The actual `Invoicing.Application.csproj` has no KafkaFlow reference (and adding one would violate framework-independence). Implementer placed the handler in `Invoicing.Infrastructure/Messaging/Kafka/Notifications/` following established convention.

### D2 — latent bug in C3's handler fixed
While re-running the test sweep after the `DeliveryChannel.None → Email` flip, the implementer discovered that `InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler` was using `IInvoicingDbContext.Invoices.SingleOrDefaultAsync(...)` to re-fetch the invoice for `InvoiceNumber`/`Total`. But `DispatchDomainEventsInterceptor` fires events INSIDE `SaveChangesAsync` — before the aggregate row is committed. The fetch would always fail in production.

Fix: enriched `InvoiceDeliveryRequestedDomainEvent` with `InvoiceNumber` + `Total` properties (data the aggregate already has at event-raise time). The handler became synchronous and lost its `IInvoicingDbContext` dependency. Cleaner end state than the original plan.

---

## Test counts (final)

| Slice | Count |
|---|---|
| Invoicing.UnitTests | 104/104 |
| Invoicing.ArchitectureTests | 30/30 |
| Invoicing.IntegrationTests | 42/42 |
| Invoicing.FunctionalTests | 26/26 |
| Notifications.UnitTests | 7/7 |
| Notifications.IntegrationTests | 1/1 |
| **Total relevant slices** | **210/210** |

---

## CI gates (final)

```
dotnet restore --locked-mode                              → exit 0
dotnet build -m                                           → 0 Warning(s), 0 Error(s)
dotnet format whitespace --no-restore --verify-no-changes → exit 0
dotnet format style --no-restore --verify-no-changes      → exit 0
```

All 6 test slices ran via `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test ...` per CLAUDE.md Testcontainers note.

---

## Phase-2 follow-ups (recommended to file as separate issues)

1. **Dead `sasTtl` parameter on `IBlobStore.UploadAsync`.** After A3's refactor the SAS URI is no longer computed during upload, but `sasTtl` is still in the interface signature (validated but unused). Either remove from the interface or add a `// dead parameter` comment until the next upload-shape refactor.
2. **Real `IEmailGateway`.** Phase 1 uses `MockEmailGateway` (logs the email). Phase 2 wires a real provider (SendGrid/SES/SMTP).
3. **Multi-channel delivery.** Schema enum supports `SMS`/`InApp`; consumer logic only handles `Email`. Add channel dispatch in the SendEmail* family when those channels are needed.
4. **Magic-link / passwordless portal URL.** Current `ViewInvoiceUrl` requires the buyer to be authenticated at the portal. For unauthenticated B2C buyers, a signed-token redirect endpoint that mints a SAS on click would close the experience.
5. **Database-backed template store.** Phase 1 has one in-process hardcoded template.
6. **Cross-process E2E test infrastructure** — current "end-to-end" test simulates the Notifications round-trip in-process (`EmailNotificationSentEventKafkaHandler` invoked directly). A true two-process test (real Kafka, both services running) would catch wire-format mismatches but adds significant fixture complexity.

---

## Issues closed

- [#123](https://github.com/DavidCapcuch/DotNetAtlas/issues/123) — InvoiceDeliveredEvent.avsc + outbox publisher + Notifications delivery consumer + `DeliveryChannel.None → Email` flip — all shipped end-to-end across commits `b7e4879` (Avro), `e562cbc` (publisher), `5f08c51` (reciprocal consumer), `cd3fe17` (channel flip), plus the Notifications BC pipeline (`44c8996` → `e281586`).
- [#131](https://github.com/DavidCapcuch/DotNetAtlas/issues/131) — `PdfBlobRef.BlobUri → BlobName` (commit `d93337a`); `InvoiceIssuedEvent.PdfBlobUri → PdfBlobName` (`58919a7`); symmetric rename on `CreditNoteIssuedEvent` (`54e6614`); EF migration with data-preserving `RenameColumn` (`6eb7da7`).
