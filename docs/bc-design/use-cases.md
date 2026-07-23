# Use Case Catalog — eShop Reference Solution

> **Companion file:** [bff.md](./bff.md) (BFF aggregation endpoints)
> **Per-BC design docs:** [catalog.md](./catalog.md), [basket.md](./basket.md), [ordering.md](./ordering.md), [inventory.md](./inventory.md), [invoicing.md](./invoicing.md), [payments.md](./payments.md)
> **Master design:** [eshop-master-design.md § 7](../eshop-master-design.md)

This document enumerates every command and query exposed by the Catalog, Basket, Ordering, Inventory, and Invoicing services. (Payments exposes only an admin-read HTTP surface plus Kafka-driven commands, catalogued in [`payments.md` § 8–9](payments.md).) For each use case it specifies: HTTP route, authorization, `Platform.CQRS` interface, request shape, response shape, validator rules, handler flow, and emitted internal events. It also documents how saga-issued commands enter each service.

---

## Conventions

### HTTP surface

- **Commands** use `POST` / `PUT` / `DELETE` under `/api/{service}/{resource}[/{action}]`.
- **Queries** use `GET` under `/api/{service}/{resource}[/{subresource}]`.
- **Pagination** parameters: `PageNumber` (1-indexed, default 1), `PageSize` (default 20, max 100). Responses include `Total`, `PageNumber`, `PageSize`, `Items[]`.
- **Dates** are always `DateTimeOffset` UTC (serialized as ISO-8601 with `Z`).
- **IDs** are `Guid` (`D` format, lowercase 36 chars).
- **Money** is represented on the wire as `{ Amount: decimal, Currency: string (ISO 4217, 3 letters) }`.

### Validation

- **FluentValidation** `AbstractValidator<TCommand>` / `AbstractValidator<TQuery>` classes, discovered by the `ValidationBehavior` in `platform/Platform.CQRS/Behaviors/ValidationBehavior.cs`.
- Domain-layer `Result<T>` failures that originate from value-object `Create` factories surface as `422 Unprocessable Entity` via `Send.SendErrorResponseAsync` (the codebase-wide `Result<T>`-failure → ProblemDetails convention).
- **HTTP 409 Conflict** for state-transition violations (e.g., cancelling a shipped order).
- **HTTP 404 Not Found** for missing aggregate lookups by id.

### Idempotency-Key header (all mutating HTTP commands)

All HTTP **mutating** commands (`POST`, `PUT`, `DELETE`) MUST accept an optional `Idempotency-Key` header. Clients SHOULD supply a UUID (v7 recommended — matches the solution's UUID v7 id convention) on every retry-safe command. Applies to every command under `POST /api/{basket,catalog,ordering,inventory}/**` including `AddItemToBasket`, `ChangeItemQuantity`, `CheckoutBasket`, `CreateProduct`, `UpdateProductPrice`, `DiscontinueProduct`, `CreateCategory`, `ReceiveStock`, `AdjustStock`, `MarkOrderShipped`, `MarkOrderDelivered`, etc. Saga-issued commands (consumed from Kafka) use MessageId via the Inbox middleware and do NOT need the header.

Mechanism:

1. Client generates `Guid.CreateVersion7()` and sends `Idempotency-Key: {guid}` header.
2. Per-service `idempotency_keys` table: `{Key uuid PK, RequestHash text, ResponseCode int, ResponseBody jsonb, StoredAtUtc timestamptz}`. 24-hour retention (background cleanup job).
3. An ASP.NET Core middleware wraps every mutating endpoint:
   - On request: lookup by `Key`. If found + `RequestHash` matches → return cached response (cache hit). If found + hash mismatches → HTTP 422 `IdempotencyKeyConflict` (same key reused for different request body).
   - On miss: execute handler; store `(Key, Hash, ResponseCode, ResponseBody)` transactionally with the handler's DbContext save.
4. Clients that do not supply the header get normal semantics (no dedupe). For strictly-non-retryable operations (rare — e.g., admin one-shots), endpoints may document "Idempotency-Key recommended but not enforced".

Error if missing on a strictly-critical endpoint (decided per endpoint — default: header is optional but recommended):
- `POST /api/v1/basket/checkout` **requires** `Idempotency-Key` → 400 Bad Request if missing (double-checkout is the most damaging retry mistake).
- Other mutating commands accept-without-header.

Implementation: one shared middleware `Platform.Api.Idempotency.IdempotencyMiddleware` (to be added to Platform in the implementation wave) + per-service `idempotency_keys` table via the existing migration pattern.

### Authentication & authorization

- All endpoints require Keycloak-issued JWT **unless** marked `AllowAnonymous` (public reads — catalog browse + Inventory stock-availability overlays, §§ 4.4.1–4.4.2).
- `UserId` / `BuyerId` from `ClaimTypes.NameIdentifier` via FastEndpoints `[FromClaim(ClaimTypes.NameIdentifier, isRequired: true, removeFromSchema: true)]`.
- Admin / ops endpoints require policy `AuthPolicies.Admin` (follows the codebase-wide `AuthPolicies`-gated admin endpoint-group pattern).
- Row-level authorization (buyer reads own order only) is enforced in the query handler against `BuyerId == claim.sub` with an admin bypass.

### Handler pattern

- Commands implement `Platform.CQRS.ICommand` (no response) or `ICommand<TResponse>` (with response).
- Queries implement `Platform.CQRS.IQuery<TResponse>`.
- Handlers implement the matching `ICommandHandler<TCommand>[,TResponse]` / `IQueryHandler<TQuery,TResponse>` interface and return `Task<Result>` / `Task<Result<TResponse>>` from `FluentResults`.
- Aggregate transitions that raise internal domain events are flushed via `DbContext.SaveChangesAsync(ct)` — the `DispatchDomainEventsInterceptor` fans out to `IDomainEventHandler<T>`s in-process.
- External events are transactionally appended to the outbox by in-process domain-event handlers (the codebase-wide outbox-publisher pattern).

### How saga-issued commands reach a service — cross-cutting plumbing

The Checkout saga **does not** mutate any service's database directly. It publishes Avro-serialized command messages onto a `{service}-commands` Kafka topic. Each target service runs a dedicated KafkaFlow consumer that:

1. Deserializes the Avro command via `Platform.Avro.UniversalSerDes`.
2. Wraps the handler call in the platform **Inbox middleware** (`Platform.ReliableMessaging.Inbox.EFCore`) keyed on the Kafka message id — duplicate deliveries are idempotently skipped.
3. Constructs the corresponding internal `ICommand` DTO (plain CLR object, decoupled from the Avro contract).
4. Dispatches through `ICommandHandler<TCommand>` resolved from DI — the same handler that would be invoked via HTTP.
5. On success, lets KafkaFlow commit the offset. On `Result.Fail` that is NOT a transient concurrency error, throws `InvalidOperationException` to route the message to the Dead Letter Topic (DLT) middleware.

Each service below lists its inbound command topic (where applicable) and the Kafka handler mapping. The saga-side producer contract for these command topics is owned by **Stage 2 Agent 6** (Checkout saga designer); this document fixes the consumer-side command DTO shapes.

---

## 1. Catalog Service Use Cases

**Base HTTP path:** `/api/v1/catalog/`
**Storage:** PostgreSQL schema `catalog` (write model + `product_search_view` projection).
**Consumes (Kafka):** *none in v1*. Catalog is the upstream authority; it has no saga-driven inbound command path.
**Produces (Kafka):** `catalog.products`, `catalog.categories` via outbox.

### 1.1 Commands

#### 1.1.1 `CreateProductCommand`

- **HTTP:** `POST /api/v1/catalog/products`
- **Authorization:** `AuthPolicies.Admin` (only catalog operators may create products).
- **Interface:** `ICommand<Guid>` (returns the created `ProductId`).
- **Request shape:**
  ```
  {
    "sku": "string (1-32 chars, ^[A-Za-z0-9][A-Za-z0-9-]*$, normalized uppercase)",
    "name": "string (1-200 chars, whitespace collapsed)",
    "description": "string (0-4000 chars; HTML rejected)",
    "categoryId": "Guid (must exist in catalog.categories)",
    "brand": "string (1-100 chars)",
    "price": { "amount": "decimal (> 0)", "currency": "string (ISO 4217, 3 uppercase letters)" },
    "dimensions": {
      "length": "decimal (> 0)",
      "width": "decimal (> 0)",
      "height": "decimal (> 0)",
      "unit": "string (cm|mm|in)"
    } | null,
    "images": [
      {
        "url": "string (absolute URL)",
        "altText": "string (1-200 chars)",
        "displayOrder": "int (>= 0)"
      }
    ]
  }
  ```
- **Response:** `{ "productId": "Guid" }` — HTTP 201 Created with `Location: /api/v1/catalog/products/{productId}`.
- **Handler class:** `CreateProductCommandHandler` in `Catalog.Application.Products.CreateProduct`.
- **Validator rules (`CreateProductCommandValidator`):**
  - `Sku` — NotEmpty; Length(1,32); Matches `^[A-Za-z0-9][A-Za-z0-9-]*$`.
  - `Name` — NotEmpty; MaximumLength(200).
  - `Description` — MaximumLength(4000); MustNotContainHtml (custom rule scans for `<` followed by letter).
  - `CategoryId` — NotEmpty (must be non-`Guid.Empty`).
  - `Brand` — NotEmpty; MaximumLength(100).
  - `Price.Amount` — GreaterThan(0).
  - `Price.Currency` — NotEmpty; Matches `^[A-Z]{3}$`.
  - `Dimensions` — conditional: when present, all three lengths > 0, `Unit` in whitelist `{cm, mm, in}`.
  - `Images` — each `Url` MustBeAbsoluteUri; `AltText` NotEmpty, MaximumLength(200); `DisplayOrder` GreaterThanOrEqualTo(0). Collection-level: at most one image per `DisplayOrder` value.
- **Flow:**
  1. Validate SKU uniqueness: `await _dbContext.Products.AnyAsync(p => p.Sku.Value == command.Sku.ToUpperInvariant(), ct)`. If exists, return `Result.Fail(ProductErrors.SkuAlreadyExists(command.Sku))`.
  2. Validate category exists: `await _dbContext.Categories.AnyAsync(c => c.Id == command.CategoryId, ct)`. If not, return `Result.Fail(CategoryErrors.NotFound(command.CategoryId))`.
  3. Build value objects via their `Create` factories; propagate `Result.Fail` on any failure.
  4. Call `Product.Create(sku, name, description, categoryId, brand, price, dimensions, images)`.
  5. `_dbContext.Products.Add(product); await _dbContext.SaveChangesAsync(ct);`.
  6. Return `Result.Ok(product.Id)`.
- **Emits internal event(s):** `ProductCreatedDomainEvent` (raised inside `Product.Create`). Handler fan-out:
  - `ProductCreatedProjectionDomainEventHandler` — INSERT row into `catalog.product_search_view`.
  - `ProductCreatedOutboxPublisherDomainEventHandler` — writes `ProductCreatedEvent` (Avro) to outbox for topic `catalog.products`.

#### 1.1.2 `UpdateProductPriceCommand`

- **HTTP:** `PUT /api/v1/catalog/products/{productId}/price`
- **Authorization:** `AuthPolicies.Admin`.
- **Interface:** `ICommand` (no response body; 204 No Content).
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)",
    "newPrice": { "amount": "decimal (> 0)", "currency": "string (ISO 4217)" }
  }
  ```
- **Response:** 204 No Content on success; 404 on missing product; 422 on VO validation failure; 409 if product is `Discontinued`.
- **Handler class:** `UpdateProductPriceCommandHandler`.
- **Validator rules:**
  - `ProductId` — NotEmpty.
  - `NewPrice.Amount` — GreaterThan(0).
  - `NewPrice.Currency` — Matches `^[A-Z]{3}$`.
- **Flow:**
  1. Load product: `_dbContext.Products.FirstOrDefaultAsync(p => p.Id == productId, ct)`. If null, return `Result.Fail(ProductErrors.NotFound)`.
  2. Build `Money.Create(newPrice.Amount, newPrice.Currency)`; cascade `Result.Fail` on invalid.
  3. Call `product.UpdatePrice(money, _timeProvider.GetUtcNow())`. Surface any `Result.Fail` (e.g. `CannotRepriceDiscontinued`, `CannotChangePriceCurrency`).
  4. `await _dbContext.SaveChangesAsync(ct);`.
- **Emits internal event(s):** `ProductPriceChangedDomainEvent` (if price actually changed; no-op otherwise). Handler fan-out:
  - `ProductPriceChangedProjectionDomainEventHandler` — UPDATE `PriceAmount`, `LastUpdatedAtUtc`.
  - `ProductOutboxPublisherDomainEventHandler` — writes `ProductPriceChangedEvent` (Avro) to outbox for topic `catalog.products`.

#### 1.1.3 `DescribeProductCommand`

- **HTTP:** `PUT /api/v1/catalog/products/{productId}/description`
- **Authorization:** `AuthPolicies.Admin`.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)",
    "newDescription": "string (0-4000 chars)"
  }
  ```
