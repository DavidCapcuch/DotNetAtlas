# Error Taxonomy — eShop Reference Solution

> Single source of truth for every `*Error` type produced by handlers and aggregates across the six new bounded contexts (Catalog, Basket, Ordering, Inventory, Payments, Invoicing) plus the Checkout saga. Each row specifies: source, HTTP mapping, saga/infrastructure semantics, retry-ability, and dead-letter behavior. Cross-linked from each BC chapter's "Error types" subsection and from [use-cases.md](use-cases.md).
>
> **Conventions (reiterating [master design § 12.2](../eshop-master-design.md) "Result pattern"):**
> - Handlers return `Result.Fail(new SomeError(...))` for **user-actionable** errors. Error types implement FluentResults `IError` (existing codebase convention — see `Weather.Domain.Alerts.Errors.WeatherAlertErrors` for the canonical shape using `Platform.SharedKernel.Errors.ValidationError`).
> - Aggregate methods throw [`DataIntegrityException`](../../platform/Platform.SharedKernel/Exceptions/DataIntegrityException.cs) for **corrupted-state / bug-class** errors. These propagate up the pipeline and hit the global exception middleware → HTTP 5xx, or the KafkaFlow [`DeadLetterMiddleware`](../../platform/Platform.KafkaFlow.DeadLetter/DeadLetterMiddleware.cs) → `.DLT` topic when consumed from Kafka.
> - Errors from external services (Catalog HTTP down, Kafka produce fail) use adapter-specific errors (e.g., `BasketErrors.CatalogUnavailable`) and are categorised as infrastructure/upstream failures.
> - "Retry-ability" in this document refers to **caller/client** retry semantics; Kafka consumer retry is governed by [kafka-dlq-strategy.md](kafka-dlq-strategy.md).

---

## 1. Master Error Table

