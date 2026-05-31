# Error Taxonomy — eShop Reference Solution

> Single source of truth for every error type and exception produced by handlers and aggregates across the six bounded contexts (Catalog, Basket, Ordering, Inventory, Payments, Invoicing) plus the Checkout saga. Each row in § 1 specifies BC, category, HTTP mapping, saga/infrastructure semantics, retry-ability, and dead-letter behavior. Cross-linked from each BC chapter's "Error types" subsection and from [use-cases.md](use-cases.md).
>
> **Conventions (reiterating [master design § 12.2](../eshop-master-design.md) "Result pattern"):**
> - Handlers return `Result.Fail(SomeFactory(...))` for **user-actionable** errors. Each factory returns one of the six canonical [`DomainError`](../../platform/Platform.SharedKernel/Errors/DomainError.cs) subclasses — `ValidationError`, `NotFoundError`, `ConflictError`, `ServiceUnavailableError`, `ForbiddenError`, `NotImplementedError`. `ErrorCode` is a **property** on `DomainError`; there is no `Metadata` dictionary.
> - Aggregate methods and saga-command handlers throw [`DataIntegrityException`](../../platform/Platform.SharedKernel/Exceptions/DataIntegrityException.cs) (or a BC-scoped subclass — see § 3.6) for **corrupted-state / bug-class** errors. These propagate up the pipeline and hit the [`PlatformExceptionHandler`](../../platform/Platform.ServiceDefaults/Exceptions/PlatformExceptionHandler.cs) → HTTP 500, or the KafkaFlow [`DeadLetterMiddleware`](../../platform/Platform.KafkaFlow.DeadLetter/DeadLetterMiddleware.cs) → `.DLT` topic when raised inside a consumer.
> - Errors from external services (Catalog HTTP down, payment gateway timeout) use adapter-specific factories (e.g., `BasketAclErrors.CatalogUnavailable`) that return `ServiceUnavailableError` (503).
> - "Retry-ability" in this document refers to **caller/client** retry semantics; Kafka consumer retry is governed by [kafka-dlt-strategy.md](kafka-dlt-strategy.md).

---

## 1. Master Error Table