- **Response:** 204 on success; 404 if missing; 409 if `Discontinued`; 422 on validation failure.
- **Handler class:** `DescribeProductCommandHandler`.
- **Validator rules:**
  - `ProductId` — NotEmpty.
  - `NewDescription` — MaximumLength(4000). Null is rejected (use empty string to clear).
  - `NewDescription` — MustNotContainHtml.
- **Flow:**
  1. Load product; 404 if missing.
  2. Build `ProductDescription.Create(newDescription)`; cascade `Result.Fail`.
  3. Call `product.Describe(description)`.
  4. `SaveChangesAsync`.
- **Emits internal event(s):** `ProductDescribedDomainEvent`. Fan-out:
  - `ProductDescribedProjectionDomainEventHandler` — UPDATE `Description`, `LastUpdatedAtUtc`.
  - *No external event* — described in `catalog.md` as deliberately not-emitted in v1.

#### 1.1.4 `DiscontinueProductCommand`

- **HTTP:** `POST /api/v1/catalog/products/{productId}/discontinue`
- **Authorization:** `AuthPolicies.Admin`.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)",
    "reason": "string (1-500 chars)"
  }
  ```
- **Response:** 204 on success; 404 if missing; 409 if already discontinued or not in `Active` status; 422 if reason empty.
- **Handler class:** `DiscontinueProductCommandHandler`.
- **Validator rules:**
  - `ProductId` — NotEmpty.
  - `Reason` — NotEmpty; MaximumLength(500).
- **Flow:**
  1. Load product; 404 if missing.
  2. Call `product.Discontinue(reason)`.
  3. `SaveChangesAsync`.
- **Emits internal event(s):** `ProductDiscontinuedDomainEvent`. Fan-out:
  - `ProductDiscontinuedProjectionDomainEventHandler` — UPDATE `Status`, `LastUpdatedAtUtc`.
  - `ProductOutboxPublisherDomainEventHandler` — writes `ProductDiscontinuedEvent` (Avro) to outbox for topic `catalog.products`.

#### 1.1.5 `ReactivateProductCommand`

- **HTTP:** `POST /api/v1/catalog/products/{productId}/reactivate`
- **Authorization:** `AuthPolicies.Admin` — reactivation is an admin override of a discontinued-product state.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)",
    "adminReactivation": "bool (must be true)"
  }
  ```
- **Response:** 204 on success; 404 if missing; 409 if product is not `Discontinued`; 422 if `adminReactivation` is false.
- **Handler class:** `ReactivateProductCommandHandler`.
- **Validator rules:**
  - `ProductId` — NotEmpty.
  - `AdminReactivation` — Equal(true) — the flag MUST be true; `ProductErrors.ReactivationRequiresAdminFlag()` otherwise.
- **Flow:**
  1. Load product; 404 if missing.
  2. Call `product.Reactivate(command.AdminReactivation)`; propagate any `Result.Fail`.
  3. `SaveChangesAsync`.
- **Emits internal event(s):** `ProductReactivatedDomainEvent`. Fan-out:
  - `ProductReactivatedProjectionDomainEventHandler` — UPDATE `Status`, `LastUpdatedAtUtc`.
  - *No external event* in v1.

#### 1.1.6 `AddProductImageCommand` — **removed (out of reference-repo scope)**

Post-creation image management was never built in `Catalog.Api` — the `Product` aggregate carries only the images supplied at `CreateProduct` (no `AddImage`/`RemoveImage` methods, no `ProductImageAdded`/`RemovedDomainEvent` in code). The add/remove-image command pair is cut; reinstate only when a real consumer needs post-creation image editing.

#### 1.1.7 `RemoveProductImageCommand` — **removed (out of reference-repo scope)**

See § 1.1.6 — the add/remove-image pair was never implemented and is cut together.

#### 1.1.8 `CreateCategoryCommand`

- **HTTP:** `POST /api/v1/catalog/categories`
- **Authorization:** `AuthPolicies.Admin`.
- **Interface:** `ICommand<Guid>` — returns the created `CategoryId`.
- **Request shape:**
  ```
  {
    "name": "string (1-100 chars)",
    "parentCategoryId": "Guid | null"
  }
  ```
- **Response:** 201 Created `{ "categoryId": "Guid" }` with `Location: /api/v1/catalog/categories/{categoryId}`; 404 if `ParentCategoryId` provided but not found; 422 if new path would exceed depth 5 or slug is malformed.
- **Handler class:** `CreateCategoryCommandHandler`.
- **Validator rules:**
  - `Name` — NotEmpty; MaximumLength(100).
  - `ParentCategoryId` — optional; when provided, `NotEqual(Guid.Empty)`.
- **Flow:**
  1. If `ParentCategoryId != null`, load parent category: `_dbContext.Categories.FirstOrDefaultAsync(c => c.Id == parentCategoryId, ct)`. If missing, return `Result.Fail(CategoryErrors.ParentNotFound)`.
  2. Call `Category.Create(name, parentCategoryId, parent?.Path)` — enforces depth ≤ 5, computes new `CategoryPath`. Cascade `Result.Fail`.
  3. `_dbContext.Categories.Add(category); await _dbContext.SaveChangesAsync(ct);`.
  4. Return `Result.Ok(category.Id)`.
- **Emits internal event(s):** `CategoryCreatedDomainEvent`. Fan-out:
  - `CategoryCreatedProjectionDomainEventHandler` — no-op placeholder for future breadcrumb seeding (per `catalog.md` § 9; see [roadmap.md § 2.3 Catalog](../roadmap.md)).
  - `CategoryCreatedOutboxPublisherDomainEventHandler` — writes `CategoryCreatedEvent` (Avro) to outbox for topic `catalog.categories`.

#### 1.1.9 `ReparentCategoryCommand`

