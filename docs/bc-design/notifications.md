# Notifications Bounded Context

> **Status:** Authored 2026-05-30. The other six BC design docs (catalog/basket/ordering/inventory/payments/invoicing) reference Notifications as a downstream consumer but no design doc for the BC itself existed. This file fills that gap and **codifies what is actually implemented**, which diverges from how earlier docs describe the integration shape (see § 11 — Cross-doc divergence).
> **Scope:** A thin, single-channel **outbound notification dispatcher**. Receives generic SendEmail commands from other BCs, renders a template, calls an email gateway, and emits a delivery-confirmation event. No domain model, no public HTTP surface.
> **Pattern showcased:** **Command-driven fan-in plumbing BC.** Producers explicitly *ask* Notifications to send an email; Notifications never reaches into another BC's event stream to decide policy. The producer owns the *intent* (which user, which template, which idempotency key); Notifications owns the *channel* (render → send → confirm).
> **Storage:** PostgreSQL, schema `notifications`. Only platform Inbox / Outbox tables — no domain tables.
> **Folder:** `services/Notifications/` (4-project layout — Domain / Application / Infrastructure / Api).

---

## 1. Purpose & Strategic Classification

Notifications is a **generic supporting subdomain** in the strategic-DDD sense — it does not contain any of the e-shop's competitive value and could in principle be replaced by a SaaS like SendGrid + a thin lambda. The reference solution implements it in-process to demonstrate the integration pattern, not to model a rich domain.

Its job is intentionally narrow:

1. Consume `SendEmailNotificationCommand` from `notifications.email-commands` (one topic, one consumer group).
2. Render the requested template against the supplied data.
3. Call `IEmailGateway.SendAsync(...)`.
4. On gateway success, write `EmailNotificationSentEvent` to the outbox → `notifications.email-events`.

That is the entire feature set. There is no aggregate to model, no business invariants beyond gateway success/failure semantics, and no query-side. The Application layer's DI extension is literally:

```csharp
public IServiceCollection AddApplication() => services;
```

(see [`ApplicationDependencyInjection.cs`](../../services/Notifications/Notifications.Application/Common/ApplicationDependencyInjection.cs)) — reserved for future CQRS handlers, but registers nothing today.

### 1.1 Why this BC has no Domain layer

The `Notifications.Domain` project exists for layering consistency with the other BCs, but contains only `IAssemblyMarker.cs`. There is no aggregate because:

- There is no business decision to make. The producer has already decided *what* to send and *to whom*; Notifications only routes it through a gateway.
- There are no multi-step state machines (no `Pending → Sent → Bounced → Retried` lifecycle persisted as an aggregate). KafkaFlow's retry/DLT middleware handles transient failures; idempotency is handled by the inbox.
- No aggregate-level invariants exist that the email-gateway adapter could not enforce on its own.

A future evolution that adds e.g. a `NotificationDigest` aggregate (group N notifications for a buyer into a single daily email), a suppression list, or per-user channel preferences would justify a Domain layer. None of those exist in v1.

`NotificationsDbContext.OnModelCreating(...)` explicitly skips `ApplyConfigurationsFromAssembly` to silence EF's "no `IEntityTypeConfiguration<>` types found" warning — the comment on [`NotificationsDbContext.cs:23-32`](../../services/Notifications/Notifications.Infrastructure/Persistence/Database/NotificationsDbContext.cs#L23) documents the convention to follow when the first domain entity lands.

---

## 2. Architecture Pattern: Command-driven fan-in

The producing BC is responsible for the editorial decision ("the buyer should be emailed about *this* business moment with *this* data"). Notifications is responsible only for the delivery mechanics. Wire shape:

```
   (producer BC)                           Notifications BC                       (consumer BC)
       │                                          │                                       │
       │  SendEmailNotificationCommand            │                                       │
       │  on notifications.email-commands         │                                       │
       │  ───────────────────────────────────────▶│                                       │
       │  • UserId       (recipient identity)     │                                       │
       │  • TemplateId   ("{bc}.{event-name}")    │                                       │
       │  • TemplateData (map<string,string>)     │   IEmailTemplateRenderer.Render(...)  │
       │  • IdempotencyKey (BC-deterministic)     │   IEmailGateway.SendAsync(...)        │
       │  • OccurredOnUtc                         │                                       │
       │                                          │                                       │
       │                                          │  EmailNotificationSentEvent           │
       │                                          │  on notifications.email-events        │
       │                                          │ ─────────────────────────────────────▶│
       │                                          │  • UserId                             │
       │                                          │  • TemplateId                         │
       │                                          │  • IdempotencyKey  (carry-through)    │
       │                                          │  • SentAtUtc                          │
       │                                          │  • OccurredOnUtc                      │
```

