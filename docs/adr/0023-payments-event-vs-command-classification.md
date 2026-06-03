# ADR-0023: Payments Event-vs-Command Classification

## Status

Accepted (2026-05-30)

## Context

[`docs/bc-design/payments.md § 6`](../bc-design/payments.md) — *External Events (Avro) + Topics* — has carried a **"Known classification debt"** paragraph since the Payments BC chapter was authored:

> several `*Event`-named messages have exactly one consumer (PaymentProcessingSaga) and per master-design § 3.5 are really commands. The Checkout saga agent has explicit authority to propose renames (`PaymentRequestedEvent` → `RequestPaymentCommand` on the `payments.payment-commands` topic). Proposals surface in the session summary; user approval required before implementation.

Concretely, the Payments BC and PaymentProcessingSaga together produce nine `*Event`-named Avro records on `payments.transactions` (FORWARD_TRANSITIVE per [ADR-0007](0007-avro-compatibility-modes.md)) — and several of them have exactly one consumer, the textbook signal of a command per [`master-design § 3.5`](../eshop-master-design.md):

> *"If an event has one expected consumer performing specific logic with guaranteed feedback, it's probably a command, not an event."*
>
> Decision test (run for every proposed cross-service message):
>
> | Signal | Event | Command |
> |---|---|---|
> | Consumer count | zero-or-many | exactly one known |
> | Caller expectation | fire-and-forget (reactive) | specific response expected |
> | Naming | past-tense business moment (`OrderConfirmed`) | imperative verb (`ConfirmOrder`) |
> | Topic | `{domain}.{aggregate}` | `{domain}.{aggregate}-commands` |
> | Schema subject | …Event | …Command |

This ADR closes the debt: confirms the actual consumer counts from code, separates the rule's **letter** (the decision table) from the **spirit** (the article-cited "guaranteed feedback" test), classifies each message, renames the one message where the letter and the spirit both agree, and records the rationale for keeping the rest as events.

### Ground-truth consumer wiring (from code, not from the older docs)

| Avro External Event | Producer (file) | Consumer(s) (file) | # consumers |
|---|---|---|---|
| `PaymentRequestedEvent` | Checkout saga ([`CheckoutSagaOrchestrator.cs`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/CheckoutSagaOrchestrator.cs)) | PaymentProcessingSaga | **1** |
| `PaymentAuthorizedEvent` | Payments BC ([`PaymentAuthorizedOutboxPublisherDomainEventHandler.cs`](../../services/Payments/Payments.Application/Outbox/PaymentAuthorizedOutboxPublisherDomainEventHandler.cs)) | PaymentProcessingSaga | **1** |
| `PaymentAuthorizationFailedEvent` | Payments BC | PaymentProcessingSaga | **1** |
| `PaymentCapturedEvent` | Payments BC | PaymentProcessingSaga + **Invoicing** ([`PaymentCapturedInvoiceProjectionKafkaHandler.cs`](../../services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Projections/PaymentCapturedInvoiceProjectionKafkaHandler.cs)) | **2** |
| `PaymentCaptureFailedEvent` | Payments BC | PaymentProcessingSaga | **1** |
| `PaymentVoidedEvent` | Payments BC | PaymentProcessingSaga | **1** |
| `PaymentRefundedEvent` | Payments BC | Checkout saga + **Invoicing** ([`PaymentRefundedCreditNoteProjectionKafkaHandler.cs`](../../services/Invoicing/Invoicing.Infrastructure/Messaging/Kafka/Projections/PaymentRefundedCreditNoteProjectionKafkaHandler.cs)) | **2** |
| `PaymentCompletedEvent` | **PaymentProcessingSaga** ([`PaymentProcessingSagaOrchestrator.cs`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/PaymentProcessingSagaOrchestrator.cs)) | Checkout saga | **1** |
| `PaymentFailedEvent` | **PaymentProcessingSaga** | Checkout saga | **1** |

