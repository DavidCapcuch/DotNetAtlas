# Checkout Saga — State Machine (Mermaid source)

> **Render:** paste into any mermaid renderer, or open in drawio via the MCP tool. The live drawio link is in `docs/eshop-master-design.md § 10.2`.

```mermaid
---
title: Checkout Saga — State Machine
---
stateDiagram-v2
    [*] --> AwaitingOrderCreation : BasketCheckoutInitiated

    AwaitingOrderCreation --> AwaitingStockReservation : OrderCreated
    AwaitingOrderCreation --> Failed : OrderFailed / Timeout

    AwaitingStockReservation --> AwaitingPaymentAuthorization : all StockReserved
    AwaitingStockReservation --> CompensatingStock : StockReservationFailed / Timeout

    AwaitingPaymentAuthorization --> AwaitingConfirmation : PaymentAuthorized (confirm order + reservations)
    AwaitingPaymentAuthorization --> CompensatingStock : PaymentFailed (auth decline, fast-fail) / Timeout

    AwaitingConfirmation --> AwaitingPaymentCapture : OrderConfirmed (ApproveCapture = PIVOT)
    AwaitingConfirmation --> CompensatingStock : ConfirmationFailed / Timeout (AbortCapture = pre-capture void)

    AwaitingPaymentCapture --> Confirmed : PaymentCompleted
    AwaitingPaymentCapture --> CompensationStuck : PaymentFailed / Timeout (post-pivot, manual)

    CompensatingStock --> Compensated : all released + order cancelled
    CompensatingStock --> CompensationStuck : CompensationTimeout

    Confirmed --> [*]
    Failed --> [*]
    Compensated --> [*]
    CompensationStuck --> [*] : ops intervention

    note right of AwaitingPaymentCapture
        ADR-0026 capture pivot:
        capture deferred until
        after confirmation
    end note

    note right of Confirmed
        Happy terminal
        (order complete)
    end note

    note right of Failed
        No money moved
        (pre-payment failure)
    end note

    note right of Compensated
        Authorization voided
        (pre-capture, free) +
        reservation released
    end note

    note right of CompensationStuck
        Ops alert: saga.checkout.stuck
        Manual investigation required
    end note
```

## State glossary

| State | Kind | Meaning |
|---|---|---|
| `AwaitingOrderCreation` | Working | `CreateOrderCommand` published; waiting for `OrderCreatedEvent` |
| `AwaitingStockReservation` | Working | N `ReserveStockCommand`s fanned out (one per distinct ProductId); waiting for all `StockReservedEvent`s |
| `AwaitingPaymentAuthorization` | Working | `RequestPaymentCommand` published to `payments.payment-commands` (delegated to `PaymentProcessingSaga` per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)); waiting for the Payments-owned `PaymentAuthorizedEvent` (→ confirm) or `PaymentFailedEvent` (auth decline → fast-fail) |
| `AwaitingConfirmation` | Working | `ConfirmOrderCommand` + per-reservation `ConfirmReservationCommand`s published; waiting for `OrderConfirmedEvent`. On success approves capture (the pivot) via `ApproveCaptureCommand` |
| `AwaitingPaymentCapture` | Working | Capture approved (ADR-0026 pivot); waiting for the Payments-owned terminal `PaymentCompletedEvent` |
| `CompensatingStock` | Recovery | Releasing all active reservations via `ReleaseReservationCommand` + cancelling the order |
| `Confirmed` | Terminal | Happy path — order is complete |
| `Failed` | Terminal | No money moved; reached when pre-payment step failed and stock already released |
| `Compensated` | Terminal | Authorization voided pre-capture (free, via `AbortCaptureCommand`) **and** reservation released; reached when confirmation failed |
| `CompensationStuck` | Terminal abnormal | Compensation exceeded `CompensationTimeout`, or a post-pivot capture failure left an unrecoverable state; **ops alert fires** |

See [bc-design/checkout-saga.md](../bc-design/checkout-saga.md) for full transition table, timeout config, and compensation matrix.
