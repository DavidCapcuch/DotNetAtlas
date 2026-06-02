# ADR-0026: Checkout payment flow — defer capture to the pivot, Payments owns its terminal events

## Status

Accepted (2026-06-02)

## Context

The checkout payment flow spans two orchestrators and one aggregate:

- **Checkout saga** (top-level, MassTransit) — drives the order: reserve stock → pay → confirm.
- **`PaymentProcessingSaga`** (sub-saga) — owns the payment leg: issues `AuthorizePaymentCommand` / `CapturePaymentCommand` / `VoidPaymentCommand` to the Payments service and reacts to its events.
- **`PaymentTransaction`** aggregate (Payments BC) — the FSM `Requested → Authorized → Captured → Completed` with `Voided` / `Refunded` off-ramps, behind an `IPaymentGateway` port.

A ground-truth trace of the current implementation plus four independent best-practice research streams (Stripe/Adyen capture+refund docs, Chris Richardson's saga pattern + compensatable/pivot/retriable taxonomy, the Microsoft .NET microservices guidance on domain-vs-integration events, Vaughn Vernon / Udi Dahan / Kamil Grzybek on event ownership) surfaced five gaps. The structure is otherwise textbook-correct (two-step auth/capture FSM, a justified payment sub-saga, transactional outbox, orchestration over choreography).

### The five findings

1. **Terminal events are produced by the saga, not the Payments service.** `PaymentProcessingSaga` publishes `PaymentCompletedEvent` and `PaymentFailedEvent`; the Payments outbox publishes the other four lifecycle events (`PaymentAuthorizedEvent`, `PaymentCapturedEvent`, `PaymentVoidedEvent`, `PaymentRefundedEvent`). The aggregate raises `PaymentCompletedDomainEvent` / `PaymentFailedDomainEvent` but they have **no in-process handlers** — they are inert ([#262](https://github.com/DavidCapcuch/DotNetAtlas/issues/262)). The research is unanimous: the **owning service** must publish its own terminal integration events via its outbox; a saga cannot atomically write the Payments DB and publish, and a saga-as-publisher breaks service autonomy and creates dual authority over payment state ([microservices.io transactional outbox](https://microservices.io/patterns/data/transactional-outbox.html), [Udi Dahan — saga persistence](https://udidahan.com/2009/04/20/saga-persistence-and-event-driven-architectures/), [MS .NET integration events](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/integration-event-based-microservice-communications)).

2. **Capture fires before confirmation, forcing a refund path.** The sub-saga issues `CapturePaymentCommand` immediately on `PaymentAuthorizedEvent`, and the aggregate auto-advances `Captured → Completed`. The Checkout saga only *confirms* stock + order **after** payment completes. So a confirmation failure occurs **post-capture** and requires a **refund**. The saga **pivot** taxonomy says capture is the pivot — order the saga so pre-pivot failures are cheap **voids**, not refunds ([microservices.io saga](https://microservices.io/patterns/data/saga.html), [Azure saga pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/saga), Richardson *Microservices Patterns*). The textbook order is Reserve → Authorize → Confirm → **Capture** → Complete.

3. **Dead refund states in the sub-saga** ([#288](https://github.com/DavidCapcuch/DotNetAtlas/issues/288)). `RefundInProgress` / `RefundCompleted` / `RefundFailed`, the `RefundTimeout` schedule, and the `PaymentRefundRequestedSagaEvent` / `PaymentRefundCompletedSagaEvent` triggers are **declared and configured but unreachable** — no producer ever emits the trigger event. Unreachable states are a design defect, not forward-looking scaffolding (YAGNI; MassTransit surfaces them as dead-letter noise).

4. **Refund doc/code discrepancy** ([#290](https://github.com/DavidCapcuch/DotNetAtlas/issues/290)). `eshop-master-design.md §5.5` and `payments.md §7` say `RequestRefundCommand` is produced by `PaymentProcessingSaga` ("Checkout compensation"). The code has the **Checkout saga** issue `RequestRefundCommand` directly to Payments, bypassing the sub-saga — the sub-saga's refund states (#288) are the abandoned doc-described path.

5. **Authorization failure does not fast-fail the checkout** *(new)*. On a gateway decline the sub-saga finalizes emitting only `PaymentAuthorizationFailedEvent` (which the Checkout saga does not consume). The Checkout saga never receives a terminal and waits out the full ~90 s `PaymentTimeout` before compensating.

(A sixth, lower finding — no `Capturing`/`Completing` intermediate states for gateway-timeout reconciliation — is noted as future work, not decided here.)

## Decision Drivers (ranked)

1. **Minimize compensation cost and blast radius.** A pre-settlement **void** is free and invisible to the cardholder; a post-settlement **refund** moves money, incurs fees, and takes days. The flow should be shaped so the common failure is a void.
2. **Service autonomy + outbox atomicity for state events.** The service that owns the aggregate must publish that aggregate's integration events, atomically with the state change, via its own outbox.
3. **Single source of truth for payment state.** Exactly one component is authoritative for "payment completed/failed"; no dual authority.
4. **No dead states / no producerless commands.** Every saga state is reachable; every command/event has a producer, or is explicitly documented as a deferred capability.
5. **Fast-fail on terminal outcomes.** The orchestrator compensates on an explicit terminal event, never on a timeout it could have avoided.
6. **Preserve the nested-saga separation** — keep payment mechanics inside `PaymentProcessingSaga`, not leaked into the Checkout saga.

## Considered Options

### Option 1: Capture-at-the-pivot + sub-saga wait-state + Payments-owned terminals (chosen)

Reorder the flow so **capture is the last step**, deferred until the Checkout saga has confirmed stock + order. The sub-saga gains an `AwaitingCaptureApproval` wait-state between authorize and capture. Payments publishes **all** its lifecycle events (including terminals) via its outbox; the sub-saga orchestrates only.

**Pros:** the common failure (confirmation fails) becomes a void, not a refund (driver 1); terminal events get the correct owner + fix #262 (drivers 2-3); dead states removed (driver 4); auth decline fast-fails via the now-published `PaymentFailedEvent` (driver 5); payment FSM stays inside the sub-saga (driver 6).
**Cons:** the largest change — restructures both saga state machines and adds a capture-approval handshake; removes the post-capture refund-compensation demonstration (refund becomes a deferred customer/admin flow).

### Option 2: Status quo (auth+capture immediately, saga-owned terminals, post-capture refund compensation)

Keep capture-on-authorize; keep the saga publishing terminals; keep the refund-as-compensation path.

**Pros:** no change; demonstrates post-capture saga compensation.
**Cons:** every post-payment failure is an expensive refund (driver 1 fails); saga-owned terminals violate outbox atomicity + autonomy (driver 2); #262 inert events + #288 dead states persist; auth decline still waits out the timeout. Rejected.

### Option 3: Collapse the sub-saga into the Checkout saga

Remove `PaymentProcessingSaga`; the Checkout saga drives authorize → confirm → capture and void directly against Payments.

**Pros:** fewest moving parts.
**Cons:** payment mechanics leak into the Checkout saga (driver 6); loses the nested-saga demonstration; the payment leg's own multi-step FSM with compensation still justifies a dedicated sub-saga per the research. Rejected.

## Evaluation Matrix

| Driver (ranked) | Option 1: capture-pivot + wait-state | Option 2: status quo | Option 3: collapse sub-saga |
|---|---|---|---|
| 1. Cheap compensation (void > refund) | ✅ pre-capture void | ❌ post-capture refund | ✅ void (if also reordered) |
| 2. Service autonomy / outbox terminals | ✅ Payments owns all | ❌ saga publishes terminals | ✅ if Payments owns |
| 3. Single source of truth | ✅ | ❌ dual authority | ✅ |
| 4. No dead states / producerless cmds | ✅ removed | ❌ #288 persists | ✅ |
| 5. Fast-fail on auth decline | ✅ PaymentFailedEvent | ❌ 90 s timeout | ✅ |
| 6. Payment mechanics encapsulated | ✅ sub-saga retained | ✅ | ❌ leaks into Checkout |

## Decision

Adopt **Option 1**. Four coupled decisions:

### 1. Capture is the pivot — deferred until after confirmation

The Checkout saga flow becomes:

```
Reserve stock → Authorize → Confirm (order + reservations) → Capture (PIVOT) → Complete
```

- **Reserve fails** → terminate; nothing charged.
- **Authorization declines** → release the reservation, terminate (no void needed — nothing was captured).
- **Confirmation fails** (stock/order confirm) → **Void** the authorization + release/compensate the reservation. Pre-capture, free.
- **Post-capture** → only `Complete` remains, which cannot meaningfully fail. No post-capture compensation inside the checkout flow.

Authorize stays before confirm (don't bother confirming stock if the card declines); both are pre-pivot compensatable steps. Capture must occur well within the gateway authorization window (7 days for the major schemes — a non-issue here since confirmation completes in seconds, but the constraint is recorded).

### 2. The sub-saga gains an `AwaitingCaptureApproval` wait-state

`PaymentProcessingSaga`: `Authorize → AwaitingCaptureApproval → Capture → Completed`, with `Void` on the pre-capture failure path. After the Checkout saga confirms stock + order it sends a **capture-approval** signal; an **abort** signal (or a wait-state timeout) drives the `Void` path instead. The sub-saga retains end-to-end ownership of the payment FSM.

### 3. Payments owns all its lifecycle integration events via its outbox

The previously-inert `PaymentCompletedDomainEvent` and `PaymentFailedDomainEvent` get **outbox-publisher handlers** in Payments (symmetric with `PaymentAuthorized`/`PaymentCaptured`/etc.), so Payments publishes `PaymentCompletedEvent` / `PaymentFailedEvent` itself. **`PaymentProcessingSaga` stops publishing payment-state events** — it orchestrates (sends commands, reacts to events) only. The sub-saga, Checkout saga, and Invoicing all subscribe to Payments' topic directly. This resolves **#262** (the events are no longer inert) and **finding #5** by construction: an authorization decline drives the aggregate to `Failed`, Payments publishes `PaymentFailedEvent`, and the Checkout saga compensates immediately rather than on timeout.

### 4. Refund leaves checkout compensation; becomes a documented future flow

- Remove the dead sub-saga refund states / triggers / timeout (**#288**).
- Replace the Checkout saga's post-capture refund-compensation with the pre-capture **void** above.
- **Retain** the aggregate's `Completed → Refunded` transition, `RequestRefundCommand`, `PaymentRefundedEvent`, and Invoicing's credit-note consumer — but **explicitly documented as a deferred customer/admin-initiated refund flow with no v1 producer** (a returns / post-purchase-cancellation trigger is future work). This is a *documented* deferred capability, not accidental dead code.
- Reconcile the docs (**#290**): refund is a standalone flow, not `PaymentProcessingSaga` compensation.

## Rationale

**Capture is the pivot, so design the failure to land before it.** Richardson's compensatable/pivot/retriable taxonomy is explicit: steps after the pivot should be retriable-only and never permanently fail. Our post-capture step that *could* permanently fail (confirmation) is the textbook signal to **reorder** — moving capture after confirmation converts the expensive post-capture refund into a free pre-capture void and removes the need for a post-pivot compensation path entirely.

**The owning service publishes its own state.** Every authority consulted draws the same line: the transactional outbox lives in the service that owns the aggregate; the saga reacts to the service's events and may publish its *own* orchestration outcomes, but never the participant service's state events. The saga-as-publisher arrangement was the root cause of #262's inert domain events — fixing ownership and un-inerting the events are the same change.

**Dead states are a defect.** The sub-saga's refund states were the abandoned half of the #290 doc-described path. Keeping them invites untested code paths and (in MassTransit) dead-letter noise. Removing them — and documenting refund as a deferred, deliberately-producerless future flow — is honest about scope.

**The sub-saga still earns its keep.** Even after moving capture-triggering under the Checkout saga's control, the payment leg remains a multi-step FSM with compensation (`Authorize → wait → Capture`, `Void`), which is exactly when a dedicated sub-saga is warranted. Collapsing it would leak gateway mechanics into the Checkout saga.

## Consequences

### Positive

- The common payment failure (confirmation fails) is a free, invisible **void** instead of a fee-incurring refund.
- Payment state has a single authoritative publisher (Payments); #262's inert events become the outbox extension point they should always have been.
- Auth declines fast-fail the checkout instead of waiting out a 90 s timeout.
- The sub-saga state machine contains only reachable states; docs match code.
- The design is now defensible against the cited best-practice canon end-to-end.

### Negative

- The largest single change to the order flow — both saga state machines, the aggregate's capture trigger, and the cross-saga handshake move.
- The post-capture refund-compensation demonstration is removed; refund becomes a documented future capability rather than an exercised path. (Accepted: the textbook-correct flow is the higher-value teaching artifact.)
- A new capture-approval handshake adds one round-trip and a wait-state timeout to manage.

### Risks

- **Risk:** the `AwaitingCaptureApproval` wait-state never receives approval (Checkout saga crashes after authorize, before confirm). **Mitigation:** a wait-state timeout drives the `Void` path; the authorization is released, not left dangling.
- **Risk:** capture deferred beyond the gateway authorization window. **Mitigation:** confirmation completes in seconds; the window is 7 days. Recorded as a constraint, not a live risk.
- **Risk:** `RequestRefundCommand` / `PaymentRefundedEvent` become producerless dead code (the very smell being fixed). **Mitigation:** they are *explicitly documented* as a deferred customer/admin flow with Invoicing as a live downstream consumer — distinguished from accidental dead code by the documentation and the existing consumer.
- **Risk:** gateway-timeout-during-capture leaves an unknown outcome. **Mitigation:** out of scope here; tracked as future `Capturing`/`Completing` intermediate states + a reconciliation worker.

## Implementation Notes

Subsumes [#262](https://github.com/DavidCapcuch/DotNetAtlas/issues/262), [#288](https://github.com/DavidCapcuch/DotNetAtlas/issues/288), [#290](https://github.com/DavidCapcuch/DotNetAtlas/issues/290) and the new auth-fast-fail finding.

- **Checkout saga** — move `CapturePayment` triggering to *after* `ConfirmOrder` + `ConfirmReservation` succeed; on confirmation failure, drive the sub-saga's void path; on authorization failure, release the reservation and terminate (consuming `PaymentFailedEvent`). Remove the post-capture `RequestRefundCommand` compensation.
- **`PaymentProcessingSaga`** — add `AwaitingCaptureApproval` between authorize and capture; consume a capture-approval / abort signal from the Checkout saga; remove the `RefundInProgress`/`RefundCompleted`/`RefundFailed` states, `RefundTimeout`, and the orphaned refund saga-events; stop publishing `PaymentCompletedEvent` / `PaymentFailedEvent`.
- **Payments BC** — add outbox-publisher domain-event handlers for `PaymentCompletedDomainEvent` and `PaymentFailedDomainEvent` (symmetric with the existing four); the aggregate's capture trigger is now driven by the deferred `CapturePaymentCommand`. Confirm the aggregate's auto-advance `Captured → Completed` still holds (capture remains a single command at the gateway).
- **Capture-approval contract** — a new command (Checkout saga → sub-saga) signaling "confirmed, capture now" plus an abort variant. Define behaviorally; the implementer picks the message shape.
- **Docs** — reconcile `eshop-master-design.md §5.5`, `payments.md` (§6/§7 event/command ownership + the FSM ordering), and `checkout-saga.md` to the new flow; document refund as a deferred customer/admin flow.
- **Tests** — the Checkout + Payments integration tests must exercise: happy path (authorize → confirm → capture → complete), confirmation-failure → void, and authorization-decline → fast-fail. Assert Payments (not the saga) is the producer of `PaymentCompletedEvent` / `PaymentFailedEvent`.

### When to revisit

- Gateway-timeout reconciliation (`Capturing`/`Completing` intermediate states + worker) — a future ADR if/when the stub gateway is replaced by a real PSP. Tracked: [#297](https://github.com/DavidCapcuch/DotNetAtlas/issues/297).
- A customer/admin-initiated refund/returns flow — promotes the deferred refund capability to a real producer; its own design effort. Tracked: [#298](https://github.com/DavidCapcuch/DotNetAtlas/issues/298).

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — the orchestration posture this flow lives in.
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — the saga state machine being restructured; its state-timeout backstops the wait-state.
- [ADR-0023: Payments Event-vs-Command Classification](0023-payments-event-vs-command-classification.md) — the command/event vocabulary this flow uses.
- [ADR-0025: Kafka consumer retry & dead-letter policy](0025-kafka-consumer-retry-dlt-policy.md) — the consumer reliability policy under which these events are processed.
- [eshop-master-design.md §5.5](../bc-design/eshop-master-design.md), [payments.md](../bc-design/payments.md), [checkout-saga.md](../bc-design/checkout-saga.md) — design docs reconciled by this decision.