| Error | BC | Category | HTTP | Saga behavior | Retry? | DLQ? | Source |
|-------|----|----------|------|---------------|--------|------|--------|
| `BasketErrors.EmptyBasket` | Basket | User | 409 | Pre-saga block — `CheckoutBasketCommand` returns `Result.Fail` before publication | No | No | [basket.md § Invariants](basket.md) (Items.Count ≥ 1) |
| `BasketErrors.MaxItemsReached` | Basket | User | 409 | N/A (pre-saga) | No | No | [basket.md § Invariants](basket.md) (max 50 items) |
| `BasketErrors.InvalidQuantity` | Basket | User | 422 | N/A | No | No | [basket.md § BasketItem](basket.md) (`quantity >= 1`) |
| `BasketErrors.CurrencyMismatch` | Basket | User | 422 | N/A | No | No | [basket.md](basket.md) — all items share basket currency |
| `BasketConcurrencyError` | Basket | Conflict | 409 | N/A | **Yes — handler retries once** (documented in [basket.md § Optimistic concurrency](basket.md)) | No | Redis CAS failure on `Basket.Version` |
| `BasketErrors.CatalogUnavailable` | Basket (ACL) | Upstream | 503 | N/A | Yes (client) | No | [basket.md § ProductCatalogHttpAdapter](basket.md) — network/5xx/timeout from Catalog |
| `BasketErrors.ProductNotFound(productId)` | Basket (ACL) | User | 404 | N/A | No | No | [basket.md § ProductCatalogHttpAdapter](basket.md) — 404 from Catalog |
| `CatalogErrors.SkuAlreadyExists` | Catalog | User | 409 | N/A | No | No | [catalog.md § Product invariants](catalog.md) (SKU uniqueness) |
| `CatalogErrors.ProductNotFound` | Catalog | User | 404 | N/A | No | No | Query/command target missing |
| `ProductErrors.CannotRepriceDiscontinued` | Catalog | User | 409 | N/A | No | No | [catalog.md § ChangePrice](catalog.md) (`Status != Discontinued`) |
| `ProductErrors.ReasonRequired` | Catalog | User | 422 | N/A | No | No | [catalog.md § Discontinue](catalog.md) |
| `ProductErrors.ReactivationRequiresAdminFlag` | Catalog | User | 403 | N/A | No | No | [catalog.md § Reactivate](catalog.md) — policy error |
| `CategoryErrors.HasDependents` | Catalog | User | 409 | N/A | No | No | [catalog.md § Category invariants](catalog.md) — delete blocked when children/products |
| `CategoryErrors.MaxDepthExceeded(max: 5)` | Catalog | User | 422 | N/A | No | No | [catalog.md § Category invariants](catalog.md) |
| `CategoryErrors.CannotParentToSelf` | Catalog | User | 422 | N/A | No | No | [catalog.md § Reparent](catalog.md) |
| `OrderingErrors.CannotCancelInStatus(status)` | Ordering | User | 409 | N/A (admin HTTP) | No | No | [ordering.md § Order.Cancel](ordering.md) — `CanTransitionTo(Cancelled)` |
| `OrderingErrors.OrderNotFound` | Ordering | User | 404 | N/A | No | No | Query target missing |
| **Invalid order-status transition from saga command** | Ordering | Bug | 5xx | Saga → `Failed` (command fell through; DLT alert raised) | No | **Yes** | [use-cases.md § 3.3 Ordering saga consumers](use-cases.md) — aggregate throws `DataIntegrityException` |
| `InsufficientStockError` | Inventory | Business (expected) | 409 | Saga transitions to `CompensatingStockReservations` path | No | No | [inventory.md § Reserve](inventory.md) — `Available < qty` |
| `InventoryErrors.StockItemNotFound` | Inventory | Bug | 5xx | Saga → `Failed`; DLT | No | **Yes** | [use-cases.md § 4.3 Inventory saga consumers](use-cases.md) — should never occur when Catalog has published `ProductCreatedEvent` |
| **Reservation not Active** on confirm/release | Inventory | Bug | 5xx | DLT; ops investigates | No | **Yes** | [inventory.md § ConfirmReservation / ReleaseReservation](inventory.md) — `DataIntegrityException` from aggregate |
| `ConcurrencyError` | Inventory | Conflict | 5xx | Saga retries the step once; if still failing → compensation | Yes (1x) | No | [inventory.md § Event store optimistic concurrency](inventory.md) — stream version conflict |
| `PaymentsErrors.PaymentNotFound(paymentId)` | Payments | User | 404 | N/A (admin HTTP) | No | No | Query target missing |
| `PaymentsErrors.GatewayDeclined(reason)` | Payments | Business (expected) | 409 | Saga converts to `PaymentFailedEvent` → `CompensatingStockReservations` | No | No | [payments.md § Gateway integration](payments.md) — gateway returned a non-success code |
| `PaymentsErrors.InvalidPaymentMethod` | Payments | User | 422 | N/A (factory validation) | No | No | [payments.md § PaymentMethodId VO](payments.md) |
| `PaymentsErrors.InvalidAmount` | Payments | User | 422 | N/A (factory validation) | No | No | [payments.md § I-1](payments.md) — amount must be > 0 |
| `PaymentsErrors.GatewayUnavailable` | Payments | Upstream | 503 | Saga retry via Polly; after exhaustion → `CompensatingStockReservations` | Yes (client) | No | [payments.md § IPaymentGateway adapter](payments.md) |
| **Invalid payment-status transition** | Payments | Bug | 5xx | DLT; ops investigates | No | **Yes** | [payments.md § PaymentStatus SmartEnum](payments.md) — aggregate throws `DataIntegrityException` |
| `InvoicingErrors.InvoiceNotFound(invoiceId)` | Invoicing | User | 404 | N/A | No | No | Query target missing |
| `InvoicingErrors.InvoiceAlreadyIssued` | Invoicing | User | 409 | N/A (idempotent re-issue attempt) | No | No | [invoicing.md § Issuance projection](invoicing.md) — `pending_invoices.IssuedInvoiceId` already set |
| `InvoicingErrors.CreditNoteRefersToCancelledInvoice` | Invoicing | Bug | 5xx | DLT | No | **Yes** | [invoicing.md § I-CN-1](invoicing.md) — credit note against already-cancelled invoice |
| `InvoicingErrors.PdfGenerationFailed(detail)` | Invoicing | Bug | 5xx | DLT; QuestPDF error bubbles | No | **Yes** | [invoicing.md § PDF generation](invoicing.md) |
| `InvoicingErrors.BlobUploadFailed` | Invoicing | Upstream | 5xx | Azure.Storage.Blobs SDK retries (exponential backoff per ADR-0017 `<design_open>`); DLT after exhaustion | Yes (client) | **After retries** | [invoicing.md § Blob storage](invoicing.md) — Azurite/Azure Blob upload failure |
| `InvoicingErrors.TotalMismatch(orderTotal, paymentAmount)` | Invoicing | Bug | 5xx | DLT; ops alert (data integrity) | No | **Yes** | [invoicing.md § Example 1.4](invoicing.md) — Order.Total ≠ Payment.Amount for same CorrelationId |
| `InvoicingErrors.PartialRefundNotSupportedV1` | Invoicing | Feature-gate | 501 | Credit-note request with partial amount — rejected | No | No | [invoicing.md § Out of scope v1](invoicing.md) |
| `SagaErrors.PaymentRefundFailed` | CheckoutSaga | Ops | — | Terminal `CompensationStuck` — PagerDuty | Manual | **Yes** | [checkout-saga.md § Compensation matrix](checkout-saga.md) |
| `SagaErrors.ReservationReleaseStuck` | CheckoutSaga | Ops | — | Terminal `CompensationStuck` | Manual | **Yes** | [checkout-saga.md § Compensation matrix](checkout-saga.md) |
| `DataIntegrityException` (all BCs) | any | Bug | 5xx | Dead-lettered when raised inside a Kafka consumer | No | **Yes** | Thrown by aggregates on corrupted state |

