# Notifications Bounded Context

> **Status:** v2 target design — agreed 2026-06-03. **Supersedes** the v1 command-driven single-channel implementation (one `SendEmailNotificationCommand` → render → email → `EmailNotificationSentEvent`, no domain model, no HTTP). The v1 contracts + `notifications.email-*` topics stay physically present but **orphaned** during the v2 build (still referenced only by the to-be-deleted `src/Weather`); they are removed together with Weather in the final cleanup issue (#318). Authoritative decisions: [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md) (contract + `NotificationId`) and [ADR-0032](../adr/0032-notifications-dispatch-and-channels.md) (dispatch + channels). Preserves the command-driven direction of [events-catalog.md § 1.4 D-5](events-catalog.md). Built as a phased set of `ready-for-agent` issues; until each phase lands, this doc describes the target, not the deployed state.
> **Scope:** A **channel-agnostic notification dispatcher with an owned preference + template + quiet-hours policy.** Producers emit one `NotifyUserCommand`; Notifications resolves the user's enabled channels, renders per channel, and fans out to **email**, a **fake SMS**, and an **in-app bell** — each as an isolated background job.
> **Pattern showcased:** **Command-driven fan-out.** The producer owns the *editorial decision to notify* (which user, which template, the data) and assigns the `NotificationId`; Notifications owns *how* it is delivered (which channels, when, idempotency). Channel selection is local because preferences live here.
> **Storage:** PostgreSQL, schema `notifications`. Platform Inbox/Outbox + the v2 tables (`user_preferences`, `templates`, `template_channels`, `notification_deliveries`) + Hangfire's own tables.
> **Folder:** `services/Notifications/` (Domain / Application / Infrastructure / Api).

---

## 1. Purpose & Strategic Classification

Notifications is a **supporting subdomain**. v1 was *generic* — a swappable "dumb pipe" that could be replaced by SendGrid + a lambda. v2 graduates it to *supporting* because it now **owns policy** the e-shop cares about: per-user channel **preferences**, **templates**, and **quiet-hours** scheduling. It is not *core* (no competitive differentiation), but it is no longer a commodity gateway.

Its job in v2:

1. Consume `NotifyUserCommand` from `notifications.notify-commands`.
2. Resolve the recipient's enabled channels for the template (`enabled_channels ∩ template_channels`).
3. Fan out to each resolved channel as an isolated **Hangfire** job, scheduling quiet-hours-respecting channels for later.
4. Per channel: render the template, dispatch (email / fake SMS / SignalR bell), and — for durable channels — record a per-channel delivery outcome and emit `NotificationDeliveryStatusChangedEvent`.

### 1.1 Domain layer — yes (services), aggregate — no

v1's `Notifications.Domain` project held only an assembly marker — no business logic — because there was no business decision to make. v2 **adds** one: "which channels, and when" is a real policy. The Domain layer therefore holds genuine logic — the `ChannelType` SmartEnum, the `QuietHoursCalculator`, the channel-resolution rule, the `TemplateRenderer` — plus value objects.

There is **no aggregate root**. `NotificationPreference`, `Template`/`TemplateChannel` are **seeded reference data** with no runtime mutations (no HTTP, see § 8), and the bell is ephemeral, so there is no invariant-guarded mutable state to model as an aggregate. The per-channel `notification_deliveries` ledger is an idempotency/audit record guarded by a unique index, not an aggregate. We deliberately do **not** manufacture a `NotificationPreference` aggregate "for when HTTP lands" — that mutation surface is an explicit deferred seam (§ 13), not v2.

---

## 2. Architecture: command-driven fan-out

The producing BC owns the editorial decision ("notify *this* user about *this* business moment with *this* data") and **assigns the `NotificationId`** (a client-assigned id, the same pattern as `OrderId` per [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)). Notifications owns delivery.

```
 (producer BC)                         Notifications BC                              (consumer BC)
     │                                       │                                            │
     │  NotifyUserCommand                    │                                            │
     │  on notifications.notify-commands     │  1. inbox dedup on message.id              │
     │  ───────────────────────────────────▶│  2. load NotificationPreference + Template │
     │  • NotificationId (producer-assigned) │  3. channels = enabled ∩ template_channels │
     │  • RecipientUserId                    │  4. per channel: ExecuteAt (quiet hours)   │
     │  • TemplateKey   ({bc}.{type})        │     → enqueue Hangfire IChannelDispatcher   │
     │  • Payload       (map<string,string>) │                                            │
     │  • OccurredOnUtc                      │   Email  → MailKit → Mailpit               │
     │                                       │   Sms    → fake log (quiet-hours-aware)    │
     │                                       │   Bell   → SignalR group(RecipientUserId)  │
     │                                       │                                            │
     │                                       │  NotificationDeliveryStatusChangedEvent     │
     │                                       │  on notifications.notify-events (durable    │
     │                                       │  channels only)                             │
     │                                       │ ───────────────────────────────────────────▶│
     │                                       │  • NotificationId • Channel • Status        │  Invoicing:
     │                                       │  • RecipientUserId • TemplateKey            │  email+Dispatched
     │                                       │                                            │  → Issued→Delivered
```

Producers stay structurally unchanged from v1 — same outbox-atomic single write — except the command type/topic and the new producer-assigned `NotificationId`. Channel selection moved inside Notifications because preferences now live here; the *whether-to-notify* decision stays with the producer (D-5).

---

## 3. Inbound — `NotifyUserCommand`

**Topic:** `notifications.notify-commands` · **Key:** `RecipientUserId` · **Consumer group:** `notifications-group` · **Retention:** 7 days (command topic) · **Avro compatibility:** FULL_TRANSITIVE ([ADR-0007](../adr/0007-avro-compatibility-modes.md)).

| Field | Type | Notes |
|---|---|---|
| `NotificationId` | uuid | **Producer-assigned** intent identity (client-assigned-id pattern, ADR-0029/0031). Keys the per-channel ledger and round-trips in the delivery event for correlation. |
| `RecipientUserId` | uuid | Keycloak `sub`. Channel dispatchers resolve the address from `user_preferences`. |
| `TemplateKey` | string | `{bounded-context}.{notification-type}`, lower-kebab, e.g. `invoicing.invoice-delivered`. Joins `templates`. |
| `Payload` | map<string,string> | Template rendering data. |
| `OccurredOnUtc` | timestamp-millis | Producer creation time ([ADR-0015](../adr/0015-time-timezone-policy.md)). |

There is **no `IdempotencyKey` and no `CorrelationId`** in the payload (see § 4).

### 3.1 Handler — fan-out

`NotifyUserCommandKafkaHandler` runs inside the platform `InboxMiddleware` transaction (dedup on the `message.id` header):

1. Load `NotificationPreference` (enabled channels, contact, timezone, quiet hours) and `Template` (+ `template_channels`).
2. Resolve channels = `enabled_channels ∩ template_channels`.
3. Per channel: compute `ExecuteAt` (`QuietHoursCalculator` if `ChannelType.RespectsQuietHours`, else now) and **enqueue one fire-and-forget Hangfire job** via the Keyed-DI `IChannelDispatcher`.

Any enqueue failure throws → the inbox transaction rolls back → Kafka re-drives the whole fan-out (duplicates absorbed per § 4).

### 3.2 Retry & DLT

Unchanged from the platform policy ([ADR-0025](../adr/0025-kafka-consumer-retry-dlt-policy.md)): infra exceptions retry-forever with backoff; other exceptions DLT to `notifications.notify-commands.Notifications.DLT`. Per-**channel** retry is owned by Hangfire, not Kafka.

---

## 4. Idempotency model

Two identifiers, three layers, at-least-once:

| Concern | Key | Mechanism |
|---|---|---|
| **Don't process the same Kafka message twice** | `message.id` (header, GUID v7, auto-stamped by the outbox writer) | Platform inbox — `InboxMessage.MessageId` PK. *Transport scope.* |
| **Don't send the same channel twice** | `(NotificationId, Channel)` | `notification_deliveries` ledger, unique-indexed. Job: if `Dispatched` → skip; else **send, then UPSERT the row** (`Dispatched`/`Failed`) — first attempt inserts, a later retry of a `Failed` row updates it to `Dispatched` (never a 2nd INSERT). *Per-channel scope, durable channels only.* |
| **Cross-BC correlation** | `NotificationId` | Round-tripped in `NotificationDeliveryStatusChangedEvent`; the producer persists it (e.g. Invoicing's `delivery_notification_id`). |

**At-least-once is the ceiling** — an external email/SMS send cannot be transactional with a DB write, so a crash in the send→record window re-sends on retry. Accepted (a duplicate email / fake-SMS is benign).

**No-loss, at-least-once enqueue** (proof in [ADR-0032](../adr/0032-notifications-dispatch-and-channels.md)): the inbox row commits *after* the handler and Hangfire enqueues are independently durable, so the inbox row never exists with a channel un-enqueued. A crash between enqueue and the inbox commit re-drives the full fan-out (duplicate jobs, **not** lost ones); durable channels collapse the duplicate at the ledger, the bell may double-push (benign). No-loss, not no-duplicate.

> The v1 doc claimed the inbox deduped on an Avro `IdempotencyKey`. That was never true — the platform inbox keys on the `message.id` header ([ADR-0013](../adr/0013-idempotency-key-http.md) table; `Platform.KafkaFlow.Inbox.EFCore`). v2 makes this explicit and removes the payload key.

---

## 5. Dispatch — channels, dispatchers, scheduling

### 5.1 `ChannelType` SmartEnum

```csharp
public abstract class ChannelType : SmartEnum<ChannelType>
{
    public static readonly ChannelType Email = new EmailChannel(1, "Email", respectsQuietHours: false);
    public static readonly ChannelType Sms   = new SmsChannel(2,   "Sms",   respectsQuietHours: true);
    public static readonly ChannelType Bell  = new BellChannel(3,  "Bell",  respectsQuietHours: false);

    public bool RespectsQuietHours { get; }
    // ...
}
```

`RespectsQuietHours` is the only behavioral flag; SMS is the only channel that sets it today (a future Push inherits the deferral for free). Matches the repo's SmartEnum usage (Payments/Ordering).

### 5.2 `IChannelDispatcher` in Keyed DI

Each channel is an `IChannelDispatcher` registered in **Keyed DI** by `ChannelType`. The handler resolves and enqueues the dispatcher's Hangfire job per resolved channel — completely isolated, so an SMS retry never touches the email job. Hangfire is wired per the `src/Weather` template (`AddHangfire` + `Hangfire.PostgreSql` on the `notifications` connection, `AddHangfireServer`).

### 5.3 Channel resolution

`resolved = user_preferences.enabled_channels ∩ template_channels`. A template fires only on channels it has a body for, intersected with what the user enabled. There is **no mandatory-channel floor** in v2 (deferred seam, § 13) — with preferences pre-seeded all-ON and no mutation surface, nothing can be disabled anyway.

### 5.4 Quiet hours

Pure domain service `QuietHoursCalculator.NextAllowedUtc(DateTimeOffset nowUtc, TimeOnly quietStart, TimeOnly quietEnd, string ianaTz)` — returns `nowUtc` unless the channel `RespectsQuietHours` and the user's local time is inside the `[quiet_hours_start, quiet_hours_end)` window, in which case it returns the configured `quiet_hours_end` resolved against the **local date on which the window ends** (current local day, or the next when the window wraps past midnight, e.g. 22:00–07:00), converted local→UTC. The in/out-of-window check is done on the **UTC instant**, not wall-clock, so a DST fall-back repeated hour cannot double-classify; an ambiguous/invalid local end-time resolves via `TimeZoneInfo`'s default (standard-time offset on fall-back, skip-forward adjustment on spring-forward). `TimeProvider` + `TimeZoneInfo` (no NodaTime, per [ADR-0015](../adr/0015-time-timezone-policy.md)). The handler passes the result to Hangfire `Schedule(executeAt)`.

---

## 6. Channels (v2)

| Channel | Adapter | Durable? | Notes |
|---|---|---|---|
| **Email** | `MailKit` `SmtpEmailGateway` → **Mailpit** | ledger + delivery event | Address from `user_preferences.email`. Mailpit runs in docker-compose (`core` profile, SMTP 1025 / UI 8025); `MockEmailGateway` retained for unit tests. |
| **Sms** | fake handler (logs `"Sending SMS…"` / `"Quiet hours, deferred to …"`) | ledger + delivery event | `RespectsQuietHours = true`. **No real provider** — seam. Phone from `user_preferences.phone_number`. |
| **Bell** | `INotificationBroadcaster` → **SignalR** group `RecipientUserId` | **none** | Live push only; hub `/hubs/v1/notifications` (Keycloak JWT; versioned per the Weather `BasePaths` convention). Group join/leave in `OnConnectedAsync`/`OnDisconnectedAsync` keyed on `Context.UserIdentifier` (= `sub` = `RecipientUserId`); **no** client subscribe RPC (unlike Weather's per-location model). Offline users miss it; no feed/history/badge/mark-read/SSE (deferred, § 13). Minimal job retries (group-send to zero connections is a successful no-op). In-memory backplane (no Redis); reuses the `src/Weather` SignalR pattern. |

---

## 7. Templates

`templates` (`template_key` PK, `description`) + `template_channels` (`template_key` + `channel_type` PK, nullable `subject`, required `body`). Rendering is a **dumb `{{key}}` token-replace** over `Payload` (`TemplateRenderer`, pure, unit-tested) — no engine dependency (Scriban/Razor is a deferred seam). `template_channels` is the source of "supported channels" for § 5.3.

**Seeded (minimal):** exactly two templates.

| `template_key` | Channels | Purpose |
|---|---|---|
| `invoicing.invoice-delivered` | `[Email]` | Preserves the live Invoicing `Issued → Delivered` flow. |
| `order.shipped` | `[Email, Bell, Sms]` | Demonstrates fan-out, the per-channel ledger across durable channels, the bell, and SMS quiet-hours deferral in one path. |

---

## 8. Preferences & contact (`user_preferences`)

A **seeded local reference table** — *not* a projection. There is no Identity/Accounts BC ([ADR-0005](../adr/0005-customer-data-in-ordering.md)); `RecipientUserId` *is* the Keycloak `sub`, and Notifications holds the slice of user data it needs.

| Column | Type | Notes |
|---|---|---|
| `user_id` | uuid PK | Keycloak `sub`. |
| `email` | text | Resolved by the email dispatcher. |
| `phone_number` | text | Fake E.164 (SMS is fake). |
| `enabled_channels` | `ChannelType[]` (PG `text[]`, value-converted) | One row per user; no join on resolve. |
| `quiet_hours_start` / `quiet_hours_end` | `time` (`TimeOnly`) | Civil time-of-day in `time_zone` — the one legitimate exception to ADR-0015's `DateTimeOffset` rule (a recurring wall-clock window is not an instant). Nullable = no quiet hours. |
| `time_zone` | text | IANA, e.g. `Europe/Prague`. |

**No HTTP** to read or mutate preferences — deferred seam (§ 13).

Seeded to the four Keycloak realm users with variety so resolution + quiet hours are demoable:

| User (`sub`) | Channels | Quiet hours |
|---|---|---|
| `admin@dotnetatlas.com` / `…0001`, `dev@…` / `…1111` | all | none |
| `pleb@dotnetatlas.com` / `…0002` | **Sms OFF** | none — demonstrates `∩` suppressing a channel |
| `d.capcuch@gmail.com` / `…0003` | all | **22:00–07:00 `Europe/Prague`** — demonstrates SMS deferral |

---

## 9. Outbound — `NotificationDeliveryStatusChangedEvent`

**Topic:** `notifications.notify-events` · **Key:** `RecipientUserId` · **Retention:** infinite (audit) · **Avro compatibility:** FORWARD_TRANSITIVE.

| Field | Type | Notes |
|---|---|---|
| `NotificationId` | uuid | Carry-through; the producer correlation key. |
| `RecipientUserId` | uuid | |
| `TemplateKey` | string | Consumers route by prefix (`invoicing.*`). |
| `Channel` | string | `Email` / `Sms`. |
| `Status` | enum | `Dispatched` / `Failed`. |
| `OccurredOnUtc` | timestamp-millis | |

Emitted by **durable channels only** (email, SMS), written to the outbox **atomically with the `notification_deliveries` row**. The bell emits nothing.

### 9.1 Consumer — Invoicing

Invoicing's `NotificationDeliveryStatusChangedEventKafkaHandler` filters `Channel == email && Status == Dispatched`, looks up the invoice by the stored `delivery_notification_id` (= `NotificationId`), and drives `Issued → Delivered`. No string-parsing (the v1 `invoice-delivered-{guid}-{attempt}` key and its parser are removed).

---

## 10. Storage

Schema `notifications` (Postgres):

| Table | Owner | Purpose |
|---|---|---|
| `user_preferences` | Notifications | Seeded preference + contact reference (§ 8). |
| `templates`, `template_channels` | Notifications | Seeded template reference (§ 7). |
| `notification_deliveries` | Notifications | Per-channel ledger, PK `(notification_id, channel)`, `status` + timestamps (§ 4). |
| `inbox_messages` | `Platform.ReliableMessaging.Inbox.EFCore` | Dedup on `message.id`. |
| `outbox_messages` | `Platform.ReliableMessaging.Outbox.EFCore` | Pending `NotificationDeliveryStatusChangedEvent`. |
| Hangfire tables | `Hangfire.PostgreSql` | Background-job store (per `src/Weather`). |

Seeding: dev/docker via EF `UseAsyncSeeding` (Weather pattern, seed-if-empty, deterministic Bogus); **tests arrange their own** preferences/templates per-fixture (test migrations run Evolve SQL scripts, not `MigrateAsync`, so `UseAsyncSeeding` does not fire there). No seed data in the SQL migration scripts.

---

## 11. Topics — summary

| Topic | Retention | Key | Direction |
|---|---|---|---|
| `notifications.notify-commands` | 7 days | `RecipientUserId` | inbound |
| `notifications.notify-commands.Notifications.DLT` | 14 days | (preserved) | DLT |
| `notifications.notify-events` | infinite | `RecipientUserId` | outbound |
| `notifications.notify-events.Invoicing.DLT` | 14 days | (preserved) | DLT — Invoicing's consumer of the delivery event |

---

## 12. Observability & Testing

- `ApplicationInfo.AppName = "Notifications"`; KafkaFlow + outbox OpenTelemetry instrumentation as in v1; Hangfire jobs and the SignalR hub add spans. Structured logs tag `NotificationId`, `TemplateKey`, `Channel` (not PII; `RecipientUserId` per the BC PII rule).
- **Unit:** `QuietHoursCalculator` (in/out window, midnight-wrap, null), `ChannelType`, the resolution rule, `TemplateRenderer`.
- **Integration (Testcontainers):** fan-out (one intent → resolved channels) + ledger idempotency (redelivery / double-enqueue → no double-send); quiet-hours deferral; **email asserted via Testcontainers Mailpit REST API**; the bell via the `src/Weather` SignalR test-client pattern; the Invoicing `Issued → Delivered` round-trip.
- **Architecture:** standard layering guards + ADR-0015 (`DateTimeOffset`, no `UtcNow` in domain). No bespoke arch tests.

---

## 13. Out of scope (deferred seams)

Documented so readers don't search for them; each is a clean extension point, not a v2 deliverable:

- **Durable bell** — feed table, history, unseen-count badge, mark-read/seen, HTTP poll, SSE replay. v2's bell is ephemeral SignalR live push only.
- **Preference HTTP** — read/mutate preferences. Seeded all-ON; no API.
- **Mandatory-type floor / bypass** — would live in the resolver.
- **Marketing-consent system-of-record / Accounts BC.**
- **Real SMS/push providers** — SMS is a fake log handler; adding a provider is a new `IChannelDispatcher` + a `template_channels` column, no producer change.
- **Provider delivery webhooks** (open/bounce/spam), **fallback ladders**.
- **Templating engine** (Scriban/Razor) — token-replace only.
- **User-profile address service** — address comes from seeded `user_preferences`.
- **Ledger sweep / transactional-enqueue** — the inbox+ledger combination needs neither (ADR-0032); the sweep is the documented escalation if Hangfire-storage durability ever proves insufficient.

---

*End of Notifications BC design (v2).*
