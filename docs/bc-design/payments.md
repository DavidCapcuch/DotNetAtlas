# Payments Bounded Context

> **Status:** Authored 2026-04-19. Extracted from `eshop-master-design.md § 5.5` + [ADR-0001](../adr/0001-centralized-saga-orchestration.md) + [ADR-0004](../adr/0004-checkout-saga-topology.md) to match the chapter structure used by Catalog, Basket, Ordering, Inventory.
> **Scope:** Payment transaction lifecycle — authorize, capture, refund, void. Integrates with a payment gateway (stubbed for the reference solution).
> **Pattern showcased:** **Saga sub-orchestration** — `PaymentProcessingSaga` is a standalone orchestrator the Checkout saga delegates to via `PaymentRequestedEvent`. Also: **PCI scope minimization** — cardholder data (PAN, CVV) never enters our services; Payments holds gateway-issued `PaymentTransactionId` tokens only.
> **Storage:** PostgreSQL, schema `payments`.
> **Folder:** `services/Payments/` (renamed from `services/Payments/` in Wave 0).

---

## 1. Purpose & Role in the System

Payments is the **authority for money movement state** — it is the only BC that speaks to the external payment gateway. It receives commands from the Checkout saga (via the `PaymentProcessingSaga` sub-saga) and emits events that drive saga transitions, trigger Invoicing, and notify customers.

- **Upstream:** Checkout saga — publishes `PaymentRequestedEvent` → consumed by `PaymentProcessingSaga`, which calls Payments commands.
- **Downstream:**
  - **Invoicing** — consumes `PaymentCapturedEvent` to enrich and issue the invoice.
  - **Notifications** — consumes `PaymentRefundedEvent` to email the buyer a refund confirmation.
  - **Checkout saga** — consumes `PaymentCompletedEvent` / `PaymentFailedEvent` (terminal signals from the sub-saga) to advance or compensate.

The distinction between Payments and PaymentProcessingSaga is deliberate:
- **Payments BC** owns the aggregate (`PaymentTransaction`), the DB schema, and the gateway client. Pure CRUD-ish around a small state machine.
- **PaymentProcessingSaga** (under `saga/SagaOrchestrators/Payments/PaymentProcessingSaga/`) orchestrates the authorize → capture → (optional refund/void) flow across timeouts and retries. It is **the only caller** of Payments commands.

---

## 2. Aggregate: `PaymentTransaction`

One aggregate, keyed by `PaymentId : Guid` (UUID v7). The aggregate wraps a single saga-scoped payment lifecycle; once terminal (`Completed`, `Failed`, `Refunded`, `Voided`), no further mutations are permitted.

### 2.1 Properties

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Aggregate root identity, UUID v7 for time-sortable storage |
| `CorrelationId` | `Guid` | The originating saga CorrelationId (links to checkout, order, invoice) |
| `BuyerId` | `Guid` | JWT `sub` at checkout time; frozen |
| `OrderId` | `Guid` | The Ordering aggregate this payment belongs to |
| `Amount` | `Money` | Immutable after `Authorized` |
| `PaymentMethodId` | `string` | Gateway-issued token; never a raw PAN |
| `Status` | `PaymentStatus` (SmartEnum) | `Requested → Authorized → Captured → Completed`, off-ramps `Failed`, `Refunded`, `Voided` |
| `GatewayTransactionId` | `string?` | Gateway's transaction reference; null until first successful gateway call |
| `GatewayResponseCode` | `string?` | Last known gateway code (success or specific failure reason) |
| `AuthorizedAtUtc` | `DateTimeOffset?` | Set on `Authorized` transition |
| `CapturedAtUtc` | `DateTimeOffset?` | Set on `Captured` |
| `RefundedAtUtc` | `DateTimeOffset?` | Set on `Refunded` |
| `VoidedAtUtc` | `DateTimeOffset?` | Set on `Voided` |
| `FailureInfo` | `FailureInfo?` | Populated on any terminal failure — reason + gateway code |
| `RowVersion` | `uint` | Optimistic concurrency token |

### 2.2 Invariants

- **I-1** `Amount.Amount > 0` always. Enforced in factory.
- **I-2** `Currency` follows ISO 4217; single-currency per payment (v1 constraint).
- **I-3** `Status` transitions are guarded by `PaymentStatus.CanTransitionTo(target)` — invalid transitions throw `DataIntegrityException` (bug-class, see `error-taxonomy.md § 3.5`).
- **I-4** `GatewayTransactionId` is append-only — once set, it never changes (even on refund/void, which reuse the same gateway transaction).
- **I-5** Once `Status ∈ { Completed, Failed, Refunded, Voided }`, all mutations are rejected at the aggregate root. Saga retries become idempotent no-ops.
- **I-6** `CorrelationId`, `BuyerId`, `OrderId` are immutable post-creation.