**Count:** 38 error rows covering all six new BCs plus saga plus platform exception.

---

## 2. Categories Explained

### 2.1 User errors (4xx)

Returned by handlers via `Result.Fail(IError)`. The FastEndpoints `GlobalExceptionHandler` / `ProblemDetailsFactory` pipeline translates the FluentResults `Result` into an RFC 7807 `ProblemDetails` payload with the HTTP status code selected via the mapping table in § 4. These errors:

- Never hit a DLQ — they either come from an HTTP request (returned to caller) or they originate from a saga-consumer path where the handler consciously converts the `Result.Fail` into a business outcome event (e.g., `ReserveStockCommand` handler receives `InsufficientStockError` → emits `StockReservationFailedEvent` on `inventory.reservations` and **completes the consumer normally** so the offset commits).
- Represent legitimate end-user mistakes (empty basket, SKU clash, cancelling a shipped order) or validated-input violations.

### 2.2 Business expected (409 / saga compensation)

A distinct subset of "user errors" whose outcome is **expected in the saga flow**. Example: `InsufficientStockError` from `ReserveStockCommand` is not a bug — it is the happy-path of a shopper losing a race for the last unit. The Inventory saga-command consumer maps the failed result to a `StockReservationFailedEvent`, which the saga consumes to transition to compensation. These errors:

- Do NOT throw exceptions.
- Do NOT dead-letter (the consumer commits the offset after publishing the failure event).
- Feed the saga compensation matrix documented in [checkout-saga.md § 6](checkout-saga.md).

### 2.3 Bug-class (5xx / DLQ)

`DataIntegrityException` (defined in [`platform/Platform.SharedKernel/Exceptions/DataIntegrityException.cs`](../../platform/Platform.SharedKernel/Exceptions/DataIntegrityException.cs)) and similar `CriticalException`-derived throws represent conditions that **should never occur in a working system**. Example: the saga tells Inventory to confirm a reservation that was already released; the aggregate throws. These errors:

- Surface as HTTP 5xx through the global exception middleware when raised in an HTTP pipeline.
- Route to the `.DLT` topic via [`DeadLetterMiddleware`](../../platform/Platform.KafkaFlow.DeadLetter/DeadLetterMiddleware.cs) when raised in a KafkaFlow consumer.
- Emit an ops alert (Grafana → PagerDuty via `kafka.consumer.dlq.messages` metric — see [kafka-dlq-strategy.md § 6](kafka-dlq-strategy.md)).

### 2.4 Infrastructure / upstream

Temporary failures outside the BC's control — upstream service returning 5xx, Kafka produce failing, DB connection timing out. Example: `BasketErrors.CatalogUnavailable` surfaces when Basket's `ProductCatalogHttpAdapter` hits a network error or Catalog returns 5xx. These errors:

- Use **client retry** (the HTTP caller or outbox relay retries).
- Do NOT dead-letter on first failure — only after the configured retry policy is exhausted (see [kafka-dlq-strategy.md § 2](kafka-dlq-strategy.md) for Kafka side; Polly policy in [HttpClientsDependencyInjection](../../src/Weather.Infrastructure/Common/HttpClientsDependencyInjection.cs) for HTTP side).
- Map to HTTP 503 (Service Unavailable) when surfaced through an API.

---