| Error | BC | Category | HTTP | Saga behavior | Retry? | DLT? | Source |
|-------|----|----------|------|---------------|--------|------|--------|
| `BasketErrors.EmptyBasket` | Basket | User | 409 | Pre-saga block — `CheckoutBasketCommand` returns `Result.Fail` before publication | No | No | [basket.md § Invariants](basket.md) (Items.Count ≥ 1) |
| `BasketErrors.MaxItemsReached` | Basket | User | 409 | N/A (pre-saga) | No | No | [basket.md § Invariants](basket.md) (max 50 items) |
| `BasketErrors.InvalidQuantity` | Basket | User | 422 | N/A | No | No | [basket.md § BasketItem](basket.md) (`quantity >= 1`) |
| `BasketErrors.CurrencyMismatch` | Basket | User | 422 | N/A | No | No | [basket.md](basket.md) — all items share basket currency |
| `BasketErrors.ItemNotFound` | Basket | User | 404 | N/A | No | No | [basket.md § BasketItem](basket.md) — remove/update target missing |
| `BasketErrors.Corruption` | Basket | User | 422 | N/A | No | No | [basket.md](basket.md) — stored basket cannot be rehydrated (e.g. retired currency code) |
| `BasketConcurrencyError` (: `ConflictError`) | Basket | Conflict | 409 | N/A | **Yes — handler retries once** (`BasketConcurrencyRetry`; documented in [basket.md § Optimistic concurrency](basket.md)) | No | Redis CAS failure on `Basket.Version` |
| `BasketAclErrors.CatalogUnavailable` | Basket (ACL) | Upstream | 503 | N/A | Yes (client) | No | [basket.md § ProductCatalogHttpAdapter](basket.md) — network/5xx/timeout from Catalog |
| `BasketAclErrors.ProductNotFound(productId)` | Basket (ACL) | User | 404 | N/A | No | No | [basket.md § ProductCatalogHttpAdapter](basket.md) — 404 from Catalog |
| `ProductErrors.CategoryIdRequired` | Catalog | User | 422 | N/A | No | No | [catalog.md § Product.Create](catalog.md) |
| `ProductErrors.PriceMustBePositive` | Catalog | User | 422 | N/A | No | No | [catalog.md § Money](catalog.md) — price invariant |
| `ProductErrors.CannotRepriceDiscontinued` | Catalog | User | 409 | N/A | No | No | [catalog.md § ChangePrice](catalog.md) (`Status != Discontinued`) |
| `ProductErrors.CannotModifyDiscontinued` | Catalog | User | 409 | N/A | No | No | [catalog.md § Describe](catalog.md) |
| `ProductErrors.ReasonRequired` | Catalog | User | 422 | N/A | No | No | [catalog.md § Discontinue](catalog.md) |
| `ProductErrors.ReactivationRequiresAdminFlag` | Catalog | User | 403 | N/A | No | No | [catalog.md § Reactivate](catalog.md) — policy / authorisation error |
| `ProductErrors.NotFound` | Catalog | User | 404 | N/A | No | No | Handler-level lookup miss on any product-addressing command/query |
| `ProductErrors.SkuAlreadyExists(sku)` | Catalog | User | 409 | N/A | No | No | [catalog.md § Product invariants](catalog.md) (SKU uniqueness) |
| `ProductErrors.CannotDiscontinueInStatus(status)` | Catalog | User | 409 | N/A | No | No | [catalog.md § Discontinue](catalog.md) — FSM precondition |
| `ProductErrors.CannotReactivateInStatus(status)` | Catalog | User | 409 | N/A | No | No | [catalog.md § Reactivate](catalog.md) — FSM precondition |
| `CategoryErrors.NameRequired` / `NameTooLong(max)` | Catalog | User | 422 | N/A | No | No | [catalog.md § Category invariants](catalog.md) |
| `CategoryErrors.MaxDepthExceeded(max: 5)` | Catalog | User | 422 | N/A | No | No | [catalog.md § Category invariants](catalog.md) |
| `CategoryErrors.CannotParentToSelf` | Catalog | User | 422 | N/A | No | No | [catalog.md § Reparent](catalog.md) |
| `CategoryErrors.NotFound(categoryId)` | Catalog | User | 404 | N/A | No | No | Handler-level lookup miss |
| `CategoryErrors.ParentNotFound(parentCategoryId)` | Catalog | User | 404 | N/A | No | No | [catalog.md § Create / Reparent](catalog.md) |
| `CategoryErrors.ReparentCreatesCycle(id, newParentId)` | Catalog | User | 422 | N/A | No | No | [catalog.md § Reparent](catalog.md) — `CategoryAncestryService` |
| Value-object validators (`SkuErrors.*`, `ProductNameErrors.*`, `BrandNameErrors.*`, `DimensionsErrors.*`, `ImageReferenceErrors.*`, `CategoryPathErrors.*`, `ProductDescriptionErrors.*`) | Catalog | User | 422 | N/A | No | No | Each VO file under `services/Catalog/Catalog.Domain/.../Errors/` — all return `ValidationError` |
| `OrderingErrors.CannotCancelInStatus(status)` | Ordering | User | 409 | N/A (admin HTTP) | No | No | [ordering.md § Order.Cancel](ordering.md) — `CanTransitionTo(Cancelled)` |
| `OrderingErrors.OrderNotFound` | Ordering | User | 404 | N/A | No | No | Query target missing |
| **Invalid order-status transition from saga command** | Ordering | Bug | 500 | Saga → `Failed` (command fell through; DLT alert raised) | No | **Yes** | [use-cases.md § 3.3 Ordering saga consumers](use-cases.md) — aggregate throws `DataIntegrityException` |
| `InsufficientStockError` (: `ConflictError`) | Inventory | Business (expected) | 409 | Saga transitions to `CompensatingStockReservations` path | No | No | [inventory.md § Reserve](inventory.md) — `Available < qty` |
| `ConcurrencyError` (: `ConflictError`) | Inventory | Conflict | 409 | Saga retries the step once; if still failing → compensation | Yes (1x) | No | [inventory.md § Event store optimistic concurrency](inventory.md) — stream version conflict |
| `ReservationNotActiveError(productId, reservationId, currentStatus)` (: `ConflictError`) | Inventory | Business (expected) | 409 | Saga: caller's compensation path (no DLT) | No | No | [inventory.md § ConfirmReservation / ReleaseReservation](inventory.md) — known reservation in terminal status |
| `InventoryErrors.StockItemNotFound` | Inventory | Bug | 500 | Saga → `Failed`; DLT | No | **Yes** | [use-cases.md § 4.3 Inventory saga consumers](use-cases.md) — should never occur when Catalog has published `ProductCreatedEvent` |
| `InventoryErrors.ReservationNotFound` | Inventory | Bug | 500 | DLT; ops investigates | No | **Yes** | [inventory.md § Command semantics](inventory.md) — confirm/release for an unknown `ReservationId` (invariant 6 violation) |
| `PaymentsErrors.PaymentNotFound(paymentId)` | Payments | User | 404 | N/A (admin HTTP) | No | No | Query target missing |
| `GatewayDeclinedError(reason, gatewayCode?)` (: `ConflictError`) | Payments | Business (expected) | 409 | Saga converts to `PaymentFailedEvent` → `CompensatingStockReservations` | No | No | [payments.md § Gateway integration](payments.md) — gateway returned a non-success code |
| `PaymentsErrors.InvalidPaymentMethod` | Payments | User | 422 | N/A (factory validation) | No | No | [payments.md § PaymentMethodId VO](payments.md) |
| `PaymentsErrors.InvalidAmount` | Payments | User | 422 | N/A (factory validation) | No | No | [payments.md § I-1](payments.md) — amount must be > 0 |
| `PaymentsErrors.GatewayUnavailable` | Payments | Upstream | 503 | Saga retry via Polly; after exhaustion → `CompensatingStockReservations` | Yes (client) | No | [payments.md § IPaymentGateway adapter](payments.md) |
| **Invalid payment-status transition** | Payments | Bug | 500 | DLT; ops investigates | No | **Yes** | [payments.md § PaymentStatus SmartEnum](payments.md) — aggregate throws `DataIntegrityException` |
| `InvoicingErrors.InvoiceNotFound(invoiceId)` / `InvoiceForOrderNotFound(orderId)` / `CreditNoteNotFound(creditNoteId)` | Invoicing | User | 404 | N/A | No | No | Query target missing |
| `InvoicingErrors.InvoiceAlreadyIssued(correlationId)` | Invoicing | User | 409 | N/A (idempotent re-issue attempt) | No | No | [invoicing.md § Issuance projection](invoicing.md) — `pending_invoices.IssuedInvoiceId` already set |
| `InvoicingErrors.CreditNoteRefersToCancelledInvoice(invoiceId)` | Invoicing | User | 409 | N/A | No | No | [invoicing.md § I-CN-1](invoicing.md) |
| `InvoicingErrors.InvalidInvoiceTransition(from, to)` / `InvalidCreditNoteTransition(from, to)` | Invoicing | User | 409 | N/A | No | No | Aggregate FSM precondition surfaced as `Result.Fail` from the command layer |
| `InvoicingErrors.BlobUploadFailed` | Invoicing | Upstream | 503 | Azure.Storage.Blobs SDK retries (exponential backoff); DLT after exhaustion | Yes (client) | **After retries** | [invoicing.md § Blob storage](invoicing.md) — Azurite/Azure Blob upload failure |
| `InvoicingErrors.PartialRefundNotSupportedV1` | Invoicing | Feature-gate | 501 | Credit-note request with partial amount — rejected | No | No | [invoicing.md § Out of scope v1](invoicing.md) |
| `InvoiceTotalMismatchException` (: `DataIntegrityException`) | Invoicing | Bug | 500 | DLT; ops alert (data integrity) | No | **Yes** | [invoicing.md § Example 1.4](invoicing.md) — Order.Total ≠ Payment.Amount for same CorrelationId |
| `PdfGenerationFailedException` (: `DataIntegrityException`) | Invoicing | Bug | 500 | DLT; alert | No | **Yes** | [invoicing.md § PDF generation](invoicing.md) — `QuestPdfInvoiceGenerator` wraps `QuestPDF.Drawing.Exceptions.DocumentLayoutException` |
| `SagaErrors.PaymentRefundFailed` | CheckoutSaga | Ops | — | Terminal `CompensationStuck` — PagerDuty | Manual | **Yes** | [checkout-saga.md § Compensation matrix](checkout-saga.md) |
| `SagaErrors.ReservationReleaseStuck` | CheckoutSaga | Ops | — | Terminal `CompensationStuck` | Manual | **Yes** | [checkout-saga.md § Compensation matrix](checkout-saga.md) |
| `DataIntegrityException` (all BCs) | any | Bug | 500 | Dead-lettered when raised inside a Kafka consumer | No | **Yes** | Thrown by aggregates on corrupted state |