- **HTTP:** `PUT /api/v1/catalog/categories/{categoryId}/reparent`
- **Authorization:** `AuthPolicies.Admin`.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "categoryId": "Guid (from route)",
    "newParentCategoryId": "Guid | null"
  }
  ```
- **Response:** 204 on success; 404 if category or new parent missing; 409 if reparent creates a cycle or new path would exceed depth 5; 422 on self-parent attempt (`CategoryId == NewParentCategoryId`).
- **Handler class:** `ReparentCategoryCommandHandler`.
- **Validator rules:**
  - `CategoryId` — NotEmpty.
  - `NewParentCategoryId` — optional; if provided, `NotEqual(command.CategoryId)` (self-parent check).
- **Flow:**
  1. Load category; 404 if missing.
  2. If `NewParentCategoryId != null`, load new parent; 404 if missing.
  3. Run `CategoryAncestryService.WouldCreateCycle(categoryId, newParentCategoryId)` — `Result.Fail(CategoryErrors.ReparentCreatesCycle)` on cycle detected.
  4. Call `category.Reparent(newParentCategoryId, newParent?.Path)`; cascade `Result.Fail` for depth violations.
  5. Descendants' paths are updated by `CategoryPathService` in the same unit-of-work (bulk UPDATE via raw SQL).
  6. `SaveChangesAsync`.
- **Emits internal event(s):** `CategoryReparentedDomainEvent`. Fan-out:
  - `CategoryReparentedProjectionDomainEventHandler` — log-only seam; the descendant-row cascade (bulk UPDATE of `CategoryPath` + `CategoryBreadcrumb` for every affected `product_search_view` row) runs inside `CategoryPathService.RewriteDescendantPathsAsync` in the same UoW.
  - *No external event* in v1 (reserved for later — see [roadmap.md § 2.3 Catalog](../roadmap.md)).

#### 1.1.10 `DeleteCategoryCommand` — **removed (out of reference-repo scope)**

Category deletion was never built (`Catalog.Domain` has no `DeleteCategory` / `CategoryDeletedDomainEvent`). Categories are created and reparented but not deleted in v1; cut to keep the docs honest to the code.

### 1.2 Queries

#### 1.2.1 `GetProductByIdQuery`

- **HTTP:** `GET /api/v1/catalog/products/{productId}`
- **Authorization:** `AllowAnonymous` (public product detail).
- **Interface:** `IQuery<ProductDetailResponse>`.
- **Request shape (query params + route):**
  ```
  {
    "productId": "Guid (from route)"
  }
  ```
- **Response shape:**
  ```
  {
    "productId": "Guid",
    "sku": "string",
    "name": "string",
    "description": "string",
    "categoryId": "Guid",
    "categoryPath": "string",
    "categoryBreadcrumb": "string (e.g. 'Electronics > Computers > Laptops')",
    "brandName": "string",
    "price": { "amount": "decimal", "currency": "string" },
    "status": "string (Active|Discontinued)",
    "dimensions": { "length": "decimal", "width": "decimal", "height": "decimal", "unit": "string" } | null,
    "images": [ { "url": "string", "altText": "string", "displayOrder": "int" } ],
    "createdAtUtc": "DateTimeOffset",
    "lastUpdatedAtUtc": "DateTimeOffset"
  }
  ```
- **Handler class:** `GetProductByIdQueryHandler`.
- **Filter/paging:** none — single-row lookup.
- **Read source:** `catalog.product_search_view` (the denormalized projection). Missing row → `Result.Fail(ProductErrors.NotFound)`.

#### 1.2.2 `SearchProductsQuery`

- **HTTP:** `GET /api/v1/catalog/products` (products-collection root + query params — **not** a `/search` sub-path; matches `SearchProductsEndpoint`'s `Get(string.Empty)` under the `/catalog/products` group)
- **Authorization:** `AllowAnonymous`.
- **Interface:** `IQuery<SearchProductsResponse>`.
- **Request shape (query params):**
  ```
  {
    "text": "string? (full-text across name+description)",
    "categoryPathPrefix": "string? (e.g., '/electronics')",
    "minPrice": "decimal? (> 0 when provided)",
    "maxPrice": "decimal? (>= minPrice when provided)",
    "currency": "string? (ISO 4217; required when minPrice or maxPrice provided)",
    "status": "string? (Active|Discontinued; defaults to Active)",
    "pageNumber": "int (default 1, >= 1)",
    "pageSize": "int (default 20, 1..100)"
  }
  ```
- **Response shape:**
  ```
  {
    "total": "int",
    "pageNumber": "int",
    "pageSize": "int",
    "items": [
      {
        "productId": "Guid",
        "sku": "string",
        "name": "string",
        "categoryBreadcrumb": "string",
        "brandName": "string",
        "price": { "amount": "decimal", "currency": "string" },
        "status": "string",
        "primaryImageUrl": "string | null"
      }
    ]
  }
  ```
- **Handler class:** `SearchProductsQueryHandler`.
- **Validator rules (`SearchProductsQueryValidator`):**
  - `PageNumber` — GreaterThanOrEqualTo(1).
  - `PageSize` — InclusiveBetween(1, 100).
  - `MinPrice` — when provided, GreaterThan(0).
  - `MaxPrice` — when both min and max provided, GreaterThanOrEqualTo(command.MinPrice).
  - `Currency` — Matches `^[A-Z]{3}$` when provided; required when `MinPrice` or `MaxPrice` is set.
  - `CategoryPathPrefix` — when provided, Matches `^(/[a-z0-9][a-z0-9-]*){1,5}$`.
  - `Status` — MustBeValidSmartEnum (parses into `ProductStatus`).
- **Filter/paging:**
  - `WHERE Status = @status` (defaults to `Active`).
  - `WHERE CategoryPath LIKE @prefix || '%'` when `CategoryPathPrefix` is set.
  - `WHERE PriceAmount BETWEEN @min AND @max AND PriceCurrency = @currency` when price filter set.
  - `WHERE to_tsvector('english', Name || ' ' || Description) @@ to_tsquery('english', @text)` when `Text` is set.
  - `ORDER BY PriceAmount ASC, ProductId ASC` (deterministic tie-breaker).
  - `LIMIT @pageSize OFFSET ((@pageNumber - 1) * @pageSize)`.
- **Read source:** `catalog.product_search_view` (denormalized projection). No cache (per `catalog.md` § Infrastructure Notes: "FusionCache not applied to Catalog reads in v1 — the projection IS the cache").

#### 1.2.3 `GetCategoryTreeQuery`

- **HTTP:** `GET /api/v1/catalog/categories/tree`
- **Authorization:** `AllowAnonymous`.
- **Interface:** `IQuery<GetCategoryTreeResponse>`.
- **Request shape (query params):**
  ```
  {
    "rootCategoryId": "Guid? (when set, returns only the subtree rooted here; otherwise full tree)"
  }
  ```
- **Response shape:**
  ```
  {
    "nodes": [
      {
        "categoryId": "Guid",
        "name": "string",
        "path": "string",
        "parentCategoryId": "Guid | null",
        "depth": "int",
        "productCount": "int (count of Active products in THIS category node, not including descendants)"
      }
    ]
  }
  ```
- **Handler class:** `GetCategoryTreeQueryHandler`.
- **Filter/paging:** no paging (categories are bounded). When `RootCategoryId` is provided, filter `WHERE Path LIKE @rootPath || '%'`.
- **Read source:** `catalog.categories` JOIN count subquery on `catalog.product_search_view` grouped by `CategoryId`.

#### 1.2.4 `GetProductsByCategoryQuery`

- **HTTP:** `GET /api/v1/catalog/categories/{categoryId}/products`
- **Authorization:** `AllowAnonymous`.
- **Interface:** `IQuery<GetProductsByCategoryResponse>`.
- **Request shape (query params + route):**
  ```
  {
    "categoryId": "Guid (from route)",
    "includeDescendants": "bool (default false)",
    "pageNumber": "int (default 1)",
    "pageSize": "int (default 20, max 100)"
  }
  ```
- **Response shape:** same as `SearchProductsResponse` — paged list of product summaries.
- **Handler class:** `GetProductsByCategoryQueryHandler`.
- **Validator rules:**
  - `CategoryId` — NotEmpty.
  - `PageNumber` — GreaterThanOrEqualTo(1).
  - `PageSize` — InclusiveBetween(1, 100).
- **Filter/paging:**
  - When `IncludeDescendants == false` → `WHERE CategoryId = @categoryId`.
  - When `IncludeDescendants == true` → resolve `Path` from `catalog.categories`, then `WHERE CategoryPath LIKE @path || '%'`.
  - Default `Status = Active` (discontinued products are excluded from browse).
  - `ORDER BY Name ASC, ProductId ASC`.
- **Read source:** `catalog.product_search_view`.

### 1.3 Saga command intake

**None in v1.** Catalog does not consume any Kafka commands. The BC is the upstream authority; downstream services (Basket ACL, Inventory inbox consumer, BFF) react to its outbound events without issuing commands back.

---

## 2. Basket Service Use Cases

**Base HTTP path:** `/api/v1/basket/`
**Storage:** Redis (primary aggregate store via `IBasketRepository` → FusionCache `"basket"`). PostgreSQL schema `basket` holds only outbox/inbox tables.
**Consumes (Kafka):** *none in v1* (per `basket.md` § 13). Basket has no inbound commands; all mutations are HTTP-driven by the authenticated user.
**Produces (Kafka):** `basket.sessions` via outbox (one event: `BasketCheckoutInitiatedEvent`).

All commands below operate on the caller's own basket (keyed by JWT `sub` claim = `UserId`). Admin cross-user access is out of scope for v1.

### 2.1 Commands

#### 2.1.1 `AddItemToBasketCommand`

- **HTTP:** `POST /api/v1/basket/items`
- **Authorization:** Authenticated (any authenticated user). `UserId` from JWT claim.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "productId": "Guid (required, must exist in Catalog)",
    "quantity": "int (>= 1)",
    "userId": "Guid (from JWT claim; removed from schema)"
  }
  ```
- **Response:** 204 No Content on success; 404 if product not found in Catalog; 409 if basket would exceed 50 distinct items or has currency mismatch; 503 if Catalog is unreachable.
- **Handler class:** `AddItemToBasketCommandHandler` in `Basket.Application.Baskets.AddItem`.
- **Validator rules (`AddItemToBasketCommandValidator`):**
  - `UserId` — NotEmpty.
  - `ProductId` — NotEmpty.
  - `Quantity` — GreaterThanOrEqualTo(1); LessThanOrEqualTo(1000) (sanity cap on single-line qty).
- **Flow:**
  1. Call ACL: `_catalogPort.GetProductSnapshotAsync(productId, ct)` — returns `Result<ProductSnapshot>`. On `Result.Fail(BasketAclErrors.CatalogUnavailable)` or `ProductNotFound`, propagate.
  2. Load basket: `_basketRepository.GetByUserIdAsync(userId, ct)`. If null, create new via `Basket.Create(userId)`.
  3. Call `basket.AddItem(snapshot, command.Quantity)`; propagate `Result.Fail` (invalid quantity, max items, currency mismatch).
  4. Persist: `_basketRepository.SaveAsync(basket, expectedVersion, ct)`. On `BasketConcurrencyError`, retry exactly once (reload + re-apply); if still fails, return `Result.Fail`.
  5. Outbox write is NOT triggered here (internal events only; no external event for add).
- **Emits internal event(s):**
  - `BasketCreatedDomainEvent` (if this is the first item — new basket).
  - `ItemAddedToBasketDomainEvent` (always when add succeeds, including quantity bump on existing line).

#### 2.1.2 `RemoveItemFromBasketCommand`

- **HTTP:** `DELETE /api/v1/basket/items/{productId}`
- **Authorization:** Authenticated.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)",
    "userId": "Guid (from JWT claim)"
  }
  ```
- **Response:** 204 on success. Idempotent at every layer: removing a non-present product, or removing from a non-existent basket, both return 204 (no basket is lazily created).
- **Handler class:** `RemoveItemFromBasketCommandHandler`.
- **Validator rules:**
  - `UserId` — NotEmpty.
  - `ProductId` — NotEmpty.
- **Flow:**
  1. Load basket. If absent, return `Result.Ok()` → 204 (idempotent no-op; the aggregate is NOT lazily created on remove).
  2. Call `basket.RemoveItem(productId)` — idempotent; returns `Result.Ok()` even if item not present.
  3. `SaveAsync` (one retry on concurrency conflict) — only when the aggregate actually mutated.
- **Emits internal event(s):** `ItemRemovedFromBasketDomainEvent` (only when an item was actually removed; both no-op paths — basket absent and item absent — do not raise).

#### 2.1.3 `ChangeItemQuantityCommand`

- **HTTP:** `PUT /api/v1/basket/items/{productId}/quantity`
- **Authorization:** Authenticated.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)",
    "newQuantity": "int (>= 1, <= 1000)",
    "userId": "Guid (from JWT claim)"
  }
  ```
- **Response:** 204 on success; 404 if basket missing OR item not present; 422 on invalid quantity.
- **Handler class:** `ChangeItemQuantityCommandHandler`.
- **Validator rules:**
  - `UserId` — NotEmpty.
  - `ProductId` — NotEmpty.
  - `NewQuantity` — InclusiveBetween(1, 1000).
- **Flow:**
  1. Load basket; 404 if absent.
  2. Call `basket.ChangeQuantity(productId, newQuantity)`; `Result.Fail(BasketErrors.ItemNotFound(productId))` if item missing.
  3. `SaveAsync` (one retry on concurrency conflict).
- **Emits internal event(s):** `ItemQuantityChangedDomainEvent`.

#### 2.1.4 `RefreshBasketPricesCommand`

- **HTTP:** `POST /api/v1/basket/refresh-prices`
- **Authorization:** Authenticated.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "userId": "Guid (from JWT claim)"
  }
  ```
- **Response:** 204 on success. Idempotent no-op (still 204) when no basket exists or basket is empty — there is nothing to refresh and the ACL is not called. 503 if Catalog unreachable.
- **Handler class:** `RefreshBasketPricesCommandHandler`.
- **Validator rules:**
  - `UserId` — NotEmpty.
- **Flow:**
  1. Load basket. If absent or `Items.Count == 0`, return `Result.Ok()` → 204 (idempotent no-op; ACL is not consulted).
  2. Extract distinct `ProductId` list.
  3. Call `_catalogPort.GetManyAsync(productIds, ct)` — partial-tolerant per `basket.md` § 9.2. If full network failure, return `Result.Fail(BasketAclErrors.CatalogUnavailable)` (no partial refresh).
  4. Call `basket.RefreshPrices(snapshots)` — missing product ids are left untouched (existing snapshots retained).
  5. `SaveAsync` (one retry) — only when at least one snapshot price actually changed.
- **Emits internal event(s):** `BasketPricesRefreshedDomainEvent` (payload lists only items whose price actually changed; empty list → no event).

#### 2.1.5 `ClearBasketCommand`

- **HTTP:** `DELETE /api/v1/basket/items`
- **Authorization:** Authenticated.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "userId": "Guid (from JWT claim)"
  }
  ```
- **Response:** 204 on success. Idempotent no-op (still 204) when no basket exists, and again when the basket is already empty — both cases end the call with the same observable state. **Note**: a populated basket that is cleared is NOT deleted — it remains at `Version+1` with an empty `Items` array; TTL is refreshed. Only `CheckoutBasketCommand` deletes.
- **Handler class:** `ClearBasketCommandHandler`.
- **Validator rules:**
  - `UserId` — NotEmpty.
- **Flow:**
  1. Load basket. If absent, return `Result.Ok()` → 204 (idempotent no-op).
  2. Call `basket.Clear()` — emits no domain event when the basket was already empty.
  3. `SaveAsync` (one retry) — only when at least one item was actually removed.
- **Emits internal event(s):** `BasketClearedDomainEvent` (only when the basket actually had items to clear; both no-op paths — basket absent and basket already empty — do not raise).

#### 2.1.6 `CheckoutBasketCommand`