## 3. Per-BC Error Class Specifications

> Implementation-note format: the snippets below are sketches only. Implementation agents write the real code using `Platform.SharedKernel.Errors.ValidationError` (or a BC-specific `IError` type) as demonstrated in [`src/Weather.Domain/Alerts/Errors/WeatherAlertErrors.cs`](../../src/Weather.Domain/Alerts/Errors/WeatherAlertErrors.cs).

### 3.1 `BasketErrors` (in `Basket.Domain.Errors`)

```csharp
// Sketch — implementation agents write the final shape
public static class BasketErrors
{
    public static ValidationError EmptyBasket() =>
        new("Basket", "Basket must contain at least one item to checkout.", "Basket.Empty");

    public static ValidationError MaxItemsReached(int max) =>
        new("Items", $"Basket cannot hold more than {max} items.", "Basket.MaxItemsReached");

    public static ValidationError InvalidQuantity() =>
        new("Quantity", "Item quantity must be at least 1.", "Basket.InvalidQuantity");

    public static ValidationError CurrencyMismatch() =>
        new("Currency", "All basket items must share the same currency.", "Basket.CurrencyMismatch");

    public static ValidationError CatalogUnavailable() =>
        new("Catalog", "Product catalog is temporarily unavailable.", "Basket.CatalogUnavailable");

    public static ValidationError ProductNotFound(Guid productId) =>
        new("ProductId", $"Product '{productId}' does not exist.", "Basket.ProductNotFound");
}

public static class BasketItemErrors
{
    public static ValidationError InvalidQuantity() =>
        new("Quantity", "Quantity must be at least 1.", "BasketItem.InvalidQuantity");
}

// Separate error (typed, not a ValidationError) — concurrency is a pipeline concern
public sealed record BasketConcurrencyError(Guid UserId, int Expected, int Actual) : IError
{
    public string Message => $"Basket {UserId} version conflict: expected {Expected}, found {Actual}.";
    public Dictionary<string, object> Metadata { get; } = new() { ["ErrorCode"] = "Basket.Concurrency" };
    public List<IError> Reasons { get; } = [];
}
```

### 3.2 `CatalogErrors` / `ProductErrors` / `CategoryErrors` (in `Catalog.Domain.Errors`)

Split per aggregate for locality (`ProductErrors.cs`, `CategoryErrors.cs`, value-object errors in `*/Errors/*.cs` following the [catalog.md](catalog.md) per-VO error lists). Each method returns `ValidationError` with a unique `errorCode` identifier (`Product.CannotRepriceDiscontinued`, `Category.HasDependents`, etc.).

Value-object errors (already enumerated in [catalog.md](catalog.md)):
- `SkuErrors.Empty` / `TooLong(max: 32)` / `InvalidCharacters`
- `MoneyErrors.AmountMustBePositive` / `InvalidCurrencyCode`
- `ProductNameErrors.Empty` / `TooLong(max: 200)`
- `ProductDescriptionErrors.TooLong(max: 4000)`
- `DimensionsErrors.NonPositiveDimension` / `UnsupportedUnit`
- `CategoryPathErrors.Malformed` / `MaxDepthExceeded(max: 5)`
- `ImageReferenceErrors.InvalidUrl` / `AltTextEmpty` / `NegativeDisplayOrder`
- `BrandNameErrors.Empty` / `TooLong(max: 100)`

