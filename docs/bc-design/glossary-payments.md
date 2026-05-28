# Payments Bounded Context — Ubiquitous Language Glossary

> Scope: the Payments BC only. Terms here are how Payments speaks — not how Checkout saga, Invoicing, or Ordering speak, even when those BCs use the same word for a related concept. Translator boundaries are called out explicitly.

---

## Core terms

| Term | Definition |
|------|------------|
| **PaymentTransaction** | The sole aggregate in the Payments BC. Wraps one saga-scoped payment lifecycle from request through terminal. Not to be confused with **Invoice** (Invoicing BC) or **Order** (Ordering BC) — those are separate aggregates in different BCs. |
| **PaymentId** | UUID v7 identity for a `PaymentTransaction`. Time-sortable; assigned at aggregate creation. Different from `GatewayTransactionId` (gateway-issued). |
| **CorrelationId** | The originating saga CorrelationId (copied from `BasketCheckoutInitiatedEvent.BasketCorrelationId`). Threads through Checkout saga → PaymentProcessingSaga → Payments → Invoicing for end-to-end traceability. |
| **GatewayTransactionId** | String token issued by the external payment gateway on the first successful call (authorize or capture). Reused for subsequent capture/refund/void operations. Immutable once set. |
| **PaymentMethodId** | Gateway-issued reference token (not a PAN). Identifies *how* to charge without exposing card details. PCI-scope boundary — this is the highest-sensitivity data Payments holds. |
| **Gateway** | The external payment processor (Stripe/Adyen/Braintree in production; `StubPaymentGateway` in v1). Abstracted via `IPaymentGateway` port in `Payments.Application`. |
| **PaymentStatus** | SmartEnum with 7 values: `Requested`, `Authorized`, `Captured`, `Completed`, `Failed`, `Refunded`, `Voided`. Transitions guarded by `CanTransitionTo`. |
| **FailureInfo** | Value object recording terminal-failure reason, gateway response code, and timestamp. Populated only on `Failed`, `Refunded`, `Voided`. |
| **FailureReason** | SmartEnum categorizing terminal failures: `GatewayDeclined`, `GatewayTimeout`, `InsufficientFunds`, `FraudSuspected`, `Cancelled`, `Unknown`. |

---

## Lifecycle / state terms

| Term | Definition |
|------|------------|
| **Authorize** | First gateway call — reserves funds on the buyer's payment method without moving money. Reversible via void. |
| **Capture** | Second gateway call — actually moves money. After capture, reversal requires a refund (heavier operation). In v1 this is done **immediately after authorize** (no delayed-capture flow). |
| **Refund** | Post-capture reversal. Requires a new gateway call referencing the original `GatewayTransactionId`. Customer-visible; may be taxable. |
| **Void** | Pre-capture cancellation. Releases the authorization hold without money movement. Cheaper and invisible to the customer. Preferred compensation path when timing permits. |
| **Terminal state** | `Completed`, `Failed`, `Refunded`, `Voided`. Aggregate rejects further mutations once terminal. |
| **Compensation path** | Any saga-triggered reversal of a previously successful step. For Payments, the compensation options are `Void` (pre-capture) and `Refund` (post-capture). |

---

## Integration terms

| Term | Definition |
|------|------------|
| **PaymentProcessingSaga** | Standalone saga orchestrator under `saga/SagaOrchestrators/Payments/PaymentProcessingSaga/`. The **only** caller of Payments commands. Sub-saga of the Checkout saga, delegated via `PaymentRequestedEvent`. |
| **Checkout saga** | The top-level orchestrator under `saga/SagaOrchestrators/Checkout/`. Consumes terminal Payments events (`PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`) to drive its own state transitions. Does NOT issue Payments commands directly — that's PaymentProcessingSaga's job. |
| **payments.transactions** | Kafka topic for outbound Payments events. Infinite retention (audit). Partition key `CorrelationId`. |
| **payments.payment-commands** | Kafka topic for inbound commands from PaymentProcessingSaga. 7-day retention. Partition key `CorrelationId`. |

---

## PCI / security terms

| Term | Definition |
|------|------------|
| **PCI scope** | The set of systems that store, process, or transmit cardholder data (PAN, CVV, track data). The reference solution deliberately narrows scope to the gateway's hosted payment form — Payments BC holds only gateway-issued tokens, never cardholder data. |
| **Cardholder data** | PAN, CVV, track-1/track-2 data. MUST NEVER appear in any service, any log, any span attribute, any topic. The gateway is the only system that sees it. |
| **Tokenization** | The gateway-side process of replacing cardholder data with a reference token. The token is what Payments stores. |

---

## Cross-BC disambiguation

| Term | In Payments | In other BCs |
|------|-------------|--------------|
| **Transaction** | `PaymentTransaction` aggregate — a payment lifecycle | Ordering: implicit DB transaction scope on `Order`; Inventory: an event-stream append |
| **CorrelationId** | Links payment to the originating saga | Same across the whole system — shared linkage token |
| **Amount** | `Money` VO on `PaymentTransaction` — equals the Order total at capture time | Ordering: `Order.Total`; Invoicing: `Invoice.Total` — all three should match for a given CorrelationId |
| **Status** | `PaymentStatus` — 7-value FSM | Ordering: `OrderStatus` — 8-value FSM; Invoicing: `InvoiceStatus` — 4-value FSM |
| **Completed** | Terminal success state of the aggregate | Ordering has no `Completed`; its terminal success is `Delivered`. Checkout saga `Confirmed` is the equivalent happy-path terminal |
| **Gateway** | The external payment processor | No other BC uses this term |

---

## Deliberately omitted / deferred to v2

| Term | Reason |
|------|--------|
| **3DS / Strong Customer Authentication** | Deferred; v1 stub gateway auto-approves without SCA challenge. |
| **Chargeback** | Post-transaction dispute initiated by the card issuer. Not modelled in v1; could be added as `PaymentDisputedEvent` in v2. |
| **Partial capture** | V1 captures the full authorized amount in a single step. |
| **Partial refund** | V1 refunds the full captured amount. Partial refunds are v2 scope. |
| **Recurring / subscription billing** | Out of scope — no subscription semantics in the eShop reference. |
| **Reconciliation** | Nightly ledger-compare with the gateway. Not modelled; events provide the audit trail. |

---

*End of Payments glossary — 30 terms defined.*