- **HTTP:** `POST /api/v1/basket/checkout`
- **Authorization:** Authenticated. User may only check out their own basket.
- **Interface:** `ICommand<Guid>` — returns the pre-assigned `OrderId` (UUID v7 allocated by the handler; ADR-0029).
- **Request shape:**
  ```
  {
    "userId": "Guid (from JWT claim)",
    "shippingAddress": {
      "street1": "string (non-empty)",
      "street2": "string? (optional)",
      "city": "string (non-empty)",
      "state": "string? (optional)",
      "postalCode": "string (non-empty)",
      "countryCode": "string (ISO 3166-1 alpha-2)"
    },
    "billingAddress": "{ same shape as shippingAddress }",
    "paymentMethodId": "Guid (reference to a saved payment method in Payments)"
  }
  ```
  **Address-sourcing convention** (per ADR-0005 + review addendum): Basket does NOT own addresses; the BFF / client collects them at checkout (possibly from a local address book or a form) and includes them in this command. Basket is a courier: it validates only basic shape (non-empty strings + ISO country code) and passes the data through into `BasketCheckoutInitiatedEvent`. The Ordering service re-snapshots them onto the `Order` aggregate at `CreateFromBasket` time; those snapshots are the authoritative record for fulfillment.
- **Response:** 202 Accepted `{ "orderId": "Guid" }` — the pre-assigned Order id; the checkout is now the saga's responsibility. 404 if basket missing; 409 if basket is empty (`BasketErrors.EmptyBasket`).
- **Handler class:** `CheckoutBasketCommandHandler` (allocates the `OrderId` via `Guid.CreateVersion7()`; ADR-0029).
- **Validator rules:**
  - `UserId` — NotEmpty.
  - `ShippingAddress.Street1`, `.City`, `.PostalCode` — NotEmpty.
  - `ShippingAddress.CountryCode` — NotEmpty; 2 chars; ISO 3166-1 alpha-2.
  - `BillingAddress` — same rules as ShippingAddress (may equal it).
  - `PaymentMethodId` — NotEmpty.
- **Flow:**
  1. Load basket; 404 if absent.
  2. Pre-assign the Order's `orderId` (`Guid.CreateVersion7()`, [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)), then call `basket.Checkout(orderId, shippingAddress, billingAddress, paymentMethodId, utcNow)` — raises `BasketCheckedOutDomainEvent` carrying the full `BasketSnapshot` plus the three courier fields (see [ADR-0005](../adr/0005-customer-data-in-ordering.md); Basket ferries addresses + payment method but does not own them). `Result.Fail(BasketErrors.EmptyBasket)` if `Items.Count == 0`.
  3. **Transactional boundary:**
     - Open `BasketDbContext` transaction (only for outbox write).
     - `BasketCheckoutInitiatedOutboxPublisherDomainEventHandler` writes `BasketCheckoutInitiatedEvent` (Avro) to outbox, topic `basket.sessions`, key = `{UserId}`.
     - Commit SQL transaction.
  4. After SQL commit succeeds: `_basketRepository.DeleteAsync(userId, ct)` — direct `IConnectionMultiplexer.GetDatabase().KeyDeleteAsync("basket:{userId}")`, bypassing FusionCache. If this step fails, the outbox is the source of truth; the stale Redis key is cleaned up on the next checkout attempt or at the 30-day TTL (parallel checkouts are serialized by optimistic concurrency on the basket version — see [basket.md § 6.4](basket.md)).
  5. Return `Result.Ok(orderId)`.
- **Emits internal event(s):** `BasketCheckedOutDomainEvent`. Fan-out:
  - `BasketCheckoutInitiatedOutboxPublisherDomainEventHandler` — writes external event (Avro) to outbox.

### 2.2 Queries

#### 2.2.1 `GetBasketByUserIdQuery`

- **HTTP:** `GET /api/v1/basket`
- **Authorization:** Authenticated. Returns only the caller's own basket (JWT `sub` = `UserId`).
- **Interface:** `IQuery<GetBasketResponse>`.
- **Request shape (query params + JWT claims):**
  ```
  {
    "userId": "Guid (from JWT claim; never from URL)"
  }
  ```
- **Response shape:**
  ```
  {
    "userId": "Guid",
    "version": "int",
    "items": [
      {
        "productId": "Guid",
        "sku": "string",
        "name": "string",
        "snapshotPrice": { "amount": "decimal", "currency": "string" },
        "quantity": "int",
        "capturedAtUtc": "DateTimeOffset",
        "lineTotal": { "amount": "decimal", "currency": "string" }
      }
    ],
    "total": { "amount": "decimal", "currency": "string" },
    "createdAtUtc": "DateTimeOffset",
    "lastModifiedAtUtc": "DateTimeOffset"
  }
  ```
- **Handler class:** `GetBasketByUserIdQueryHandler`.
- **Validator rules:**
  - `UserId` — NotEmpty.
- **Filter/paging:** none — always a single basket.
- **Read source:** Redis via `IBasketRepository.GetByUserIdAsync(userId, ct)`. If key is absent, return a `GetBasketResponse` with empty `Items` and `Version = 0` (200 OK with empty basket, not 404). This matches the domain's "basket is lazily created" lifecycle.

### 2.3 Saga command intake

**None in v1.** Basket neither consumes Kafka commands nor subscribes to any Kafka topic. Its only external dependency is the synchronous HTTP ACL to Catalog (see `basket.md` § 9).

---

## 3. Ordering Service Use Cases

**Base HTTP path:** `/api/v1/ordering/`
**Storage:** PostgreSQL schema `ordering`.
**Consumes (Kafka):** `ordering.order-commands` (saga → Ordering inbox consumer). See § 3.3 for command dispatch plumbing.
**Produces (Kafka):** `ordering.orders` via outbox (six external events).

### 3.1 Commands — saga-driven (no HTTP endpoints)

The following commands are **not** exposed via HTTP. Four of them enter the service through the `ordering.order-commands` Kafka topic (see § 3.3) — `CreateOrderCommand`, `ConfirmOrderCommand`, `CancelOrderCommand`, `MarkOrderFailedCommand`. The other two — `MarkOrderStockReservedCommand` and `MarkOrderPaymentCompletedCommand` — are saga-internal: the Wave-2 saga dispatches them in-process via `ISender` (no Kafka inbox consumer). All six are full `Platform.CQRS.ICommand` types with validators and handlers — the same code path is exercised by integration tests via `ISender`.

#### 3.1.1 `CreateOrderCommand`

- **HTTP:** *None* — consumed from `ordering.order-commands` topic. Triggered by saga on receipt of `BasketCheckoutInitiatedEvent`.
- **Authorization:** Kafka consumer runs under service identity. No per-request JWT.
- **Interface:** `ICommand<Guid>` — returns the created `OrderId` (used by saga to correlate).
- **Request shape:**
  ```
  {
    "orderId": "Guid (pre-assigned at checkout; the saga correlation key — ADR-0029)",
    "buyerId": "Guid (from BasketCheckoutInitiatedEvent.UserId)",
    "basket": {
      "buyerId": "Guid",
      "currency": "string (ISO 4217)",
      "items": [
        {
          "productId": "Guid",
          "sku": "string (1-64 chars)",
          "name": "string (1-200 chars)",
          "quantity": "int (>= 1)",
          "unitPriceAmount": "decimal (> 0)"
        }
      ]
    },
    "shippingAddress": {
      "street1": "string (1-200)",
      "street2": "string? (max 200)",
      "city": "string (1-100)",
      "state": "string? (max 100)",
      "postalCode": "string (1-20)",
      "countryCode": "string (exactly 2 uppercase letters)"
    },
    "billingAddress": "Address (same shape as shippingAddress)",
    "paymentMethodId": "Guid"
  }
  ```
- **Response:** `Result.Ok(orderId)` on success; `Result.Fail` is not expected (all preconditions originate from saga data that is contractually valid). Any validation failure throws `DataIntegrityException` → routed to DLT by consumer middleware.
- **Handler class:** `CreateOrderCommandHandler` in `Ordering.Application.Orders.CreateOrder`.
- **Validator rules (`CreateOrderCommandValidator`):**
  - `OrderId` — NotEmpty.
  - `BuyerId` — NotEmpty.
  - `Basket.Items` — NotEmpty; ForEach with inner item validator (`ProductId` NotEmpty, `Quantity >= 1`, `UnitPriceAmount > 0`, `Sku` length 1-64, `Name` length 1-200).
  - `Basket.Currency` — Matches `^[A-Z]{3}$`; must equal the currency of every item (enforced in domain; validator catches obvious schema violation).
  - `ShippingAddress.Street1`, `City`, `PostalCode`, `CountryCode` — NotEmpty. `CountryCode` matches `^[A-Z]{2}$`.
  - `BillingAddress` — same rules as shipping.
  - `PaymentMethodId` — NotEmpty.
- **Flow:**
  1. **Idempotency check:** look up the existing order by primary key (the pre-assigned `OrderId`): `await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == command.OrderId, ct)`. If present, return `Result.Ok(existing.Id)` (saga retry case).
  2. Build `Address` VOs (shipping + billing) via `Address.Create`; cascade.
  3. Translate `Basket` DTO into domain `BasketSnapshot` (pure in-application transformation, no Catalog call).
  4. Call `Order.CreateFromBasket(orderId, buyerId, basketSnapshot, shippingAddress, billingAddress, paymentMethodId, utcNow)` — this may throw `DataIntegrityException` on I-7/I-8/I-9 violations.
  5. `_dbContext.Orders.Add(order); await _dbContext.SaveChangesAsync(ct);`.
  6. Return `Result.Ok(order.Id)`.
- **Emits internal event(s):** `OrderCreatedDomainEvent`. Fan-out:
  - `OrderCreatedOutboxPublisherDomainEventHandler` — writes `OrderCreatedEvent` (Avro) to outbox for topic `ordering.orders`, key = `OrderId`.

#### 3.1.2 `MarkOrderStockReservedCommand`

- **HTTP:** *None* — saga-internal application-layer command (in-process dispatch from the Wave-2 saga; **not** on `ordering.order-commands`). Triggered by saga on `StockReservedEvent` from Inventory.
- **Authorization:** service identity.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "orderId": "Guid",
    "reservationId": "Guid (from Inventory)"
  }
  ```
- **Response:** `Result.Ok()` on success. Any transition failure (e.g., order already Cancelled) yields `DataIntegrityException` from the aggregate → DLT.
- **Handler class:** `MarkOrderStockReservedCommandHandler`.
- **Validator rules:**
  - `OrderId` — NotEmpty.
  - `ReservationId` — NotEmpty.
- **Flow:**
  1. Load order by id (inline `.Where(o => o.Id == orderId)` — pure PK lookup, no spec per [ADR-0022](../adr/0022-specification-pattern-adoption.md)). If missing, return `Result.Fail(OrderingErrors.NotFound)` (saga should not have advanced).
  2. Call `order.MarkStockReserved(reservationId, utcNow)` — precondition throws if `!Status.CanTransitionTo(StockReserved)` (this is a bug if it happens — surfaces via DLT).
  3. `SaveChangesAsync`.
- **Emits internal event(s):** `OrderStockReservedDomainEvent` (audit-only — no outbox publisher; saga already knows from Inventory's own event).

#### 3.1.3 `MarkOrderPaymentCompletedCommand`

- **HTTP:** *None* — saga-internal application-layer command (in-process dispatch from the Wave-2 saga; **not** on `ordering.order-commands`). Triggered by saga on `PaymentCompletedEvent` from Payments.
- **Authorization:** service identity.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "orderId": "Guid",
    "paymentTransactionId": "Guid (from Payments)"
  }
  ```