Aggregate-level errors raised by Application-layer command/query handlers (HTTP mapping per § 4):
- `ProductErrors.CategoryIdRequired` — 422 Unprocessable (user input; aggregate rejects empty category reference at `Product.Create`).
- `ProductErrors.CannotRepriceDiscontinued` — 409 Conflict (precondition failure inside `Product.UpdatePrice`).
- `ProductErrors.CannotModifyDiscontinued` — 409 Conflict (precondition failure inside `Product.Describe`).
- `ProductErrors.ReasonRequired` — 422 Unprocessable (empty discontinue reason in `Product.Discontinue`).
- `ProductErrors.ReactivationRequiresAdminFlag` — 422 Unprocessable (missing admin flag on `Product.Reactivate`).
- `ProductErrors.NotFound(productId)` — 404 Not Found (handler-level lookup miss on any product-addressing command or query).
- `ProductErrors.SkuAlreadyExists(sku)` — 409 Conflict (uniqueness violation pre-checked inside `CreateProductCommandHandler`).
- `CategoryErrors.NameRequired` — 422 Unprocessable (`Category.Create` / `Category.Rename` rejects empty name).
- `CategoryErrors.NameTooLong(max)` — 422 Unprocessable (name exceeds `Category.MaxNameLength`).
- `CategoryErrors.MaxDepthExceeded(max)` — 422 Unprocessable (path depth > 5 on `Category.Create` / `Reparent`).
- `CategoryErrors.CannotParentToSelf` — 422 Unprocessable (`Reparent` called with `NewParentCategoryId == Id`).
- `CategoryErrors.NotFound(categoryId)` — 404 Not Found (handler-level lookup miss on any category-addressing command or query).
- `CategoryErrors.ParentNotFound(parentCategoryId)` — 404 Not Found (parent lookup miss in `CreateCategoryCommandHandler` or `ReparentCategoryCommandHandler`).
- `CategoryErrors.ReparentCreatesCycle(categoryId, newParentCategoryId)` — 422 Unprocessable (the candidate parent is the category itself or one of its descendants — surfaced by `CategoryAncestryService.WouldCreateCycleAsync` before `Category.Reparent` runs).

> Category dependency-based errors (`HasChildren`, `HasProducts`) are deferred alongside the `DeleteCategoryCommand` — see the follow-up milestone to Catalog M3.
> Product image-collection errors (`DuplicateImageDisplayOrder`, `ImageNotFound`) are deferred alongside the `AddProductImageCommand` / `RemoveProductImageCommand` handlers.

### 3.3 `OrderingErrors` (in `Ordering.Domain.Errors`)

```csharp
public static class OrderingErrors
{
    public static ValidationError CannotCancelInStatus(string status) =>
        new("Status", $"Order in status '{status}' cannot be cancelled.", "Order.CannotCancelInStatus");

    public static ValidationError OrderNotFound(Guid orderId) =>
        new("OrderId", $"Order '{orderId}' does not exist.", "Order.NotFound");
}
```

All invalid **FSM transitions** from saga commands are bug-class — they do not use `OrderingErrors`; they throw `DataIntegrityException` from `Order.MarkStockReserved` / `MarkPaymentCompleted` / `Confirm` / etc. via the `Throw.If(!Status.CanTransitionTo(...))` guard. See [ordering.md § State transitions](ordering.md) and [use-cases.md § 3.3](use-cases.md).

### 3.4 `InventoryErrors` (in `Inventory.Domain.Errors`)

```csharp
public sealed record InsufficientStockError(Guid ProductId, int Requested, int Available) : IError
{
    public string Message =>
        $"Stock item {ProductId}: requested {Requested}, available {Available}.";
    public Dictionary<string, object> Metadata { get; } = new()
    {
        ["ErrorCode"] = "Inventory.InsufficientStock",
        ["ProductId"] = ProductId,
        ["Requested"] = Requested,
        ["Available"] = Available,
    };
    public List<IError> Reasons { get; } = [];
}

public sealed record ConcurrencyError(Guid StreamId, int ExpectedVersion) : IError
{
    public string Message => $"Stream {StreamId} version conflict at {ExpectedVersion}.";
    public Dictionary<string, object> Metadata { get; } = new() { ["ErrorCode"] = "Inventory.Concurrency" };
    public List<IError> Reasons { get; } = [];
}
```

The bug-class inventory conditions (`StockItemNotFound` on confirm/release for an already-released reservation, admin-adjust leading to negative stock, etc.) throw `DataIntegrityException` per [inventory.md § Command semantics](inventory.md) and are not modelled as `*Error` types.

### 3.5 `PaymentsErrors` (in `Payments.Domain.Errors`)

```csharp
public static class PaymentsErrors
{
    public static ValidationError PaymentNotFound(Guid paymentId) =>
        new("PaymentId", $"Payment '{paymentId}' does not exist.", "Payments.NotFound");

    public static ValidationError InvalidAmount() =>
        new("Amount", "Payment amount must be strictly positive.", "Payments.InvalidAmount");

    public static ValidationError InvalidPaymentMethod() =>
        new("PaymentMethodId", "Payment method token is empty or exceeds 64 characters.", "Payments.InvalidPaymentMethod");

    public static ValidationError GatewayUnavailable() =>
        new("Gateway", "Payment gateway is temporarily unavailable.", "Payments.GatewayUnavailable");
}

// Typed error for gateway-business-failure path (consumed by saga to drive compensation)
public sealed record GatewayDeclinedError(string Reason, string? GatewayCode) : IError
{
    public string Message => $"Payment gateway declined: {Reason}" + (GatewayCode is null ? "" : $" ({GatewayCode}).");
    public Dictionary<string, object> Metadata { get; } = new() { ["ErrorCode"] = "Payments.GatewayDeclined" };
    public List<IError> Reasons { get; } = [];
}
```