### 2.1 Why not subscribe to per-BC topics directly?

A common alternative would be: Notifications subscribes to `ordering.orders`, `payments.transactions`, `inventory.reservations`, etc., and uses its own routing rules to decide what to send. That pattern *is* described in some of the other BC docs (see § 11 — Cross-doc divergence), but is **not** the implemented one. Trade-offs:

| Concern | Command-driven (current) | Direct subscription (alternative) |
|---|---|---|
| **Editorial control** | Producer BC owns "should we email?" — natural place for the policy. | Notifications owns the routing — pulls policy out of the BC that has the domain knowledge. |
| **Coupling shape** | Producer → command-topic (transient, 7-day) → Notifications. | Notifications → every-BC's event-topic (infinite-retention audit log). |
| **Idempotency key origin** | Producer mints a deterministic key from its own state (e.g. `invoice-delivered-{InvoiceId}-{Attempt}`). | Notifications must derive a stable key from each event shape it consumes — N derivations to maintain. |
| **Add a new channel (SMS, push)** | Producer emits an `SmsNotificationCommand` alongside the email command, no Notifications-side schema awareness needed. | Notifications gains another consumer per topic per channel — N×M expansion. |
| **Producer can A/B "should email"** | Yes — toggle the outbox write at the source. | Notifications must filter inbound events, requires deploy-coupled config. |

The command-driven path keeps `Notifications` a true plumbing service: every business decision is upstream, every delivery decision is local. This matches how a SaaS like SendGrid or Postmark is consumed in production — the caller sends a fully-formed request, the gateway just dispatches.

---

## 3. Application Ports

Live in `Notifications.Application.Email`. Three small types, no behaviour beyond DTO/contract.

### 3.1 `EmailMessage`

```csharp
public sealed record EmailMessage(string ToUserId, string Subject, string Body);
```

(see [`EmailMessage.cs`](../../services/Notifications/Notifications.Application/Email/EmailMessage.cs))

- `ToUserId` is a user *identity*, not a resolved email address. The gateway adapter is responsible for resolving an actual address (e.g., a future call to a user-profile service). The Phase-1 `MockEmailGateway` skips resolution and just logs.

### 3.2 `IEmailGateway`

```csharp
public interface IEmailGateway
{
    Task<Result> SendAsync(EmailMessage message, CancellationToken ct);
}
```

(see [`IEmailGateway.cs`](../../services/Notifications/Notifications.Application/Email/IEmailGateway.cs))

- Returns `FluentResults.Result` — gateway-level failures are surfaced; the handler decides whether to retry (transient) or DLT (poisoned).
- Phase-1 implementation: [`MockEmailGateway`](../../services/Notifications/Notifications.Infrastructure/Email/MockEmailGateway.cs) — logs and returns success. DI-registered by default.
- Phase-2 (not implemented): real adapter — SendGrid, AWS SES, SMTP, etc. Swap via DI.

### 3.3 `IEmailTemplateRenderer`

```csharp
public interface IEmailTemplateRenderer
{
    Result<EmailMessage> Render(string toUserId, string templateId, IDictionary<string, string> data);
}
```

(see [`IEmailTemplateRenderer.cs`](../../services/Notifications/Notifications.Application/Email/IEmailTemplateRenderer.cs))

- Returns `Result.Fail` on unknown template id or missing required template fields. The handler treats render failures as **bug-class** (`InvalidOperationException` thrown → KafkaFlow retries → eventually DLT).
- Phase-1 implementation: [`EmailTemplateRenderer`](../../services/Notifications/Notifications.Infrastructure/Email/EmailTemplateRenderer.cs) — a hardcoded `switch` over `templateId`. Currently supports exactly **one** template:

| `TemplateId` | Required `TemplateData` keys | Optional keys | Producer |
|---|---|---|---|
| `invoicing.invoice-delivered` | `InvoiceNumber`, `ViewInvoiceUrl` | `TotalAmount`, `Currency` | Invoicing (`InvoiceDeliveryRequestedOutboxPublisherDomainEventHandler`) |

Convention for template ids: `{bounded-context}.{notification-type}` (lower-kebab). Per the wire-schema doc of `SendEmailNotificationCommand.avsc`. The prefix lets consumers of `EmailNotificationSentEvent` route by simple prefix filtering.

A Phase-2 evolution (template store + Razor/Liquid) is noted in the comment of [`EmailTemplateRenderer.cs:6`](../../services/Notifications/Notifications.Infrastructure/Email/EmailTemplateRenderer.cs#L6).

---

## 4. Inbound — `SendEmailNotificationCommand`

**Topic:** `notifications.email-commands`
**Schema:** [`SendEmailNotificationCommand.avsc`](../../platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/SendEmailNotificationCommand.avsc)
**Partition key:** `UserId` (per [events-catalog.md § 2](events-catalog.md))
**Consumer group:** `notifications-group` (from `appsettings.json` — one-group-per-service rule per [events-catalog.md § 3.1](events-catalog.md))
**Retention:** 7 days (command topic — [events-catalog.md § 3.2 D-9](events-catalog.md))
**Avro compatibility:** FULL_TRANSITIVE (default per [ADR-0007](../adr/0007-avro-compatibility-modes.md)).

| Field | Type | Notes |
|---|---|---|
| `UserId` | uuid | Target user identity; gateway resolves the address. |
| `TemplateId` | string | `{bc}.{notification-type}`, e.g. `invoicing.invoice-delivered`. |
| `TemplateData` | `map<string,string>` | Template-specific key/value rendering data. |
| `IdempotencyKey` | string | Deterministic, mint by the producer (e.g. `invoice-delivered-{InvoiceId}-{Attempt}`). Becomes the inbox primary key. |
| `OccurredOnUtc` | timestamp-millis | Producer-side creation time. |

### 4.1 Handler — `SendEmailNotificationCommandKafkaHandler`

Source: [`SendEmailNotificationCommandKafkaHandler.cs`](../../services/Notifications/Notifications.Infrastructure/SendEmailNotification/SendEmailNotificationCommandKafkaHandler.cs).

Inside a single `EnsureTransactionAsync` boundary:

1. `IEmailTemplateRenderer.Render(...)` — failure → `InvalidOperationException` (bug-class; producer sent an unknown template or malformed data).
2. `IEmailGateway.SendAsync(...)` — failure → `InvalidOperationException` (treated as transient; retried per KafkaFlow middleware then DLTed).
3. `_outbox.AddOutboxMessage(EmailEvents, UserId, EmailNotificationSentEvent { ... })` — built using the platform outbox abstraction.
4. `_outbox.SaveChangesAsync(...)` — flushes outbox row in the same transaction as the inbox row that KafkaFlow's `AddInbox(typeof(SendEmailNotificationCommand))` middleware writes.

The transactional outbox guarantees: either the gateway saw a successful send AND the outbox row was written, or neither happened — at-least-once delivery to the gateway is acceptable since the producer-supplied `IdempotencyKey` is the inbox primary key.

### 4.2 Idempotency and at-least-once

`KafkaFlow.AddInbox(typeof(SendEmailNotificationCommand))` (see [`MessagingDependencyInjection.cs:97`](../../services/Notifications/Notifications.Infrastructure/Common/MessagingDependencyInjection.cs#L97)) extracts the Avro `IdempotencyKey` and uses it as the inbox primary key. A redelivered command with the same key is short-circuited before the handler runs — the email is sent at most once per `(IdempotencyKey)`. The producer is therefore responsible for minting an idempotency key that is **deterministic with respect to the business intent**: not `Guid.NewGuid()`, but a derivable string like `invoice-delivered-{InvoiceId}-{Attempt}` so that retries from the producer's outbox don't double-send.

### 4.3 Retry & DLT

[`MessagingDependencyInjection.cs:88-96`](../../services/Notifications/Notifications.Infrastructure/Common/MessagingDependencyInjection.cs#L88) configures KafkaFlow middleware:

- `RetryForever` on `DbUpdateException`, `NpgsqlException`, `TimeoutException` — exponential backoff `500ms → 1s → 2s → 5s → repeat`. Persistent infrastructure errors are retried forever (no DLT for these).
- All other exceptions (including the bug-class `InvalidOperationException` from a failed render) → `AddDeadLetter()` middleware writes to `notifications.email-commands.Notifications.DLT` (registered in `docker-compose.yaml:325` with 14-day retention). Conforms to the global [kafka-dlt-strategy.md](kafka-dlt-strategy.md).

---

## 5. Outbound — `EmailNotificationSentEvent`

**Topic:** `notifications.email-events`
**Schema:** [`EmailNotificationSentEvent.avsc`](../../platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/EmailNotificationSentEvent.avsc)
**Partition key:** `UserId`
**Retention:** infinite (audit — `docker-compose.yaml:316` has `retention.ms=-1`)
**Avro compatibility:** FORWARD_TRANSITIVE (default for events per [ADR-0007](../adr/0007-avro-compatibility-modes.md)).

| Field | Type | Notes |
|---|---|---|
| `UserId` | uuid | Copied from originating command. |
| `TemplateId` | string | Copied — consumers route by prefix (`invoicing.*`, etc.). |
| `IdempotencyKey` | string | Carry-through; lets the consumer correlate back to its original outbox row without parsing it. |
| `SentAtUtc` | timestamp-millis | When `IEmailGateway.SendAsync` returned success. |
| `OccurredOnUtc` | timestamp-millis | Domain event occurrence (currently equal to `SentAtUtc`). |

### 5.1 Current consumer — Invoicing

Today exactly **one** BC consumes this event: Invoicing's [`EmailNotificationSentEventKafkaHandler`](../../services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Notifications/EmailNotificationSentEventKafkaHandler.cs), which filters on `TemplateId.StartsWith("invoicing.")` and drives the `Issued → Delivered` invoice state transition. See [invoicing.md § 6](invoicing.md) for the receiving end.

Any future BC that needs delivery confirmation feedback (e.g. "the cancellation email was actually sent before we expire the offer") subscribes here with its own consumer group and prefix filter — no schema change required.

---

## 6. Storage

Schema `notifications` (Postgres). Two tables, both platform-shared:

| Table | Owner | Purpose |
|---|---|---|
| `notifications.InboxMessages` | `Platform.ReliableMessaging.Inbox.EFCore` | Idempotency — primary key is the producer-supplied `IdempotencyKey`. Stores `processed_at_utc`. |
| `notifications.OutboxMessages` | `Platform.ReliableMessaging.Outbox.EFCore` | Pending outbound events (`EmailNotificationSentEvent`) awaiting relay to Kafka. |

**No domain tables.** Migrations under [`services/Notifications/Notifications.Infrastructure/Persistence/Database/Migrations/`](../../services/Notifications/Notifications.Infrastructure/Persistence/Database/Migrations/):

- `20260417121247_Init` — created the Inbox + Outbox tables under the (then-named) `payment` schema. Schema choice was a copy-paste carry-over from the BC-template.
- `20260525094927_RenameSchemaToNotifications` — renames `payment.*` → `notifications.*`. Schema-rename only; no data-shape change.

Outbox relay runs in a sidecar container: `outbox-relay-notifications` (see `docker-compose.yaml:1197`) with `OutboxRelay__SchemaName=notifications` per [events-catalog.md § 2 D-6](events-catalog.md).

---

## 7. Topics — Summary

| Topic | Partitions | Retention | Key | Direction |
|---|---|---|---|---|
| `notifications.email-commands` | 3 | 7 days | `UserId` | **inbound** — Notifications consumes |
| `notifications.email-commands.Notifications.DLT` | 3 | 14 days | (preserved) | DLT for unrenderable / poisoned commands |
| `notifications.email-events` | 3 | infinite | `UserId` | **outbound** — Notifications publishes |

Both topics are registered in `docker-compose.yaml` (lines 315–316 + 325 for the DLT). The `notifications.email-events` topic is **new** in this BC's lifecycle and is **not** listed in [events-catalog.md § 3](events-catalog.md) (which predated it — see § 11.2 below).

---

## 8. HTTP API

There is no public HTTP API.

`Notifications.Api/Program.cs` boots an ASP.NET Core host purely for the standard platform conveniences:

- `MapPlatformHealthCheckEndpoints()` — `/api/liveness`, `/api/readiness`, `/api/healthz` per the platform shared kernel.
- `UsePlatformHealthChecksPrometheusExporter()` — `/metrics`.
- `MigrateOnStartupIfDevelopmentAsync<NotificationsDbContext>()` — dev-only schema migration.
- A KafkaFlow bus boot (`kafkaBus.StartAsync()`) — gated off in the test host as documented at [`Program.cs:35-44`](../../services/Notifications/Notifications.Api/Program.cs#L35).

No controllers, no minimal API endpoints, no admin surface. The container's only non-health-check work is consuming Kafka.

---

## 9. Observability

- `ApplicationInfo.AppName = "Notifications"` — used as `OTEL_SERVICE_NAME`, as the Kafka producer origin header (`MessagingDependencyInjection.KafkaProducerOrigin`), and as the outbox `ConfigureMessageOrigin` value.
- KafkaFlow's `.AddOpenTelemetryInstrumentation()` is wired in `MessagingDependencyInjection`, so consumer spans on `notifications.email-commands` and producer spans on `notifications.email-events` are emitted automatically.
- The structured log in the handler ([`SendEmailNotificationCommandKafkaHandler.cs:84-86`](../../services/Notifications/Notifications.Infrastructure/SendEmailNotification/SendEmailNotificationCommandKafkaHandler.cs#L84)) tags `UserId`, `TemplateId`, and `IdempotencyKey` — the latter two are not PII; `UserId` follows the BC-wide PII rule per [ADR-0011] (when authored).
- A custom `ActivitySource` exists ([`Common/Observability/Tracing/NotificationsActivitySource.cs`](../../services/Notifications/Notifications.Infrastructure/Common/Observability/Tracing/NotificationsActivitySource.cs)) but is not currently called from anywhere — reserved for future per-handler spans on top of the KafkaFlow OpenTelemetry instrumentation.

---

## 10. Testing

Notifications currently has no dedicated test project. Coverage for the only producer of `SendEmailNotificationCommand` lives in the producing BC (see `test/Invoicing.IntegrationTests/...` for the Invoicing-side outbox publisher + the round-trip through `EmailNotificationSentEvent`).

When a second template / a real `IEmailGateway` adapter / a domain aggregate lands, the test layout should mirror the other BCs: `test/Notifications.UnitTests/` (renderer table per template), `test/Notifications.IntegrationTests/` (Testcontainers Postgres + the Kafka handler driving the outbox), `test/Notifications.ArchitectureTests/` (layering guards).

---

## 11. Cross-doc divergence (read this before reading the other BC docs)

Five existing BC design docs and one decision row in the master events catalog describe an integration shape with Notifications that **does not match the implemented code**. The implementation is correct; the docs predate it.

### 11.1 "Notifications subscribes to per-BC topics" — stale

| Doc | Statement | Reality |
|---|---|---|
| [ordering.md § 4](ordering.md#integration) (≈ L504) | "Notifications subscribes to `ordering.orders` topic, filters by event name, renders buyer-facing emails." | Notifications does not subscribe to `ordering.orders` at all. There is no Ordering-side producer of `SendEmailNotificationCommand` either, so today no Ordering events trigger any emails. |
| ordering.md § 7 events table (L261-265) | `OrderConfirmedEvent`, `OrderCancelledEvent`, `OrderShippedEvent`, `OrderDeliveredEvent`, `OrderFailedEvent` all list "Notifications" as a consumer. | No `ordering.orders` consumer is wired in [`MessagingDependencyInjection.cs`](../../services/Notifications/Notifications.Infrastructure/Common/MessagingDependencyInjection.cs). |
| [inventory.md § 7.4](inventory.md) (L427, L731) | "Notifications (optional) consumes `ReservationConfirmedEvent` — 'Your order is being prepared' notification." | Not implemented. |
| [payments.md § 6 events table](payments.md) (L170) | `PaymentRefundedEvent` lists Notifications as a consumer. | Not implemented. |
| [invoicing.md § 6 events table](invoicing.md) (L142-145) | `InvoiceIssuedEvent`, `InvoiceCancelledEvent`, `CreditNoteIssuedEvent` list Notifications as a consumer. | The implemented flow is the *reverse*: Invoicing produces `SendEmailNotificationCommand`, Notifications produces `EmailNotificationSentEvent`, and Invoicing consumes that back to transition `Issued → Delivered`. There is no Notifications-side consumer of `invoicing.invoices`. |
| [events-catalog.md § 1.4 D-5](events-catalog.md) | "Notifications continues consuming business events directly (not a `notifications.email-commands` fan-out). Matches existing Weather→Notifications pattern. `notifications.email-commands` topic stays reserved for explicit SendEmail commands; Ordering emits `ordering.orders` and Notifications subscribes." | Direction was reversed in the implementation. Notifications consumes **only** `SendEmail` commands; producing BCs translate their domain events into commands. |

These are documentation artefacts of an earlier design intent. The implemented pattern (§ 2) is the better fit (editorial control stays in the producing BC, channel routing stays in Notifications) and should be propagated to the other docs in a follow-up.

### 11.2 `notifications.email-events` topic is undocumented in the events catalog

[events-catalog.md § 3](events-catalog.md) lists `notifications.email-commands` as a pre-existing topic but does not list `notifications.email-events`. The topic is registered in `docker-compose.yaml:316` and produced by this BC; the catalog row should be added (out of scope for this doc — flagged in § 12).

### 11.3 `EmailNotificationSentEvent` is not in the master event catalog table

[events-catalog.md § 2](events-catalog.md) lists `SendEmailNotificationCommand` (line 95) but not `EmailNotificationSentEvent`. The schema exists at `platform/Platform.SchemaRegistry.Contracts/Avro/Notifications/Email/EmailNotificationSentEvent.avsc` and is actively consumed by Invoicing. Same out-of-scope cleanup.

---

## 12. Out of scope for v1 (and follow-up cleanup)

Listed so readers don't search for them.

**Channel & template scope:**
- **Real email gateway.** Phase-1 ships `MockEmailGateway` (logs only). A Phase-2 adapter (SendGrid, AWS SES, SMTP) would be a DI swap with no domain impact.
- **User-profile address resolution.** `EmailMessage.ToUserId` is treated as opaque — the mock gateway never resolves it. A real adapter calls a user-profile service or reads from a denormalized read model.
- **Additional templates.** Today: 1 (`invoicing.invoice-delivered`). All other "Notifications consumes X" references in other BC docs would land here as new templates.
- **Template registry / external template store.** The renderer is a hardcoded `switch`. A Phase-2 evolution introduces a backing store + a render engine (Razor / Scriban / Liquid).
- **Other channels — SMS, push, in-app.** Single-channel today. The command shape (`SendEmailNotificationCommand`) is deliberately named after its channel, so adding `SendSmsNotificationCommand` on a sibling topic is the planned extension path — see [roadmap.md § 2.3 Notifications](../roadmap.md).

**Domain-layer features (currently no aggregate):**
- **Suppression lists / unsubscribe.** No persistence, no opt-out flow.
- **Per-user channel preferences.** All recipients get all templates targeted at them.
- **Notification digests / batching.** Each command produces one immediate send. A `NotificationDigest` aggregate (group N events into one daily email) would justify a Domain layer.
- **Delivery receipt callbacks (bounce / spam / open).** The current `EmailNotificationSentEvent` reflects only gateway-acceptance success. Bounce-back webhooks are a separate Phase-2 surface.

**Doc follow-ups identified during authoring:**
1. Update the five other BC docs (ordering, basket, inventory, payments, invoicing) to remove the "Notifications subscribes to {topic}" claims and replace with the command-driven shape.
2. Add `EmailNotificationSentEvent` row to [events-catalog.md § 2](events-catalog.md) and a `notifications.email-events` row to § 3.
3. Replace the stale [events-catalog.md § 1.4 D-5](events-catalog.md) decision with one that records the command-driven choice.
4. The migration history (Init created `payment` schema then renamed it) is a layering relic. Acceptable but worth a single squash if a wider migration consolidation pass happens.

---

*End of Notifications BC design.*