### 2.3 Factory

```csharp
public static Result<PaymentTransaction> Create(
    Guid paymentId,
    Guid correlationId,
    Guid buyerId,
    Guid orderId,
    Money amount,
    string paymentMethodId,
    DateTimeOffset now);
```

Returns `Result.Fail(PaymentsErrors.InvalidAmount)` if `amount.Amount <= 0`, and `Result.Fail(PaymentsErrors.InvalidPaymentMethod)` if the payment method token is empty or exceeds 64 chars.

Raises `PaymentRequestedDomainEvent`.

---

## 3. Value Objects

Live in `Payments.Domain.ValueObjects`. Immutable `sealed record` with private ctor + `Create → Result<T>` factory.

| VO | Fields | Notes |
|---|---|---|
| `Money` | shared-kernel (`Platform.SharedKernel.ValueObjects.Money`) | Positive amount + ISO 4217 currency |
| `PaymentMethodId` | `Value : string` | Gateway-issued token; 1–64 chars; no raw PAN |
| `GatewayResponseCode` | `Code : string`, `Message : string` | Enriched from gateway response |
| `FailureInfo` | `Reason : FailureReason` (SmartEnum), `GatewayCode : string?`, `RecordedAtUtc : DateTimeOffset` | Terminal-failure detail |

`FailureReason` SmartEnum: `GatewayDeclined`, `GatewayTimeout`, `InsufficientFunds`, `FraudSuspected`, `Cancelled`, `Unknown`.

---

## 4. `PaymentStatus` SmartEnum

```
Requested ──authorize──▶ Authorized ──capture──▶ Captured ──complete──▶ Completed
    │                         │                                          
    │                         └──void──▶ Voided (terminal)               
    └──reject──▶ Failed (terminal)      
                                                                         
Captured ──refund──▶ Refunded (terminal)                                 
```

### 4.1 Transitions

| From | Event | To | Trigger |
|---|---|---|---|
| Requested | `AuthorizePaymentCommand` success | Authorized | Gateway auth succeeded |
| Requested | `AuthorizePaymentCommand` failure | Failed | Gateway declined or timeout |
| Authorized | `CapturePaymentCommand` success | Captured | Gateway capture succeeded |
| Authorized | `CapturePaymentCommand` failure | Failed | Rare; gateway capture failure |
| Authorized | `VoidPaymentCommand` | Voided | Saga compensation pre-capture |
| Captured | (auto) | Completed | All steps complete |
| Captured | `RequestRefundCommand` | Refunded | Cancel-post-capture compensation |

`Completed` is the successful terminal state. `Failed`, `Refunded`, `Voided` are compensation terminals.

Transition guard: `PaymentStatus.CanTransitionTo(target)` consults a readonly `_allowed` dictionary. Invalid transitions throw `DataIntegrityException` (bug-class).

---

## 5. Domain Events (internal, 8)

Raised by the aggregate; dispatched in-process via `IDomainEventHandler<T>`. Never published to Kafka directly — external events are translated by outbox publishers.

- `PaymentRequestedDomainEvent` — aggregate created
- `PaymentAuthorizedDomainEvent` — gateway auth success
- `PaymentAuthorizationFailedDomainEvent` — gateway auth failure
- `PaymentCapturedDomainEvent` — gateway capture success
- `PaymentCaptureFailedDomainEvent` — rare; capture failure after successful auth
- `PaymentCompletedDomainEvent` — aggregate reaches `Completed`
- `PaymentRefundedDomainEvent` — aggregate reaches `Refunded`
- `PaymentVoidedDomainEvent` — aggregate reaches `Voided`
- `PaymentFailedDomainEvent` — aggregate reaches `Failed` (any path)

---

## 6. External Events (Avro) + Topics

**Topic:** `payments.transactions` — infinite retention (audit), partition key `CorrelationId`.

| External event | Triggered by | Consumer(s) |
|---|---|---|
| `PaymentRequestedEvent` | `PaymentRequestedDomainEvent` | PaymentProcessingSaga |
| `PaymentAuthorizedEvent` | `PaymentAuthorizedDomainEvent` | PaymentProcessingSaga |
| `PaymentAuthorizationFailedEvent` | `PaymentAuthorizationFailedDomainEvent` | PaymentProcessingSaga |
| `PaymentCapturedEvent` | `PaymentCapturedDomainEvent` | PaymentProcessingSaga, **Invoicing** (enrichment trigger) |
| `PaymentCaptureFailedEvent` | `PaymentCaptureFailedDomainEvent` | PaymentProcessingSaga |
| `PaymentCompletedEvent` | `PaymentCompletedDomainEvent` | Checkout saga (drives `AwaitingConfirmation → Confirmed`) |
| `PaymentFailedEvent` | `PaymentFailedDomainEvent` | Checkout saga (drives compensation) |
| `PaymentRefundedEvent` | `PaymentRefundedDomainEvent` | Checkout saga (cancel-post-capture confirmation), Notifications, **Invoicing** (credit-note trigger) |
| `PaymentVoidedEvent` | `PaymentVoidedDomainEvent` | PaymentProcessingSaga |