Invalid FSM transitions on `PaymentTransaction` (e.g., calling `Capture` from `Failed`) are bug-class and throw `DataIntegrityException` — they do not use `PaymentsErrors`.

### 3.6 `InvoicingErrors` (in `Invoicing.Domain.Errors`)

```csharp
public static class InvoicingErrors
{
    public static ValidationError InvoiceNotFound(Guid invoiceId) =>
        new("InvoiceId", $"Invoice '{invoiceId}' does not exist.", "Invoicing.InvoiceNotFound");

    public static ValidationError InvoiceAlreadyIssued(Guid correlationId) =>
        new("CorrelationId", $"Invoice already issued for correlation '{correlationId}'.", "Invoicing.InvoiceAlreadyIssued");

    public static ValidationError PartialRefundNotSupportedV1() =>
        new("Amount", "Partial refunds are not supported in v1; credit notes must be full-amount.", "Invoicing.PartialRefundNotSupportedV1");

    public static ValidationError BlobUploadFailed() =>
        new("Blob", "Invoice PDF upload to object storage failed after retries.", "Invoicing.BlobUploadFailed");
}

// Typed errors for bug-class integrity violations — these are `IError`-shaped but surface through the DLT pipeline, not user-facing
public sealed record TotalMismatchError(decimal OrderTotal, decimal PaymentAmount, Guid CorrelationId) : IError
{
    public string Message =>
        $"Total mismatch on correlation {CorrelationId}: order total {OrderTotal}, payment amount {PaymentAmount}.";
    public Dictionary<string, object> Metadata { get; } = new() { ["ErrorCode"] = "Invoicing.TotalMismatch" };
    public List<IError> Reasons { get; } = [];
}

public sealed record PdfGenerationFailedError(string Detail) : IError
{
    public string Message => $"PDF generation failed: {Detail}";
    public Dictionary<string, object> Metadata { get; } = new() { ["ErrorCode"] = "Invoicing.PdfGenerationFailed" };
    public List<IError> Reasons { get; } = [];
}
```

Invariant violations on `Invoice` / `CreditNote` aggregates (e.g., issuing a credit note against a `Cancelled` invoice — I-CN-1) throw `DataIntegrityException`.

### 3.7 `SagaErrors` (in `SagaOrchestrators.Checkout.Errors`)