- **Response:** `Result.Ok()`.
- **Handler class:** `MarkOrderPaymentCompletedCommandHandler`.
- **Validator rules:**
  - `OrderId` — NotEmpty.
  - `PaymentTransactionId` — NotEmpty.
- **Flow:**
  1. Load order; 404 → `Result.Fail(OrderingErrors.NotFound)`.
  2. Call `order.MarkPaymentCompleted(paymentTransactionId, utcNow)`.
  3. `SaveChangesAsync`.
- **Emits internal event(s):** `OrderPaymentCompletedDomainEvent` (audit-only — no outbox publisher).

#### 3.1.4 `ConfirmOrderCommand`

- **HTTP:** *None* — from saga (issued after saga observes both `StockReserved` and `PaymentCompleted` Avro events).
- **Authorization:** service identity.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "orderId": "Guid"
  }
  ```
- **Response:** `Result.Ok()`.
- **Handler class:** `ConfirmOrderCommandHandler`.
- **Validator rules:**
  - `OrderId` — NotEmpty.
- **Flow:**
  1. Load order; 404 → `Result.Fail(OrderingErrors.NotFound)`.
  2. Call `order.Confirm(utcNow)`.
  3. `SaveChangesAsync`.
- **Emits internal event(s):** `OrderConfirmedDomainEvent`. Fan-out:
  - `OrderConfirmedOutboxPublisherDomainEventHandler` — writes `OrderConfirmedEvent` (Avro) to outbox for topic `ordering.orders`.

#### 3.1.5 `MarkOrderFailedCommand`

- **HTTP:** *None* — from saga on compensation or timeout.
- **Authorization:** service identity.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "orderId": "Guid",
    "errorCode": "string (1-100 chars, e.g. 'STOCK_UNAVAILABLE', 'PAYMENT_FAILED', 'PAYMENT_TIMEOUT', 'CONFIRMATION_TIMEOUT')",
    "errorMessage": "string (1-500 chars)"
  }
  ```
- **Response:** `Result.Ok()`.
- **Handler class:** `MarkOrderFailedCommandHandler`.
- **Validator rules:**
  - `OrderId` — NotEmpty.
  - `ErrorCode` — NotEmpty; MaximumLength(100).
  - `ErrorMessage` — NotEmpty; MaximumLength(500).
- **Flow:**
  1. Load order; 404 → `Result.Fail(OrderingErrors.NotFound)`.
  2. Call `order.Fail(errorCode, errorMessage, utcNow)`.
  3. `SaveChangesAsync`.
- **Emits internal event(s):** `OrderFailedDomainEvent`. Fan-out:
  - `OrderFailedOutboxPublisherDomainEventHandler` — writes `OrderFailedEvent` (Avro) to outbox for topic `ordering.orders`.

### 3.2 Commands — HTTP admin/ops

These commands enter via HTTP; the buyer or admin initiates them through the BFF or an admin tool.

#### 3.2.1 `CancelOrderCommand`

- **HTTP:** `POST /api/v1/ordering/orders/{orderId}/cancel`
- **Authorization:** Authenticated. Buyer may cancel their own order (`BuyerId == claim.sub`) up to `Confirmed` status; admin may cancel any order up to `Confirmed`. Nobody may cancel `Shipped` / `Delivered` / `Cancelled` / `Failed`.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "orderId": "Guid (from route)",
    "reason": "string (1-500 chars)",
    "buyerId": "Guid (from JWT claim)",
    "isAdmin": "bool (derived from role claim by handler; removed from schema)"
  }
  ```
- **Response:** 204 on success; 404 if not found (or not owned by buyer when not admin); 409 if current status disallows cancel (`OrderingErrors.CannotCancelInStatus`); 422 on empty reason.
- **Handler class:** `CancelOrderCommandHandler`.
- **Validator rules:**
  - `OrderId` — NotEmpty.
  - `Reason` — NotEmpty; MaximumLength(500).
  - `BuyerId` — NotEmpty.
- **Flow:**
  1. Load order by id (inline `.Where(o => o.Id == orderId)` — pure PK lookup, no spec per [ADR-0022](../adr/0022-specification-pattern-adoption.md)); if missing → `Result.Fail(OrderingErrors.NotFound)`.
  2. Authorization check: if `!command.IsAdmin && order.BuyerId != command.BuyerId` → return `Result.Fail(OrderingErrors.NotFound)` (don't leak existence of other buyers' orders).
  3. Call `order.Cancel(reason, utcNow)` — returns `Result.Fail(OrderingErrors.CannotCancelInStatus(Status))` if already terminal or shipped.
  4. `SaveChangesAsync`.
- **Emits internal event(s):** `OrderCancelledDomainEvent`. Fan-out:
  - `OrderCancelledOutboxPublisherDomainEventHandler` — writes `OrderCancelledEvent` (Avro) to outbox. Saga compensation (release reservation, refund if paid) is emergent from this external event.

#### 3.2.2 `MarkOrderShippedCommand`

- **HTTP:** `POST /api/v1/ordering/orders/{orderId}/ship`
- **Authorization:** `AuthPolicies.Admin` — warehouse/fulfillment operator.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "orderId": "Guid (from route)",
    "carrier": "string (1-100 chars, e.g. 'FedEx', 'DHL', 'UPS')",
    "trackingNumber": "string (1-100 chars)"
  }
  ```
- **Response:** 204 on success; 404 if not found; 409 if order is not in `Confirmed` status.
- **Handler class:** `MarkOrderShippedCommandHandler`.
- **Validator rules:**
  - `OrderId` — NotEmpty.
  - `Carrier` — NotEmpty; MaximumLength(100).
  - `TrackingNumber` — NotEmpty; MaximumLength(100).
- **Flow:**
  1. Load order; 404 if missing.
  2. Call `order.MarkShipped(carrier, trackingNumber, utcNow)` — throws `DataIntegrityException` if not currently `Confirmed` (admin UI should pre-validate).
  3. `SaveChangesAsync`.
- **Emits internal event(s):** `OrderShippedDomainEvent`. Fan-out:
  - `OrderShippedOutboxPublisherDomainEventHandler` — writes `OrderShippedEvent` (Avro) to outbox (→ Notifications email with tracking).

#### 3.2.3 `MarkOrderDeliveredCommand`

- **HTTP:** `POST /api/v1/ordering/orders/{orderId}/deliver`
- **Authorization:** `AuthPolicies.Admin` — in v1, human-triggered. A future carrier-webhook adapter would invoke the same handler.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "orderId": "Guid (from route)"
  }
  ```
- **Response:** 204 on success; 404 if not found; 409 if not in `Shipped` status.
- **Handler class:** `MarkOrderDeliveredCommandHandler`.
- **Validator rules:**
  - `OrderId` — NotEmpty.
- **Flow:**
  1. Load order; 404 if missing.
  2. Call `order.MarkDelivered(utcNow)` — throws if not currently `Shipped`.
  3. `SaveChangesAsync`.
- **Emits internal event(s):** `OrderDeliveredDomainEvent`. Fan-out:
  - `OrderDeliveredOutboxPublisherDomainEventHandler` — writes `OrderDeliveredEvent` (Avro) to outbox.

### 3.3 Saga command intake — plumbing

**Topic:** `ordering.order-commands` (see `ordering.md` § 10.2 Option Y — recommended). Single topic carrying a union of four command records distinguished by Avro schema name.

**Consumer group:** `ordering-commands-consumer`.

**Kafka handler classes** (in `Ordering.Infrastructure.Messaging.Kafka.SagaCommands`):

| Avro command record | Handler class | Internal command it dispatches |
|---------------------|---------------|-------------------------------|
| `Ordering.Commands.CreateOrderCommand` | `CreateOrderCommandKafkaHandler` | `CreateOrderCommand` |
| `Ordering.Commands.ConfirmOrderCommand` | `ConfirmOrderCommandKafkaHandler` | `ConfirmOrderCommand` |
| `Ordering.Commands.CancelOrderCommand` | `CancelOrderCommandKafkaHandler` | `CancelOrderCommand` |
| `Ordering.Commands.MarkOrderFailedCommand` | `MarkOrderFailedCommandKafkaHandler` | `MarkOrderFailedCommand` |

**Middleware pipeline** (applied to the topic in `Ordering.Api.Program.cs`):

1. **Avro deserialization** via `Platform.Avro.UniversalSerDes`.
2. **Inbox middleware** (`Platform.ReliableMessaging.Inbox.EFCore`) — keyed on Kafka message id; duplicate deliveries skipped at write time; idempotent.
3. **Handler** — Kafka handler constructs the internal command and invokes `ICommandHandler<TCommand>`.
4. **Dead Letter** — on `InvalidOperationException` (from a failed `Result`) or unhandled exception, the KafkaFlow DLT middleware routes the message to `ordering.order-commands.dlt`.

**Shape of a Kafka handler** (mirrors `PurchaseCompletedKafkaHandler.cs`):

```text
public sealed class CreateOrderKafkaHandler
    : IMessageHandler<Ordering.Commands.CreateOrderCommand>
{
    private readonly ICommandHandler<CreateOrderCommand, Guid> _commandHandler;

    public async Task Handle(IMessageContext ctx, Ordering.Commands.CreateOrderCommand msg)
    {
        var cmd = new CreateOrderCommand
        {
            OrderId = msg.OrderId,
            BuyerId = msg.BuyerId,
            Basket = Translate(msg.Basket),
            ShippingAddress = Translate(msg.ShippingAddress),
            BillingAddress = Translate(msg.BillingAddress),
            PaymentMethodId = msg.PaymentMethodId,
        };
        var result = await _commandHandler.HandleAsync(cmd, ctx.ConsumerContext.WorkerStopped);
        if (result.IsFailed)
            throw new InvalidOperationException($"CreateOrder failed: {string.Join(",", result.Errors)}");
    }
}
```

### 3.4 Queries

#### 3.4.1 `GetOrderByIdQuery`

- **HTTP:** `GET /api/v1/ordering/orders/{orderId}`
- **Authorization:** Authenticated. Buyer must own the order (`BuyerId == claim.sub`) unless `admin` role.
- **Interface:** `IQuery<GetOrderByIdResponse>`.
- **Request shape:**
  ```
  {
    "orderId": "Guid (from route)",
    "buyerId": "Guid (from JWT claim)",
    "isAdmin": "bool (from role claim)"
  }
  ```
- **Response shape:**
  ```
  {
    "orderId": "Guid",
    "buyerId": "Guid",
    "status": "string (Created|StockReserved|PaymentCompleted|Confirmed|Shipped|Delivered|Cancelled|Failed)",
    "items": [
      {
        "productId": "Guid",
        "sku": "string",
        "name": "string",
        "quantity": "int",
        "unitPriceAmount": "decimal",
        "lineTotalAmount": "decimal"
      }
    ],
    "totalAmount": "decimal",
    "currency": "string",
    "shippingAddress": "Address",
    "billingAddress": "Address",
    "paymentMethodId": "Guid",
    "shipment": { "carrier": "string", "trackingNumber": "string", "shippedAtUtc": "DateTimeOffset" } | null,
    "cancellation": { "reason": "string", "atStatus": "string", "cancelledAtUtc": "DateTimeOffset" } | null,
    "failure": { "errorCode": "string", "errorMessage": "string", "atStatus": "string", "failedAtUtc": "DateTimeOffset" } | null,
    "createdAtUtc": "DateTimeOffset",
    "stockReservedAtUtc": "DateTimeOffset | null",
    "paymentCompletedAtUtc": "DateTimeOffset | null",
    "confirmedAtUtc": "DateTimeOffset | null",
    "deliveredAtUtc": "DateTimeOffset | null"
  }
  ```
- **Handler class:** `GetOrderByIdQueryHandler`.
- **Validator rules:**
  - `OrderId` — NotEmpty.
  - `BuyerId` — NotEmpty.
- **Filter/paging:** none.
- **Read source:** `ordering.orders` + `ordering.order_items` via inline LINQ in `GetOrderByIdQueryHandler` (`.Where(o => o.Id == query.OrderId).Select(...).FirstOrDefaultAsync()`) — SQL-side projection, no `Ardalis.Specification` per [ADR-0021](../adr/0021-read-side-no-specifications.md). Ownership is enforced post-SELECT in-memory: if `!query.IsAdmin && response.BuyerId != query.BuyerId`, return `Result.Fail(OrderingErrors.OrderNotFound)` (same failure for not-owned vs not-existing — no existence leak; cross-buyer attempt logged at Warning).

#### 3.4.2 `GetOrdersByBuyerQuery`

- **HTTP:** `GET /api/v1/ordering/orders`
- **Authorization:** Authenticated. v1 lists only the caller's own orders (`BuyerId` from the JWT `sub`). The admin `?buyerId=` override is **deferred to v2+** (`ordering.md` Appendix B); the v1 request DTO (`GetOrdersByBuyerRequest`) binds no `buyerId` param.
- **Interface:** `IQuery<GetOrdersByBuyerResponse>`.
- **Request shape:**
  ```
  {
    "status": "string? (Created|StockReserved|PaymentCompleted|Confirmed|Shipped|Delivered|Cancelled|Failed)",
    "pageNumber": "int (default 1)",
    "pageSize": "int (default 20, max 100)",
    "buyerId": "Guid (from JWT sub claim; never a wire param in v1)"
  }
  ```
- **Response shape:**
  ```
  {
    "total": "int",
    "pageNumber": "int",
    "pageSize": "int",
    "items": [
      {
        "orderId": "Guid",
        "status": "string",
        "totalAmount": "decimal",
        "currency": "string",
        "itemCount": "int",
        "createdAtUtc": "DateTimeOffset",
        "lastStatusChangeAtUtc": "DateTimeOffset"
      }
    ]
  }
  ```
- **Handler class:** `GetOrdersByBuyerQueryHandler`.
- **Validator rules:**
  - `PageNumber` — GreaterThanOrEqualTo(1).
  - `PageSize` — InclusiveBetween(1, 100).
  - `Status` — when provided, MustBeValidOrderStatus (parses into `OrderStatus` SmartEnum).
- **Filter/paging:**
  - v1: `buyerId = caller's JWT sub` (no admin override; the `?buyerId=` admin path is v2+ per `ordering.md` Appendix B).
  - `WHERE BuyerId = @buyerId AND (@status IS NULL OR Status = @status)`.
  - `ORDER BY CreatedAtUtc DESC, OrderId DESC`.
  - `LIMIT @pageSize OFFSET ((@pageNumber - 1) * @pageSize)`.
