# Payments Bounded Context

> **Status:** Authored 2026-04-19. Extracted from `eshop-master-design.md § 5.5` + [ADR-0001](../adr/0001-centralized-saga-orchestration.md) + [ADR-0004](../adr/0004-checkout-saga-topology.md) to match the chapter structure used by Catalog, Basket, Ordering, Inventory.
> **Scope:** Payment transaction lifecycle — authorize, capture, refund, void. Integrates with a payment gateway (stubbed for the reference solution).
> **Pattern showcased:** **Saga sub-orchestration** — `PaymentProcessingSaga` is a standalone orchestrator the Checkout saga delegates to via `RequestPaymentCommand` on `payments.payment-commands` (renamed from `PaymentRequestedEvent` per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)). Also: **PCI scope minimization** — cardholder data (PAN, CVV) never enters our services; Payments holds gateway-issued `PaymentTransactionId` tokens only.
> **Storage:** PostgreSQL, schema `payments`.
> **Folder:** `services/Payments/` (renamed from `services/Finance/` in Wave 0).

---

## 1. Purpose & Role in the System

Payments is the **authority for money movement state** — it is the only BC that speaks to the external payment gateway. It receives commands from the Checkout saga (via the `PaymentProcessingSaga` sub-saga) and emits events that drive saga transitions, trigger Invoicing, and notify customers.

- **Upstream:** Checkout saga — publishes `RequestPaymentCommand` on `payments.payment-commands` (renamed from `PaymentRequestedEvent` per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)) → consumed by `PaymentProcessingSaga`, which calls Payments commands.
- **Downstream:**
  - **Invoicing** — consumes `PaymentCapturedEvent` to enrich and issue the invoice.
  - **Notifications** — not wired in v1. A refund-confirmation email would route via the command-driven pattern in [notifications.md § 2](notifications.md) — Payments would emit a `NotifyUserCommand` (v2; [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)) rather than have Notifications subscribe to `payments.transactions`.
  - **Checkout saga** — consumes `PaymentAuthorizedEvent` (to drive order + reservation confirmation) and the Payments-owned terminals `PaymentCompletedEvent` / `PaymentFailedEvent` (to finalize or fast-fail compensate). All three are published by the Payments BC's outbox, not by the sub-saga (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)).

The distinction between Payments and PaymentProcessingSaga is deliberate:
- **Payments BC** owns the aggregate (`PaymentTransaction`), the DB schema, and the gateway client. Pure CRUD-ish around a small state machine. It publishes **all** its lifecycle integration events — including the terminals `PaymentCompletedEvent` / `PaymentFailedEvent` — via its own transactional outbox.
- **PaymentProcessingSaga** (under `saga/SagaOrchestrators/Payments/PaymentProcessingSaga/`) orchestrates the authorize → await capture approval → capture flow across timeouts and retries, with `Void` as the pre-capture compensation path. It **sends commands and reacts to events only — it publishes no payment-state events** (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)). It is **the only caller** of Payments' authorize/capture/void commands. (Refund is a deferred standalone flow, not sub-saga-driven — see § 6 / § 7.)

---

## 2. Aggregate: `PaymentTransaction`

One aggregate, keyed by `PaymentId : Guid` (UUID v7). The aggregate wraps a single saga-scoped payment lifecycle; once terminal (`Completed`, `Failed`, `Refunded`, `Voided`), no further mutations are permitted.

### 2.1 Properties

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Aggregate root identity — UUID v7 (time-sortable). Minted by the saga and carried on `AuthorizePaymentCommand` as `PaymentTransactionId`; **distinct from `CorrelationId`** (see I-7). |
| `CompletedAtUtc` | `DateTimeOffset?` | Set on auto-advance from `Captured`. |
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
| `VoidReason` | `string?` | Saga-supplied reason on `Voided` (Wave-1 closeout H-5; nullable until `Void` succeeds). |
| `RowVersion` | `uint` | Optimistic concurrency token |

### 2.2 Invariants