---

### 1.5 `DataIntegrityException` scope

This subsection is the architectural rule that the per-BC arch-tests (`test/<BC>.ArchitectureTests/BaseTest.cs`, `test/Inventory.ArchitectureTests/Application/ResultPatternTests.cs`) reference by name. The rule:

- **Aggregates and saga-command handlers throw [`DataIntegrityException`](../../platform/Platform.SharedKernel/Exceptions/DataIntegrityException.cs) (or a BC-scoped subclass) for state-corruption bugs — nothing else.** Aggregates must NOT throw `ArgumentException`, `InvalidOperationException`, `NotImplementedException`, `KeyNotFoundException`, or any other generic-CLR exception type for domain-state violations. Use a `DomainError` subclass and `Result.Fail` if the condition is user-actionable; use `DataIntegrityException` if the condition signals a bug that should never occur in a working system.
- **`DataIntegrityException` is a subclass of [`CriticalException`](../../platform/Platform.SharedKernel/Exceptions/CriticalException.cs)** — `CriticalException` is the marker that the consumer DLT middleware and the API `PlatformExceptionHandler` both catch on. Subclassing `DataIntegrityException` (instead of catching, parsing, and re-throwing) is how BCs carry **typed payload fields** to logs and DLT messages.
- **BC-scoped subclasses** live under the BC's own namespace and carry primary-ctor-backed properties for whatever values the throw site captured. Reference implementation: `Invoicing.Application.Common.Exceptions.InvoiceTotalMismatchException` carries `OrderTotal`, `PaymentAmount`, `CorrelationId`; `Invoicing.Application.Common.Exceptions.PdfGenerationFailedException` carries `Detail` and the inner QuestPDF exception. Both inherit `DataIntegrityException` directly so the existing `catch (CriticalException)` branches in middleware route them unchanged.
- **Argument-null / state-precondition pre-checks** at method entry (`ArgumentNullException.ThrowIfNull(...)`, `ArgumentOutOfRangeException.ThrowIfNegative(...)`) are exempt: they catch programmer errors at the call boundary before the aggregate's invariants come into play. The arch-tests allow these.

