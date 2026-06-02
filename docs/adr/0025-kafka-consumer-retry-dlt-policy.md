# ADR-0025: Kafka consumer retry & dead-letter policy — classify by failure class

## Status

Accepted (2026-06-01)

## Context

Every Kafka consumer in the solution wires the same middleware chain (outermost
→ innermost):

```
AddSchemaRegistryAvroDeserializer → AddCorrelationIdConsumerMiddleware
  → AddDeadLetter → RetryForever(Handle<DbUpdateException, NpgsqlException, TimeoutException>,
                                 WithTimeBetweenTriesPlan(500ms, 1s, 2s, 5s))
  → AddInbox → AddTypedHandlers
```

This is uniform across **9 consumers in 6 BCs** (Catalog, Inventory ×3,
Invoicing ×3, Notifications, Ordering). Payments was the lone exception after
[#247](https://github.com/DavidCapcuch/DotNetAtlas/issues/247): its
`payments.payment-commands` consumer was changed to a **bounded**
`RetrySimple(TryTimes: 8)` then DLT.

The wiring branches on **exception _type_**. That is the defect. A
`DbUpdateException` / `NpgsqlException` carries no information about whether the
failure is recoverable:

- A `DbUpdateException` from *connection refused / failover in progress /
  deadlock / serialization failure* is **transient** — the same message will
  succeed once the infrastructure recovers. Dead-lettering it loses a good
  message and floods the DLT during an outage.
- A `DbUpdateException` from *`23505` unique_violation / `23514` check_violation
  on a malformed payload* is **poison** — it fails identically on every attempt.
  Retrying it forever blocks the partition (head-of-line blocking).

Same exception type, opposite correct responses. Neither in-place strategy is
right on its own:

- **`RetryForever`** (the 6 BCs): never dead-letters the poison case → a
  structural poison message blocks the partition indefinitely; for the
  saga-critical command topics (`ordering.order-commands`,
  `inventory.reservation-commands`) this silently defeats the DLT alerting +
  mandatory-postmortem process in [kafka-dlt-strategy.md §4/§5](../bc-design/kafka-dlt-strategy.md).
- **`RetrySimple(8)`** (Payments, #247): dead-letters the *transient* case after
  ~40s of backoff → a DB outage longer than the bound sends a flood of perfectly
  good messages to the DLT, requiring manual replay. This is the
  dead-letter-on-connectivity **anti-pattern**.

Two documentation artifacts contradict the code and each other:

- [kafka-dlt-strategy.md §2](../bc-design/kafka-dlt-strategy.md) claims *"there
  is currently no generic retry middleware"* and *"a single unhandled exception
  dead-letters"* — both false; every consumer wires `RetryForever`, and the
  three handled types never dead-letter.
- §7 **F-5** records the retry-then-DLT middleware as *"intentionally OMITTED for
  v1"* — which #247 then partially contradicted by adding `RetrySimple` to
  Payments.

### What the research says (top-practitioner consensus)

Four independent research streams (Confluent, Uber Engineering, Spring Kafka
reference docs, Conduktor/Redpanda/Kai Waehner, PostgreSQL/Npgsql/EF Core docs,
and the KafkaFlow retry-extensions source) converge:

1. **Classify by failure class, not exception type.** Spring's
   `DefaultErrorHandler.addNotRetryableExceptions` is the canonical expression:
   some failures are *fatal* (deserialization, conversion, constraint
   violations) and must skip retry entirely; the rest are retried.
   ([Spring Kafka — Handling Exceptions](https://docs.spring.io/spring-kafka/reference/kafka/annotation-error-handling.html))
2. **Never dead-letter a transient failure.** *"Transient errors heal, poison
   pills never do."* ([Conduktor](https://www.conduktor.io/blog/dead-letter-topics-handling-poison-pills));
   *"Putting messages into a DLQ because of failed connectivity does not help"*
   ([Kai Waehner](https://www.kai-waehner.de/blog/2022/05/30/error-handling-via-dead-letter-queue-in-apache-kafka/)).
3. **Never retry a poison message forever** — head-of-line blocking.
4. **For ordered topics, blocking retry is the recommended pattern; non-blocking
   retry topics are discouraged** because they destroy per-partition ordering —
   *"By using this strategy you lose Kafka's ordering guarantees for that topic"*
   ([Spring Kafka — How the Pattern Works](https://docs.spring.io/spring-kafka/reference/retrytopic/how-the-pattern-works.html)).
   Uber makes "out-of-order acceptable" a hard precondition for retry topics
   ([Uber Engineering](https://www.uber.com/us/en/blog/reliable-reprocessing/)).
   This is catastrophic for saga-command orchestration ([ADR-0001](0001-centralized-saga-orchestration.md))
   and event-sourced aggregates ([ADR-0006](0006-event-sourcing-for-inventory.md)).
5. **`IsTransient` already encodes the right split.** `Npgsql.PostgresException`
   / `NpgsqlException.IsTransient` returns `true` for SQLSTATE classes `08*`
   (connection), `40001` (serialization_failure), `40P01` (deadlock_detected),
   `53*` (insufficient_resources), `55*` (object-in-use), `57P0*`
   (admin/crash shutdown), `58*` (system error) — and `false` for `23*`
   (integrity constraints), `22*` (data), `42*` (syntax/access).
   ([Npgsql `PostgresException.cs`](https://github.com/npgsql/npgsql/blob/main/src/Npgsql/PostgresException.cs))

The KafkaFlow `RetryForever` middleware **already pauses the consumer** on first
retry (hardcoded in the library), which approximates the expert-recommended
"pause-on-dependency-outage" behaviour: during a real DB outage every message
fails identically, so the consumer effectively parks until recovery — no message
lost, no DLT flood. The real defect was never the unbounded-ness; it was the
**absence of poison classification**.

## Decision Drivers (ranked)

1. **No message loss on a transient outage.** A DB failover or restart must never
   send good messages to the DLT.
2. **No head-of-line blocking on poison.** A deterministically-failing message
   must not wedge a partition forever; it goes to the DLT so the partition
   advances.
3. **Preserve per-partition ordering** for saga-command and event-sourced topics.
   This rules out non-blocking retry topics.
4. **One classification, one source of truth.** "Is this exception retryable?"
   must be answered identically by all 10 consumers and be unit-testable in one
   place.
5. **Operational clarity.** A message on a DLT must mean *"genuinely
   unprocessable"* — never *"the DB blipped"* — so the
   [kafka-dlt-strategy.md §4.5](../bc-design/kafka-dlt-strategy.md) mandatory-
   postmortem semantics stay meaningful.
6. **Minimal infrastructure for a reference solution.** Prefer the least new
   moving parts that satisfies 1–5; defer heavier machinery unless it earns its
   keep.

## Considered Options

### Option 1: Classified `RetryForever` + consumer pause (chosen)

Keep `AddDeadLetter` → `RetryForever`, but replace the exception-**type** filter
with a failure-**class** predicate: retry iff the exception is retryable
(`IsTransient` ∨ a `RetryableException` marker ∨ `TimeoutException`, excluding
`OperationCanceledException`). Non-retryable (poison) exceptions are not handled
by the retry middleware, so they fall through to the already-wired
`AddDeadLetter` and dead-letter immediately. Transient failures retry forever
with the existing backoff plan, with the consumer paused (KafkaFlow's
`RetryForever` behaviour).

**Pros:**
- No message loss on outage (transient retries until recovery) **and** no
  head-of-line block on poison (poison → DLT now). Satisfies drivers 1 + 2
  simultaneously — the only option that does.
- Preserves per-partition ordering (blocking retry; no retry topics).
- Minimal change: the middleware chain is unchanged in shape; only the
  `Handle(...)` predicate changes. No new infrastructure, no new Kafka topics,
  no external retry store.
- DLT becomes poison-only by construction → driver 5 satisfied.

**Cons:**
- A *misclassified* poison (an exception that wrongly reports `IsTransient ==
  true`, e.g. the known Npgsql command-timeout edge —
  [npgsql#2239](https://github.com/npgsql/npgsql/issues/2239)) still blocks the
  partition forever. Mitigated by the consumer-lag alert
  ([kafka-dlt-strategy.md §6](../bc-design/kafka-dlt-strategy.md)) and saga
  state-timeout ([ADR-0004](0004-checkout-saga-topology.md)) backstops.

### Option 2: Bounded `RetrySimple(N)` then DLT (the #247 Payments shape)

Retry a fixed number of times with backoff, then dead-letter.

**Pros:** poison eventually dead-letters; bounded blast radius for a
misclassification.
**Cons:** dead-letters **transient** messages once the bound is exceeded — the
exact anti-pattern from the research (driver 1 fails). A DB outage longer than
`N × maxBackoff` floods the DLT with good messages that must be replayed by hand.
This is the behaviour #247 introduced and this ADR reverses.

### Option 3: KafkaFlow `RetryDurable`

Park failed messages in an external store (Postgres provider exists) + a
dedicated retry topic + embedded consumer group + polling cron; the main
partition advances.

**Pros:** genuinely non-blocking; no message loss.
**Cons:** verified against library source — **no built-in max-attempts and no
automatic DLQ**; it retries indefinitely until success or *manual operator
cancellation via an HTTP management API*
([durable-retries](https://farfetch.github.io/kafkaflow-retry-extensions/docs/guides/durable-retries)).
Its ordered mode (`GuaranteeOrderedConsumption`) re-introduces per-key blocking,
defeating the point for our ordered topics. Heavy infrastructure (second
consumer group, retry topic, retry-state tables, management API) for a reference
solution — fails driver 6, and the no-clean-terminal property fails driver 5.

### Option 4: Non-blocking retry topics (Spring `@RetryableTopic` style)

Tiered retry topics with escalating backoff; main partition advances; final DLT.

**Pros:** main partition never blocks; bounded with a clean DLT terminal.
**Cons:** **destroys per-partition ordering** — explicitly discouraged by every
source for saga-command and event-sourced topics (driver 3 fails). KafkaFlow has
no first-class equivalent; would be hand-rolled. Out of scope.

### Option 5: Circuit-breaker / health-gated consumer pause

A breaker keyed on infra-transient errors that pauses the whole consumer on a
systemic outage (failure-ratio threshold) and resumes on a `SELECT 1` health
probe; isolated per-message transients get short bounded retry; poison → DLT.

**Pros:** the most operationally precise — one "circuit open" event instead of N
per-message retry logs; proactive whole-consumer park.
**Cons:** most engineering (custom breaker middleware + probe + manual KafkaFlow
pause wiring, since the high-level API exposes no clean global pause). Option 1
converges on the same end state (consumer parks until recovery) for ~10% of the
code. Deferred — a clean future ADR if production noise justifies it.

## Evaluation Matrix

| Driver (ranked) | 1: Classified RetryForever | 2: Bounded RetrySimple | 3: RetryDurable | 4: Retry topics | 5: Circuit-breaker |
|---|---|---|---|---|---|
| 1. No loss on transient outage | ✅ retries until recovery | ❌ DLTs after bound | ✅ durable store | ✅ until DLT terminal | ✅ pauses |
| 2. No head-of-line block on poison | ✅ poison → DLT now | ✅ → DLT after bound | ✅ non-blocking | ✅ non-blocking | ✅ → DLT now |
| 3. Preserve ordering | ✅ blocking, no reorder | ✅ blocking | ⚠️ only in ordered mode (re-blocks) | ❌ reorders | ✅ blocking |
| 4. Single classification SoT | ✅ shared predicate | ✅ shared predicate | ✅ | ✅ | ✅ |
| 5. DLT = poison only | ✅ by construction | ❌ transients land too | ⚠️ no clean terminal | ✅ | ✅ |
| 6. Minimal infra | ✅ predicate-only change | ✅ | ❌ store + topic + cron + API | ❌ topic fan-out | ⚠️ custom middleware |

## Decision

We adopt **Option 1 — classified `RetryForever` + consumer pause** as the
canonical consumer error-handling policy for **all 10 EF-Core + Postgres command
/ event consumers** (Catalog, Inventory ×3, Invoicing ×3, Notifications,
Ordering, Payments).

**Classification (the allowlist).** A consumed message is retried iff its
exception is **retryable**:

- `DbException.IsTransient == true` — covers `DbUpdateException` /
  `NpgsqlException` / `PostgresException` whose SQLSTATE is a transient class
  (`08*`, `40001`, `40P01`, `53*`, `55*`, `57P0*`, `58*`); **and**
- the exception is a `RetryableException` marker (a deliberate "this is a
  retryable business-transient" signal thrown by a handler); **and**
- the exception is a `System.TimeoutException` (transient-leaning — a timeout is
  far more often load/lock-wait than a deterministic poison).

Everything else is **non-retryable (poison)** and falls through to
`AddDeadLetter`: integrity-constraint / data / syntax violations (`23*`, `22*`,
`42*`), deserialization failures, `ArgumentException`, `DataIntegrityException`,
and any unrecognised exception. **`OperationCanceledException` is explicitly
excluded** from the retryable set — `DeadLetterMiddleware` already rethrows it on
graceful shutdown so the offset stays uncommitted ([kafka-dlt-strategy.md §2](../bc-design/kafka-dlt-strategy.md)); the allowlist must not swallow it.

**Structure (where it lives).**

- The classification predicate (`IsRetryable(Exception)`) and the
  `RetryableException` marker type are **shared in Platform** and unit-tested
  once. They are the single source of truth for driver 4.
- Each BC keeps its own `.AddDeadLetter().RetryForever(...)` wiring **visible at
  its call site**; the `RetryForever` config references the shared predicate
  (`c => c.Handle(ctx => ConsumerRetry.IsRetryable(ctx.Exception))`). The backoff
  plan stays per-call-site. We deliberately did **not** hide the whole chain
  behind a single `.AddClassifiedRetryThenDeadLetter()` helper — the retry
  behaviour stays readable where the consumer is declared.

**Default semantics.** "Retryable" now means "retry until it succeeds, consumer
paused." DLT now means **"a genuinely unprocessable (poison) message"** — never a
transient blip.

## Rationale

**Branching on failure class is the whole point.** The type-based filter is the
root cause of both failure modes — it cannot distinguish a recoverable DB blip
from a deterministic constraint violation. `IsTransient` already carries exactly
that distinction (driver 1 + 2 simultaneously), which is why every research
stream lands on the same answer.

**Blocking retry is correct for our topic shapes.** Our consumers are
saga-command (`*-commands`) and aggregate / event-sourced event streams — all
order-sensitive. The expert consensus is unambiguous: non-blocking retry topics
(Options 3-ordered/4) sacrifice ordering and are *the wrong tool* here. A
blocking consumer that parks on a real outage is a *feature* — nothing can
succeed during the outage anyway, and the partition resumes in order on recovery.

**`RetryForever` was closer to right than `RetrySimple`.** The original 6-BC
pattern already retried-then-paused on transient faults and already routed
non-handled exceptions to the DLT; its only flaw was retrying the *poison*
subset of `DbUpdateException`. The fix is to subtract poison from the retry set,
not to add a bound that re-introduces message loss. This ADR therefore **reverts
#247's `RetrySimple(8)`** on Payments back to the unified classified
`RetryForever` — there is no money-handling exception; one policy governs all 10
consumers.

**The misclassification tail is backstopped, not ignored.** Option 1's one weak
spot — a poison that lies about `IsTransient` — is caught by two independent
existing safety nets: the `kafka.consumer.lag > 1000 sustained 5 min` alert
([kafka-dlt-strategy.md §6](../bc-design/kafka-dlt-strategy.md)) and, for
saga-critical command topics, the saga state-timeout that triggers compensation
([ADR-0004](0004-checkout-saga-topology.md)). A blocked partition is loud, not
silent.

**Minimal infrastructure is the right call for a reference solution.** Options 3
and 5 are more capable but carry real operational weight (an external retry store
+ topic + cron + management API; or a bespoke circuit-breaker + health probe).
For the failure profile this solution actually exhibits, a predicate change buys
the correctness; the heavier machinery is a documented future step, not a v1
requirement (driver 6).

## Consequences

### Positive

- A DB outage no longer dead-letters good messages; a poison message no longer
  wedges a partition. The two failure modes are resolved together.
- Per-partition ordering is preserved for every saga and event-sourced stream.
- DLT contents become meaningful: every message on a DLT is genuinely
  unprocessable and warrants the mandatory postmortem.
- One unified policy across all 10 consumers; the classification logic is
  unit-tested in one place and cannot drift between BCs.
- `kafka-dlt-strategy.md §2` and F-5 get rewritten to match reality, closing a
  doc-vs-code contradiction.

### Negative

- The retry decision is no longer obvious from the exception type at the call
  site — a reader must follow `ConsumerRetry.IsRetryable` to see the policy.
  Mitigated by this ADR + the shared predicate's XML docs + its unit tests.
- A genuinely-transient class that Npgsql does *not* flag `IsTransient` (e.g. an
  application-level `HttpRequestException` from a future outbound call) will
  dead-letter unless a handler wraps it in `RetryableException`. The marker is
  the deliberate escape hatch; handlers must use it consciously.

### Risks

- **Risk:** a handler throws a bare `DbUpdateException` to *force* a retry (as
  Inventory's `OrderCancelledEventKafkaHandler` does today) — under the new
  predicate that exception has `IsTransient == false` and would route to DLT,
  silently breaking at-least-once release. **Mitigation:** that handler is
  migrated to throw `RetryableException` instead (see Implementation Notes); the
  pattern "throw a fake DB exception to trigger retry" is retired.
- **Risk:** double-retry if EF Core's `EnableRetryOnFailure` execution strategy
  retries inside a consumer handler scope while the consumer middleware also
  retries; the in-handler EF retry also blocks the worker for its duration
  (KafkaFlow cannot pause around a retry it never sees). **Decision (accepted):**
  left as-is. The EF inner-retry is bounded (`RetryMaxCount` 6 × `maxRetryDelay`
  ≤ 30 s, comfortably inside the 5-min `max.poll.interval.ms`), runs inside the
  inbox transaction (idempotent re-execution — no double-apply), and only ever
  retries the same transient class the middleware would, so it never reclassifies
  poison or loses a message — the classifier still governs DLT routing. A
  per-scope disable (a `ConsumerExecutionScope` marker swapping the execution
  strategy) was prototyped and rejected as overengineering for the current
  failure profile; EF forbids the lighter alternatives anyway — an unwrapped
  transaction or a manually constructed `NonRetryingExecutionStrategy` both throw
  *"does not support user-initiated transactions"* once a write runs inside the
  transaction. Revisit only if backlog-induced eviction is ever observed.
  ([EF Core connection resiliency](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency))
- **Risk:** a `23505` on the **inbox dedup** table gets classified as poison and
  dead-lettered, when it actually means "duplicate already processed."
  **Mitigation:** inbox dedup runs *before* the handler as a no-op pre-check (it
  does not surface as a thrown unique-violation) per
  [events-catalog.md §7](../bc-design/events-catalog.md); the classifier never
  sees it. Preserve that ordering when implementing.
- **Risk:** `IsTransient` mis-flags some command-timeout exceptions
  ([npgsql#2239](https://github.com/npgsql/npgsql/issues/2239)). **Mitigation:**
  accepted; the lag-alert + saga-timeout backstops cover a wrongly-retried
  message, and `TimeoutException` is intentionally retryable anyway.

## Implementation Notes

Tracked under [#247](https://github.com/DavidCapcuch/DotNetAtlas/issues/247)
(expanded from Payments-only to cross-BC).

### Shared (Platform)

- Add a `RetryableException` marker type (e.g. in
  `Platform.SharedKernel.Exceptions`, alongside `DataIntegrityException`) so
  Application/Infrastructure handlers can request a retry without a messaging
  dependency.
- Add the `IsRetryable(Exception)` predicate (e.g.
  `Platform.KafkaFlow.*`, referenced by each BC's `MessagingDependencyInjection`)
  encoding the allowlist above (`IsTransient` ∨ `RetryableException` ∨
  `TimeoutException`; never `OperationCanceledException`). Unit-test the
  transient/poison/marker/cancellation cases.

### Per-BC

- All 10 consumers: change `RetryForever(c => c.Handle<DbUpdateException>()...
  .Handle<TimeoutException>())` to `RetryForever(c => c.Handle(ctx =>
  ConsumerRetry.IsRetryable(ctx.Exception)).WithTimeBetweenTriesPlan(...))`.
  Keep the existing backoff plan and middleware order.
- **Payments**: revert the #247 `RetrySimple(TryTimes: 8)` to the unified
  classified `RetryForever`.
- **Inventory `OrderCancelledEventKafkaHandler`**: throw `RetryableException`
  (with the existing WARN log + idempotent re-query rationale) instead of
  rethrowing a synthesized `DbUpdateException`.

### Documentation

- Rewrite [kafka-dlt-strategy.md §2](../bc-design/kafka-dlt-strategy.md) to
  describe the classified-retry policy; update/retire F-5 (retry middleware is
  no longer "intentionally omitted").
- Reconcile [error-taxonomy.md](../bc-design/error-taxonomy.md)'s "errors → DLT
  vs business outcome" table with the transient/poison/business-expected split.
- Fix the three dangling references to the never-created
  `docs/runbooks/payments-dlt.md` (code comment in
  `Payments.Infrastructure/Common/MessagingDependencyInjection.cs`, the
  `PaymentCommandsDLTRoutingTests` docstring, and the
  `payments-followup-session1.md` session summary that falsely claims it was
  "authored") — repoint at `kafka-dlt-strategy.md`, which already covers the
  Payments DLT.

### Testing

- Unit-test `IsRetryable` (transient PG codes retry; `23*/22*/42*` + bare
  `DbUpdateException` → poison; `RetryableException` retries;
  `OperationCanceledException` excluded).
- Implement the previously-stubbed
  `PaymentCommandsDLTRoutingTests.PoisonCommand_AfterRetryExhaustion_LandsOnPaymentsPaymentCommandsDLT`
  — the `KafkaTestContainer` harness exists (see
  `Catalog.IntegrationTests/Common/IntegrationTestFixture.cs`); follow it rather
  than the obsolete "no wave1 BC has it" claim in the current placeholder. Assert
  a poison (`23505`) command lands on the DLT and a transient
  (`IsTransient`) failure does **not**.

### When to revisit

- If production noise justifies it, escalate to **Option 5** (circuit-breaker +
  health-gated pause) — a new ADR, additive to this one.
- If a future consumer genuinely needs out-of-order tolerance and high
  throughput under failure, **Option 3/4** (durable / non-blocking retry topics)
  may apply to that *specific* consumer — documented as a per-consumer exception,
  never the default.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — the saga-command topics whose ordering this policy protects.
- [ADR-0004: Checkout Saga Topology](0004-checkout-saga-topology.md) — saga state-timeout, the backstop for a misclassified blocked partition.
- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md) — event-sourced streams where ordering is non-negotiable; rules out non-blocking retry topics.
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — the "minimal infrastructure for a reference solution" posture behind driver 6.
- [ADR-0023: Payments Event-vs-Command Classification](0023-payments-event-vs-command-classification.md) — the prior Payments closeout wave that produced #247.
- [kafka-dlt-strategy.md](../bc-design/kafka-dlt-strategy.md) — the operational DLT strategy doc this ADR reconciles (§2 rewrite, F-5 retirement).
- [error-taxonomy.md](../bc-design/error-taxonomy.md) — DLT-vs-business-outcome routing, aligned to the failure-class split here.
