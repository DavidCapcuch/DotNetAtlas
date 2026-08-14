# ADR-0031: Channel-Agnostic NotifyUserCommand & Producer-Assigned NotificationId

## Status

Accepted (2026-06-03) — supersedes the per-channel sibling-command proposal in [notifications.md § 12](../bc-design/notifications.md) and corrects the idempotency-key claim in [notifications.md § 4.2](../bc-design/notifications.md). Preserves the command-driven direction of [events-catalog.md § 1.4 D-5](../bc-design/events-catalog.md). Applies the client-assigned-id pattern of [ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md). The fan-out **dispatch** mechanics (Hangfire per-channel jobs, the per-channel ledger's send/record edge, quiet hours) are a **separate forthcoming ADR**; this ADR settles only the *contract* and the *idempotency-key identity*.

## Context

Notifications v1 is a single-channel "dumb pipe": a producer emits `SendEmailNotificationCommand` (channel named in the type), Notifications renders → sends email → emits `EmailNotificationSentEvent`. Notifications v2 adds a second and third channel (in-app bell, fake SMS) and a preference center it owns, so the producer must stop naming the channel — it emits one channel-agnostic intent and Notifications fans out to the channels a user's preferences resolve to (D-5's principle: the producer owns *whether* to notify, Notifications owns *how*).

That fan-out forces an idempotency question, which is the crux of this ADR. Three different keys are in play and were being conflated:

- **Transport message id** — `message.id`, a GUID v7 stamped automatically by the outbox writer (`OutboxWriter.cs:64`) when the outbox row is created, and the key the Kafka **inbox** dedups on (`Platform.KafkaFlow.Inbox.EFCore/InboxMiddleware.cs`; corroborated by [ADR-0013](0013-idempotency-key-http.md)'s comparison table — "Producer (per message, usually MessageId)"). The producer **never learns** this value (it is generated inside the writer, not returned).
- **A producer-minted business key** — v1's Avro `IdempotencyKey`, e.g. `invoice-delivered-{InvoiceId}-{Attempt}`. `notifications.md § 4.2` *claims* this is the inbox primary key. **It is not** — the inbox keys on `message.id`. The field's real uses today are (a) carry-through correlation and (b) a string Invoicing's consumer **parses** to recover `InvoiceId` (via a fiddly dash-aware `TryParseInvoiceIdFromIdempotencyKey`, because GUIDs contain dashes).
- **A per-channel dedup key** — needed by v2 so that one intent fanned out to N channels sends each channel at-most-once across retries.

The conflation produced a stale doc and an unclear contract. With a second channel landing, we need to name, once, what identifies a *notification intent*, what dedups *transport*, and what dedups *per channel*.

This is a non-production reference solution; breaking changes are free (root `CLAUDE.md`).

## Decision Drivers (ranked)

1. **Idempotency correctness across fan-out** — one intent, redelivered or re-enqueued, must not double-send any single channel; the model must make this provable.
2. **Honest separation of concerns** — a transport envelope id (infra) and a business intent id (domain) are different things and should not be overloaded into one field. This is the teachable shape (CloudEvents / idempotent-consumer).
3. **Kill the parse smell** — recovering `InvoiceId` by string-parsing a key is a documented smell; correlation should be a field read.
4. **Idiom + repo consistency** — match the just-shipped client-assigned-id pattern ([ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md)) and the `message.id`-is-infra model.
5. **Minimal producer burden / no platform churn** — don't change the platform outbox contract for a need only Notifications has.

## Considered Options

### Option 1: Reuse the transport `message.id` as the only idempotency key

Drop any payload key; dedup transport at the inbox and seed the per-channel ledger on `message.id`. Simple and zero producer discipline. **Rejected as complete:** `message.id` is opaque and producer-unknown, so it cannot serve cross-BC correlation (Invoicing can't map a delivery confirmation back to an invoice), and it dedups only physical redelivery, never duplicate *production*.

### Option 2: Make `message.id` producer-supplied & deterministic (remake the outbox)

Change `IOutboxWriter.AddOutboxMessage` so producers supply a deterministic GUID; one key then does transport dedup, per-channel dedup, *and* correlation. **Rejected:** it overloads a transport envelope id with domain semantics (the anti-pattern of Driver 2), forces UUIDv5/v8 hashing to stay a `Guid` (losing readability), pushes a determinism/collision footgun onto **every** BC's producer (a reused GUID = silent message loss), and changes a platform contract for a one-BC need (Driver 5). A clever shortcut that reads as a smell in a reference codebase.

### Option 3: Keep a producer-minted composite **string** key (status quo `IdempotencyKey`)

Retain `invoice-delivered-{InvoiceId}-{Attempt}`. **Rejected:** a composite key named "Id"/"Key" perpetuates the `InvoiceId`-by-string-parse smell (Driver 3), and it duplicates what `message.id` already does for transport.

### Option 4: Producer-assigned `NotificationId` (GUID) as the intent identity (chosen)

The producer assigns an opaque `NotificationId` (GUID) — the identity of the notification **intent** — exactly as the client assigns `OrderId` ([ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md)). `message.id` stays the infra transport-dedup token, unchanged and auto-generated. The two ids are cleanly separated; idempotency falls out of identity.

## Evaluation Matrix

| Driver (ranked) | Opt 1: message.id only | Opt 2: producer-supplied message.id | Opt 3: composite string key | Opt 4: NotificationId GUID (chosen) |
|---|---|---|---|---|
| 1. Fan-out idempotency | Per-channel works; no prod-dedup | Strong (all three) | Works, ugly | **Strong — ledger keyed `(NotificationId, Channel)`** |
| 2. Honest separation | Conflates (no domain id) | **Overloads** infra id | Two keys, blurred | **Clean: infra id + domain id** |
| 3. Kill parse smell | n/a (no correlation) | n/a | **Smell retained** | **Field read, no parse** |
| 4. Idiom / repo consistency | partial | breaks platform idiom | off | **Matches ADR-0029** |
| 5. Producer burden / churn | none | **platform-wide footgun** | low | low (one GUID, like OrderId) |

## Decision

Adopt **Option 4**. Concretely:

1. **Supersede `SendEmailNotificationCommand`** with channel-agnostic **`NotifyUserCommand`** on **`notifications.notify-commands`** (partition key `RecipientUserId`). Notifications + Invoicing migrate off the v1 command; the v1 `.avsc` + `notifications.email-*` topics stayed **orphaned** (at the time still referenced only by `src/Weather`) and were physically removed with the Weather reference service in the final cleanup ticket (#318) — **not** deleted by the v2 build itself:

   ```
   NotifyUserCommand
     NotificationId   uuid                 // producer-assigned intent identity (client-assigned-id pattern, ADR-0029)
     RecipientUserId  uuid                 // recipient; channel adapters resolve to an address
     TemplateKey      string               // lower-kebab {bounded-context}.{notification-type}, e.g. "invoicing.invoice-delivered"
     Payload          map<string,string>   // rendering data
     OccurredOnUtc    timestamp-millis
   ```

2. **Idempotency is layered, not single-keyed:**
   - **Transport dedup** = `message.id` (header, auto, infra) at the Kafka inbox — unchanged, no payload field, identical to every other consumer.
   - **Per-channel dedup** = a dispatch ledger keyed **`(NotificationId, Channel)`**. Because `NotificationId` is producer-controlled, this ledger absorbs three cases: (i) Hangfire job retry after a successful send, (ii) double-enqueue if the handler runs twice, and (iii) duplicate *production* that reuses the same `NotificationId` (which `message.id` cannot catch). The producer chooses idempotency-of-intent by reusing vs regenerating `NotificationId`. *(Ledger record/send mechanics → the dispatch ADR.)*

3. **Cross-BC correlation is a field read, not a parse.** The delivery event echoes `NotificationId`. Invoicing **persists the `NotificationId` it assigned** on the invoice row and correlates the delivery confirmation by `WHERE delivery_notification_id = @id`. The `invoice-delivered-{guid}-{attempt}` string and its parser are deleted.

4. **Generalize the delivery event** `EmailNotificationSentEvent` → `NotificationDeliveryStatusChangedEvent` carrying at least `{ NotificationId, RecipientUserId, TemplateKey, Channel, Status, OccurredOnUtc }`. Invoicing's consumer filters `Channel == email && Status == Dispatched` and correlates on `NotificationId`. *(The `Status` set and per-channel emission → the dispatch ADR.)*

## Rationale

`message.id` and a business id genuinely differ — one is "which physical message," the other is "which intent" — and the cleanest, most teachable model keeps them separate (Driver 2). Once `NotificationId` is producer-assigned, it does everything a business key must: it is the ledger key that makes fan-out at-most-once-per-channel (Driver 1), and it is the opaque correlation handle Invoicing stores and matches on, deleting the parse smell (Driver 3). It is the same allocate-early, GUID-v7, index-friendly client-assigned-id pattern ADR-0029 established for `OrderId`, and it leaves the platform outbox untouched (Drivers 4, 5). Reusing `message.id` (Opt 1) cannot correlate; remaking the outbox (Opt 2) overloads infra with domain meaning and arms a silent-message-loss footgun across every BC for a need only Notifications has.

## Consequences

### Positive

- One clear answer to "what identifies a notification" (`NotificationId`) and "what dedups transport" (`message.id`); the doc finally matches the code.
- Fan-out idempotency is provable: `(NotificationId, Channel)` is the single per-channel guard.
- Invoicing correlation is a typed field read; the dash-aware string parser is gone.
- No platform-outbox change; no new producer footgun beyond assigning a GUID (already done for `OrderId`).

### Negative

- Producers must assign `NotificationId` (a real but standard change — the `OrderId` precedent).
- Invoicing gains a `delivery_notification_id` column + a migration to correlate (replacing a free-but-smelly string parse).
- Two ids exist for a notification flow (`message.id` infra + `NotificationId` domain). Acceptable: they are honestly different concerns; this is the intended separation, not redundancy.

### Risks

- **Producer reuses a `NotificationId` across genuinely different intents** → the ledger would suppress the second as a duplicate (silent miss). Mitigation: producers mint a fresh GUID v7 per intent (default), and reuse only when deliberately expressing "same intent." Documented as the idempotency lever.
- **ADR number collision** with the parallel order-keyed-saga track — renumber if needed (non-prod).

## Implementation Notes

- **Contract (additive — v1 stayed orphaned for Weather, deleted in #318):** new `NotifyUserCommand.avsc` (FULL_TRANSITIVE per [ADR-0007](0007-avro-compatibility-modes.md)) + new `NotificationDeliveryStatusChangedEvent.avsc` (FORWARD_TRANSITIVE), both in namespace `Notifications`; regenerated bindings via `generate-avro.ps1` (commit `.avsc` + `.cs` together; `git checkout --` the EOL-only sibling churn). **Add** topics `notifications.notify-commands` / `notifications.notify-events` (+ their `.Notifications.DLT` / `.Invoicing.DLT`) in `docker-compose.yaml`. The v1 `SendEmailNotificationCommand.avsc` / `EmailNotificationSentEvent.avsc` + the `notifications.email-*` topics + DLTs were left in place (orphaned; Weather still built the command type) and removed with the Weather reference service in #318 — **not** renamed/retired here.
- **Inbox:** unchanged — keeps deduping on `message.id`. Whitelist `NotifyUserCommand` via `.AddInbox(typeof(NotifyUserCommand))`.
- **Per-channel ledger:** a `notifications` table keyed `(NotificationId, Channel)` recording `Status`; mechanics (write order vs the external send) are decided in the dispatch ADR.
- **Invoicing producer:** assign `NotificationId` (GUID v7) when emitting `NotifyUserCommand`; persist it on the invoice. **Invoicing consumer:** correlate on `NotificationId`; delete `TryParseInvoiceIdFromIdempotencyKey`.
- **Docs:** rewrite `notifications.md` §§4–5 + §12; fix the §4.2 inbox-key claim; update `events-catalog.md` Notifications rows (topic/command/event renames).

## Related Decisions

- [ADR-0029: Order-Keyed Saga & Pre-Assigned OrderId](0029-order-keyed-saga-and-pre-assigned-orderid.md) — the client-assigned-id precedent `NotificationId` follows.
- [ADR-0013: Idempotency-Key HTTP Pattern](0013-idempotency-key-http.md) — its inbox/HTTP table corroborates that the Kafka inbox keys on `MessageId`; Notifications adds a third, per-channel layer.
- [ADR-0007: Avro Compatibility Modes](0007-avro-compatibility-modes.md) — governs the event-schema generalization.
- [events-catalog.md § 1.4 D-5](../bc-design/events-catalog.md) — command-driven Notifications; preserved (producers still emit a command, now channel-agnostic).
- [ADR-0032: Notifications v2 Dispatch & Channels](0032-notifications-dispatch-and-channels.md) — Hangfire per-channel fan-out, the ledger's record/send edge, quiet hours; consumes the `NotificationId` identity this ADR defines.
