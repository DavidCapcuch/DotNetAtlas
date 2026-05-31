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

    AwaitingStockReservation --> AwaitingPayment : all StockReserved
    AwaitingStockReservation --> CompensatingStock : StockReservationFailed / Timeout

    AwaitingPayment --> AwaitingConfirmation : PaymentCompleted
    AwaitingPayment --> CompensatingStock : PaymentFailed / Timeout

    AwaitingConfirmation --> Confirmed : OrderConfirmed
    AwaitingConfirmation --> CompensatingPayment : ConfirmationFailed / Timeout

    CompensatingStock --> Failed : all released (no refund)
    CompensatingStock --> Compensated : all released (after refund)
    CompensatingStock --> CompensationStuck : CompensationTimeout

    CompensatingPayment --> CompensatingStock : PaymentRefunded
    CompensatingPayment --> CompensationStuck : RefundFailed

    Confirmed --> [*]
    Failed --> [*]
    Compensated --> [*]
    CompensationStuck --> [*] : ops intervention

    note right of Confirmed
        Happy terminal
        (order complete)
    end note

    note right of Failed
        No money moved
        (pre-payment failure)
    end note

    note right of Compensated
        Money moved & refunded
        (post-confirmation failure)
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
| `AwaitingPayment` | Working | `RequestPaymentCommand` published to `payments.payment-commands` (delegated to `PaymentProcessingSaga` per [ADR-0023](../adr/0023-payments-event-vs-command-classification.md)); waiting for `PaymentCompletedEvent`/`PaymentFailedEvent` |
| `AwaitingConfirmation` | Working | `ConfirmOrderCommand` published; waiting for `OrderConfirmedEvent` |
| `CompensatingStock` | Recovery | Releasing all active reservations via `ReleaseReservationCommand` |
| `CompensatingPayment` | Recovery | Refund requested via `RequestRefundCommand`; waiting for `PaymentRefundedEvent` |
| `Confirmed` | Terminal | Happy path — order is complete |
| `Failed` | Terminal | No money moved; reached when pre-payment step failed and stock already released |
| `Compensated` | Terminal | Money moved **and** refunded; reached when post-confirmation step failed |
| `CompensationStuck` | Terminal abnormal | Compensation exceeded `CompensationTimeout`; **ops alert fires** |

See [bc-design/checkout-saga.md](../bc-design/checkout-saga.md) for full transition table, timeout config, and compensation matrix.
