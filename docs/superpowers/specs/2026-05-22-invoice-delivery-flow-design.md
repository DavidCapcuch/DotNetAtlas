# Invoice delivery flow (Phase 1) + PdfBlobRef.BlobName refactor

**Date:** 2026-05-22
**Branch:** `aaqwdqwd` (per user instruction — keep work on existing branch)
**Closes:** [#123](https://github.com/DavidCapcuch/DotNetAtlas/issues/123) (InvoiceDeliveredEvent.avsc + delivery consumer), [#131](https://github.com/DavidCapcuch/DotNetAtlas/issues/131) (pdf_blob_uri staleness)
**Status:** Draft — awaiting user approval before invoking writing-plans.

## Goal

Land end-to-end invoice email delivery in one bundle, on top of a clean immutable-blob-reference foundation. Today, `IssueInvoiceCommandHandler` hard-codes `DeliveryChannel.None`, no Notifications consumer exists, and `InvoiceIssuedEvent.avsc` ships a SAS URL that goes stale under 10-year topic retention. After this spec lands:

- `PdfBlobRef` is keyed on canonical `BlobName` (immutable); URI is computed on demand.
- `InvoiceIssuedEvent.avsc` field renamed `PdfBlobUri → PdfBlobName`. This is a breaking schema change (rejected under FORWARD_TRANSITIVE per ADR-0007), acceptable here because this repo is a non-production reference solution — registry subjects are nuked and re-registered.
- A new `InvoiceDeliveryRequestedOutboxPublisher` packages a configurable portal URL (no SAS) into a generic `SendEmailNotificationCommand` for Notifications and routes through the existing half-built generic email pattern. No SAS URL ever appears in any Kafka message or email body — buyers click through to the portal, which authenticates them and serves a freshly-minted SAS via the existing `GET /api/v1/invoices/{id}` endpoint.
- Notifications consumes the command, sends via a mockable `IEmailGateway`, and emits a generic `EmailNotificationSentEvent` — knowing nothing about Invoicing.
- Invoicing's reciprocal consumer hears the sent event, calls `Invoice.Deliver(now)`, which raises `InvoiceDeliveredDomainEvent`. The new `InvoiceDeliveredOutboxPublisher` fans it to `InvoiceDeliveredEvent.avsc` on `invoicing.invoices`.
- `IssueInvoiceCommandHandler.cs:159` flips `DeliveryChannel.None → DeliveryChannel.Email`.

## Architectural principles

1. **No bearer credentials in messages or emails.** SAS URLs never appear in Kafka messages or email bodies. The only SAS-minting code path is Invoicing's existing `GET /api/v1/invoices/{id}` query handler, where the buyer is already authenticated. Notifications never sees `IBlobStore` and never reaches into Invoicing's blob container.
2. **Long-retention events carry canonical immutable identifiers** (`BlobName`), not bearer credentials. Issue #131 is satisfied by changing the contract.
3. **Generic where possible, specific only when necessary.** Notifications stays a BC-agnostic courier. The cross-BC contracts are `SendEmailNotificationCommand` (in) and `EmailNotificationSentEvent` (out). No Invoicing-domain knowledge leaks into Notifications.
4. **In-process domain events stay in-process.** `InvoiceDeliveryRequestedDomainEvent` is NOT promoted to an Avro contract — its xmldoc already documents this intent. Only `InvoiceDeliveredEvent` becomes external.
5. **Result pattern for expected errors, exceptions for bugs.** Delivery-already-done → `Result.Fail` → log + no-op. Schema/integrity violations → `DataIntegrityException` → DLT.

## Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Invoicing BC                                                                 │
│                                                                              │
│  IssueInvoiceCommandHandler (existing, one line flip at :159)                │
│    deliveryChannel = DeliveryChannel.Email   ← was .None                     │
│    Invoice.Issue(pdfBlobRef, utcNow) →                                       │
│       raises InvoiceIssuedDomainEvent                                        │
│       raises InvoiceDeliveryRequestedDomainEvent (in-process, no Avro)       │
│                                                                              │
│  Outbox publishers (domain-event interceptor dispatches synchronously,       │
│  inside the same EF tx as the aggregate save):                               │
│                                                                              │
│    InvoiceIssuedOutboxPublisher (existing — UPDATE to emit PdfBlobName)      │
│       → InvoiceIssuedEvent.avsc on invoicing.invoices (10-yr retention)      │
│                                                                              │
│    InvoiceDeliveryRequestedOutboxPublisher (NEW)                             │
│       (sync, no IBlobStore — pure data mapping)                              │
│       → SendEmailNotificationCommand on notifications.email-commands         │
│         (NEW topic, 7-day retention)                                         │
│         TemplateData carries the BUYER PORTAL URL, not a SAS                 │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Notifications BC                                                             │
│                                                                              │
│  SendEmailNotificationCommandKafkaHandler (NEW, subscribes to                │
│  notifications.email-commands; inbox-deduped via IdempotencyKey)             │
│       EmailTemplateRenderer.Render(TemplateId, TemplateData) → EmailMessage  │
│       IEmailGateway.SendAsync(message, ct) → Result                          │
│       on success: outbox EmailNotificationSentEvent                          │
│         on notifications.email-events (NEW topic, 7-day retention)           │
│       on failure: throw → KafkaFlow retry/DLT                                │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Invoicing BC (reciprocal consumer)                                           │
│                                                                              │
│  EmailNotificationSentEventKafkaHandler (NEW, subscribes to                  │
│  notifications.email-events; inbox-deduped)                                  │
│       filter: TemplateId starts with "invoicing."                            │
│       parse InvoiceId from IdempotencyKey ("invoice-delivered-{id}-{n}")     │
│       load Invoice; invoice.Deliver(timeProvider.GetUtcNow())                │
│         → raises InvoiceDeliveredDomainEvent                                 │
│                                                                              │
│  InvoiceDeliveredOutboxPublisher (NEW)                                       │
│       → InvoiceDeliveredEvent.avsc on invoicing.invoices                     │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Components

### 1. Domain — `PdfBlobRef` refactor (Invoicing.Domain)

`services/Invoicing/Invoicing.Domain/Common/ValueObjects/PdfBlobRef.cs`

| Before | After |
|---|---|
| `BlobUri : Uri` | `BlobName : string` |
| `ContentHash : string` | unchanged |
| `SizeBytes : long` | unchanged |

Factory: `PdfBlobRef.Create(blobName, contentHash, sizeBytes) : Result<PdfBlobRef>`. Validates `blobName` non-empty and reasonable shape (path-like, no leading slash, ends in `.pdf`). Existing hash + size validation unchanged.

Callers updated:
- `Invoice.Issue(pdfBlobRef, utcNow)` — signature unchanged (still takes `PdfBlobRef`).
- `IssueInvoiceCommandHandler` — `_blobStore.UploadAsync(...)` already returns `PdfBlobRef`; verify the infra implementation produces `BlobName` (it already constructs the name as `InvoicePdfBlobName.For(invoiceNumber)`, so the URI is derivable, just deprecate keeping it).
- `CreditNote.Issue(...)` — same shape; mirror the change.

### 2. Application — Query handlers (Invoicing.Application)

All four query handlers (`GetInvoiceByIdQueryHandler`, `GetInvoicesByBuyerQueryHandler`, `GetInvoiceByOrderIdQueryHandler`, `GetCreditNoteByIdQueryHandler`) already re-mint via `_blobStore.GetSasUrlAsync(...)`. Update them to build the SAS from `invoice.PdfBlobRef.BlobName` directly (no need to derive from a stored URI). Output DTO (`GetInvoiceByIdResponse` etc.) still emits a `pdfDownloadUrl` string — shape unchanged for callers.

### 3. Infrastructure — EF persistence (Invoicing.Infrastructure)

`InvoiceConfiguration.cs:121-138` + `CreditNoteConfiguration.cs` — column rename `pdf_blob_uri → pdf_blob_name`. Column type stays `text`/`varchar`.

**Migration boundary:** I will **stop and ask the user to generate the EF migration** per CLAUDE.md (never auto-generate). User's command sequence is deterministic; spec just lists the boundary.

### 4. Avro contracts (platform/Platform.SchemaRegistry.Contracts/Avro)

**Compatibility note:** `invoicing.invoices` is `FORWARD_TRANSITIVE` per [ADR-0007](docs/adr/0007-avro-compatibility-modes.md). A field rename violates this. **Accepted break** — repo is a non-production reference solution; production deployments would dual-publish + cut over. Operational requirement: Schema Registry's existing subject `Invoicing.Invoices.InvoiceIssuedEvent` must be deleted before re-registration, otherwise the registry rejects publish. Add to the schema-registry-init bootstrap or document in the M11 session-summary.

#### 4a. `InvoiceIssuedEvent.avsc` — field rename

```json
{
  "name": "PdfBlobName",
  "type": "string",
  "doc": "Canonical immutable blob name (e.g., '2026/05/INV-2026-000142.pdf'). Consumers must call Invoicing's GET endpoint (or re-mint via a shared IBlobStore for in-Invoicing readers) to get a fresh SAS URL — never embed long-lived URLs in this stream (issue #131)."
}
```

Removed: `PdfBlobUri`. `InvoiceIssuedMapper` populates `PdfBlobName = source.PdfBlobRef.BlobName`. Regen the C# binding via `avrogen` per existing toolchain.

#### 4b. `InvoiceDeliveredEvent.avsc` — NEW

Path: `platform/Platform.SchemaRegistry.Contracts/Avro/Invoicing/Invoices/InvoiceDeliveredEvent.avsc`. Mirrors the 3 existing Invoicing events (PascalCase field names per existing schema convention; the prompt's snake_case proposal is reconciled to match house style).

```json
{
  "type": "record",
  "name": "InvoiceDeliveredEvent",
  "namespace": "Invoicing.Invoices",
  "doc": "Emitted when an Invoice transitions Issued -> Delivered (a downstream delivery channel reported success). Topic 'invoicing.invoices' has 10-year retention; consumers may need this to advance their own state machines (BFF cache, audit reports). FORWARD_TRANSITIVE compat.",
  "fields": [
    { "name": "InvoiceId",       "type": { "type": "string", "logicalType": "uuid" },             "doc": "Aggregate id." },
    { "name": "BuyerId",         "type": { "type": "string", "logicalType": "uuid" },             "doc": "Partition key; matches InvoiceIssuedEvent.BuyerId." },
    { "name": "DeliveredAtUtc",  "type": { "type": "long",   "logicalType": "timestamp-millis" }, "doc": "UTC instant the channel reported success." },
    { "name": "Channel",         "type": "string",                                                "doc": "DeliveryChannel SmartEnum name: 'Email' (v1) or 'TaxAuthorityWebhook' (v2)." },
    { "name": "CorrelationId",   "type": { "type": "string", "logicalType": "uuid" },             "doc": "Checkout saga correlation id (passed through from Issuance)." },
    { "name": "OccurredOnUtc",   "type": { "type": "long",   "logicalType": "timestamp-millis" }, "doc": "Domain event occurrence time." }
  ]
}
```

#### 4c. `EmailNotificationSentEvent.avsc` — NEW

Path: `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/EmailNotificationSentEvent.avsc`. Generic, BC-agnostic.

```json
{
  "type": "record",
  "name": "EmailNotificationSentEvent",
  "namespace": "Notifications.Email",
  "doc": "Emitted after IEmailGateway reports successful send for a SendEmailNotificationCommand. Generic — consumers route by TemplateId prefix.",
  "fields": [
    { "name": "UserId",          "type": { "type": "string", "logicalType": "uuid" },             "doc": "Recipient user id, copied from the originating command." },
    { "name": "TemplateId",      "type": "string",                                                "doc": "Template id from the originating command; consumers filter by prefix (e.g., 'invoicing.*')." },
    { "name": "IdempotencyKey",  "type": "string",                                                "doc": "Copied from originating command. Carries the BC-specific correlation hint (e.g., 'invoice-delivered-{InvoiceId}-{Attempt}')." },
    { "name": "SentAtUtc",       "type": { "type": "long",   "logicalType": "timestamp-millis" }, "doc": "When IEmailGateway returned success." },
    { "name": "OccurredOnUtc",   "type": { "type": "long",   "logicalType": "timestamp-millis" }, "doc": "Domain event occurrence time." }
  ]
}
```

Subject (Record Name Strategy): `Notifications.Email.EmailNotificationSentEvent`.

#### 4d. `SendEmailNotificationCommand.avsc` — UNCHANGED

The schema is already shipped. We become its first publisher (Invoicing) and its first consumer (Notifications) — completing what was half-built.

### 5. Topics + options

| Topic | Owner | Retention | Compat | Partition key | Producers | Consumers |
|---|---|---|---|---|---|---|
| `invoicing.invoices` | Invoicing | 10y | FORWARD_TRANSITIVE | BuyerId | InvoiceIssued/Cancelled/Delivered, CreditNoteIssued outbox publishers | Audit, BFF, future |
| `notifications.email-commands` | Notifications | 7d | FULL_TRANSITIVE | UserId | Invoicing (+ Weather already) | Notifications |
| `notifications.email-events` | Notifications | 7d | FORWARD_TRANSITIVE | UserId | Notifications | Invoicing (filter `invoicing.*`) |

Add `NotificationsTopicsOptions` (new file in `services/Notifications/Notifications/Common/Messaging/`):
- `string EmailCommands` — `"notifications.email-commands"`
- `string EmailEvents` — `"notifications.email-events"`
- `string DltTopicSuffix`

Extend `TopicsOptions` with:
- `string NotificationsEmailCommands` — outbound, where Invoicing publishes `SendEmailNotificationCommand`.
- `string NotificationsEmailEvents` — inbound, where Invoicing subscribes for `EmailNotificationSentEvent`.

`appsettings.json` updates in both Invoicing.Api and Notifications. Topics provisioned in `docker-compose` Kafka init if a topic-creation companion exists (check during implementation; if not, accept auto-create-on-first-publish).

### 6. Invoicing.Application — new components

#### 6a. `InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler`

Path: `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler.cs`. Mirrors the 3 existing outbox publisher handlers.

```csharp
public sealed class InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceDeliveryRequestedDomainEvent>
{
    // ctor: ITransactionalOutbox<IInvoicingDbContext> outbox, IInvoicingDbContext db,
    //       IOptions<TopicsOptions> topics, IOptions<BuyerPortalOptions> portal,
    //       TimeProvider clock, ILogger<...> logger.

    public async Task Handle(InvoiceDeliveryRequestedDomainEvent e, CancellationToken ct)
    {
        // Load invoice (still attached to ChangeTracker in this transaction) for number + totals
        // that go into the email body. No blob access — the SAS is minted by the GET endpoint
        // when the buyer clicks through from the portal.
        var invoice = await _db.Invoices.SingleAsync(i => i.Id == e.InvoiceId, ct);

        var portalUrl = $"{_portal.BaseUrl.TrimEnd('/')}/invoices/{e.InvoiceId}";

        var command = new SendEmailNotificationCommand
        {
            UserId = e.BuyerId.ToString(),
            TemplateId = "invoicing.invoice-delivered",
            TemplateData = new Dictionary<string, string>
            {
                ["InvoiceNumber"] = invoice.InvoiceNumber!.Value,
                ["TotalAmount"]   = invoice.Total.Amount.ToString(CultureInfo.InvariantCulture),
                ["Currency"]      = invoice.Total.Currency.Name,
                ["ViewInvoiceUrl"]= portalUrl
            },
            IdempotencyKey = $"invoice-delivered-{e.InvoiceId}-{e.Attempt}",
            OccurredOnUtc = _clock.GetUtcNow().UtcDateTime
        };

        _outbox.AddOutboxMessage(_topics.NotificationsEmailCommands, e.BuyerId.ToString(), command);
        _logger.LogInformation("Queued invoice-delivery email request. InvoiceId={InvoiceId}, Attempt={Attempt}", e.InvoiceId, e.Attempt);
    }
}
```

Async only because of the EF read for the invoice number/totals. No blob-store call. The DispatchDomainEventsInterceptor awaits this inside the EF transaction — atomic with the aggregate save and `pending_invoices` projection-row update.

**New options class:** `services/Invoicing/Invoicing.Application/Common/Notifications/BuyerPortalOptions.cs`

```csharp
public sealed class BuyerPortalOptions
{
    public const string Section = "BuyerPortal";

    [Required(AllowEmptyStrings = false)]
    [Url]
    public required string BaseUrl { get; set; } // e.g. "https://invoicing.example.com" or "http://localhost:5400" in dev
}
```

Bound from `BuyerPortal` config section; validated eagerly. Dev appsettings can point at the API itself; production points at the buyer portal frontend.

#### 6b. `EmailNotificationSentEventKafkaHandler`

Path: `services/Invoicing/Invoicing.Application/Messaging/EmailNotificationSentEventKafkaHandler.cs` (mirrors KafkaFlow handler locations in other BCs — verify exact path during implementation).

```csharp
public sealed class EmailNotificationSentEventKafkaHandler : IMessageHandler<EmailNotificationSentEvent>
{
    public async Task Handle(IMessageContext ctx, EmailNotificationSentEvent message)
    {
        // Route filter — only invoice-delivery emails advance Invoice state.
        if (!message.TemplateId.StartsWith("invoicing.invoice-delivered", StringComparison.Ordinal))
        {
            return;
        }

        if (!TryParseInvoiceIdFromIdempotencyKey(message.IdempotencyKey, out var invoiceId))
        {
            // Bug-class: producer-side mismatch. DLT for operator inspection.
            throw new DataIntegrityException(
                "Invoicing.MalformedDeliveryIdempotencyKey",
                $"Cannot parse InvoiceId from IdempotencyKey '{message.IdempotencyKey}'.");
        }

        var token = ctx.ConsumerContext.WorkerStopped;
        await _outbox.Database.EnsureTransactionAsync(async () =>
        {
            var invoice = await _db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId, token);
            if (invoice is null)
            {
                throw new DataIntegrityException(
                    "Invoicing.InvoiceUnknownOnDeliveryConfirmation",
                    $"No invoice for id '{invoiceId}'.");
            }

            var deliverResult = invoice.Deliver(_clock.GetUtcNow());
            if (deliverResult.IsFailed)
            {
                // Expected-failure path (e.g., already Delivered from a re-delivery race).
                // Log + no-op; the inbox dedup already protected against exact-duplicate processing.
                _logger.LogWarning("Invoice.Deliver no-op for {InvoiceId}: {Errors}", invoiceId, string.Join("; ", deliverResult.Errors.Select(e => e.Message)));
                return;
            }

            await _db.SaveChangesAsync(token); // raises InvoiceDeliveredDomainEvent via interceptor
        }, token);
    }

    private static bool TryParseInvoiceIdFromIdempotencyKey(string key, out Guid id) =>
        // "invoice-delivered-{guid}-{attempt}"
        Guid.TryParse(key.Split('-', 5).ElementAtOrDefault(2), out id);
}
```

DI registration in `MessagingDependencyInjection` (Invoicing.Application): KafkaFlow subscriber adds `notifications.email-events` to the consumer pipeline; `EmailNotificationSentEventKafkaHandler` registered. Inbox middleware enabled for idempotency.

#### 6c. `InvoiceDeliveredOutboxPublisherDomainEventHandler`

Path: `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveredOutboxPublisherDomainEventHandler.cs`. Mirrors `InvoiceIssuedOutboxPublisherDomainEventHandler.cs` exactly.

```csharp
public sealed class InvoiceDeliveredOutboxPublisherDomainEventHandler
    : IDomainEventHandler<InvoiceDeliveredDomainEvent>
{
    public Task Handle(InvoiceDeliveredDomainEvent e, CancellationToken ct)
    {
        var integrationEvent = e.ToInvoiceDeliveredEvent();
        _outbox.AddOutboxMessage(_topics.Invoices, e.BuyerId.ToString(), integrationEvent);
        _logger.LogInformation("Queued InvoiceDeliveredEvent to outbox. InvoiceId: {InvoiceId}, CorrelationId: {CorrelationId}", e.InvoiceId, e.CorrelationId);
        return Task.CompletedTask;
    }
}
```

#### 6d. `InvoiceDeliveredMapper`

Path: `services/Invoicing/Invoicing.Application/Outbox/InvoiceDeliveredMapper.cs`. Static extension `ToInvoiceDeliveredEvent` mirroring `InvoiceIssuedMapper`.

#### 6e. `IssueInvoiceCommandHandler.cs:159` flip

```csharp
var deliveryChannel = DeliveryChannel.Email; // M8 ships SendEmailNotificationCommand fan-out via InvoiceDeliveryRequestedOutboxPublisher
```

### 7. Notifications BC — new components

#### 7a. `IEmailGateway` + `MockEmailGateway`

Path: `services/Notifications/Notifications/Email/IEmailGateway.cs` + `MockEmailGateway.cs`. Lightweight; production-grade real gateway is Phase 2.

```csharp
public sealed record EmailMessage(string ToUserId, string Subject, string Body);

public interface IEmailGateway
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct);
}

internal sealed class MockEmailGateway(ILogger<MockEmailGateway> logger, TimeProvider clock) : IEmailGateway
{
    public Task<Result> SendAsync(EmailMessage m, CancellationToken ct)
    {
        logger.LogInformation("[MOCK EMAIL] to={ToUserId} subject='{Subject}' body-len={BodyLen} at={At:O}",
            m.ToUserId, m.Subject, m.Body.Length, clock.GetUtcNow());
        return Task.FromResult(Result.Ok());
    }
}
```

#### 7b. `EmailTemplateRenderer`

Path: `services/Notifications/Notifications/Email/EmailTemplateRenderer.cs`. Phase 1: in-process hard-coded templates keyed on `TemplateId`. Phase 2: template store + Razor or Liquid.

```csharp
public interface IEmailTemplateRenderer
{
    Result<EmailMessage> Render(string toUserId, string templateId, IDictionary<string, string> data);
}

internal sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public Result<EmailMessage> Render(string toUserId, string templateId, IDictionary<string, string> data) => templateId switch
    {
        "invoicing.invoice-delivered" => RenderInvoiceDelivered(toUserId, data),
        _ => Result.Fail<EmailMessage>($"Unknown template '{templateId}'.")
    };

    private static Result<EmailMessage> RenderInvoiceDelivered(string toUserId, IDictionary<string, string> d)
    {
        if (!d.TryGetValue("InvoiceNumber", out var num)) return Result.Fail<EmailMessage>("Missing 'InvoiceNumber'.");
        if (!d.TryGetValue("ViewInvoiceUrl", out var url)) return Result.Fail<EmailMessage>("Missing 'ViewInvoiceUrl'.");
        var subject = $"Invoice {num} — your copy is ready";
        var body = $"Hello,\n\nYour invoice {num} is ready. Sign in to view & download: {url}";
        return Result.Ok(new EmailMessage(toUserId, subject, body));
    }
}
```

#### 7c. `SendEmailNotificationCommandKafkaHandler`

Path: `services/Notifications/Notifications/Notifications/SendEmailNotification/SendEmailNotificationCommandKafkaHandler.cs` (mirrors the existing `AuthorizePayment` handler folder shape).

```csharp
public sealed class SendEmailNotificationCommandKafkaHandler : IMessageHandler<SendEmailNotificationCommand>
{
    public async Task Handle(IMessageContext ctx, SendEmailNotificationCommand cmd)
    {
        var token = ctx.ConsumerContext.WorkerStopped;
        await _outbox.Database.EnsureTransactionAsync(async () =>
        {
            var renderResult = _renderer.Render(cmd.UserId, cmd.TemplateId, cmd.TemplateData);
            if (renderResult.IsFailed)
            {
                throw new DataIntegrityException("Notifications.UnknownTemplate", string.Join("; ", renderResult.Errors.Select(e => e.Message)));
            }

            var sendResult = await _gateway.SendAsync(renderResult.Value, token);
            if (sendResult.IsFailed)
            {
                // Transient — let KafkaFlow retry; eventually DLT.
                throw new InvalidOperationException("Email gateway failed: " + string.Join("; ", sendResult.Errors.Select(e => e.Message)));
            }

            _outbox.AddOutboxMessage(_topics.EmailEvents, cmd.UserId, new EmailNotificationSentEvent
            {
                UserId = cmd.UserId,
                TemplateId = cmd.TemplateId,
                IdempotencyKey = cmd.IdempotencyKey,
                SentAtUtc = _clock.GetUtcNow().UtcDateTime,
                OccurredOnUtc = _clock.GetUtcNow().UtcDateTime
            });
            await _outbox.SaveChangesAsync(token);
        }, token);
    }
}
```

KafkaFlow subscriber config in Notifications adds `notifications.email-commands` with `EmailNotificationSentEventKafkaHandler` registered.

### 8. DI + KafkaFlow wiring

- Invoicing.Application `MessagingDependencyInjection`: register the 3 new handlers (2 domain-event handlers + 1 KafkaFlow consumer). Register `BuyerPortalOptions` from config section `BuyerPortal`.
- Invoicing KafkaFlow consumer: extend to subscribe to `notifications.email-events`. Inbox middleware on (idempotency).
- Notifications `MessagingDependencyInjection`: register `IEmailGateway`, `IEmailTemplateRenderer`, `SendEmailNotificationCommandKafkaHandler`. KafkaFlow consumer subscribes to `notifications.email-commands`.
- Notifications `Common/Config/TopicsOptions.cs`: add `EmailCommands`, `EmailEvents`.
- Invoicing `TopicsOptions.cs`: add `NotificationsEmailCommands`, `NotificationsEmailEvents`.
- `appsettings.json` updates in both BCs: topic names + `BuyerPortal:BaseUrl` (Invoicing only; dev default points at the Invoicing API itself).

### 9. Tests (TDD per BC)

#### Invoicing.UnitTests
- `PdfBlobRefTests` — refactor existing tests for `BlobName` shape; remove URI tests.
- `Invoices/IssueInvoice/InvoiceDeliveryRequestedOutboxPublisherTests` — fake `BuyerPortalOptions.BaseUrl`, assert template-data map shape (InvoiceNumber, TotalAmount, Currency, ViewInvoiceUrl), partition key, topic, IdempotencyKey shape.
- `Invoices/Delivery/InvoiceDeliveredOutboxPublisherTests` — mirrors `InvoiceIssuedOutboxPublisherTests` precisely.
- `Invoices/Delivery/InvoiceDeliveredMapperTests` — projection from domain event to Avro event.
- Existing `InvoiceInvariantsTests` — confirm `Invoice.Deliver` test coverage; extend if needed.

#### Invoicing.IntegrationTests
- Extend `IssueInvoiceCommandHandlerTests` — after issuance assert outbox has BOTH `InvoiceIssuedEvent` row AND `SendEmailNotificationCommand` row (template data: InvoiceNumber, TotalAmount, Currency, ViewInvoiceUrl built from `BuyerPortalOptions.BaseUrl` + InvoiceId).
- New `Messaging/Kafka/EmailNotificationSentEventKafkaHandlerTests` — drive the Kafka handler with a synthesized `EmailNotificationSentEvent`, assert invoice transitions to Delivered, `InvoiceDeliveredEvent` outbox row written. Cover the "template-prefix-mismatch → no-op" filter branch and the "invoice already Delivered → Result.Fail → log + no-op" branch. Mirrors `ConfirmReservationCommandKafkaHandlerTests` from Inventory.

#### Invoicing.ArchitectureTests
- `DomainEventHandlerTests` already enforces "every domain-event handler discovered + registered"; new handlers picked up automatically. Add assertion for new outbox publishers.

#### Invoicing.FunctionalTests
- Existing tests (`GetInvoiceById`, `GetInvoicesByBuyer`) — confirm DTO `pdfDownloadUrl` still serves a fresh SAS post-refactor. Update `InvoiceSeed` if needed.

#### Notifications.UnitTests (new project — check if exists; if not, create mirroring Inventory.UnitTests)
- `EmailTemplateRendererTests` — happy + missing-key paths for `invoicing.invoice-delivered`.
- `SendEmailNotificationCommandKafkaHandlerTests` — mock `IEmailGateway`, assert outbox row on success, retry-throw on gateway failure.
- `MockEmailGatewayTests` — basic shape + logger interaction.

#### Notifications.IntegrationTests (new project — Testcontainers fixture mirroring Invoicing.IntegrationTests)
- Full Kafka + Postgres + Schema Registry round-trip: publish `SendEmailNotificationCommand`, assert mock gateway invoked, assert `EmailNotificationSentEvent` lands in outbox.

#### End-to-end (Invoicing.IntegrationTests, since cross-BC E2E lives in single-process tests today)
- A "from IssueInvoiceCommand to InvoiceDeliveredEvent outbox row" test. Sequence:
  1. Seed converged `pending_invoices` row + dependent state (existing helper).
  2. Dispatch `IssueInvoiceCommand`.
  3. Assert outbox rows: `InvoiceIssuedEvent` + `SendEmailNotificationCommand`.
  4. Simulate Notifications by directly publishing an `EmailNotificationSentEvent` matching the just-emitted `IdempotencyKey` to the Invoicing-side `EmailNotificationSentEventKafkaHandler`.
  5. Assert invoice.Status == Delivered + `InvoiceDeliveredEvent` outbox row.

**Mock the email gateway in all tests.** Never reach for SMTP/SendGrid.

### 10. Migration boundary

DB migration for column rename `invoices.pdf_blob_uri → pdf_blob_name`:
> **STOP. Ask user to generate the migration via `dotnet ef migrations add RenamePdfBlobUriToBlobName` in the Invoicing.Infrastructure project, then resume.** CLAUDE.md is explicit: never auto-generate migrations.

The migration will reflect the column rename. Verify the migration's `Up` method uses `RenameColumn` (not `DropColumn` + `AddColumn`, which would lose data — even for a reference repo we want the right operation).

### 11. Build + format gates (per CLAUDE.md)

After each commit:
- `dotnet build -m` (must touch Platform.SharedKernel? No — we change only Invoicing.Domain, Invoicing.Application, Notifications, and Avro contracts. But run anyway as defense-in-depth — see CLAUDE.md history of CS9035).
- `dotnet format whitespace --no-restore --verify-no-changes`
- `dotnet format style --no-restore --verify-no-changes`

Per-BC test runs (proxy-stripped):
```bash
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.UnitTests/Invoicing.UnitTests.csproj
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.IntegrationTests/Invoicing.IntegrationTests.csproj
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.FunctionalTests/Invoicing.FunctionalTests.csproj
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Invoicing.ArchitectureTests/Invoicing.ArchitectureTests.csproj
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Notifications.UnitTests/Notifications.UnitTests.csproj
unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj
```

(`Notifications.UnitTests`/`.IntegrationTests` projects don't exist yet; create them mirroring Inventory's test-project structure.)

### 12. Commits (one logical change each)

1. **`refactor(invoicing): PdfBlobRef.BlobUri -> BlobName (issue #131)`** — Domain VO change + EF column rename + query-handler updates + Avro field add + InvoiceIssuedMapper update + InvoiceIssuedOutboxPublisherTests update. Stops at the EF migration boundary (user generates, then I resume).
2. **`feat(notifications): SendEmailNotificationCommand consumer + EmailNotificationSentEvent`** — Notifications projects (Unit + Integration), IEmailGateway/MockEmailGateway, EmailTemplateRenderer, SendEmailNotificationCommandKafkaHandler, EmailNotificationSentEvent.avsc + binding, DI, appsettings, tests.
3. **`feat(invoicing): wire InvoiceDeliveryRequested + InvoiceDelivered outbox publishers + reciprocal consumer; flip DeliveryChannel.Email`** — InvoiceDeliveryRequestedOutboxPublisher + InvoiceDeliveredOutboxPublisher + InvoiceDeliveredMapper + EmailNotificationSentEventKafkaHandler + InvoiceDeliveredEvent.avsc + binding + DI + `IssueInvoiceCommandHandler.cs:159` flip + tests.
4. **`test(e2e): IssueInvoice → InvoiceDelivered round-trip integration test`** — end-to-end test in Invoicing.IntegrationTests with mock Notifications publish step.
5. **`docs: close out #123 + #131`** — session summary + issue close comments referencing the commits.

## Out of scope (file as Phase-2 follow-ups)

- **Real email gateway** (SendGrid/SES/SMTP) behind `IEmailGateway`. Mock-only in Phase 1.
- **Multi-channel delivery** — SMS + InApp channels. Schema enum supports the channel; consumer logic doesn't.
- **Magic-link / passwordless portal URL** — Phase 2: if the buyer-portal flow benefits from a one-click signed link (e.g., for unauthenticated B2C scenarios) the portal URL can be replaced with a signed-token redirect. Out of scope here.
- **Database-backed template store** — Phase 1 has one in-process hardcoded template.
- **BFF cache invalidation on `InvoiceDeliveredEvent`** — separate BC concern.
- **Notifications delivery retry policy beyond KafkaFlow default** — tunable in a later pass.
- **Cross-BC functional test infrastructure** (real Kafka round-trip across Notifications + Invoicing processes simultaneously) — Phase 1 uses an in-process synthesized round-trip.

## Risks (pre-implementation)

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| `PdfBlobName` validation in `PdfBlobRef.Create` rejects historical data | Low | Med | Validate against `InvoicePdfBlobName.For(...)` shape only; relax if existing fixtures fail. |
| KafkaFlow consumer wiring on a new topic (`notifications.email-events`) in Invoicing requires schema-registry pre-registration | Med | Low | Subject auto-registered on first publish per UniversalAvroSerializer convention; verify in integration test. |
| Notifications project structure doesn't have `MessagingDependencyInjection` shape | Med | Low | Existing `Common/MessagingDependencyInjection.cs` exists; extend it. |
| Inbox-dedup window for `EmailNotificationSentEvent` may interact with re-delivery attempts | Low | Low | Phase 1 has Attempt=1 always; re-delivery is a Phase 2 concern. |
| FORWARD_TRANSITIVE break — registry rejects renamed schema on publish | High (guaranteed if subject pre-exists) | Med | Delete the existing `Invoicing.Invoices.InvoiceIssuedEvent` subject before first publish (registry REST: `DELETE /subjects/{subject}`); document in commit message + session summary. Or update `schema-registry-init` bootstrap to seed the new shape. |
| Local dev environments retain old subject in cached registry volume | Med | Low | Add a one-shot reset note to dev onboarding docs / docker-compose; or document `docker compose down -v` requirement. |

## Open questions

None blocking. The following can be resolved during implementation by reading the codebase:

- Exact KafkaFlow subscriber-config file path in Notifications (current scaffold has one Kafka handler; subscriber may be in `MessagingDependencyInjection.cs`).
- Whether `Notifications.UnitTests` + `Notifications.IntegrationTests` projects already exist (search returned no files; will create if missing).
- Whether `InvoiceConfiguration.cs` stores `PdfBlobUri` as a single column or as an owned-entity sub-table (column rename vs. table rename).
- Whether existing `appsettings.json` has the bound `InvoicingTopics` section in both Invoicing.Api and Invoicing.IntegrationTests (yes per prior wave-1 work).

## Resolution

- `gh issue close 123 -c "Resolved by <commits>: shipped InvoiceDeliveredEvent.avsc + outbox publisher + Notifications delivery consumer (mock email channel) + flipped DeliveryChannel.None -> Email."`
- `gh issue close 131 -c "Resolved by <commits>: PdfBlobRef now keyed on canonical BlobName; InvoiceIssuedEvent.avsc renamed PdfBlobUri -> PdfBlobName (accepted breaking change for the reference repo per ADR-0007 deviation note)."`