Saga-scoped errors are emitted as `FailureInfo` VO data on `CheckoutSagaState.Failure` (see [checkout-saga.md](checkout-saga.md) — `ErrorCode`, `ErrorMessage`, `AtStatus`, `FailedAtUtc`). The saga surfaces them as OpenTelemetry span attributes plus metric counters; per [master design § E.2](../eshop-master-design.md#e2-saga-terminal-events--decided-to-omit), no saga-terminal Kafka events are emitted in v1.

Canonical `ErrorCode` values (referenced in [ordering.md § FailureInfo](ordering.md)): `PAYMENT_FAILED`, `PAYMENT_TIMEOUT`, `STOCK_UNAVAILABLE`, `STOCK_TIMEOUT`, `CONFIRMATION_TIMEOUT`, `ORDER_CREATION_TIMEOUT`, `COMPENSATION_STUCK`.

---

## 4. HTTP Mapping Registration

The API-layer `ProblemDetailsFactory` (FastEndpoints + ASP.NET Core middleware) inspects `IError.Metadata["ErrorCode"]` and maps using this table:

| `ErrorCode` prefix / pattern | HTTP status | `type` (RFC 7807) |
|---|---|---|
| `Basket.Empty`, `Basket.MaxItemsReached`, `Basket.CatalogUnavailable` when classified as conflict, `Order.CannotCancelInStatus`, `Category.HasDependents`, `Product.CannotRepriceDiscontinued`, `Sku.AlreadyExists` | **409 Conflict** | `/errors/conflict` |
| `*.NotFound` (any BC) | **404 Not Found** | `/errors/not-found` |
| `*.InvalidQuantity`, `*.MaxDepthExceeded`, `*.CannotParentToSelf`, `Dimensions.*`, `Money.*`, any validator-produced `ValidationError` from FluentValidation | **422 Unprocessable Entity** | `/errors/validation` |
| `Basket.Concurrency`, `Inventory.Concurrency` (when surfaced to HTTP, rare — after retry exhaustion) | **409 Conflict** | `/errors/concurrency` |
| `Product.ReactivationRequiresAdminFlag` | **403 Forbidden** | `/errors/forbidden` |
| `Basket.CatalogUnavailable`, any upstream/infrastructure error | **503 Service Unavailable** | `/errors/upstream` |
| Uncaught `DataIntegrityException` / `CriticalException` | **500 Internal Server Error** | `/errors/internal` |
| Uncaught generic `Exception` | **500 Internal Server Error** | `/errors/internal` |

**Implementation detail:** the mapping is registered as a decorator over `ValidationBehavior` in each service's `ApplicationDependencyInjection.AddCqrsHandlerBehaviors` chain (see [master design § Appendix B.3](../eshop-master-design.md)), and as a fallback middleware in `Api/Program.cs`. The FluentResults `Result` returned from a handler is inspected in the endpoint wrapper:

```csharp
// Conceptual — FastEndpoints endpoint HandleAsync sketch
var result = await _sender.Send(command, ct);
if (result.IsSuccess) return TypedResults.Ok(result.Value);
return result.ToProblemDetails();  // extension method that consults the mapping table
```

---

## 5. Saga Compensation Semantics

Cross-reference to [checkout-saga.md § 6 Compensation matrix](checkout-saga.md) — the table below summarizes which errors trigger which compensation path:

| Upstream error (on saga-consumed event / command reply) | Saga state transition |
|---|---|
| `OrderCreatedEvent` never arrives within timeout | `AwaitingOrderCreation` → `Failed` (no side effects) |
| `StockReservationFailedEvent(InsufficientStockError)` | `AwaitingStockReservation` → `CompensatingStockReservations` → release any prior reservations for this `CorrelationId` → `CancelOrder` → `Failed` |
| `StockReservationFailedEvent` with bug-class error (reservation ID clash etc.) | Same as above — compensation path is identical; ops alert additionally raised due to DLT message on the ORIGINATING consumer |
| `PaymentFailedEvent` | `AwaitingPayment` → `CompensatingStockReservations` → `CancelOrder` → `Failed` |
| Confirmation fails (order FSM rejects `Confirm` after payment succeeded) | `AwaitingConfirmation` → `CompensatingPayment` (`RequestRefund` via PaymentProcessingSaga) → `CompensatingStockReservations` → `CancelOrder` → `Compensated` |
| `ReservationReleasedEvent` never arrives during compensation within 300 s | `CompensatingStockReservations` → `CompensationStuck` (ops alert, manual intervention via [saga-stuck runbook](checkout-saga.md)) |
| `PaymentRefundedEvent` never arrives during `CompensatingPayment` within 300 s | `CompensatingPayment` → `CompensationStuck` |

Bug-class errors inside Ordering / Inventory that cause a saga-command consumer to DLT (e.g., `DataIntegrityException` on `ConfirmReservationCommand` for an already-confirmed reservation) leave the saga in its *Awaiting* state until the corresponding response event is produced. If no response event arrives before the state timeout, the saga transitions to its compensation path using the generic timeout error code (`STOCK_TIMEOUT` / `CONFIRMATION_TIMEOUT`). The DLT alert and the saga timeout are **complementary signals** — they frequently fire together for the same incident.

---

## 6. Cross-References

- [master-design § 11.1 Result pattern](../eshop-master-design.md) — top-level `Result` vs `DataIntegrityException` split
- [kafka-dlq-strategy.md](kafka-dlq-strategy.md) — DLT routing and alerting for Kafka errors
- [architecture-tests.md § 1.5](architecture-tests.md) — NetArchTest rules that enforce "no raw `ArgumentException`/`InvalidOperationException` for user errors"
- [use-cases.md](use-cases.md) — every `*Command` / `*Query` documents the `Result.Fail(...)` paths it can return
- [catalog.md](catalog.md), [basket.md](basket.md), [ordering.md](ordering.md), [inventory.md](inventory.md) — BC-chapter "Errors" subsections enumerating VO-level error names
- [checkout-saga.md § 6](checkout-saga.md) — full compensation matrix backing § 5 above