- **I-1** `Amount.Amount > 0` always. Enforced in factory.
- **I-2** `Currency` follows ISO 4217; single-currency per payment (v1 constraint).
- **I-3** `Status` transitions are guarded by `PaymentStatus.CanTransitionTo(target)` — invalid transitions throw `DataIntegrityException` (bug-class, see `error-taxonomy.md § 3.5`).
- **I-4** `GatewayTransactionId` is append-only — once set, it never changes (even on refund/void, which reuse the same gateway transaction).
- **I-5** Once `Status ∈ { Completed, Failed, Refunded, Voided }`, all mutations are rejected at the aggregate root. Saga retries become idempotent no-ops.
- **I-6** `CorrelationId`, `BuyerId`, `OrderId` are immutable post-creation.
- **I-7** One payment per order is enforced by the unique index `ux_payment_transactions_order_id` on `order_id` ([ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md): the saga is keyed on `OrderId`, so `CorrelationId == OrderId`). `PaymentId` (saga-minted UUID v7) is the aggregate key, distinct from the saga key. The saga reuses the same `PaymentTransactionId` across `AuthorizePaymentCommand` retries, so it doubles as the command's idempotency anchor.

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

Raises no domain event — the transaction is created in `Requested` status and the `PaymentProcessingSaga` drives all subsequent transitions.

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
                                                                         