The arch-tests enforce the rule by walking every public method on every aggregate and saga consumer and asserting that any `throw new` expression resolves to a `CriticalException` subclass (with the precondition-helper exemptions noted above).

---

## 2. Categories Explained

### 2.1 User errors (4xx)

Returned by handlers via `Result.Fail(SomeFactory(...))`. The endpoint wrapper calls [`IResponseSender.SendErrorResponseAsync(result, ct)`](../../platform/Platform.Api/Extensions/ResponseSenderExtensions.cs) from `Platform.Api.Extensions.ResponseSenderExtensions`, which type-switches on the `DomainError` subclass and emits an RFC 9457 `ProblemDetails` payload with the HTTP status code selected via the mapping table in § 4. These errors:

- Never hit a DLT — they either come from an HTTP request (returned to caller) or they originate from a saga-consumer path where the handler consciously converts the `Result.Fail` into a business outcome event (e.g., `ReserveStockCommand` handler receives `InsufficientStockError` → emits `StockReservationFailedEvent` on `inventory.reservations` and **completes the consumer normally** so the offset commits).
- Represent legitimate end-user mistakes (empty basket, SKU clash, cancelling a shipped order) or validated-input violations.

### 2.2 Business expected (409 / saga compensation)

A distinct subset of "user errors" whose outcome is **expected in the saga flow**. Example: `InsufficientStockError` from `ReserveStockCommand` is not a bug — it is the happy-path of a shopper losing a race for the last unit. The Inventory saga-command consumer maps the failed result to a `StockReservationFailedEvent`, which the saga consumes to transition to compensation. These errors:

- Do NOT throw exceptions.
- Do NOT dead-letter (the consumer commits the offset after publishing the failure event).
- Feed the saga compensation matrix documented in [checkout-saga.md § 6](checkout-saga.md).
- Are modelled as concrete subclasses of one of the canonical `DomainError` types (typically `ConflictError`) so that handlers can filter them via `result.Errors.OfType<InsufficientStockError>()` and read typed payload fields without parsing `.Message`.

### 2.3 Bug-class (5xx / DLT)

`DataIntegrityException` (and its BC-scoped subclasses — see § 1.5) represents conditions that **should never occur in a working system**. Example: the saga tells Inventory to confirm a reservation that was already released; the aggregate throws. These errors:

- Surface as HTTP 500 through [`PlatformExceptionHandler`](../../platform/Platform.ServiceDefaults/Exceptions/PlatformExceptionHandler.cs) when raised in an HTTP pipeline.
- Route to the `.DLT` topic via [`DeadLetterMiddleware`](../../platform/Platform.KafkaFlow.DeadLetter/DeadLetterMiddleware.cs) when raised in a KafkaFlow consumer.
- Emit an ops alert (Grafana → PagerDuty via `kafka.consumer.dlt.messages` metric — see [kafka-dlt-strategy.md § 6](kafka-dlt-strategy.md)).

### 2.4 Infrastructure / upstream

Temporary failures outside the BC's control — upstream service returning 5xx, Kafka produce failing, DB connection timing out. Example: `BasketAclErrors.CatalogUnavailable` surfaces when Basket's `ProductCatalogHttpAdapter` hits a network error or Catalog returns 5xx. These errors:

- Use **client retry** (the HTTP caller or outbox relay retries).
- Do NOT dead-letter on first failure — only after the configured retry policy is exhausted (see [kafka-dlt-strategy.md § 2](kafka-dlt-strategy.md) for Kafka side; per-adapter Polly policy for HTTP side).
- Map to HTTP 503 (Service Unavailable) when surfaced through an API. They are constructed as `ServiceUnavailableError` (a `DomainError` subclass) so the type-switch in § 4 routes them correctly.

---

## 3. Per-BC Error Class Specifications

Each BC declares a static factory class (and any custom `DomainError` subclasses) under its own `Errors/` folder. The factories never construct raw `DomainError` instances — they return one of the six canonical typed subclasses (`ValidationError`, `NotFoundError`, `ConflictError`, `ServiceUnavailableError`, `ForbiddenError`, `NotImplementedError`) so that § 4's type-switch dispatch knows which HTTP status to emit.

### 3.1 `Basket`

User-actionable factories in [`Basket.Domain.Baskets.Errors.BasketErrors`](../../services/Basket/Basket.Domain/Baskets/Errors/BasketErrors.cs):

