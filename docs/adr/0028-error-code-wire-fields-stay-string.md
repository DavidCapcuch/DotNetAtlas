# ADR-0028: `ErrorCode` wire fields stay `string` — reject the Avro `enum` migration

## Status

Accepted (2026-06-02)

> The *decision* (keep `string`) is Accepted. The *proposal this ADR evaluates* — migrating `ErrorCode` to an Avro `enum` ([#286](https://github.com/DavidCapcuch/DotNetAtlas/issues/286)) — is **rejected**.

## Context

Nine Avro records across four bounded contexts carry an `ErrorCode` field typed as `string` with an open ("e.g., …") vocabulary:

| Avro record | Producer BC | `ErrorCode` vocabulary (from `doc`) | Vocabulary class |
|---|---|---|---|
| `Payments.Transactions.PaymentFailedEvent` | Payments | `INSUFFICIENT_FUNDS`, `CARD_DECLINED`, `FRAUD_SUSPECTED` | **provider-sourced** |
| `Payments.Transactions.PaymentAuthorizationFailedEvent` | Payments | same provider set | **provider-sourced** |
| `Payments.Transactions.PaymentCaptureFailedEvent` | Payments | "from the payment provider" | **provider-sourced** |
| `Ordering.Orders.OrderFailedEvent` | Ordering | `STOCK_UNAVAILABLE`, `PAYMENT_FAILED`, `PAYMENT_TIMEOUT`, `CONFIRMATION_TIMEOUT` | internally-owned |
| `Ordering.Orders.MarkOrderFailedCommand` | Checkout saga → Ordering | same internal set | internally-owned |
| `Checkout.Sagas.CheckoutFailedEvent` | Checkout saga | `STOCK_UNAVAILABLE`, `PAYMENT_FAILED`, `ORDER_CREATION_TIMEOUT`, `CONFIRMATION_FAILED` | internally-owned |
| `Checkout.Sagas.CheckoutStuckEvent` | Checkout saga | always `COMPENSATION_TIMEOUT` | internally-owned |
| `Weather.Alerts.AlertSubscriptionActivationFailedEvent` | Weather | open | **out of scope** (see below) |
| `Weather.Alerts.AlertSubscriptionExtensionActivationFailedEvent` | Weather | open | **out of scope** (see below) |

[#286](https://github.com/DavidCapcuch/DotNetAtlas/issues/286) proposes migrating these `string` fields to Avro `enum` so renames break at compile time and consumers can `switch` exhaustively. It frames the recently-added owner-side constant classes — `CheckoutSagaErrorCodes`, `PaymentProcessingSagaErrorCodes` — as a *stopgap* that "tightens producer-side rename safety within one BC" while "the cross-BC problem remains," and argues the standard objection to Avro enums (schema-evolution cost) is collapsed by the repo being a non-production reference solution where breaking changes are allowed (per [ADR-0009](0009-reference-solution-target-profile.md), [CLAUDE.md](../../CLAUDE.md)).

This ADR evaluates that proposal against the actual code and rejects it.

### Ground truth (from code, not from the issue)

Three observations decide this, and all three came from reading the wired code rather than the schemas alone:

1. **Nothing branches on a wire `ErrorCode`.** Every cross-BC consumption is *forward-or-label*, never *control-flow*:
   - [`PaymentFailedCheckoutConsumer`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/Consumers/PaymentFailedCheckoutConsumer.cs) — the issue's own cited example of "intimate knowledge of an upstream BC's literal vocabulary" — copies `message.ErrorCode` verbatim into `PaymentFailedSagaEvent.ErrorCode` and logs it. No `switch`, no `==`, no comparison against any literal.
   - [`PaymentFailedCheckoutActivity`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/Observability/Activities/PaymentFailedCheckoutActivity.cs), `OrderFailedConsumer`, and the Payments-saga `*FailedActivity` set use `ErrorCode` only as an **OpenTelemetry trace tag** (`SetTag(SagaActivityTags.ErrorCode, …)`) and a **metric label** (`RecordPaymentFailed(message.ErrorCode)`).
   - The one `Contains`-style check that exists — Inventory's `BusinessExpectedErrorCodes.Contains(domainError.ErrorCode)` in [`SagaCommandHandlerBase`](../../services/Inventory/Inventory.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs) — is on an **in-process domain `Error`**, not on any Avro wire field.

   Avro `enum` buys compile-time exhaustiveness *only where code branches on the value*. Nothing branches. The benefit the migration is sold on does not exist at any consumer.

2. **The "string is a stopgap" premise is contradicted by the code's own documented intent.** The constant classes the issue calls a stopgap explicitly declare strings deliberate:
   > *(from [`CheckoutSagaErrorCodes`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/CheckoutSagaErrorCodes.cs))* "Codes propagated from upstream bounded contexts … are deliberately not listed here — the saga is a consumer of those vocabularies, not the owner, and reaching across BC boundaries to share a constant would be heavier than warranted. The Avro schemas type those fields as `string` on purpose (extensible vocabulary)."
   >
   > *(from [`PaymentProcessingSagaErrorCodes`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/PaymentProcessingSagaErrorCodes.cs))* "Codes propagated from upstream events (e.g. `CARD_DECLINED`, `CAPTURE_FAILED`, `GATEWAY_TIMEOUT` originating in the Payments BC's gateway adapter) are deliberately not listed here — the saga forwards them unchanged."

   The constants are not a half-step toward enums; they are the *owner-side* source of truth for the codes a BC *originates*, paired with a deliberate decision to treat *forwarded/external* codes as opaque strings.

3. **`ErrorCode` is persisted as `varchar`.** [`CheckoutSagaStateMap`](../../saga/SagaOrchestrators/Common/Persistence/Database/CheckoutSagaStateMap.cs) maps `error_code` as `HasMaxLength(64)`; [`PaymentProcessingSagaStateMap`](../../saga/SagaOrchestrators/Common/Persistence/Database/PaymentProcessingSagaStateMap.cs) the same; Ordering's [`OrderConfiguration`](../../services/Ordering/Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs) maps the owned `failure.ErrorCode` as `MaxLength(100)`. A migration touches three persisted columns plus an EF value-conversion for zero functional gain.

### One correction to the issue

The issue states Schema Registry compatibility is "currently `BACKWARD` by default." Per [ADR-0007](0007-avro-compatibility-modes.md) it is **not**: the `schema-registry-init` bootstrap sets a global `FORWARD_TRANSITIVE` default and per-subject `FORWARD_TRANSITIVE` (events) / `FULL_TRANSITIVE` (commands) by filename suffix. This *strengthens* the rejection — see Driver 3.

## Decision Drivers (ranked)

1. **Real benefit at the point of use.** A type change is only worth a contract migration if some code is made safer or clearer by it. The benefit must land at a consumer that branches, not at one that forwards or labels.
2. **Producer-intent and ownership fidelity.** The wire type should reflect who owns the vocabulary. A field whose values originate in an external payment gateway is not a closed set this system controls.
3. **Avro evolution cost under the *real* compatibility modes.** Whatever we choose must survive `FORWARD_TRANSITIVE`/`FULL_TRANSITIVE` and the suffix-driven registry bootstrap, not the `BACKWARD` mode the issue assumed.
4. **Cross-BC coupling direction.** The change must not reintroduce the producer↔consumer lockstep the string design was explicitly chosen to avoid.
5. **Reference-value.** As a teaching repo ([ADR-0009](0009-reference-solution-target-profile.md)), the codebase should model the *correct* pattern for open cross-service failure vocabularies — not demonstrate enums where they are the wrong tool just because breaking changes are cheap here.

## Considered Options

### Option 1: Keep `string`; ratify the owner-side constants as the resting state (chosen)

`ErrorCode` stays `string` on every schema. The producing BC owns its originated codes as constants (`CheckoutSagaErrorCodes`, `PaymentProcessingSagaErrorCodes`); forwarded/provider codes stay opaque strings. This ADR records *why*, so the "why not enum?" question stops recurring.

**Pros:**
- Matches how the codes are actually used (forward + telemetry-label) — no consumer loses anything.
- Provider-sourced codes remain honestly modelled as an open vocabulary the system does not own.
- Zero contract migration, zero DB migration, no registry reconfiguration.
- Keeps the constants, which already give producer-side rename safety *within the owning BC* — the only place a rename can actually be checked.

**Cons:**
- A cross-BC rename of an *internally-owned* code (e.g. `STOCK_UNAVAILABLE`) is still only caught by tests, not the compiler. Accepted: those values are wire contracts (documented as such in the constant classes), and no consumer branches on them anyway.

### Option 2: Full `enum` migration (the #286 proposal)

Every `ErrorCode` becomes an Avro `enum`; generated C# stubs become real enums; constants deleted.

**Pros:**
- Compile-time rename breakage and exhaustive `switch` — *if* anything switched.

**Cons:**
- **Provider codes can't be an enum.** `PaymentFailedEvent.ErrorCode` carries `CARD_DECLINED` / `FRAUD_SUSPECTED` / `GATEWAY_TIMEOUT` straight from the gateway adapter. The system does not own that set; encoding it as a closed enum is a lie that breaks the first time the gateway returns a code we didn't enumerate.
- **Enum evolution is *worse* than string under our real modes.** Under `FORWARD_TRANSITIVE`, a producer that adds a new symbol emits messages an un-redeployed consumer cannot decode unless the enum declares a `default`; with a `default`, every new failure category silently collapses into the default at old consumers. For a vocabulary the docs explicitly mark open ("e.g., …"), that is a regression from `string`, which carries the exact value forever.
- **Breaks the registry bootstrap contract.** [ADR-0007](0007-avro-compatibility-modes.md)'s bootstrap classifies every `.avsc` by `*Event`/`*Command` suffix and **fails with exit-1** on any other suffix. A shared `FailureCode.avsc` enum has neither suffix → bootstrap breakage; the only alternative is inlining (and duplicating) the enum type in every event.
- **Reintroduces cross-BC lockstep** the string design avoids: a consumer's generated enum must be regenerated and redeployed to even render a producer's new symbol on a metric label.
- Touches three persisted `varchar` columns + EF conversions.
- "Breaking changes are free here" answers *can we?*, not *should we?* — Driver 5 says model the right pattern.

### Option 3: Hybrid `{ FailureCategory: enum, ErrorCode: string }`

Add a bounded `FailureCategory` enum for routing/metrics; keep `ErrorCode` string for forensics.

**Pros:**
- Honestly separates the closed "category" dimension from the open "specific code" dimension — the theoretically cleanest model.

**Cons:**
- **No consumer needs the category today.** Adding a field with no reader is speculative generality; the same enum-evolution and bootstrap problems as Option 2 apply to the `FailureCategory` half.
- Widens every failure schema for a benefit that is currently zero.
- Can be adopted later with no loss if a real branching consumer appears — so building it now is pure YAGNI.

## Evaluation Matrix

| Driver (ranked) | Option 1: Keep string (chosen) | Option 2: Full enum | Option 3: Hybrid |
|---|---|---|---|
| 1. Real benefit at point of use | ✅ matches forward+label usage | ❌ no consumer branches | ❌ no consumer reads a category |
| 2. Producer-intent / ownership | ✅ provider codes stay open | ❌ closed enum misrepresents gateway codes | ⚠️ category honest, but unused |
| 3. Avro evolution under real modes | ✅ string is evolution-free | ❌ new symbol unreadable / silently defaulted; suffix-gate breakage | ❌ same on the enum half |
| 4. Cross-BC coupling | ✅ no lockstep | ❌ regen+redeploy to render new symbol | ⚠️ partial lockstep |
| 5. Reference-value | ✅ models the right pattern for open vocab | ❌ models enums where they're the wrong tool | ⚠️ teaches a split few systems need |

## Decision

`ErrorCode` **stays `string`** on all schemas. We **reject** the Avro `enum` migration (#286).

- The owner-side constant classes (`CheckoutSagaErrorCodes`, `PaymentProcessingSagaErrorCodes`) **stay** — they are the ratified source of truth for the codes each BC *originates*, not a stopgap. They are **not** deleted (reversing that DoD item from #286).
- Codes a BC *forwards* (provider/gateway codes, or upstream codes a saga relays) remain opaque strings — the consumer is not the owner of those vocabularies.
- The door to a **narrow, internally-owned `FailureCategory` enum** is left open *only* if a future consumer genuinely needs to branch on a closed category (Option 3, adopted lazily). Until such a consumer exists, YAGNI.

## Consequences

### Positive

- No schema, registry, or database change; the codebase already embodies the decision.
- Provider-sourced failure vocabularies stay honestly open-ended.
- The recurring "why is this a string and not an enum?" question now has a durable answer.

### Negative

- Cross-BC renames of internally-owned codes remain test-caught, not compiler-caught. The constant classes' XML docs already flag these values as stable wire contracts to compensate.

### Risks

- **Risk:** a future contributor re-opens the enum migration unaware of this analysis. **Mitigation:** this ADR, plus a one-line pointer added to the two constant classes' remarks.
- **Risk:** an internally-owned category eventually *does* need consumer branching. **Mitigation:** Option 3 is pre-analysed here and can be added for that one field without disturbing the others.

## Implementation Notes

- **No code change is required by this decision.** It ratifies the current state.
- Add a one-line `// See ADR-0028` pointer to the remarks on `CheckoutSagaErrorCodes` and `PaymentProcessingSagaErrorCodes` so the rejection is discoverable from the code that prompted the question. (Optional, low-priority.)
- The two **Weather** schemas (`AlertSubscription*ActivationFailedEvent`) were out of scope: `src/Weather` was reference scaffolding, removed in #318 (final Notifications v2 cleanup). They stayed `string`; no investment was made.
- If Option 3 is ever taken: add `FailureCategory` as a field **inline** in the specific event (not a shared `*.avsc`, which would fail the ADR-0007 suffix-gate), declare an enum `default` symbol (e.g. `UNKNOWN`) so `FORWARD_TRANSITIVE` evolution stays safe, and add it only to the event whose consumer needs to branch.

## Related Decisions

- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — `FORWARD_TRANSITIVE`/`FULL_TRANSITIVE` and the suffix-driven bootstrap are why enum symbol evolution here is worse than `string`, and why a standalone enum schema can't exist.
- [ADR-0009: Reference-Solution Target Profile](0009-reference-solution-target-profile.md) — "breaking changes are allowed" answers *can we migrate*, not *should we*; Driver 5 turns the reference-value argument against the migration.
- [ADR-0023: Payments Event-vs-Command Classification](0023-payments-event-vs-command-classification.md) — same discipline of reading ground-truth consumer wiring before changing a contract; established that renames are breaking subject migrations.
- [ADR-0026: Checkout payment flow — capture pivot](0026-checkout-payment-flow-capture-pivot.md) — established that Payments owns the terminal `PaymentFailedEvent`; the saga forwards provider codes unchanged, which is why those codes are not saga-owned constants.