Captured / Completed ──refund──▶ Refunded (terminal)                                 
```

### 4.1 Transitions

| From | Event | To | Trigger |
|---|---|---|---|
| Requested | `AuthorizePaymentCommand` success | Authorized | Gateway auth succeeded |
| Requested | `AuthorizePaymentCommand` failure | Failed | Gateway declined or timeout |
| Authorized | `CapturePaymentCommand` success | Captured | Gateway capture succeeded |
| Authorized | `CapturePaymentCommand` failure | Failed | Rare; gateway capture failure |
| Authorized | `VoidPaymentCommand` | Voided | Pre-capture compensation — driven by `AbortCaptureCommand` or a capture-approval-wait timeout in the sub-saga (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) |
| Captured | (auto) | Completed | All steps complete |
| Captured | `RequestRefundCommand` | Refunded | **Deferred customer/admin refund flow** — no v1 producer (see § 7) |
| Completed | `RequestRefundCommand` | Refunded | **Deferred customer/admin refund flow** — no v1 producer; capture auto-advances to `Completed` (see § 7) |

`Completed` is the happy-path success state but is not final — it stays reversible to `Refunded` for the **deferred customer/admin-initiated refund flow** (no v1 producer; per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md), refund is no longer Checkout compensation). `Failed`, `Voided`, `Refunded` are the true terminals with no further transitions.

Transition guard: `PaymentStatus.CanTransitionTo(target)` consults a readonly `_allowed` dictionary. Invalid transitions throw `DataIntegrityException` (bug-class).

---

## 5. Domain Events (internal, 8)

Raised by the aggregate; dispatched in-process via `IDomainEventHandler<T>`. Never published to Kafka directly — external events are translated by outbox publishers.

- `PaymentAuthorizedDomainEvent` — gateway auth success
- `PaymentAuthorizationFailedDomainEvent` — gateway auth failure
- `PaymentCapturedDomainEvent` — gateway capture success
- `PaymentCaptureFailedDomainEvent` — rare; capture failure after successful auth
- `PaymentCompletedDomainEvent` — aggregate reaches `Completed`
- `PaymentRefundedDomainEvent` — aggregate reaches `Refunded`
- `PaymentVoidedDomainEvent` — aggregate reaches `Voided`
- `PaymentFailedDomainEvent` — aggregate reaches `Failed` (co-raised on `MarkAuthorizationFailed` and `MarkCaptureFailed`)

All eight now have outbox-publisher handlers — including `PaymentCompletedDomainEvent` and `PaymentFailedDomainEvent`, which gained handlers in [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md) (previously inert, [#262](https://github.com/DavidCapcuch/DotNetAtlas/issues/262)) so Payments — not the sub-saga — owns the terminal integration events `PaymentCompletedEvent` / `PaymentFailedEvent` (§ 6). `PaymentCompletedDomainEvent` is co-raised with `PaymentCapturedDomainEvent` on a successful capture.

---

## 6. External Events (Avro) + Topics

**Topic:** `payments.transactions` — per-topic topology (partitions / retention / class) in [kafka-topology.md](../kafka-topology.md); partition / correlation key per [events-catalog.md § 2](events-catalog.md).

The table below lists external lifecycle events on `payments.transactions`. Producer attribution matters: **all of them are Payments-BC-produced via the transactional outbox** — including the terminals `PaymentCompletedEvent` and `PaymentFailedEvent`, which gained outbox-publisher handlers symmetric with the existing Authorized/Captured/Voided/Refunded handlers (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md), resolving the previously-inert `PaymentCompletedDomainEvent` / `PaymentFailedDomainEvent`, [#262](https://github.com/DavidCapcuch/DotNetAtlas/issues/262)). `PaymentProcessingSaga` no longer publishes any payment-state events. The upstream message that **invokes** the Payments sub-orchestration is `RequestPaymentCommand` on `payments.payment-commands` (renamed from `PaymentRequestedEvent` per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)); see § 7 below and [events-catalog.md § 2](events-catalog.md).

| External event | Producer | Triggered by | Consumer(s) |
|---|---|---|---|
| `PaymentAuthorizedEvent` | Payments BC | `PaymentAuthorizedDomainEvent` | PaymentProcessingSaga, **Checkout saga** (drives order + reservation confirmation) |
| `PaymentAuthorizationFailedEvent` | Payments BC | `PaymentAuthorizationFailedDomainEvent` | PaymentProcessingSaga |
| `PaymentCapturedEvent` | Payments BC | `PaymentCapturedDomainEvent` | PaymentProcessingSaga, **Invoicing** (enrichment trigger) |
| `PaymentCaptureFailedEvent` | Payments BC | `PaymentCaptureFailedDomainEvent` | PaymentProcessingSaga |
| `PaymentCompletedEvent` | Payments BC | `PaymentCompletedDomainEvent` (co-raised with `PaymentCapturedDomainEvent` on a successful capture) | Checkout saga (drives `AwaitingPaymentCapture → Confirmed`) |
| `PaymentFailedEvent` | Payments BC | `PaymentFailedDomainEvent` (co-raised on `MarkAuthorizationFailed` (auth decline) **and** `MarkCaptureFailed` (capture decline)) | Checkout saga (fast-fail compensation) |
| `PaymentRefundedEvent` | Payments BC | `PaymentRefundedDomainEvent` | **Invoicing** (credit-note trigger). Part of the **deferred customer/admin refund flow** (no v1 producer for `RequestRefundCommand`); not consumed by the Checkout saga. Refund-confirmation email is deferred (would route as a `NotifyUserCommand` (v2; [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)) per [notifications.md § 2](notifications.md), not as a Notifications subscription to this topic). |
| `PaymentVoidedEvent` | Payments BC | `PaymentVoidedDomainEvent` | PaymentProcessingSaga |

**Consumers** are canonical in [events-catalog.md § 2](events-catalog.md) (§ 2 wins on any divergence; the column above mirrors it). **Schema compatibility** is *derived* from topic class — event-log → `FORWARD_TRANSITIVE` — see [ADR-0007](../adr/0007-avro-compatibility-modes.md).

**Classification analysis:** the 1-consumer rows above are *fact-shaped* under [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)'s 2-part test (specific logic at consumer **+ producer awaits guaranteed feedback** — the second leg fails for every Payments-emitted lifecycle event, terminals included). They remain `*Event`-named. The one prior message in this BC that the test classifies as a **command** — what was `PaymentRequestedEvent` — has been renamed to `RequestPaymentCommand` and moved to `payments.payment-commands` (see § 7). See ADR-0023 for the per-message classification table and the rationale for deferring further renames.

---

## 7. Commands (Avro) + Command Topic

**Topic:** `payments.payment-commands` — per-topic topology in [kafka-topology.md](../kafka-topology.md); partition key per [events-catalog.md § 2](events-catalog.md). Carries imperative intent toward the Payments aggregate (directly, or via its `PaymentProcessingSaga` sub-saga per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)).

| Command | Producer | Consumer | Trigger |
|---|---|---|---|
| `RequestPaymentCommand` | Checkout saga | PaymentProcessingSaga | Checkout saga reaches `AwaitingPaymentAuthorization`; renamed from `PaymentRequestedEvent` and moved off `payments.transactions` per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md). |
| `AuthorizePaymentCommand` | PaymentProcessingSaga | Payments | Sub-saga's first command after receiving `RequestPaymentCommand` |
| `ApproveCaptureCommand` | Checkout saga | PaymentProcessingSaga | After the Checkout saga confirms stock + order; tells the sub-saga (in `AwaitingCaptureApproval`) to issue `CapturePaymentCommand`. Fields: `CorrelationId`, `UserId`, `RequestedAtUtc`. Per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md). |
| `AbortCaptureCommand` | Checkout saga | PaymentProcessingSaga | On confirmation failure; tells the sub-saga to take the pre-capture `Void` path (free) instead of capturing. Fields: `CorrelationId`, `UserId`, `Reason`, `RequestedAtUtc`. Per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md). |
| `CapturePaymentCommand` | PaymentProcessingSaga | Payments | After the Checkout saga approves capture (`ApproveCaptureCommand`) — capture is the pivot, deferred until stock + order are confirmed (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) |
| `RequestRefundCommand` | *(deferred — no v1 producer; future customer/admin-initiated refund flow)* | Payments | `Completed → Refunded` off-ramp; a returns / post-purchase-cancellation trigger is future work (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)) |
| `VoidPaymentCommand` | PaymentProcessingSaga | Payments | Pre-capture compensation — issued on `AbortCaptureCommand` or a capture-approval-wait timeout |

**Consumers** are canonical in [events-catalog.md § 2](events-catalog.md). **Schema compatibility** is *derived* from topic class — command → `FULL_TRANSITIVE` — see [ADR-0007](../adr/0007-avro-compatibility-modes.md).

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
- `PaymentsErrors.GatewayDeclined(string reason)` — business-expected; drives the aggregate to `Failed`, and Payments' outbox publishes `PaymentFailedEvent`
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
- **Integration tests** (`test/Payments.IntegrationTests/`) — Testcontainers Postgres + Kafka. Tests the outbox publisher chain: command → domain event → outbox row → Kafka message (with Avro schema registry stub); the one-payment-per-order unique index (I-7).
- **Functional tests** (`test/Payments.FunctionalTests/`) — `WebApplicationFactory`-based full-stack admin HTTP endpoints with auth.
- **Gateway stub** (`StubPaymentGateway`) — deterministic responses in test mode; swap via DI using options.

Full example-mapping sessions in [`example-mapping/payments.md`](example-mapping/payments.md).

---

## 15. Out of scope for v1

Listed so readers don't look for them:

Planned scope is catalogued in [roadmap.md § 2.3 Payments](../roadmap.md):

- **3-D Secure** (SCA) — deferred; all stub authorizations succeed without 3DS step. Real gateway integration would add this.
- **Partial captures / partial refunds** — single amount per transaction today.
- **Multi-currency** — single currency per payment (matches Order-level single-currency constraint).
- **Chargebacks / disputes** — event hook left open (`PaymentDisputedEvent` is planned scope); not modelled today.
- **Stored payment methods / tokenization vault** — the gateway holds the vault; Payments holds only per-transaction tokens.
- **Reconciliation jobs** — planned nightly job comparing gateway ledger to our `payments.transactions` table.

---

## 16. Relationship to Checkout Saga (for orientation)

The Checkout saga's payment leg is orchestrated by `PaymentProcessingSaga`, but capture is **deferred to the pivot** — it fires only after the Checkout saga has confirmed stock + order (per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md)). The sub-saga gains an `AwaitingCaptureApproval` wait-state between authorize and capture, and Payments publishes its own terminal events:

```
Checkout saga   ─RequestPaymentCommand─▶   PaymentProcessingSaga   ─AuthorizePaymentCommand─▶   Payments BC
                ◀──────── PaymentAuthorizedEvent (Payments outbox) ───────────────────────────────┤
   (confirm order + reservations, then:)                                                          │
                ─ApproveCaptureCommand─▶   PaymentProcessingSaga   ─CapturePaymentCommand─▶   Payments BC
                ◀──────── PaymentCompletedEvent (Payments outbox) ────────────────────────────────┘
   (on confirmation failure: ─AbortCaptureCommand─▶ sub-saga ─VoidPaymentCommand─▶ Payments — free pre-capture void)
```

Payments BC has no knowledge of the Checkout saga directly — it sees authorize/capture/void commands from `PaymentProcessingSaga` plus the capture-approval handshake (`ApproveCaptureCommand` / `AbortCaptureCommand`) the sub-saga relays from the Checkout saga, and it publishes all lifecycle events (including terminals) via its own outbox, consumed by both sagas and Invoicing. Clean separation per ADR-0001; capture ordering + terminal ownership per [ADR-0026](../adr/0026-checkout-payment-flow-capture-pivot.md).

---

*End of Payments BC design.*