| Factory | Returns | HTTP | Notes |
|---|---|---|---|
| `EmptyBasket()` | `ConflictError` | 409 | Pre-checkout invariant — basket must have ≥ 1 item |
| `MaxItemsReached(int max)` | `ConflictError` | 409 | 50-item cap |
| `InvalidQuantity()` | `ValidationError` | 422 | `quantity >= 1` |
| `CurrencyMismatch()` | `ValidationError` | 422 | All items share basket currency |
| `ItemNotFound(Guid productId)` | `NotFoundError` | 404 | Item removal / quantity update target |
| `Corruption(Guid userId)` | `ValidationError` | 422 | Stored basket state cannot be rehydrated |

ACL adapter factories live separately in [`Basket.Application.Baskets.Common.Errors.BasketAclErrors`](../../services/Basket/Basket.Application/Baskets/Common/Errors/BasketAclErrors.cs) (the Application layer owns the Catalog ACL):

| Factory | Returns | HTTP | Notes |
|---|---|---|---|
| `CatalogUnavailable()` | `ServiceUnavailableError` | 503 | Catalog HTTP 5xx / timeout |
| `ProductNotFound(Guid productId)` | `NotFoundError` | 404 | Catalog returned 404 |

Custom typed subclass:

- [`BasketConcurrencyError(Guid UserId, int Expected, int Actual)`](../../services/Basket/Basket.Domain/Baskets/Errors/BasketConcurrencyError.cs) **`: ConflictError`** — Redis CAS failure. Flows through `Result.Fail`; intercepted by `BasketConcurrencyRetry.ExecuteAsync<T>()` which calls `result.HasError<BasketConcurrencyError>()` and re-attempts the entire command once before surfacing.

### 3.2 `Catalog`

Aggregate-level factories split per aggregate for locality. All return `ValidationError` / `ConflictError` / `NotFoundError` / `ForbiddenError` directly:

[`ProductErrors`](../../services/Catalog/Catalog.Domain/Products/Errors/ProductErrors.cs):

| Factory | Returns | HTTP |
|---|---|---|
| `CategoryIdRequired()` | `ValidationError` | 422 |
| `PriceMustBePositive()` | `ValidationError` | 422 |
| `CannotRepriceDiscontinued()` | `ConflictError` | 409 |
| `CannotModifyDiscontinued()` | `ConflictError` | 409 |
| `ReasonRequired()` | `ValidationError` | 422 |
| `ReactivationRequiresAdminFlag()` | `ForbiddenError` | 403 |
| `NotFound(Guid productId)` | `NotFoundError` | 404 |
| `SkuAlreadyExists(string sku)` | `ConflictError` | 409 |
| `CannotDiscontinueInStatus(string currentStatus)` | `ConflictError` | 409 |
| `CannotReactivateInStatus(string currentStatus)` | `ConflictError` | 409 |

[`CategoryErrors`](../../services/Catalog/Catalog.Domain/Categories/Errors/CategoryErrors.cs):

| Factory | Returns | HTTP |
|---|---|---|
| `NameRequired()` / `NameTooLong(int max)` | `ValidationError` | 422 |
| `MaxDepthExceeded(int max)` | `ValidationError` | 422 |
| `CannotParentToSelf()` | `ValidationError` | 422 |
| `NotFound(Guid categoryId)` | `NotFoundError` | 404 |
| `ParentNotFound(Guid parentCategoryId)` | `NotFoundError` | 404 |
| `ReparentCreatesCycle(Guid categoryId, Guid newParentCategoryId)` | `ValidationError` | 422 |

Value-object validators — each declares a small static class returning `ValidationError`:
[`SkuErrors`](../../services/Catalog/Catalog.Domain/Products/Errors/SkuErrors.cs), [`ProductNameErrors`](../../services/Catalog/Catalog.Domain/Products/Errors/ProductNameErrors.cs), [`ProductDescriptionErrors`](../../services/Catalog/Catalog.Domain/Products/Errors/ProductDescriptionErrors.cs), [`BrandNameErrors`](../../services/Catalog/Catalog.Domain/Products/Errors/BrandNameErrors.cs), [`DimensionsErrors`](../../services/Catalog/Catalog.Domain/Products/Errors/DimensionsErrors.cs), [`ImageReferenceErrors`](../../services/Catalog/Catalog.Domain/Products/Errors/ImageReferenceErrors.cs), [`CategoryPathErrors`](../../services/Catalog/Catalog.Domain/Categories/Errors/CategoryPathErrors.cs).

### 3.3 `Ordering`

[`OrderingErrors`](../../services/Ordering/Ordering.Domain/Errors/OrderingErrors.cs):

| Factory | Returns | HTTP |
|---|---|---|
| `CannotCancelInStatus(string status)` | `ConflictError` | 409 |
| `OrderNotFound(Guid orderId)` | `NotFoundError` | 404 |