Two doc-drift observations against the older sources:
- The events-catalog § 2 / payments.md § 6 / master-design § 3.5 references to `Notifications` consuming `PaymentRefundedEvent`, `PaymentCompletedEvent`, or `PaymentFailedEvent` are **stale** — `services/Notifications/` has no Avro Payment-* consumer of any kind (only `SendEmailNotificationCommandKafkaHandler`). The `notifications` consumer-group entries in those documents reflect aspirational v2 wiring, not v1 code.
- Master-design § 3.5's *"Known misnamed events in Payments"* list mentioned `PaymentCapturedEvent`. **Wrong** — code has 2 consumers (PaymentProcessingSaga + Invoicing); it is correctly classified as an event.

## Decision Drivers (ranked)

1. **Match the rule's spirit, not its letter.** The article master-design § 3.5 cites is explicit that the test for "command-shape" is *"one expected consumer performing specific logic **with guaranteed feedback**"* — i.e., the **producer** awaits a specific reply to proceed. Consumer count alone is brittle: a fact-shaped event can legitimately have one consumer today and three tomorrow.
2. **Avro schema-evolution cost.** Per [ADR-0007](0007-avro-compatibility-modes.md), every rename creates a new subject (Record Name Strategy: `{Namespace}.{RecordName}`) and is **breaking** under FORWARD_TRANSITIVE / FULL_TRANSITIVE. Each rename is a contract migration — not a refactor.
3. **Producer intent vs consumer convenience.** Naming the wire shape should reflect what the producer is saying, not what the consumer happens to do with it. `PaymentAuthorized` IS a past-tense fact; renaming to `CapturePayment` because the lone consumer happens to capture next inverts authorial intent.
4. **Stability of the named API surface.** A "deferred rename" is preferable to a broken-by-Notifications-arriving-later rename. Keeping a fact-shaped event with one consumer is reversible (just add another consumer); renaming to a command and then needing to broadcast to many consumers is harder to undo.
5. **Internal consistency.** Whatever we apply must explain why some 1-consumer messages stay events and others become commands, with a test future agents can re-apply.

## Considered Options

### Option 1: Apply the rule's spirit — rename one, defer six (chosen)

Apply the article's full 2-part test (specific logic + **guaranteed feedback**) on every 1-consumer message:

| Avro External Event | Specific logic at consumer? | Producer awaits guaranteed feedback? | Result |
|---|---|---|---|
| `PaymentRequestedEvent` | Yes (saga drives auth→capture FSM) | **Yes** — Checkout saga's `AwaitingPayment` blocks on `PaymentCompletedEvent` or `PaymentFailedEvent` correlated by `CorrelationId`. Without the reply the Checkout saga times out and compensates. | **Command** |
| `PaymentAuthorizedEvent` | Yes (saga issues Capture) | **No** — Payments BC publishes after committing local state; doesn't care if saga acts. | Event |
| `PaymentAuthorizationFailedEvent` | Yes | No | Event |
| `PaymentCaptureFailedEvent` | Yes | No | Event |
| `PaymentVoidedEvent` | Yes | No | Event |
| `PaymentCompletedEvent` | Yes | **No** — PaymentProcessingSaga finalizes immediately after publishing; doesn't await anything from Checkout saga. | Event |
| `PaymentFailedEvent` | Yes | No (same as Completed) | Event |

Result: **rename only `PaymentRequestedEvent` → `RequestPaymentCommand` and move it to `payments.payment-commands`**. Defer the others; their wire names accurately describe past-tense business moments and they remain perfectly available to any future consumer (Notifications, BFF analytics, fraud-detection, audit-archive).

**Pros:**
- Honors the source article master-design § 3.5 quotes.
- Minimum contract-migration cost: 1 schema rename, not 7.
- Stable under "second consumer arrives later" — six potential renames are pre-emptively unnecessary if Notifications or another consumer joins.
- The one rename matches the canonical example master-design § 3.5 already proposed (`PaymentRequestedEvent → RequestPaymentCommand`).

**Cons:**
- The decision table in master-design § 3.5 (consumer count → command) becomes a misleading shorthand if read in isolation.
- Asymmetric outcome inside one BC needs explanation (this ADR is that explanation).