- **Read source:** `ordering.orders` via inline LINQ in `GetOrdersByBuyerQueryHandler` (`.Where(...).OrderByDescending(...).Skip(...).Take(...).Select(...)`) — predicate, paging, and projection are co-located on the handler and translated SQL-side; no `Ardalis.Specification` per [ADR-0021](../adr/0021-read-side-no-specifications.md). `LastStatusChangeAtUtc` is derived by taking `COALESCE(CancelledAtUtc, FailedAtUtc, DeliveredAtUtc, ShippedAtUtc, ConfirmedAtUtc, PaymentCompletedAtUtc, StockReservedAtUtc, CreatedAtUtc)` — the timestamp of the order's current state. Terminal Cancellation/Failure timestamps sit at the front of the chain: when set, they supersede any retained happy-path timestamp (a Confirmed-then-Cancelled row's `Status` is `Cancelled`, so its `LastStatusChangeAtUtc` is `CancelledAtUtc`, not the now-superseded `ConfirmedAtUtc`).

---

## 4. Inventory Service Use Cases

**Base HTTP path:** `/api/v1/inventory/`
**Storage:** PostgreSQL schema `inventory` — event store (`inventory.stock_events`), projections (`current_stock_levels`, `reservation_audit`), inbox (for Catalog event consumption), outbox (for reservation + stock-level events), command inbox (idempotency per `inventory.md` § 10.3).
**Consumes (Kafka):**
- `catalog.products` → `InitializeStockItemCommand` (on `ProductCreatedEvent`).
- `inventory.reservation-commands` (saga → Inventory). See § 4.3 for plumbing.

**Produces (Kafka):** `inventory.stock-events`, `inventory.reservations` via outbox.

### 4.1 Commands — saga & event-driven (no HTTP endpoints for these)

#### 4.1.1 `InitializeStockItemCommand`

- **HTTP:** *None* — consumed from `catalog.products` inbox (on `ProductCreatedEvent`).
- **Authorization:** service identity via Kafka consumer.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "productId": "Guid (from ProductCreatedEvent.ProductId)"
  }
  ```
- **Response:** `Result.Ok()` (idempotent — re-delivery is a no-op via command inbox).
- **Handler class:** `InitializeStockItemCommandHandler`.
- **Validator rules:**
  - `ProductId` — NotEmpty.
- **Flow:**
  1. Check command inbox for duplicate `command.Id` — if present, return `Result.Ok()`.
  2. Rehydrate stream: `SELECT ... FROM inventory.stock_events WHERE StreamId = @productId ORDER BY Version ASC`.
  3. If `stockItem.Version > 0` — already initialized. Return `Result.Ok()` (not an error; Catalog retry / re-send).
  4. Call `stockItem.Initialize(productId)`.
  5. **Transaction:** INSERT `StockItemInitializedDomainEvent` at `Version=1`; UPSERT `inventory.current_stock_levels`; INSERT command inbox row. COMMIT.
- **Emits internal event(s):** `StockItemInitializedDomainEvent` (ES event, appended to stream, projected by `CurrentStockLevelsProjectionDomainEventHandler`). No external event by default (per `inventory.md` § 7).

#### 4.1.2 `ReserveStockCommand`

- **HTTP:** *None* — consumed from `inventory.reservation-commands` topic (saga).
- **Authorization:** service identity.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "reservationId": "Guid (saga-supplied; unique per reservation)",
    "productId": "Guid",
    "quantity": "int (>= 1)",
    "orderId": "Guid (saga correlation)",
    "ttlSeconds": "int? (optional override; defaults to InventoryOptions.ReservationTtl = 900)"
  }
  ```
- **Response:** `Result.Ok()` on success; `Result.Fail(InsufficientStockError)` when `Available < Quantity`. The external outcome event (`StockReservedEvent` or `StockReservationFailedEvent`) is produced inside the same transaction.
- **Handler class:** `ReserveStockCommandHandler`.
- **Validator rules:**
  - `ReservationId` — NotEmpty.
  - `ProductId` — NotEmpty.
  - `Quantity` — GreaterThanOrEqualTo(1).
  - `OrderId` — NotEmpty.
  - `TtlSeconds` — optional; when provided, InclusiveBetween(60, 3600).
- **Flow:**
  1. Idempotency: command inbox dedupe on `command.Id`.
  2. Rehydrate stream for `ProductId`.
  3. If `stockItem.Version == 0` → `DataIntegrityException` (saga should not reference uninitialized products) → DLT.
  4. If `ReservationId` already in `stockItem.Reservations` → return `Result.Ok()` (idempotent — already reserved).
  5. Call `stockItem.Reserve(reservationId, quantity, orderId, ttl)`.
     - If `Available < quantity` → `Result.Fail(InsufficientStockError(productId, requested, available))`. No ES event appended. **Failure path:** application layer builds `StockReservationFailedEvent` (external Avro) and writes it to outbox; transaction commits (inbox + outbox). Saga observes the external failure.
  6. Success path: append `StockReservedDomainEvent` at `Version+1`; UPSERT projections; build `StockReservedEvent` (external) + optional `StockLevelChangedEvent` (if `Available` crosses to 0) and write both to outbox.
  7. Concurrency: if INSERT fails with UNIQUE (StreamId, Version) violation → rehydrate + retry once; on second failure return `Result.Fail(ConcurrencyError)` (saga treats as transient).
- **Emits internal event(s):** `StockReservedDomainEvent` (ES; success) OR none (failure). External events are wired by the handler itself, not a separate outbox publisher — because the failure path has no ES event to react to.

#### 4.1.3 `ConfirmReservationCommand`

- **HTTP:** *None* — consumed from `inventory.reservation-commands` (saga fires after payment + order confirmation).
- **Authorization:** service identity.
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "reservationId": "Guid",
    "productId": "Guid"
  }
  ```
- **Response:** `Result.Ok()` on success. `DataIntegrityException` → DLT if reservation is unknown or not `Active`.
- **Handler class:** `ConfirmReservationCommandHandler`.
- **Validator rules:**
  - `ReservationId` — NotEmpty.
  - `ProductId` — NotEmpty.
- **Flow:**
  1. Idempotency via command inbox.
  2. Rehydrate stream.
  3. Call `stockItem.ConfirmReservation(reservationId)` — throws if not `Active` (bug: saga double-confirmed).
  4. Append `ReservationConfirmedDomainEvent` at `Version+1`; UPSERT projections (`OnHand -= qty`, `Reserved -= qty`; `reservation_audit.Status = 'Confirmed'`); outbox write for external `ReservationConfirmedEvent` + optional `StockLevelChangedEvent`.
- **Emits internal event(s):** `ReservationConfirmedDomainEvent` (ES). Fan-out:
  - `CurrentStockLevelsProjectionDomainEventHandler` — UPDATE `OnHand`, `Reserved`, `LastUpdatedUtc`, `LastVersion`.
  - `ReservationAuditProjectionHandler` — UPDATE `Status='Confirmed', ResolvedAtUtc`.
  - `ReservationOutboxPublisherDomainEventHandler` — writes external `ReservationConfirmedEvent` (Avro) to outbox (`inventory.reservations`).
  - `StockLevelChangedOutboxPublisherDomainEventHandler` — if `Available` crosses threshold (e.g. remaining reserved drops to 0 freeing availability), writes `StockLevelChangedEvent` (Avro) to outbox (`inventory.stock-events`).

#### 4.1.4 `ReleaseReservationCommand`

- **HTTP:** *None* — consumed from three sources: (1) `inventory.reservation-commands` (saga compensation on failure), (2) `ReservationExpiryWorker` (TTL-expired reservations), (3) buyer/admin cancel via the Ordering service publishing `OrderCancelledEvent` that the saga translates. In v1 all three paths land in the same handler; only `ReleaseReason` differs.
- **Authorization:** service identity (Kafka) or internal hosted service (expiry worker).
- **Interface:** `ICommand`.
- **Request shape:**
  ```
  {
    "reservationId": "Guid",
    "productId": "Guid",
    "releaseReason": "string (Compensation|Expiry|Cancellation)"
  }
  ```
- **Response:** `Result.Ok()` on success. `DataIntegrityException` → DLT if reservation unknown or not `Active`.
- **Handler class:** `ReleaseReservationCommandHandler`.
- **Validator rules:**
  - `ReservationId` — NotEmpty.
  - `ProductId` — NotEmpty.
  - `ReleaseReason` — MustBeValid enum `{Compensation, Expiry, Cancellation}`.
- **Flow:**
  1. Idempotency via command inbox.
  2. Rehydrate stream.
  3. If reservation not found OR status already `Released` → return `Result.Ok()` (idempotent). Only if status is `Confirmed` return `Result.Fail` (cannot un-confirm; saga bug).
  4. Call `stockItem.ReleaseReservation(reservationId, releaseReason)`.
  5. Append `ReservationReleasedDomainEvent` at `Version+1`; UPSERT projections; outbox write for external `ReservationReleasedEvent` + optional `StockLevelChangedEvent` (if `Available` crosses back to positive).
- **Emits internal event(s):** `ReservationReleasedDomainEvent` (ES). Fan-out: same shape as confirmation handler, writing `ReservationReleasedEvent` (external Avro) and conditional `StockLevelChangedEvent`.

### 4.2 Commands — HTTP admin/ops

#### 4.2.1 `ReceiveStockCommand`

- **HTTP:** `POST /api/v1/inventory/stock-items/{productId}/receive`
- **Authorization:** `AuthPolicies.WritePolicy` (`InventoryWriteScope`: requires the `admin` realm role **and** the `inventory.write` scope — ADR-0010). Warehouse receiving-dock operator.
- **Interface:** `ICommand<StockLevelResponse>` — returns the post-mutation projection snapshot.
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)",
    "quantity": "int (>= 1)",
    "source": "string (1-100 chars, e.g. 'receiving-dock', 'returns', 'transfer-in')",
    "receivedByUserId": "Guid? (body; nullable for system-initiated receipts)"
  }
  ```