All invalid **FSM transitions** from saga commands are bug-class — they do not use `OrderingErrors`; they throw `DataIntegrityException` from `Order.MarkStockReserved` / `MarkPaymentCompleted` / `Confirm` / etc. via the `Throw.If(!Status.CanTransitionTo(...))` guard. See [ordering.md § State transitions](ordering.md) and [use-cases.md § 3.3](use-cases.md).

### 3.4 `Inventory`

[`InventoryErrors`](../../services/Inventory/Inventory.Domain/StockItems/Errors/InventoryErrors.cs):

| Factory | Returns | HTTP |
|---|---|---|
| `InsufficientStock(Guid productId, int requested, int available)` | `InsufficientStockError : ConflictError` | 409 |
| `Concurrency(Guid streamId, int expectedVersion)` | `ConcurrencyError : ConflictError` | 409 |
| `ReservationNotActive(productId, reservationId, currentStatus)` | `ReservationNotActiveError : ConflictError` | 409 |
| `StockItemNotFound(Guid productId)` | `NotFoundError` | 404 |
| `ReservationNotFound(Guid reservationId)` | `NotFoundError` | 404 |

Custom typed subclasses (each inherits `ConflictError` so they map to 409 in § 4):

- [`InsufficientStockError(Guid ProductId, int Requested, int Available)`](../../services/Inventory/Inventory.Domain/StockItems/Errors/InsufficientStockError.cs)
- [`ConcurrencyError(Guid StreamId, int ExpectedVersion)`](../../services/Inventory/Inventory.Domain/StockItems/Errors/ConcurrencyError.cs)
- [`ReservationNotActiveError(Guid ProductId, Guid ReservationId, ReservationStatus CurrentStatus)`](../../services/Inventory/Inventory.Domain/StockItems/Errors/ReservationNotActiveError.cs)

Filtered downstream via `result.Errors.OfType<InsufficientStockError>()` (e.g., `ReserveStockCommandHandler` reads the typed properties to populate the outbox `StockReservationFailedEvent`).

The bug-class inventory conditions (admin-adjust leading to negative stock, confirm/release for an aggregate that has no record of the reservation) throw `DataIntegrityException` per [inventory.md § Command semantics](inventory.md) and are not modelled as `*Error` types.

### 3.5 `Payments`

[`PaymentsErrors`](../../services/Payments/Payments.Domain/Errors/PaymentsErrors.cs):

| Factory | Returns | HTTP |
|---|---|---|
| `PaymentNotFound(Guid paymentId)` | `NotFoundError` | 404 |
| `InvalidAmount()` | `ValidationError` | 422 |
| `InvalidPaymentMethod()` | `ValidationError` | 422 |
| `GatewayUnavailable()` | `ServiceUnavailableError` | 503 |

Custom typed subclass:

- [`GatewayDeclinedError(string Reason, string? GatewayCode)`](../../services/Payments/Payments.Domain/Errors/GatewayDeclinedError.cs) **`: ConflictError`** — gateway-business-failure path consumed by the saga to drive compensation. Filtered via `OfType<GatewayDeclinedError>()` in `AuthorizePaymentCommandHandler` / `CapturePaymentCommandHandler` to populate `FailureInfo`.

Invalid FSM transitions on `PaymentTransaction` (e.g., calling `Capture` from `Failed`) are bug-class and throw `DataIntegrityException` — they do not use `PaymentsErrors`.

### 3.6 `Invoicing`

[`InvoicingErrors`](../../services/Invoicing/Invoicing.Domain/Common/Errors/InvoicingErrors.cs) — user-actionable + feature-gate factories:

| Factory | Returns | HTTP |
|---|---|---|
| `InvoiceNotFound(Guid invoiceId)` | `NotFoundError` | 404 |
| `InvoiceForOrderNotFound(Guid orderId)` | `NotFoundError` | 404 (variant for by-Order lookup; same error code) |
| `CreditNoteNotFound(Guid creditNoteId)` | `NotFoundError` | 404 |
| `InvoiceAlreadyIssued(Guid correlationId)` | `ConflictError` | 409 |
| `PartialRefundNotSupportedV1()` | `NotImplementedError` | 501 |
| `BlobUploadFailed()` | `ServiceUnavailableError` | 503 |
| `CreditNoteRefersToCancelledInvoice(Guid invoiceId)` | `ConflictError` | 409 |
| `InvalidInvoiceTransition(string from, string to)` | `ConflictError` | 409 |
| `InvalidCreditNoteTransition(string from, string to)` | `ConflictError` | 409 |

