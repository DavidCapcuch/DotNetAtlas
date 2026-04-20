# eShop BC Map — Entities per bounded context (Mermaid source)

> **Render:** paste into any mermaid renderer, or open in drawio via the MCP tool. The live drawio link is in `docs/eshop-master-design.md § 10.2`.
>
> Modeled as a mermaid `classDiagram` — each BC's entities shown with their key fields and cross-BC references labeled on dashed edges. Matches the DDD context-map style of showing entities per bounded context with shared identifiers on the seams.

```mermaid
---
title: eShop BC Map — Entities per bounded context
---
classDiagram
    class Product {
        +ProductId: Guid
        +Sku: string
        +Name: string
        +Price: Money
        +CategoryId: Guid
        +Status: ProductStatus
    }
    class Category {
        +CategoryId: Guid
        +Name: string
        +ParentCategoryId: Guid?
        +Path: string
    }
    class ProductImage {
        +Url: string
        +AltText: string
        +DisplayOrder: int
    }

    class Basket {
        +UserId: Guid
        +Version: uint
        +Items: BasketItem[]
        +CreatedAtUtc
    }
    class BasketItem {
        +ProductId: Guid
        +Snapshot: ProductSnapshot
        +Quantity: int
    }
    class ProductSnapshot {
        +Sku: string
        +Name: string
        +Price: Money
        +CapturedAtUtc
    }

    class Order {
        +OrderId: Guid
        +BuyerId: Guid
        +Status: OrderStatus
        +ShippingAddress: Address
        +BillingAddress: Address
        +CorrelationId: Guid
    }
    class OrderItem {
        +ProductId: Guid
        +Sku, Name
        +UnitPrice: Money
        +Quantity: int
    }
    class Address {
        +Street1, Street2
        +City, State
        +PostalCode
        +CountryCode
    }

    class StockItem {
        +ProductId: Guid
        +OnHand: int
        +Reserved: int
        -folded from stream
    }
    class Reservation {
        +ReservationId: Guid
        +OrderId: Guid
        +Quantity: int
        +ExpiresAtUtc
        +Status
    }
    class StockEvent {
        +StreamId: Guid
        +Version: int
        +EventType: string
        +Payload: bytes
    }

    Product "1" --> "1" Category : belongs to
    Product "1" --> "*" ProductImage : has
    Basket "1" --> "*" BasketItem : contains
    BasketItem "1" --> "1" ProductSnapshot : copies from Catalog (ACL)
    ProductSnapshot ..> Product : ProductId reference
    Order "1" --> "*" OrderItem : contains
    Order "1" --> "2" Address : shipping + billing
    OrderItem ..> Product : ProductId reference
    Product ..> StockItem : triggers init via ProductCreatedEvent
    StockItem "1" --> "*" Reservation : tracks
    StockItem "1" --> "*" StockEvent : folded from
    Reservation ..> Order : OrderId saga correlation

    note for Product "Catalog BC (yellow): CQRS read projection"
    note for Basket "Basket BC (green): Redis-backed technical BC"
    note for Order "Ordering BC (blue): status FSM"
    note for StockItem "Inventory BC (orange): Event Sourced"
```

## Cross-BC reference conventions

- Solid arrows within a BC → composition / ownership
- Dashed arrows across BCs → **reference by ID only** (never by aggregate type — enforced by architecture tests, see `architecture-tests.md`)
- `ProductId` is the most-shared identifier; it appears in Basket's `ProductSnapshot`, Ordering's `OrderItem`, and Inventory's `StockItem` streams
- `OrderId` is the correlation key for the Checkout saga's Ordering ↔ Inventory handshake
- `UserId` (from Keycloak JWT `sub` claim) is NOT shown here — it's omnipresent and owned by Keycloak (not by any BC) per [ADR-0005](../adr/0005-customer-data-in-ordering.md)