- **Response:** **200 OK** with `StockLevelResponse` `{ productId, onHand, reserved, available, lastUpdatedUtc, lastVersion }` (post-mutation snapshot). Declared error responses: 400, 401, 403, 409.
- **Handler class:** `ReceiveStockCommandHandler`.
- **Validator rules:**
  - `ProductId` — NotEmpty.
  - `Quantity` — GreaterThanOrEqualTo(1).
  - `Source` — NotEmpty; MaximumLength(100).
- **Flow:**
  1. Idempotency via command inbox.
  2. Rehydrate stream; if `Version == 0` → `Result.Fail(StockItemErrors.NotInitialized)`.
  3. Call `stockItem.ReceiveStock(quantity, source, userId)`.
  4. Append `StockReceivedDomainEvent` at `Version+1`; UPSERT projections; outbox write for `StockLevelChangedEvent` if `Available` crosses from 0 to positive.
- **Emits internal event(s):** `StockReceivedDomainEvent` (ES). Fan-out:
  - `CurrentStockLevelsProjectionDomainEventHandler` — UPDATE `OnHand += quantity`.
  - `StockLevelChangedOutboxPublisherDomainEventHandler` — conditional external event on threshold crossing.

#### 4.2.2 `AdjustStockCommand`

- **HTTP:** `POST /api/v1/inventory/stock-items/{productId}/adjust`
- **Authorization:** `AuthPolicies.WritePolicy` (`InventoryWriteScope`: requires the `admin` realm role **and** the `inventory.write` scope — ADR-0010). Ops adjustment — damage write-off, recount.
- **Idempotency-Key:** **required** — the endpoint returns 400 (`Inventory.IdempotencyKeyMissing`) when the header is absent; cached 24 h per ADR-0013.
- **Interface:** `ICommand<StockLevelResponse>` — returns the post-mutation projection snapshot.
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)",
    "delta": "int (signed; cannot be zero)",
    "reason": "string (1-500 chars)",
    "adjustedByUserId": "Guid (body; admin id for audit — bound from the body, not the JWT)"
  }
  ```
- **Response:** **200 OK** with `StockLevelResponse` `{ productId, onHand, reserved, available, lastUpdatedUtc, lastVersion }` (post-mutation snapshot). Declared error responses: 400, 401, 403, 409.
- **Handler class:** `AdjustStockCommandHandler`.
- **Validator rules:**
  - `ProductId` — NotEmpty.
  - `Delta` — NotEqual(0).
  - `Reason` — NotEmpty; MaximumLength(500).
  - `UserId` — NotEmpty (admin must be identifiable for audit).
- **Flow:**
  1. Idempotency via command inbox.
  2. Rehydrate stream; 404 if uninitialized.
  3. Call `stockItem.AdjustStock(delta, reason, userId)` — `Result.Fail(StockItemErrors.AdjustmentBelowZero)` if precondition fails.
  4. Append `StockAdjustedDomainEvent`; UPSERT projections; outbox write for `StockLevelChangedEvent` if threshold crossed.
- **Emits internal event(s):** `StockAdjustedDomainEvent` (ES). Projection updates `OnHand`. Conditional external event.

### 4.3 Saga command intake — plumbing

**Topic:** `inventory.reservation-commands`.

**Consumer group:** `inventory-group` (one-group-per-service rule per [events-catalog.md § 3.1](events-catalog.md)).

**Kafka handler classes** (in `Inventory.Infrastructure.Messaging.Kafka.Handlers`):

| Avro command record | Handler class | Internal command it dispatches |
|---------------------|---------------|-------------------------------|
| `Inventory.Commands.ReserveStockCommand` | `ReserveStockKafkaHandler` | `ReserveStockCommand` |
| `Inventory.Commands.ConfirmReservationCommand` | `ConfirmReservationKafkaHandler` | `ConfirmReservationCommand` |
| `Inventory.Commands.ReleaseReservationCommand` | `ReleaseReservationKafkaHandler` | `ReleaseReservationCommand` |

Plus a separate consumer for the Catalog event inbox:

**Topic:** `catalog.products` (read-only consumer; filters on `ProductCreatedEvent` record name).
**Consumer group:** `inventory-catalog-inbox-consumer`.
**Kafka handler:** `ProductCreatedKafkaHandler` → dispatches `InitializeStockItemCommand`.

**Middleware pipeline** (applied in `Inventory.Api.Program.cs`):

1. Avro deserialization.
2. Inbox middleware (`Platform.ReliableMessaging.Inbox.EFCore`) — critical for Inventory since both saga retries and Catalog redeliveries are common.
3. Handler dispatch.
4. DLT on `InvalidOperationException`.

### 4.4 Queries

#### 4.4.1 `GetStockLevelQuery`

- **HTTP:** `GET /api/v1/inventory/stock-items/{productId}`
- **Authorization:** `AllowAnonymous` — the public product-page availability overlay. Resolved in favour of the original "public overlay" intent: this single read was previously scope-gated in code (`AuthPolicies.ReadPolicy`) but is now aligned with its `AllowAnonymous` bulk sibling (§ 4.4.2) and ADR-0034 § Implementation Notes. Availability is public shopper-facing data, and oversell safety is structural — the reservation decision path is event-sourced and never reads this display projection/cache (ADR-0034 / ADR-0006), so an anonymous read cannot affect it. No separate `/admin/stock-items/{productId}` variant exists.
- **Interface:** `IQuery<StockLevelResponse>`.
- **Request shape:**
  ```
  {
    "productId": "Guid (from route)"
  }
  ```
- **Response shape (`StockLevelResponse`):**
  ```
  {
    "productId": "Guid",
    "onHand": "int",
    "reserved": "int",
    "available": "int",
    "lastUpdatedUtc": "DateTimeOffset",
    "lastVersion": "int"
  }
  ```
- **Handler class:** `GetStockLevelByProductIdQueryHandler`.
- **Validator rules:**
  - `ProductId` — NotEmpty.
- **Filter/paging:** none.
- **Read source:** `inventory.current_stock_levels` projection. Missing row → `Result.Fail(StockItemErrors.NotInitialized)` → 404.

#### 4.4.2 `GetStockLevelsBulkQuery`

> ✅ **BUILT (ahead of its consumer)** per ADR-0034 § Decision (1). `POST /api/v1/inventory/stock-items/bulk` is implemented in `Inventory.Api` (`GetStockLevelsBulkEndpoint` → `GetStockLevelsBulkQueryHandler` over the Inventory-owned read-through cache) with integration coverage. The **BFF consumer** that will call it is still not built (the BFF service is not yet started, per `CLAUDE.md`); the endpoint stands on its own — anonymous, partial-tolerant — until then.

- **HTTP:** `POST /api/v1/inventory/stock-items/bulk` (POST because the list of ids may exceed URL length for basket-sized collections; body is read-only despite the verb).
- **Authorization:** `AllowAnonymous` per ADR-0034 § Implementation Notes — consistent with its single-read sibling § 4.4.1 (`GET /stock-items/{productId}`), which is also `AllowAnonymous`. Both display reads share one public posture.
- **Interface:** `IQuery<GetStockLevelsBulkResponse>`.
- **Request shape:**
  ```
  {
    "productIds": "Guid[] (1..200 items)"
  }
  ```
- **Response shape:**
  ```
  {
    "items": [
      {
        "productId": "Guid",
        "onHand": "int",
        "reserved": "int",
        "available": "int",
        "lastUpdatedUtc": "DateTimeOffset"
      }
    ],
    "missingProductIds": [ "Guid" ]
  }
  ```
- **Handler class:** `GetStockLevelsBulkQueryHandler`.
- **Validator rules:**
  - `ProductIds` — NotEmpty; Must.Count.InclusiveBetween(1, 200); ForEach NotEmpty.
- **Filter/paging:** bulk read; no paging.
- **Read source:** Inventory-owned **read-through cache** (FusionCache over `redis-cache`) in front of `SELECT * FROM inventory.current_stock_levels WHERE ProductId = ANY(@ids)` — see [ADR-0034](../adr/0034-inventory-stock-availability-read-path.md) and [inventory.md § 9.1](inventory.md). Ids absent from result appear in `MissingProductIds` (uninitialized or unknown product). Partial-tolerant by design — matches BFF's batch pattern. The cache is hidden behind this HTTP endpoint; the BFF calls the API and never the cache, and the reservation decision path bypasses it (oversell-safe via ES).

#### 4.4.3 `GetReservationByIdQuery`

- **HTTP:** `GET /api/v1/inventory/reservations/{reservationId}`
- **Authorization:** `AuthPolicies.AdminReadPolicy` (`InventoryAdminReadScope` — requires the `admin` realm role AND a read-capable scope, `inventory.read` *or* `inventory.write`). Reservation-audit rows correlate a reservation to an `orderId` (cross-aggregate, internal ops/audit data), so this read is admin-gated — tighter than the public stock-availability display reads (§§ 4.4.1–4.4.2). A plain `inventory.read` service token gets `403`.
- **Interface:** `IQuery<GetReservationByIdResponse>`.
- **Request shape:**
  ```
  {
    "reservationId": "Guid (from route)"
  }
  ```
- **Response shape:**
  ```
  {
    "reservationId": "Guid",
    "productId": "Guid",
    "quantity": "int",
    "orderId": "Guid",
    "status": "string (Active|Confirmed|Released)",
    "reservedAtUtc": "DateTimeOffset",
    "expiresAtUtc": "DateTimeOffset",
    "resolvedAtUtc": "DateTimeOffset | null",
    "releaseReason": "string | null (Compensation|Expiry|Cancellation when Released)"
  }
  ```
- **Handler class:** `GetReservationByIdQueryHandler`.
- **Validator rules:**
  - `ReservationId` — NotEmpty.
- **Filter/paging:** none.
- **Read source:** `inventory.reservation_audit` projection. Missing → `Result.Fail(ReservationErrors.NotFound)` → 404.

#### 4.4.4 `GetReservationsByOrderQuery` — **removed (out of reference-repo scope)**

An admin "all reservations for order O" HTTP read was specced but never built, and had no programmatic consumer — the saga correlates over Kafka (events carry `OrderId`), the BFF doesn't use it, and ops can use Jaeger / direct SQL. The by-order *access pattern* still lives **internally** (`OrderCancelledEventKafkaHandler` queries `reservation_audit WHERE OrderId = … AND Status = Active`), so the `idx_reservation_audit_order` index is retained; only the public endpoint is dropped.

---

## 5. Cross-Service Command Flow Summary

The saga-to-service command flow can be summarized as follows, aligning the three touch points (saga, Kafka topic, service handler):

| Saga step | Outbound Kafka command topic | Target service | Target internal `ICommand` | Outbound service event (via outbox) |
|-----------|-----------------------------|----------------|-----------------------------|-------------------------------------|
| 1. Checkout initiated (consume `basket.sessions`) | `ordering.order-commands` | Ordering | `CreateOrderCommand` | `OrderCreatedEvent` on `ordering.orders` |
| 2. Reserve stock per line-item | `inventory.reservation-commands` | Inventory | `ReserveStockCommand` | `StockReservedEvent` OR `StockReservationFailedEvent` on `inventory.reservations` |
| 3a. All reservations succeed → mark order stock-reserved | `(in-process)` | Ordering | `MarkOrderStockReservedCommand` | *(audit-only internal event)* |
| 3b. Any reservation failed → fail order + release successful reservations | `ordering.order-commands` + `inventory.reservation-commands` | Ordering + Inventory | `MarkOrderFailedCommand` + `ReleaseReservationCommand` (per successful reservation) | `OrderFailedEvent`; `ReservationReleasedEvent(Compensation)` |
| 4. Process payment | (Payments saga sub-orchestration — existing; out of scope here) | Payments | *(Payments commands)* | `PaymentCompletedEvent` OR `PaymentFailedEvent` |
| 5a. Payment completed → mark paid | `(in-process)` | Ordering | `MarkOrderPaymentCompletedCommand` | *(audit-only)* |
| 5b. Payment failed → fail order + release reservations | `ordering.order-commands` + `inventory.reservation-commands` | Ordering + Inventory | `MarkOrderFailedCommand` + `ReleaseReservationCommand(Compensation)` | `OrderFailedEvent`; `ReservationReleasedEvent` |
| 6. Confirm order | `ordering.order-commands` | Ordering | `ConfirmOrderCommand` | `OrderConfirmedEvent` |
| 7. Confirm reservation per line-item (stock physically leaves warehouse) | `inventory.reservation-commands` | Inventory | `ConfirmReservationCommand` | `ReservationConfirmedEvent` |

**HTTP commands (no saga):**

- Catalog: all admin CRUD.
- Basket: all user-initiated basket mutations including `CheckoutBasketCommand` (which produces the saga-initiating `BasketCheckoutInitiatedEvent`).
- Ordering: `CancelOrderCommand`, `MarkOrderShippedCommand`, `MarkOrderDeliveredCommand`.
- Inventory: `ReceiveStockCommand`, `AdjustStockCommand`.

**Event-driven commands (not saga, not HTTP):**

- Inventory: `InitializeStockItemCommand` from `catalog.products` inbox consumer.

---

## 6. Invoicing Service Use Cases

Commands and queries shipped in Wave 1 under `services/Invoicing/Invoicing.Application/**`. Each subsection mirrors the § 1 – § 4 shape: handler class, command/query payload, validator rules, error paths (cross-reference [error-taxonomy.md § 3.6](error-taxonomy.md)), and produced domain events (cross-reference [events-catalog.md § 5.7](events-catalog.md)).

### 6.1 IssueInvoiceCommand

- **Trigger:** event-driven via `OrderConfirmedInvoiceProjectionKafkaHandler` after the convergent enrichment of `OrderConfirmedEvent` + `PaymentCapturedEvent` (from `payments.transactions`) on a single `pending_invoices` row keyed by `OrderId`. Not an HTTP command — Invoicing has no public POST `/invoices` surface.
- **Handler:** `IssueInvoiceCommandHandler` (`services/Invoicing/Invoicing.Application/Invoices/IssueInvoice/`).
- **Payload:** `{ OrderId }` — the handler loads the full data (BuyerId, IssuedAtUtc, BillingAddress, Lines[], Currency, VatLines[]) from the converged `PendingInvoice` projection row keyed by `OrderId` ([ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)).
- **Validator:** `IssueInvoiceCommandValidator` — `OrderId NotEmpty`.
- **Side-effects:** allocates a gap-free invoice number (`InvoiceNumber.From(year, sequence)` via `PostgresInvoiceNumberAllocator` under `SELECT … FOR UPDATE`), renders the PDF (QuestPDF, byte-deterministic), uploads to Azure Blob (`invoices/{YYYY}/01/{number}.pdf`, SHA-256 content-addressed), then persists the `Invoice` aggregate in `Issued` state.
- **Result paths:**
  - `Result.Ok(InvoiceId)` — happy path.
  - `Result.Fail(InvoicingErrors.InvoiceAlreadyIssued(orderId))` — idempotent re-issue attempt (409 if surfaced as HTTP; consumer just commits the inbox row).
  - `Result.Fail(InvoicingErrors.BlobUploadFailed())` — after `Azure.Storage.Blobs` SDK retry exhaustion (5xx; DLT).
  - `Throw DataIntegrityException(Invoicing.TotalMismatch)` — bug-class; DLT.
- **Domain events:** `InvoiceIssuedDomainEvent` (always) → outbox publisher emits Avro `InvoiceIssuedEvent` on `invoicing.invoices`; plus `InvoiceDeliveryRequestedDomainEvent` (channel `Email`) → `InvoiceDeliveryRequestedOutboxPublisher` fans out a `NotifyUserCommand` (v2; [ADR-0031](../adr/0031-notify-user-command-and-notification-id.md)) to Notifications.

### 6.2 IssueCreditNoteCommand

- **Trigger:** event-driven via `OrderCancelledCreditNoteProjectionKafkaHandler` (cancellation path) or `PaymentRefundedCreditNoteProjectionKafkaHandler` (refund path). Both consume the converged `PendingCreditNote` projection.
- **Handler:** `IssueCreditNoteCommandHandler` (`services/Invoicing/Invoicing.Application/CreditNotes/IssueCreditNote/`).
- **Payload:** `{ OrderId }` — the handler resolves the original invoice, reason (`CreditNoteReason`), and sign-flipped reversal lines (`Invoice.LinesForReversal()`) from the converged `PendingCreditNote` projection row keyed by `OrderId` ([ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md)).
- **Validator:** `IssueCreditNoteCommandValidator` — `OrderId NotEmpty`.
- **Side-effects:** allocates credit-note number (`CreditNoteNumber` format `CN-YYYY-NNNNNN`), renders PDF, uploads to blob, persists `CreditNote` aggregate in `Issued` state, links to source `Invoice` (transitions invoice to `Cancelled` on full-amount path).
- **Result paths:**
  - `Result.Ok(CreditNoteId)` — happy path.
  - `Result.Fail(InvoicingErrors.PartialRefundNotSupportedV1())` — payment refund amount < invoice total (501; the convergence consumer logs a warning and commits the inbox row rather than DLT'ing — partial-refund credit notes are planned scope, see [roadmap.md § 2.3 Invoicing](../roadmap.md)).
  - `Throw DataIntegrityException(Invoicing.CreditNoteRefersToCancelledInvoice)` — bug-class; DLT.
- **Domain events:** `CreditNoteIssuedDomainEvent` + `InvoiceCancelledDomainEvent` (when the full-amount credit-note flips the invoice to `Cancelled`). Outbox publishers emit Avro `CreditNoteIssuedEvent` + `InvoiceCancelledEvent` on `invoicing.invoices`.

### 6.3 ResendInvoiceCommand

- **Trigger:** admin HTTP — `POST /api/v1/invoicing/invoices/{InvoiceId}/resend` with `Idempotency-Key` header (24 h Redis cache per ADR-0013).
- **Handler:** `ResendInvoiceCommandHandler` — **STUB** (validates existence + resendable state, logs, returns 204; the `invoice_delivery_log` insert (keyed `(InvoiceId, Channel)`, `Attempt` column) + outbox `NotifyUserCommand` carrying a fresh producer-assigned `NotificationId` described in `bc-design/invoicing.md § 12` is planned scope — see the `ResendInvoiceCommandHandler` production-handler item in [roadmap.md § 2.3 Invoicing](../roadmap.md); the OpenAPI `Description` carries a "stub" marker).
- **Auth:** `AuthPolicies.InvoicingAdmin` (Keycloak realm role `Admin`; ADR-0010 scope-based gating is planned scope (v2+) — see [roadmap.md § 2.3 Invoicing](../roadmap.md)).
- **Payload:** `{ InvoiceId, Channel (DeliveryChannel SmartEnum) }`.
- **Validator:** `ResendInvoiceCommandValidator` — `InvoiceId NotEmpty`.
- **Result paths:**
  - `Result.Ok()` → HTTP 204 (no-op acknowledgement).
  - `Result.Fail(InvoicingErrors.InvoiceNotFound)` → 404.
- **Domain events:** none today (the stub raises nothing); the production handler (see [roadmap.md § 2.3 Invoicing](../roadmap.md)) will raise `InvoiceDeliveryRequestedDomainEvent` per resend — the same event the issuance path already emits.

### 6.4 GetInvoiceByIdQuery

- **HTTP:** `GET /api/v1/invoicing/invoices/{InvoiceId}` — buyer-scoped + admin override.
- **Handler:** `GetInvoiceByIdQueryHandler`.
- **Auth:** authenticated; manual `User.GetBuyerIdOrNull()` short-circuit + IDOR check in the handler (returns `InvoiceNotFound` on cross-buyer reads). Admin (`User.IsInvoicingAdmin()`) bypasses the buyer scope.
- **Payload:** `{ InvoiceId }`.
- **Validator:** `GetInvoiceByIdQueryValidator` — `InvoiceId NotEmpty`.
- **Response (`GetInvoiceByIdResponse`):** `{ InvoiceId, InvoiceNumber, IssueDate, Status, SubtotalAmount, VatLines[], TotalAmount, Currency, PdfPresignedUrl, PdfPresignedUrlExpiresAtUtc, … }` — `PdfPresignedUrl` is a SAS URL freshly minted on every read (10-min TTL, ADR-0017).
- **Result paths:** `Result.Ok(response)` / `Result.Fail(InvoicingErrors.InvoiceNotFound)`.

### 6.5 GetInvoiceByOrderIdQuery

- **HTTP:** `GET /api/v1/invoicing/invoices/by-order/{OrderId}` — buyer-scoped + admin override.
- **Handler:** `GetInvoiceByOrderIdQueryHandler`.
- **Same auth / response shape as § 6.4**; the underlying read pivots on `Invoice.OrderId` (`PendingInvoice.OrderId` for issuance-projection-staged rows).

### 6.6 GetInvoicesByBuyerQuery

- **HTTP:** `GET /api/v1/invoicing/invoices?pageNumber=&pageSize=&buyerId=` — buyer callers are scoped to their own JWT subject; an admin (`User.IsInvoicingAdmin()`) may pass `?buyerId={guid}` to list another buyer's invoices.
- **Handler:** `GetInvoicesByBuyerQueryHandler`.
- **Auth:** JWT bearer required. A non-admin caller passing a `buyerId` other than their own is rejected with 403; a caller that is neither an admin nor carries a buyer `sub` gets 401.
- **Payload:** optional `buyerId` (Guid; admin-only override) + `pageNumber` (default 1) / `pageSize` (default 20, max 100) query params.
- **Response:** paginated list `{ Items[] (GetInvoiceByIdResponse), Total, PageNumber, PageSize }`.

### 6.7 GetCreditNoteByIdQuery

- **HTTP:** `GET /api/v1/invoicing/credit-notes/{CreditNoteId}` — buyer-scoped + admin override.
- **Handler:** `GetCreditNoteByIdQueryHandler`.
- **Same auth / response shape as § 6.4**, swapped for `CreditNote` aggregate fields. `PdfSasUrl` re-minted with 10-min TTL on every read.

---

**End of Use Case Catalog.**