**Bug-class typed exceptions** (live under `Invoicing.Application.Common.Exceptions`, both inherit `DataIntegrityException` so the consumer middleware's existing `catch (CriticalException)` branch DLTs them unchanged — see § 1.5):

- [`InvoiceTotalMismatchException(decimal OrderTotal, decimal PaymentAmount, Guid CorrelationId)`](../../services/Invoicing/Invoicing.Application/Common/Exceptions/InvoiceTotalMismatchException.cs) — raised by `IssueInvoiceCommandHandler` when `OrderConfirmedEvent.TotalAmount ≠ PaymentCapturedEvent.Amount` for the same `CorrelationId` (example-mapping 1.4). `ErrorCode = "Invoicing.TotalMismatch"`.
- [`PdfGenerationFailedException(string Detail, Exception innerException)`](../../services/Invoicing/Invoicing.Application/Common/Exceptions/PdfGenerationFailedException.cs) — raised by `QuestPdfInvoiceGenerator` wrapping `QuestPDF.Drawing.Exceptions.DocumentLayoutException` (QuestPDF's only publicly-thrown exception type as of v2026.5.0). `ErrorCode = "Invoicing.PdfGenerationFailed"`. The original QuestPDF exception is preserved as `InnerException` for diagnostics.

Invariant violations on `Invoice` / `CreditNote` aggregates (e.g., issuing a credit note against a `Cancelled` invoice — I-CN-1) throw plain `DataIntegrityException`.

### 3.7 `CheckoutSaga`

Saga-scoped errors are emitted as `FailureInfo` VO data on `CheckoutSagaState.Failure` (see [checkout-saga.md](checkout-saga.md) — `ErrorCode`, `ErrorMessage`, `AtStatus`, `FailedAtUtc`). The saga surfaces them as OpenTelemetry span attributes plus metric counters; per [master design § E.2](../eshop-master-design.md), no saga-terminal Kafka events are emitted in v1.

Canonical saga-owned `ErrorCode` values are the source-of-truth constants in [`CheckoutSagaErrorCodes`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/CheckoutSagaErrorCodes.cs): `STOCK_UNAVAILABLE`, `STOCK_TIMEOUT`, `ORDER_CREATION_TIMEOUT`, `PAYMENT_TIMEOUT`, `CONFIRMATION_TIMEOUT`, `COMPENSATION_TIMEOUT`. The saga also forwards upstream-owned codes unchanged — notably `PAYMENT_FAILED` from Payments BC and `ORDER_VALIDATION_FAILED` / `CONFIRMATION_FAILED` from Ordering BC (per [`OrderFailedSagaEvent`](../../saga/SagaOrchestrators/Checkout/CheckoutSaga/InternalSagaEvents/OrderFailedSagaEvent.cs)). (Note: `CompensationStuck` is the terminal *state* the saga enters when `COMPENSATION_TIMEOUT` fires — not an `ErrorCode` value; see [ordering.md § FailureInfo](ordering.md) for the wire-level field.)

### 3.8 `PaymentProcessingSaga`

Saga-scoped errors are persisted on `PaymentProcessingSagaState.ErrorCode` / `.ErrorMessage` and surfaced as OpenTelemetry span attributes plus metric counters. On the `CaptureTimeout` path the code is also published on the wire as `PaymentFailedEvent.ErrorCode` to `payments.transactions` (consumed by the Checkout saga in parallel with the compensating `VoidPaymentCommand`); the other three timeout paths (`AuthorizationTimeout`, `VoidTimeout`, `RefundTimeout`) stay internal.

Canonical saga-owned `ErrorCode` values are the source-of-truth constants in [`PaymentProcessingSagaErrorCodes`](../../saga/SagaOrchestrators/Payments/PaymentProcessingSaga/PaymentProcessingSagaErrorCodes.cs): `AUTHORIZATION_TIMEOUT`, `CAPTURE_TIMEOUT`, `VOID_TIMEOUT`, `REFUND_TIMEOUT` — each emitted on the corresponding `*TimeoutExpired` schedule firing. The saga also forwards upstream-owned codes unchanged from the Payments BC's gateway adapter (e.g. `CARD_DECLINED`, `CAPTURE_FAILED`, `GATEWAY_TIMEOUT`), retaining them on `PaymentProcessingSagaState.ErrorCode` and — on the capture-failure path — re-emitting them as `PaymentFailedEvent.ErrorCode`.

---

## 4. HTTP Mapping Registration

The API-layer dispatch is a pure type-switch in [`Platform.Api.Extensions.ResponseSenderExtensions.MapToProblem`](../../platform/Platform.Api/Extensions/ResponseSenderExtensions.cs) — there is no `ProblemDetailsFactory` lookup, no `Metadata["ErrorCode"]` inspection, and no per-BC override hook. Each canonical `DomainError` subclass maps to exactly one HTTP status:

| `DomainError` subclass | HTTP status |
|---|---|
| `ServiceUnavailableError` | **503 Service Unavailable** |
| `NotImplementedError` | **501 Not Implemented** |
| `ForbiddenError` | **403 Forbidden** |
| `ConflictError` (and any subclass — e.g. `InsufficientStockError`, `BasketConcurrencyError`, `GatewayDeclinedError`) | **409 Conflict** |
| `NotFoundError` | **404 Not Found** |
| `ValidationError` | **422 Unprocessable Entity** (RFC 9457: well-formed but semantically invalid; 400 is reserved for pre-handler input-shape validators) |
| Unknown `DomainError` subclass | **400 Bad Request** |
| Non-`DomainError` `IError` (or empty failure list) | **500 Internal Server Error** |
| Unhandled exception (`DataIntegrityException`, anything else) | **500 Internal Server Error** via [`PlatformExceptionHandler`](../../platform/Platform.ServiceDefaults/Exceptions/PlatformExceptionHandler.cs) |

**Precedence (most-severe wins).** When a `Result` carries multiple errors with different statuses, the order top-to-bottom in the table above is the precedence chain: `503 > 501 > 403 > 409 > 404 > 422 > 400`. Implementation: `MapToProblem` records per-category flags as it iterates `result.Errors`, then assigns the status code in a chain of `if` statements where later assignments override earlier ones.

**Closed-world rule.** BCs that need a status not covered by the canonical subclasses must add a new `DomainError` subclass under [`platform/Platform.SharedKernel/Errors/`](../../platform/Platform.SharedKernel/Errors/) and a new `case` arm in `MapToProblem` — never a local per-BC override. Custom subclasses of an existing canonical type (e.g., `InsufficientStockError : ConflictError`) do not require any change to `MapToProblem` because the pattern match falls through to the base-class arm.

**Endpoint usage.** Each FastEndpoints endpoint inspects the dispatched `Result` and forwards failures via the extension method:

```csharp
var result = await _sender.SendAsync(command, ct);
if (result.IsFailed)
{
    await ep.SendErrorResponseAsync(result, ct);
    return;
}

await ep.SendOkAsync(result.Value, ct);
```

`SendErrorResponseAsync` is the entry point that invokes `MapToProblem` and emits the RFC 9457 payload via `HttpContext.Response.SendErrorsAsync`. The wrapped `IResponseSender` is the FastEndpoints abstraction over `HttpContext.Response`; no ASP.NET Core middleware sits between the endpoint and the dispatcher.

---

## 5. Saga Compensation Semantics

Cross-reference to [checkout-saga.md § 6 Compensation matrix](checkout-saga.md) — the table below summarizes which errors trigger which compensation path:

| Upstream error (on saga-consumed event / command reply) | Saga state transition |
|---|---|
| `OrderCreatedEvent` never arrives within timeout | `AwaitingOrderCreation` → `Failed` (no side effects) |
| `StockReservationFailedEvent(InsufficientStockError)` | `AwaitingStockReservation` → `CompensatingStockReservations` → release any prior reservations for this `CorrelationId` → `CancelOrder` → `Failed` |
| `StockReservationFailedEvent` with bug-class error (reservation ID clash etc.) | Same as above — compensation path is identical; ops alert additionally raised due to DLT message on the ORIGINATING consumer |
| `PaymentFailedEvent` (from `GatewayDeclinedError`) | `AwaitingPayment` → `CompensatingStockReservations` → `CancelOrder` → `Failed` |
| Confirmation fails (order FSM rejects `Confirm` after payment succeeded) | `AwaitingConfirmation` → `CompensatingPayment` (`RequestRefund` via PaymentProcessingSaga) → `CompensatingStockReservations` → `CancelOrder` → `Compensated` |
| `ReservationReleasedEvent` never arrives during compensation within 300 s | `CompensatingStockReservations` → `CompensationStuck` (ops alert, manual intervention via [saga-stuck runbook](saga-stuck-runbook.md)) |
| `PaymentRefundedEvent` never arrives during `CompensatingPayment` within 300 s | `CompensatingPayment` → `CompensationStuck` |

Bug-class errors inside Ordering / Inventory that cause a saga-command consumer to DLT (e.g., `DataIntegrityException` on `ConfirmReservationCommand` for an already-confirmed reservation) leave the saga in its *Awaiting* state until the corresponding response event is produced. If no response event arrives before the state timeout, the saga transitions to its compensation path using the generic timeout error code (`STOCK_TIMEOUT` / `CONFIRMATION_TIMEOUT`). The DLT alert and the saga timeout are **complementary signals** — they frequently fire together for the same incident.

---

## 6. Cross-References

- [master-design § 12.2 Result pattern](../eshop-master-design.md) — top-level `Result` vs `DataIntegrityException` split
- [kafka-dlt-strategy.md](kafka-dlt-strategy.md) — DLT routing and alerting for Kafka errors
- [architecture-tests.md](architecture-tests.md) — NetArchTest rules that enforce § 1.5 (`DataIntegrityException`-only for aggregate bug-class throws)
- [use-cases.md](use-cases.md) — every `*Command` / `*Query` documents the `Result.Fail(...)` paths it can return
- [catalog.md](catalog.md), [basket.md](basket.md), [ordering.md](ordering.md), [inventory.md](inventory.md), [payments.md](payments.md), [invoicing.md](invoicing.md) — BC-chapter "Error types" subsections delegate to this document; per-VO error names enumerate in those files
- [checkout-saga.md § 6](checkout-saga.md) — full compensation matrix backing § 5 above