**Schema compatibility:** FORWARD_TRANSITIVE per [ADR-0007](../adr/0007-avro-compatibility-modes.md).

**Known classification debt:** several `*Event`-named messages have exactly one consumer (PaymentProcessingSaga) and per master-design § 3.5 are really commands. The **Checkout saga agent** has explicit authority to propose renames (`PaymentRequestedEvent` → `RequestPaymentCommand` on a new `payments.commands` topic). Proposals surface in the session summary; user approval required before implementation.

---

## 7. Commands (Avro) + Command Topic

**Topic:** `payments.commands` (formerly `payments.payment-commands` — renamed in Wave 0) — 7-day retention, partition key `CorrelationId`.

| Command | Producer | Consumer | Trigger |
|---|---|---|---|
| `AuthorizePaymentCommand` | PaymentProcessingSaga | Payments | Checkout saga → auth step |
| `CapturePaymentCommand` | PaymentProcessingSaga | Payments | After `PaymentAuthorized`, immediately capture (v1 single-step flow) |
| `RequestRefundCommand` | PaymentProcessingSaga (Checkout compensation) | Payments | Cancel-post-capture path |
| `VoidPaymentCommand` | PaymentProcessingSaga | Payments | Compensation pre-capture |

**Schema compatibility:** FULL_TRANSITIVE.

---

## 8. HTTP API (admin-only)

All routes under `/api/v1/payments/`. Public API is zero — Payments is command-driven from Kafka. Admin endpoints exist for ops:

| Endpoint | Use case | Authorization |
|---|---|---|
| `GET /api/v1/payments/{id}` | Ops lookup by PaymentId | `AuthPolicies.PaymentsAdmin` |
| `GET /api/v1/payments?orderId=…` | Ops lookup by order | `AuthPolicies.PaymentsAdmin` |

No POST/PATCH/DELETE HTTP endpoints for Payments aggregate mutation — saga commands only.

---

## 9. Use Cases (summary)

Full use-case catalog in [`use-cases.md § 5`](use-cases.md) (new § added in the Payments BC authoring wave).

### 9.1 Commands (Kafka-driven)

- `AuthorizePaymentCommandHandler` — loads aggregate by `PaymentId`, calls `IPaymentGateway.Authorize(...)`, transitions aggregate, raises domain event.
- `CapturePaymentCommandHandler` — loads aggregate, calls gateway capture, transitions.
- `RequestRefundCommandHandler` — loads aggregate in `Captured`, calls gateway refund, transitions.
- `VoidPaymentCommandHandler` — loads aggregate in `Authorized`, calls gateway void, transitions.

### 9.2 Queries (HTTP admin)

- `GetPaymentByIdQuery(PaymentId)` → `PaymentDto`
- `GetPaymentsByOrderQuery(OrderId)` → `IReadOnlyList<PaymentDto>`

---

## 10. Integration with Payment Gateway

**Abstraction:** `IPaymentGateway` (port in `Payments.Application`, adapter in `Payments.Infrastructure`).

```csharp
public interface IPaymentGateway
{
    Task<Result<AuthorizeResponse>> AuthorizeAsync(PaymentTransaction tx, CancellationToken ct);
    Task<Result<CaptureResponse>> CaptureAsync(string gatewayTransactionId, Money amount, CancellationToken ct);
    Task<Result<RefundResponse>> RefundAsync(string gatewayTransactionId, Money amount, string reason, CancellationToken ct);
    Task<Result<VoidResponse>> VoidAsync(string gatewayTransactionId, CancellationToken ct);
}
```

**v1 adapter:** `StubPaymentGateway` — deterministic responses based on amount last digit (e.g., `.99 → decline`) for integration-test predictability. Production would swap in a real adapter (Stripe, Adyen, Braintree) via DI.

**PCI scope minimization (teaching point):** the reference solution demonstrates the **token-only pattern** — the stub gateway returns a `GatewayTransactionId` token on first call; all subsequent operations reference the token. No PAN, CVV, or full card number ever enters Payments or any other service. In production this is how PCI DSS scope is narrowed to just the payment form page (served by the gateway) and the aggregate's token column.

