# Payments — Example Mapping Sessions

> Format: Matt Wynne's Example Mapping (Story / Rules / Examples / Questions) with BDD Given/When/Verify/Then for Examples. Each session corresponds to one non-trivial business rule or invariant on the `PaymentTransaction` aggregate. These sessions are the seed for executable acceptance-test specs during implementation.

---

## Session 1 — Authorize-then-capture happy path

**Story:** As the Checkout saga, I need payment authorized and captured so that stock confirmation can proceed and the order can be `Confirmed`.

### Rules

- **R1** — Authorization precedes capture. An aggregate in `Requested` cannot transition directly to `Captured`.
- **R2** — On successful authorize, `PaymentAuthorizedDomainEvent` is raised with a non-null `GatewayTransactionId`.
- **R3** — On successful capture, `PaymentCapturedDomainEvent` is raised followed immediately by `PaymentCompletedDomainEvent` (v1 auto-complete on capture).
- **R4** — `PaymentCapturedEvent` (external) is consumed by Invoicing — do not break this contract.

### Example 1.1 — Authorize + capture a valid $100 payment

- **Given** `PaymentTransaction` created with `Amount = $100 USD`, `Status = Requested`, valid `PaymentMethodId`
- **When** `AuthorizePaymentCommand` is handled and the stub gateway returns success with `GatewayTransactionId = "gw-tx-abc123"`
- **Then** `Status → Authorized`, `GatewayTransactionId = "gw-tx-abc123"`, `AuthorizedAtUtc` is set
- **And** `PaymentAuthorizedDomainEvent` is raised
- **Verify** outbox contains one `PaymentAuthorizedEvent` row on topic `payments.transactions`
- **When** `CapturePaymentCommand` is handled and the gateway returns success
- **Then** `Status → Captured → Completed` (auto-complete), `CapturedAtUtc` is set
- **And** `PaymentCapturedDomainEvent` + `PaymentCompletedDomainEvent` are raised (in that order)
- **Verify** outbox contains `PaymentCapturedEvent` + `PaymentCompletedEvent` on topic `payments.transactions`

### Example 1.2 — Skipping authorize is forbidden

- **Given** `PaymentTransaction` in `Status = Requested`
- **When** `CapturePaymentCommand` is handled directly (no authorize first)
- **Then** `DataIntegrityException` is thrown (bug-class — saga has an ordering bug)
- **Verify** no domain events raised; aggregate state unchanged

### Questions

- *(empty — resolved)* Should capture be a separate gateway call or bundled? **Decision v1:** two separate gateway calls, auto-chained. Keeps the state machine explicit; matches how real gateways expose the API; demonstrable in integration tests.

---

## Session 2 — Gateway decline on authorize

**Story:** As the Checkout saga, I need to know when payment is declined so I can compensate stock reservation and fail the order.

### Rules

- **R1** — A gateway decline at authorize is a **business-expected failure**, not a bug. The handler returns `Result.Fail(PaymentsErrors.GatewayDeclined(reason))`.
- **R2** — The aggregate transitions to `Failed` with a populated `FailureInfo`.
- **R3** — `PaymentFailedEvent` (external) is published with `FailureInfo.Reason` — the Checkout saga uses this to select the compensation branch.
- **R4** — A declined aggregate is terminal. Any subsequent command (even retries) is rejected.

### Example 2.1 — Gateway returns `insufficient_funds`

- **Given** `PaymentTransaction` with `Amount = $9.99 USD` (stub gateway rule: amount ending `.99` declines)
- **When** `AuthorizePaymentCommand` is handled
- **Then** the gateway returns `Result.Fail(GatewayDeclinedError("insufficient_funds"))`
- **And** aggregate `Status → Failed`, `FailureInfo.Reason = InsufficientFunds`, `FailureInfo.GatewayCode = "insufficient_funds"`
- **And** `PaymentFailedDomainEvent` is raised
- **Verify** outbox contains `PaymentFailedEvent` with `Reason = InsufficientFunds`
- **Verify** `PaymentAuthorizedDomainEvent` is NOT raised (no auth success)
- **Verify** handler returns `Result.Ok` (the command succeeded — the decline is a business outcome, not a handler error)

### Example 2.2 — Retry on a declined aggregate is idempotent

- **Given** `PaymentTransaction` in `Status = Failed` (from Example 2.1)
- **When** the same `AuthorizePaymentCommand` is replayed (saga retry)
- **Then** the handler loads the aggregate, sees terminal status, returns `Result.Ok` without calling the gateway
- **Verify** no new domain events raised
- **Verify** no new outbox rows
- **Verify** gateway is NOT called again (check via stub spy)

### Questions

- *(empty)*

---

## Session 3 — Compensation via void (pre-capture) vs refund (post-capture)

**Story:** As the Checkout saga, when I need to cancel a payment, I want the cheapest reversal — void before the money moves, refund if it already did.

### Rules

- **R1** — Void is only valid from `Authorized`. From `Captured` onwards, void is rejected — refund is required.
- **R2** — A voided aggregate sets `VoidedAtUtc` and raises `PaymentVoidedDomainEvent` + `PaymentRefundedDomainEvent` (the Checkout saga treats both terminal-reversal events uniformly, so v1 emits `PaymentRefundedEvent` externally in both cases for saga simplicity).
- **R3** — Refund is only valid from `Captured`. From `Refunded` / `Voided` / `Failed`, it is rejected as a bug-class FSM violation.

### Example 3.1 — Void before capture (saga compensation pre-capture)

- **Given** `PaymentTransaction` in `Status = Authorized` with `GatewayTransactionId = "gw-tx-xyz"`
- **When** `VoidPaymentCommand` is handled and the gateway returns void success
- **Then** `Status → Voided`, `VoidedAtUtc` is set
- **And** `PaymentVoidedDomainEvent` is raised
- **Verify** no money movement; the gateway `Void` call was invoked with `gw-tx-xyz`
- **Verify** outbox contains a terminal-reversal event the saga can consume

### Example 3.2 — Refund after capture (saga compensation post-capture)

- **Given** `PaymentTransaction` in `Status = Captured` → `Completed` with `GatewayTransactionId = "gw-tx-xyz"`
- **When** `RequestRefundCommand` is handled (saga's cancel-post-capture compensation)
- **Then** the gateway refund call succeeds; `Status → Refunded`, `RefundedAtUtc` is set
- **And** `PaymentRefundedDomainEvent` is raised
- **Verify** outbox contains `PaymentRefundedEvent` — consumed by both Checkout saga (compensation confirmation) and Invoicing (credit-note trigger)

### Example 3.3 — Void attempted post-capture is a bug

- **Given** `PaymentTransaction` in `Status = Completed` (captured)
- **When** `VoidPaymentCommand` is handled
- **Then** `DataIntegrityException` is thrown — the saga should have issued a refund command, not a void
- **Verify** no gateway call, no state change, no events

### Questions

- *(empty)* Should `PaymentVoidedEvent` and `PaymentRefundedEvent` be collapsed to one terminal-reversal event? **Decision v1:** keep distinct for observability (they're operationally different on gateway ledgers), but the Checkout saga treats them identically in its state machine.

---

*End of Payments example mapping — 3 sessions.*
