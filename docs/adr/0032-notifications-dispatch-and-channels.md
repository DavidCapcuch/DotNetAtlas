# ADR-0032: Notifications v2 Dispatch & Channels — Hangfire Per-Channel Fan-out, At-Least-Once Ledger, SignalR Bell

## Status

Accepted (2026-06-03) — builds on [ADR-0031](0031-notify-user-command-and-notification-id.md) (the `NotifyUserCommand` contract + `NotificationId` identity this ADR consumes). **Reverses** the in-app-bell transport decision of the Notifications v2 PRD ([#304](https://github.com/DavidCapcuch/DotNetAtlas/issues/304)): that PRD specified a *durable* bell (feed table + HTTP poll + unseen-count + mark-read + optional SSE replay, "SignalR is not used"); this ADR ships an **ephemeral SignalR live push with durability deferred**. Uses the Hangfire wiring and SignalR pattern from the former `src/Weather` reference service (since removed) as the template, giving both patterns a permanent home. Aligns with [ADR-0015](0015-time-timezone-policy.md) (TimeProvider/timezone) and [ADR-0025](0025-kafka-consumer-retry-dlt-policy.md) (retry/DLT).

## Context

[ADR-0031](0031-notify-user-command-and-notification-id.md) makes the inbound command channel-agnostic: one `NotifyUserCommand` fans out to the channels a user's preferences resolve to. v2 has three channels — **email** (real SMTP to Mailpit), **fake SMS** (logs only, but quiet-hours-aware), and an **in-app bell** (real-time push). This ADR decides *how* the consumer turns one intent into N channel sends.

The forces:

- **A single Kafka partition is processed sequentially.** If the consumer does the slow/uncertain channel I/O inline (SMTP handshake, waiting out quiet hours), it blocks every other notification behind it on that partition.
- **Channels fail independently.** A flaky SMTP server must not stall or re-drive the bell or SMS.
- **Split-time delivery.** One intent can need email *now* and SMS *later* (quiet hours): `order.shipped` to a user asleep in `Europe/Prague` should email immediately but defer SMS to `07:00` local.
- **At-least-once must become at-most-once-*per-channel*.** Kafka redelivery + background-job retries must not spam a channel.
- **Reference-minimal.** The repo already demonstrated Hangfire (`src/Weather` and `Inventory`) and SignalR (`src/Weather`); reuse, don't invent.

This is a non-production reference solution; breaking changes are free (root `CLAUDE.md`).

## Decision Drivers (ranked)

1. **Per-channel idempotency correctness** — redelivery and job retries must never double-send a durable channel beyond a rare, documented window.
2. **Failure & latency isolation** — no channel can block the Kafka partition or another channel.
3. **Split-time scheduling** — defer per channel (quiet hours) without sleeping on the consumer thread.
4. **Reuse repo patterns** — Hangfire + SignalR as wired in `src/Weather`.
5. **Reference-minimal** — no machinery beyond what the above require.

## Considered Options

### Option 1: Dispatch inline in the Kafka consumer

Resolve channels and send all of them synchronously inside the handler. **Rejected:** blocks the partition on the slowest channel, cannot defer SMS without sleeping the consumer, and a transient SMTP failure re-drives the *entire* fan-out via Kafka retry (re-pushing the bell, re-logging SMS).

### Option 2: Hangfire per-channel jobs + per-channel ledger (chosen)

The consumer resolves channels and enqueues **one fire-and-forget Hangfire job per channel** (delayed for quiet-hours channels). Each job is an isolated `IChannelDispatcher` (Keyed-DI by `ChannelType`). Durable channels guard sends with a per-channel ledger `(NotificationId, Channel)`.

### Option 3: Ledger-as-queue + recurring sweep (transactional outbox for jobs)

Consumer writes N `Pending` ledger rows in the inbox transaction; a recurring sweep executes/re-enqueues them. **Rejected as over-engineered here:** the platform `InboxMiddleware` already writes the inbox row in a transaction that commits *after* the handler, and Hangfire enqueues are independently durable — so there is **no crash window that loses a channel** (proof below). The sweep solves a problem this combination doesn't have. Recorded as the fallback if Hangfire-storage durability ever proves insufficient.

## Evaluation Matrix

| Driver (ranked) | Opt 1: inline | Opt 2: Hangfire per-channel (chosen) | Opt 3: ledger-queue + sweep |
|---|---|---|---|
| 1. Per-channel idempotency | full re-fan-out on retry | **ledger `(NotificationId, Channel)`** | ledger + sweep |
| 2. Isolation | ✗ partition-blocking | **✅ per-job** | ✅ per-row |
| 3. Split-time | ✗ must sleep | **✅ `Schedule(delay)`** | ✅ `NotBeforeUtc` |
| 4. Reuse repo | n/a | **✅ Weather Hangfire+SignalR** | partial (Inventory poll) |
| 5. Minimal | simplest but wrong | **✅ inbox+ledger suffice** | extra table + sweeper |

## Decision

Adopt **Option 2**.

1. **Fan-out in the consumer.** Dedup (inbox/`message.id`) → load `NotificationPreference` + `Template` → resolve channels = `enabled_channels ∩ template_channels` → for each, compute `ExecuteAt` (`QuietHoursCalculator` for `ChannelType.RespectsQuietHours`, else now) → enqueue one isolated Hangfire job per channel via the Keyed-DI `IChannelDispatcher` (scheduled when `ExecuteAt` is in the future, fire-and-forget otherwise). Any enqueue failure throws → no inbox row → clean Kafka re-drive.

2. **Idempotency is layered and at-least-once:**
   - **Inbox (`message.id`)** dedups Kafka redelivery — unchanged platform behavior.
   - **Per-channel ledger `(NotificationId, Channel)`** (durable channels only) makes each channel at-most-once *except* a rare duplicate in the send→record crash window. Sequence: check ledger → if `Dispatched`, skip; else **send, then UPSERT the row** (`Dispatched`/`Failed`) on the unique `(NotificationId, Channel)` key **+ emit the matching delivery event in one transaction**. The write is an UPSERT, **never a blind INSERT**: the first attempt inserts (`Failed` or `Dispatched`); a later retry of a `Failed` row **updates it in place** to `Dispatched` and emits `Dispatched` (a second INSERT would hit the unique index and throw). Accepted at-least-once.
   - **No-loss (at-least-once enqueue):** the `InboxMiddleware` inserts the inbox row and runs the handler in one transaction that **commits after** the handler; Hangfire enqueues commit **independently and immediately**. So either (a) the handler completes and the inbox row commits — but a crash *between* enqueue and that commit rolls the inbox row back, so Kafka re-drives and **re-enqueues every channel**; or (b) any earlier crash also rolls back the inbox row → the same full re-enqueue. There is therefore no window where the inbox row exists but a channel was never enqueued — but there **is** a duplicate-*enqueue* window. Durable channels absorb the duplicate at the ledger; the **bell (no ledger) may double-push** (benign). The best-effort transactional-enqueue note in §5 closes even this window if it composes. So this is *no-loss*, not *no-duplicate*.

3. **Channels (`ChannelType` SmartEnum, `RespectsQuietHours`):**
   - **Email** — `MailKit` `SmtpEmailGateway` → Mailpit; address resolved from `user_preferences`; ledger + delivery event.
   - **SMS** — fake handler that logs `"Sending SMS…"` (the consumer logs `"Quiet hours, deferred to …"` — quiet hours are evaluated once, at enqueue time; the dispatcher never re-checks them); `RespectsQuietHours = true`; ledger + delivery event; **no real provider**.
   - **Bell** — `INotificationBroadcaster` → SignalR group `RecipientUserId`; **no ledger, no delivery event, no durability** (offline users miss it); **minimal retries** (a group-send to zero connections is a successful no-op). A 3rd isolated Hangfire job for uniformity and partition isolation.

4. **Delivery event** (`NotificationDeliveryStatusChangedEvent`, ADR-0031) is emitted by **durable channels only**, written to the outbox **atomically with the ledger row** (`Dispatched`/`Failed` consistent with what was recorded). Invoicing consumes `Channel == email && Status == Dispatched`.

5. **Hangfire enqueue ↔ inbox transaction (best-effort).** Implementers SHOULD try enlisting the Hangfire enqueue in the inbox EF transaction (Hangfire.PostgreSql `GetConnection()` / `EnableTransactionScopeEnlistment`) so jobs are durable only if the inbox row commits — removing even the harmless duplicate enqueues. If it doesn't compose cleanly with `InboxMiddleware`, fall back to fire-and-forget; **correctness is unaffected** (the ledger covers duplicates either way).

## Rationale

Always-enqueue keeps the consumer's work cheap and bounded (resolve + enqueue + commit), so a slow SMTP server or a deferred SMS never blocks the partition (Drivers 2, 3), and `Schedule(delay)` expresses quiet-hours deferral without sleeping a consumer thread. The inbox already prevents duplicate *processing*; the per-channel ledger turns the residual at-least-once delivery into at-most-once-per-channel beyond a narrow, accepted send→record window (Driver 1) — and because external email/SMS sends cannot be transactional with a DB write, at-least-once is the honest ceiling, which a duplicate email/fake-SMS tolerates. The sweep (Opt 3) was tempting but the inbox-after-handler + independently-durable-enqueue combination already closes the loss window, so a sweeper would be ceremony. The bell is deliberately the odd one out: its value is *immediacy*, it has no retry value (an offline user is missed regardless), and giving it a ledger or a durable feed is exactly the scope ([#304](https://github.com/DavidCapcuch/DotNetAtlas/issues/304)) deferred — SignalR live push is the minimal honest shape, and it rehouses the SignalR pattern formerly stuck in throwaway `src/Weather`.

## Consequences

### Positive

- Channels are isolated: independent retry, independent latency, no partition blocking.
- Quiet-hours deferral is a first-class `Schedule(executeAt)`, demoable via the seeded `Europe/Prague` user.
- Idempotency is provable and minimal (inbox + ledger; no sweep, no transactional-enqueue requirement).
- Rehouses Weather's Hangfire + SignalR wiring; gives both a permanent, production-grade home.

### Negative

- Email/fake-SMS are **at-least-once** — a crash in the send→record window re-sends on retry. Accepted (duplicate email/fake-SMS is benign; documented).
- The bell is **best-effort** — offline users miss notifications; no history, no badge, no mark-read. Accepted (deferred seam).
- Hangfire becomes a Notifications dependency (new schema/tables in the `notifications` Postgres DB) and the per-message job churn includes ephemeral bell jobs that no-op when the user is offline.

### Risks

- **Permanent job failure** leaves a durable channel unsent — surfaced in Hangfire's failed-jobs dashboard. The dispatch job's `[AutomaticRetry(ExceptOn = CriticalException)]` splits these: a **bug-class** failure (`DataIntegrityException` — unknown template, missing subject/tokens/preference) **fails fast**, parked Failed on the *first* attempt (no ledger row — it throws before the send), rather than burning retries against a deterministically-failing condition; a **transient** failure (SMTP down) records a `Failed` ledger row and retries (≤3×) before parking. Accepted for a reference repo; the Opt-3 sweep is the documented escalation.
- **SignalR backplane** — single-instance is in-memory; multi-instance needs the Redis backplane ([ADR-0016](0016-redis-topology.md)). Noted, not required for the reference profile.
- **ADR number collision** with the parallel order-keyed-saga track — renumber if needed (non-prod).

## Implementation Notes

- **Hangfire:** wired per `src/Weather` (`AddHangfire` + `Hangfire.PostgreSql` on the `notifications` connection string, `AddHangfireServer`, `IRecurringJobManager`/`IBackgroundJobClientV2`). Dispatchers in Keyed DI by `ChannelType`. Per-message jobs (not recurring); the only recurring job, if any, is a future ledger sweep (not built).
- **Ledger:** `notifications` table keyed `(NotificationId, Channel)` (unique), `Status`, timestamps; written by durable-channel jobs. Bell writes nothing.
- **SignalR:** `INotificationBroadcaster` in Application, hub `/hubs/v1/notifications` in Api (versioned per Weather's `BasePaths` convention; Keycloak JWT; group = `RecipientUserId` via `Context.UserIdentifier`, joined in `OnConnectedAsync` / left in `OnDisconnectedAsync` — no client subscribe RPC, unlike Weather's per-location model). In-memory backplane only (Weather's mandatory `AddStackExchangeRedis` omitted; Redis backplane is the multi-instance seam). `Notifications.IntegrationTests` ported Weather's `SignalRClientFactory` + hub test client (the Weather one was bound to `WeatherAlertHub` — re-implemented, not referenced).
- **Email:** `MailKit` (new package, `services/Directory.Packages.props`); `mailpit` compose service (`core` profile, SMTP 1025 / UI 8025); integration tests via Testcontainers `axllent/mailpit` asserting on Mailpit's REST API.
- **Quiet hours:** pure `QuietHoursCalculator` (`TimeProvider` + `TimeZoneInfo`, no NodaTime per ADR-0015); `TimeOnly`/Postgres `time` for the civil-time window.
- **Tests:** unit (`QuietHoursCalculator`, `ChannelType`, resolution rule, `TemplateRenderer`); integration (fan-out + ledger idempotency, quiet-hours deferral, Mailpit assertion, SignalR bell, Invoicing `issued→delivered` round-trip); arch (standard layering + ADR-0015 guards).

## Related Decisions

- [ADR-0031: NotifyUserCommand & Producer-Assigned NotificationId](0031-notify-user-command-and-notification-id.md) — the contract + identity this dispatch consumes.
- [ADR-0015: Time & Timezone Policy](0015-time-timezone-policy.md) — `TimeProvider`/`TimeZoneInfo` for quiet hours.
- [ADR-0025: Kafka Consumer Retry & DLT Policy](0025-kafka-consumer-retry-dlt-policy.md) — the consumer-side retry/DLT around the inbox is unchanged; Hangfire owns per-channel retry.
- [ADR-0016: Redis Topology](0016-redis-topology.md) — SignalR backplane if multi-instance.
- Notifications v2 PRD ([#304](https://github.com/DavidCapcuch/DotNetAtlas/issues/304)) — the durable-bell/SSE scope this ADR deliberately reverses to an ephemeral SignalR push.