---

## 11. Error Classes

Single source of truth: [`error-taxonomy.md § 3.5`](error-taxonomy.md) (`PaymentsErrors` row set + C# sketch). Do not duplicate.

Key user/business errors:
- `PaymentsErrors.PaymentNotFound(Guid paymentId)` — 404 on admin lookup
- `PaymentsErrors.GatewayDeclined(string reason)` — business-expected; saga converts to `PaymentFailedEvent`
- `PaymentsErrors.InvalidPaymentMethod` — factory validation
- `PaymentsErrors.InvalidAmount` — factory validation

FSM guard violations (bug-class) throw `DataIntegrityException`, not `Result.Fail`.

---

## 12. Observability

- `PaymentsActivitySource` — OTel `ActivitySource` for tracing. Every command handler opens a root span tagged with `Payment.Id`, `Payment.Amount.Amount`, `Payment.Amount.Currency`, `Payment.Status`.
- Metrics (via `System.Diagnostics.Metrics`):
  - `payments.authorize.count` (counter) tagged `outcome=success|declined|gateway-error`
  - `payments.capture.count` (counter) tagged `outcome=success|failed`
  - `payments.refund.count` (counter)
  - `payments.gateway.latency.seconds` (histogram) tagged `operation=authorize|capture|refund|void`
- Correlation-ID propagation: per ADR-0008 (when authored) — `X-Correlation-Id` HTTP header → Kafka message header → MDC on inbox consumers.
- **PII rule:** never tag span attributes with `PaymentMethodId`, `BuyerId`, or `GatewayTransactionId`. Use a hashed token if cardinality matters.

---

## 13. Storage

- Schema `payments` (Postgres).
- Tables: `payments.transactions` (one row per `PaymentTransaction`), `payments.outbox_messages`, `payments.inbox_messages` (standard `Platform.ReliableMessaging.Outbox/Inbox.EFCore` shape).
- All timestamps persisted as `timestamptz`; domain types are `DateTimeOffset` per ADR-0015 (when authored).
- Concurrency: explicit `RowVersion : uint` — shared-kernel convention.

---

## 14. Testing Strategy

- **Unit tests** (`test/Payments.UnitTests/`) — `PaymentTransaction` state transitions, factory validation, invariants (I-1 through I-6), SmartEnum transition table.
- **Architecture tests** (`test/Payments.ArchitectureTests/`) — no cross-BC references; no direct `StackExchange.Redis` imports in `Payments.Domain`; aggregates have private ctor + static factory; enforced `*DomainEvent` suffix on internal events.
- **Integration tests** (`test/Payments.IntegrationTests/`) — Testcontainers Postgres + Kafka. Tests the outbox publisher chain: command → domain event → outbox row → Kafka message (with Avro schema registry stub).
- **Functional tests** (`test/Payments.FunctionalTests/`) — `WebApplicationFactory`-based full-stack admin HTTP endpoints with auth.
- **Gateway stub** (`StubPaymentGateway`) — deterministic responses in test mode; swap via DI using options.

Full example-mapping sessions in [`example-mapping/payments.md`](example-mapping/payments.md).

---

## 15. Out of scope for v1

Listed so readers don't look for them:

- **3-D Secure** (SCA) — deferred; all stub authorizations succeed without 3DS step. Real gateway integration would add this.
- **Partial captures / partial refunds** — single amount per transaction in v1.
- **Multi-currency** — single currency per payment (matches Order-level single-currency constraint).
- **Chargebacks / disputes** — event hook left open (`PaymentDisputedEvent` could be added in v2); not modelled in v1.
- **Stored payment methods / tokenization vault** — the gateway holds the vault; Payments holds only per-transaction tokens.
- **Reconciliation jobs** — v2 would run a nightly job comparing gateway ledger to our `payments.transactions` table.

---

## 16. Relationship to Checkout Saga (for orientation)

The Checkout saga's payment step is delegated in full to `PaymentProcessingSaga`:

```
Checkout saga   ─PaymentRequestedEvent─▶   PaymentProcessingSaga   ─AuthorizePaymentCommand─▶   Payments BC
                                                    │                                               │
                                                    │   (capture is automatic post-auth)            │
                                                    │                                               │
                Checkout saga   ◀─PaymentCompletedEvent─   PaymentProcessingSaga   ◀─PaymentCaptured/Failed─┘
```

Payments BC has no knowledge of the Checkout saga directly — it only sees commands from `PaymentProcessingSaga` and emits events consumed by both sagas. Clean separation per ADR-0001.

---

*End of Payments BC design.*