### Option 2: Apply the rule's letter — rename all seven

Treat the decision table as authoritative: any 1-consumer Avro record becomes a command.

| Rename | New name | Target topic |
|---|---|---|
| `PaymentRequestedEvent` | `RequestPaymentCommand` | `payments.payment-commands` |
| `PaymentAuthorizedEvent` | `CapturePaymentCommand`? | conflicts with existing `CapturePaymentCommand` — would need a different verb (`MarkPaymentAuthorizedCommand`?). Already shows the unnatural shape. |
| `PaymentAuthorizationFailedEvent` | `RejectPaymentCommand`? | semantically awkward — Payments BC already rejected. |
| `PaymentCaptureFailedEvent` | similar awkwardness |  |
| `PaymentVoidedEvent` | `ConfirmVoidCommand`? | doesn't read as a command — void already happened. |
| `PaymentCompletedEvent` | `ConfirmCompletionCommand`? | inverts producer intent; PaymentProcessingSaga is announcing the end, not requesting. |
| `PaymentFailedEvent` | `ConfirmFailureCommand`? | same. |

**Pros:**
- Maximum literal adherence to the decision table.

**Cons:**
- Six of the seven renames produce awkward, intent-inverting names because the wire payload IS a past-tense fact.
- Each rename is a breaking Avro contract migration (new subject per ADR-0007).
- Adding any future consumer (Notifications email-on-refund-failure, BFF audit dashboard, fraud team's stream) would force a *third* rename back to event-shape — exactly the churn this ADR exists to prevent.
- Loses producer-intent visibility: a reader of `payments.transactions` would no longer see lifecycle events flowing — only Captured and Refunded would remain.

### Option 3: Defer ALL renames (status quo)

Keep every `*Event` name; just add a doc note explaining the audit and call it done.

**Pros:**
- Zero risk, zero work.

**Cons:**
- `PaymentRequestedEvent` IS imperative intent — the Checkout saga literally awaits a reply to advance its state machine. Leaving it event-shaped misrepresents what's happening on the wire. The master-design § 3.5 canonical example already calls this out.
- Leaves the decision-table doc unreliable for future agents who'll keep re-asking the question.

## Evaluation Matrix

| Driver (ranked) | Option 1: Spirit (rename 1) | Option 2: Letter (rename 7) | Option 3: Defer all |
|---|---|---|---|
| 1. Match the rule's spirit | ✅ direct application of the 2-part test | ⚠️ literal but loses the spirit | ❌ ignores both letter and spirit on `PaymentRequestedEvent` |
| 2. Avro schema-evolution cost | ✅ one migration | ❌ seven migrations | ✅ zero migrations |
| 3. Producer intent visibility | ✅ preserved for past-tense events | ❌ inverted | ⚠️ partially preserved (PaymentRequested still misnamed) |
| 4. Stability under future consumer arrival | ✅ events are open to any number of consumers | ❌ would need un-rename if Notifications joins | ✅ no churn |
| 5. Internal consistency / testability | ✅ explainable 2-part test | ✅ explainable single rule | ⚠️ "we agreed not to think about it" |

## Decision

We will use **Option 1** (Spirit interpretation):

- **RENAME** `PaymentRequestedEvent` → `RequestPaymentCommand` and move it from `payments.transactions` to `payments.payment-commands`.
- **DEFER** `PaymentAuthorizedEvent`, `PaymentAuthorizationFailedEvent`, `PaymentCaptureFailedEvent`, `PaymentVoidedEvent`, `PaymentCompletedEvent`, `PaymentFailedEvent` — they remain `*Event`-named on `payments.transactions`.

`PaymentCapturedEvent` and `PaymentRefundedEvent` are already correctly classified (≥ 2 real consumers) and need no action; master-design § 3.5's older "misnamed" list is corrected to reflect this.

## Rationale

**The rule's spirit is the 2-part test.** The article master-design § 3.5 cites is explicit: *"one expected consumer performing specific logic **with guaranteed feedback**"*. The decision table that follows is a useful summary but lossy — it treats "consumer count = 1" as sufficient when the article requires also "**producer awaits feedback**". Applying both parts cleanly partitions the seven 1-consumer messages into one true command (`PaymentRequestedEvent`) and six fact-shaped events.

**`PaymentRequestedEvent` is the one true command.** The Checkout saga's `AwaitingPayment` state is a blocking wait for a *specific* response — `PaymentCompletedEvent` or `PaymentFailedEvent` correlated by `CorrelationId`, with a 90-second timeout that triggers compensation if the reply doesn't arrive. The producer (Checkout saga) cannot make forward progress without the reply. That is request/reply over a shared correlation key, dressed as a pair of independent events. Naming the outbound shape `RequestPaymentCommand` and moving it to the command topic admits what's happening structurally and aligns with the existing precedent set by `AuthorizePaymentCommand`, `CapturePaymentCommand`, `VoidPaymentCommand`, `RequestRefundCommand`.

**`PaymentCompletedEvent` is NOT also a command — even though it travels with `PaymentRequestedEvent`.** The article-spirit test asks about the producer, not the consumer. PaymentProcessingSaga publishes `PaymentCompletedEvent` and immediately calls `.TransitionTo(PaymentCompleted).Schedule(SuccessFinalizationTimeout)` — it does not block on any reply from Checkout. From PaymentProcessingSaga's perspective the publish is fire-and-forget; it has finalized as far as it cares. The fact that Checkout's state machine treats it as a reply is a consumer-side concern and doesn't change the wire shape. The same applies to `PaymentFailedEvent` and to all five Payments-BC-produced `*Event` records.

**Avro-rename cost matters precisely because it is breaking.** Per [ADR-0007](0007-avro-compatibility-modes.md), every rename produces a new subject in the schema registry; the old subject is orphaned. Doing this for one message where the spirit clearly demands it is justified. Doing it for six where the names are already correct is contract churn for symbolic clarity — and would be un-done the moment a second consumer arrives for any of those records (the most likely future evolutions: a Notifications service subscribing to `PaymentAuthorizationFailedEvent` / `PaymentFailedEvent` to send a "card declined" email; a BFF analytics stream consuming the full lifecycle for ops dashboards; an audit-archive consumer reading every `payments.transactions` message for the 10-year EU VAT trail Invoicing already relies on).

**Topic move is non-negotiable for the one rename.** A command named `Command` on the `*-commands` topic is the only consistent placement — keeping a `RequestPaymentCommand` on `payments.transactions` would split the convention and confuse future agents. The target topic `payments.payment-commands` already exists with the right retention (7 days), the right partition key (`CorrelationId`), and the right compatibility mode (FULL_TRANSITIVE — bidirectional, suitable for independent saga/Payments deploy cadence). Adding `RequestPaymentCommand` to this topic broadens its documented purpose from "PaymentProcessingSaga → Payments imperative intent" to **"imperative intent toward the Payments aggregate (directly or via its sub-saga)"** — same partition key, same retention, same compat mode, no infrastructure change.

**Hard cutover, not parallel-publish.** This is a non-production reference repo with no live deploys to gate; the entire codebase moves in lockstep on every PR. ADR-0007's multi-phase breaking-change process (add deprecated, dual-publish, eventually cut over) is the right discipline for production but unnecessary here. The old `PaymentRequestedEvent` schema is deleted in the same change that adds `RequestPaymentCommand`; the producer and consumer swap atomically.

## Consequences

### Positive

- One Avro schema migrates; six stay stable. Lowest contract-churn outcome consistent with master-design § 3.5's authority.
- Payments BC's external surface is no longer "mostly events with one mystery command": the command is named a command, on the command topic, with the rest of the imperative-intent siblings.
- Future-second-consumer arrival for any of the six deferred messages costs zero — they're already events.
- Cross-doc drift (Notifications consumer-group claims, `PaymentCapturedEvent` misclassification, XML-doc producer attributions on the two saga-produced events) is corrected in the same change set, so the ADR lands a consistent codebase rather than codifying a partially-stale story.

### Negative

- The Payments BC's outcome is asymmetric across messages — readers must consult this ADR to understand why `PaymentRequestedEvent` was renamed but `PaymentAuthorizedEvent` was not, despite both having one consumer.
- One subject (`Payments.Transactions.PaymentRequestedEvent`) becomes orphaned in the Schema Registry history; harmless but lingers.
- The `BasketCheckoutInitiatedSagaEvent` and `CheckoutSagaState` carry XML doc references to the new name; if a future rename happens, those docs need a sweep.

### Risks

- **Risk:** an implementer naively re-applies the master-design § 3.5 decision table to a different BC and renames every 1-consumer event. **Mitigation:** master-design § 3.5 is updated to point at this ADR's 2-part test as the authoritative interpretation.
- **Risk:** a future Notifications subscriber arrives for one of the six deferred messages; the "1-consumer" pre-condition for re-evaluation evaporates and the deferral becomes moot. **Mitigation:** that's the desired outcome — adding a consumer makes the event-shape verdict more, not less, correct, and removes the deferral question entirely.
- **Risk:** moving the rename's target message off `payments.transactions` (infinite retention, audit) loses it from the audit-trail replay. **Mitigation:** the audit-trail purpose of `payments.transactions` is the *lifecycle* of a payment — Authorized → Captured → Completed / Refunded / Voided / Failed. The *request* that *initiates* a payment is upstream input, not aggregate-lifecycle output; recording it for 7 days on `payments.payment-commands` matches every other saga-issued command in the system (Authorize/Capture/Void/Refund). Forensic correlation across topics by `CorrelationId` still works because all messages carry it.
- **Risk:** the new internal saga event name `PaymentInitiatedSagaEvent` (kept unchanged) breaks the wire-mirror naming pattern used by every other Payments-saga internal event. **Mitigation:** documented in [`RequestPaymentCommandConsumer.cs`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/Consumers/RequestPaymentCommandConsumer.cs)'s XML doc — the imperative-to-declarative translation at the consumer boundary is intentional and reinforces the command-vs-event distinction.

## Implementation Notes

### Code changes (this PR)

- **Avro schema:** `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/RequestPaymentCommand.avsc` (new), generated `RequestPaymentCommand.cs` via [`generate-avro.ps1`](../../platform/Platform.SchemaRegistry.Contracts/generate-avro.ps1). Old `PaymentRequestedEvent.avsc` and `.cs` deleted (hard cutover).
- **Producer:** [`CheckoutSagaOrchestrator.cs`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/CheckoutSagaOrchestrator.cs) — `BuildPaymentRequestedEvent` renamed to `BuildRequestPaymentCommand`; both `.PublishToOutbox(...)` call sites switched from `PaymentsTransactions` to `PaymentsPaymentCommands` and from `PaymentRequestedEvent` to `RequestPaymentCommand`.
- **Consumer:** `PaymentRequestedConsumer.cs` renamed to [`RequestPaymentCommandConsumer.cs`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/Consumers/RequestPaymentCommandConsumer.cs); still maps to internal `PaymentInitiatedSagaEvent` (semantic name preserved — the imperative-to-declarative boundary is intentional).
- **DI wiring:** [`PaymentProcessingSagaDependencyInjection.cs`](../../saga/SagaOrchestrators/Common/SagasDependencyInjection/PaymentProcessingSagaDependencyInjection.cs) — new `TopicEndpoint` block subscribing PaymentProcessingSaga to `payments.payment-commands` (consumer group `saga-payment-processing` shared with the existing `payments.transactions` subscription).
- **Tests:** saga unit + integration tests + Payments integration tests updated in lockstep. Topic targets for the renamed command swapped to `PaymentsPaymentCommands`.
- **Weather dev scaffolding:** `PublishPaymentRequestedEvent` folder + endpoints renamed to `PublishRequestPaymentCommand`; producer method renamed and points to `_paymentCommandsTopic`. (Reference scaffolding only; per [`CLAUDE.md`](../../CLAUDE.md) Weather is not production code.)

### Bundled doc corrections (same PR)

- [`payments.md § 6`](../bc-design/payments.md) — "Known classification debt" paragraph replaced with a one-line pointer to this ADR; producer/consumer table corrected to reflect ground-truth wiring (`PaymentRefundedEvent` → Checkout + Invoicing; `PaymentCapturedEvent` → PaymentProcessingSaga + Invoicing).
- [`eshop-master-design.md § 3.5`](../eshop-master-design.md) — *"Known misnamed events in Payments"* paragraph rewritten: `PaymentCapturedEvent` removed from the misnamed list; `Notifications` removed from the "≥ 2 genuine consumers" claim; canonical example tightened; pointer to this ADR added. § 5.5 row count adjusted (still 9 messages total; 1 is now a command, 8 are events).
- [`events-catalog.md § 2 + § 3`](../bc-design/events-catalog.md) — `PaymentRequestedEvent` row replaced with `RequestPaymentCommand` row on `payments.payment-commands`; stale `notifications` consumer-group on `PaymentRefundedEvent` removed; Invoicing added to `PaymentCapturedEvent` consumers; consumer-group naming corrected from `payment-saga` to `saga-payment-processing` to match code.
- [`ADR-0004 § Implementation Notes`](0004-checkout-saga-topology.md) — references to `PaymentRequestedEvent` updated to `RequestPaymentCommand`; topology diagram description tweaked.
- [`checkout-saga.md`](../bc-design/checkout-saga.md), [`checkout-saga-state.md`](../diagrams/checkout-saga-state.md), [`glossary-payments.md`](../bc-design/glossary-payments.md) — references swapped.
- [`PaymentCompletedDomainEvent.cs`](../../services/Payments/Payments.Domain/Transactions/Events/PaymentCompletedDomainEvent.cs) and [`PaymentFailedDomainEvent.cs`](../../services/Payments/Payments.Domain/Transactions/Events/PaymentFailedDomainEvent.cs) — XML doc producer-attribution corrected from "the Checkout saga" to "PaymentProcessingSaga" (verified against [`PaymentProcessingSagaOrchestrator.cs:226, 284, 320`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/PaymentProcessingSagaOrchestrator.cs)).

### When to revisit

If any of the six deferred messages gains a second consumer in a future PR, the deferral question is moot — the message was correctly classified as an event from the start. No ADR update needed.

If a future PR proposes adding a saga-response *that the producer awaits before proceeding* (e.g., Inventory publishes a response to a saga query and the producer blocks on it), apply the 2-part test from this ADR fresh — the consumer-count letter alone isn't enough.

### Out of scope for this ADR

- The `PaymentId == CorrelationId` collapse (was out of scope of this ADR) — **resolved in cross-cutting wave1-followup #255**: the saga now mints a distinct UUID v7 `PaymentTransactionId` (carried on `AuthorizePaymentCommand`) and Payments uses it as the aggregate id, so `PaymentId` is independent of `CorrelationId`. The one-payment invariant is enforced by a unique index — since superseded by [ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md), which re-keys the saga on `OrderId` and moves that index from `correlation_id` to `order_id` (see [payments.md § 2.2 I-7](../bc-design/payments.md)).
- Notifications BC actually consuming any Payments events — currently aspirational; no v1 code path exists. If/when Notifications adds a consumer, the events catalog updates are mechanical; this ADR's classifications stay correct.
- The `PaymentRefundCompletedSagaEvent` dead-code path in [`PaymentProcessingSagaOrchestrator.cs`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/PaymentProcessingSagaOrchestrator.cs) (the orchestrator references it but no consumer translates `PaymentRefundedEvent` → that saga-internal event because Checkout saga handles the refund flow directly). Worth a separate cleanup pass.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — parent decision establishing the saga pattern this rename clarifies.
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — defines the request/reply pattern between Checkout saga and PaymentProcessingSaga that this ADR re-names the request half of.
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — why a rename is a breaking contract migration (new subject) rather than an in-place edit, and why we constrain the rename to the one case where the cost is justified.
- [`master-design § 3.5`](../eshop-master-design.md) — the rule's letter (decision table) and the rule's spirit (article-cited 2-part test) that this ADR resolves the tension between.
