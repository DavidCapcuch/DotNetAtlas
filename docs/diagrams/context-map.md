# Context Map — Mermaid source

> **Render:** paste into any mermaid renderer, or open in drawio via the MCP tool. The live drawio link is in `docs/eshop-master-design.md § 10.2`.

```mermaid
---
title: eShop Context Map — BC integration patterns
---
flowchart LR
    User([User])
    BFF[BFF<br/>Aggregation]

    subgraph Core["Core Bounded Contexts"]
        Catalog[Catalog<br/>CQRS Read Projection]
        Basket[Basket<br/>Redis aggregate, ACL]
        Ordering[Ordering<br/>Rich Status FSM]
        Inventory[Inventory<br/>Event Sourcing]
    end

    subgraph Orchestration["Orchestration (saga service)"]
        CheckoutSaga{{CheckoutSaga}}
        PaymentSaga{{PaymentProcessingSaga}}
    end

    subgraph Generic["Generic - Reused"]
        Payments[Payments]
        Notifications[Notifications]
    end

    User -->|HTTPS| BFF
    BFF -->|HTTP read| Catalog
    BFF -->|HTTP read/write| Basket
    BFF -->|HTTP read| Ordering
    BFF -->|HTTP read| Inventory

    Basket -.->|ACL snapshot| Catalog
    Catalog -.->|ProductCreated| Inventory
    Inventory -.->|StockLevelChangedEvent| Catalog

    Basket ==>|BasketCheckoutInitiated| CheckoutSaga
    CheckoutSaga ==>|CreateOrder / Confirm / Cancel cmd| Ordering
    Ordering ==>|OrderCreated / Confirmed / Failed| CheckoutSaga
    CheckoutSaga ==>|ReserveStock / Confirm / Release cmd| Inventory
    Inventory ==>|StockReserved / Failed / Released| CheckoutSaga

    CheckoutSaga ==>|PaymentRequested| PaymentSaga
    PaymentSaga ==>|Authorize / Capture / Void / Refund cmd| Payments
    Payments ==>|Payment events| PaymentSaga
    PaymentSaga ==>|PaymentCompleted / Failed / Refunded| CheckoutSaga

    Ordering -.->|OrderConfirmed / Shipped / Delivered| Notifications

    classDef core fill:#FCEA78,stroke:#af7e02,stroke-width:2px,color:#222
    classDef generic fill:#96D98E,stroke:#2d6a1f,stroke-width:2px,color:#222
    classDef orch fill:#AEC6E8,stroke:#305bab,stroke-width:2px,color:#222
    classDef ext fill:#F2B7B7,stroke:#bd0909,stroke-width:2px,color:#222

    class Catalog,Basket,Ordering,Inventory core
    class Payments,Notifications generic
    class CheckoutSaga,PaymentSaga,BFF orch
    class User ext
```

## Legend

- **Solid arrow** (`-->`) — synchronous HTTP call
- **Dashed arrow** (`-.->`) — fire-and-forget published event (Kafka)
- **Thick arrow** (`==>`) — saga-mediated command or event flow (Kafka)

## Integration pattern reference

| Edge | Pattern |
|---|---|
| BFF → services | Customer/Supplier (HTTP) |
| Basket → Catalog | **Anti-Corruption Layer** (Basket copies into `ProductSnapshot`) |
| Catalog → Inventory | **Published Language** (`ProductCreatedEvent`) |
| Inventory → Catalog | **Published Language** (`StockLevelChangedEvent` on threshold crossing) |
| Basket ↔ CheckoutSaga | Saga trigger |
| CheckoutSaga ↔ {Ordering, Inventory, PaymentSaga} | **Saga orchestration** (commands out, events in) |
| Ordering → Notifications | **Published Language** (order lifecycle events) |
