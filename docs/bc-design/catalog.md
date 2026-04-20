## Catalog Bounded Context

> **Scope:** Product information authority for the eShop reference solution — what is sold, how it is categorized, and at what price.
> **Pattern showcased:** **CQRS read-side projections** — a denormalized `product_search_view` is built in-process from internal domain events via a projection handler in the same Catalog database.
> **Storage:** PostgreSQL (per [eshop-general-plan.md § Infrastructure Changes](../eshop-general-plan.md) and recent `0aaf53c feat: migrate SqlServer to Postgre`), schema `catalog`.
> **Related ADR:** [ADR-0002 — Pricing inside Catalog (v1)](../adr/0002-pricing-in-catalog.md). Prices are flat and single-currency per product; dynamic pricing/promotions are deferred to a future Pricing BC.

### Ubiquitous Language

Catalog is the authoritative source for the ubiquitous terms **Product**, **SKU**, **Category**, **Category Path**, **Price**, **Brand**, **Product Status**, and **Discontinued/Reactivated**. A _Product_ in Catalog refers to the sellable item definition (identity, description, price, category, brand) — **not** inventory on hand (that lives in Inventory) and **not** a line in a customer's cart (that lives in Basket as a snapshot). See [`docs/bc-design/glossary-catalog.md`](glossary-catalog.md) for the full term table.

### Aggregates

This BC contains two aggregate roots: **Product** (the primary lifecycle root) and **Category** (the taxonomy root). Aggregates reference one another by ID only — `Product.CategoryId` is an ID reference, not a navigation property — consistent with the pattern in [`AlertSubscriber` + `MonitoredLocationAlertsSubscription`](../../src/Weather.Domain/Alerts/AlertSubscriber.cs) and [`MonitoredLocationAlertsSubscription.Create(monitoredLocationId)`](../../src/Weather.Domain/Alerts/Entities/MonitoredLocationAlertsSubscription.cs).

Both aggregates derive from [`AggregateRoot<TId>`](../../platform/Platform.SharedKernel/Base/AggregateRoot.cs) with `TId = Guid`, use a private parameterless constructor, and raise domain events via `AddDomainEvent(...)`.

#### Product (aggregate root)

**Purpose:** The Product aggregate represents a sellable item in the catalog. It owns its identity (`ProductId`), business key (`Sku`), descriptive content (name, description, images, dimensions, brand), classification (category reference), commercial terms (`Price`), and its lifecycle status (`Draft` → `Active` → `Discontinued` → optionally reactivated). All write operations flow through factory methods or state-transition methods, which are the sole legal path to mutate state; EF Core uses the private parameterless constructor for rehydration.

**Properties**

| Name | Type | Constraint |
|------|------|------------|
| `Id` | `Guid` | Aggregate identity. `Guid.CreateVersion7()` at creation. PK. |
| `Sku` | `Sku` (VO) | Non-empty, length 1–32, alphanumeric + dashes. Unique across all products (enforced by DB unique constraint + pre-create application check). |
| `Name` | `ProductName` (VO) | Non-empty, max 200 chars. |
| `Description` | `ProductDescription` (VO) | Max 4000 chars. Empty string allowed for Draft products. |
| `CategoryId` | `Guid` | Non-empty. Must reference an existing `Category.Id` (application-level referential check on write, DB FK on `product_search_view`). |
| `Brand` | `BrandName` (VO) | Non-empty, max 100 chars. |
| `Price` | `Money` (VO) | `Amount > 0`, ISO 4217 `Currency`. Pattern mirrors `Money` (planned: `Platform.SharedKernel.ValueObjects.Money`). |
| `Status` | `ProductStatus` (SmartEnum) | `Draft` (0), `Active` (1), `Discontinued` (2). See transition matrix below. |
| `Dimensions` | `Dimensions?` (VO, nullable) | Optional. Present for physical goods; null for digital/service products. |
| `Images` | `IReadOnlyCollection<ImageReference>` | Ordered by `DisplayOrder`. May be empty for Draft. At most one image with `DisplayOrder == 0` (the "primary"). |
| `CreatedUtc` | `DateTimeOffset` | `IAuditableEntity`, set by infrastructure. |
| `LastModifiedUtc` | `DateTimeOffset` | `IAuditableEntity`, updated on every persisted change. |

**Invariants**

- SKU is unique across all products (DB unique index + application-level `Product.SkuExistsAsync(sku)` check before `Create`).
- `Price.Amount > 0` and `Currency` is a defined ISO 4217 code (enforced by `Money.Create` returning `Result.Fail` on invalid input — same contract as `Money` (shared-kernel VO)).
- `CategoryId` is required; a product cannot be created without a category reference.
- A product in `Draft` status cannot be referenced by Basket. This is a **query-time validator** (not a domain invariant) — Basket's product-snapshot fetch layer rejects non-`Active` products.
- Status transitions are gated by [`ProductStatus.CanTransitionTo(...)`](#productstatus). Transitions that fail `CanTransitionTo` throw `DataIntegrityException` (they represent system bugs, not user input).
- `Discontinued → Active` requires an explicit `adminReactivation: true` flag on `Reactivate(...)`. Without it, the method returns `Result.Fail` (user-actionable error).
- Changing the price must move through `UpdatePrice(...)`; the VO's immutability and positivity check together guarantee `Amount > 0`.
- At most one image per `DisplayOrder` value; the collection is treated as ordered by `DisplayOrder`.

**Factory methods**

- `public static Result<Product> Create(Sku sku, ProductName name, ProductDescription description, Guid categoryId, BrandName brand, Money price, Dimensions? dimensions, IReadOnlyCollection<ImageReference> images)` — Creates a product in `Draft` status with `Id = Guid.CreateVersion7()`. Validates `categoryId != Guid.Empty` (returns `Result.Fail` if empty) and that the caller has already verified SKU uniqueness via an application service. Raises `ProductCreatedDomainEvent`. Because value-object construction returns `Result<T>` (standard shared-kernel VO factory pattern), callers assemble VOs first and pass them in; `Product.Create` only composes them. Returns `Result<Product>`.

**State-transition methods**

Each method returns `Result`/`Result<T>` for user-actionable domain errors (following the guidance in `CLAUDE.md`: result pattern for expected errors, exceptions only for bugs). `DataIntegrityException` is thrown only when the caller reached a branch that should be impossible (e.g., transitioning a status in a way `CanTransitionTo` explicitly forbids).

- `UpdatePrice(Money newPrice, DateTimeOffset utcNow) : Result`
  - **Precondition:** `Status != Discontinued` (discontinued products are read-only for price changes). Returns `Result.Fail(ProductErrors.CannotRepriceDiscontinued())` otherwise.
  - **Effect:** Compares `newPrice` to current `Price`; if identical (same amount and currency), no-op returning `Result.Ok()`. Otherwise assigns and raises `ProductPriceChangedDomainEvent { ProductId, OldPrice, NewPrice, OccurredOnUtc }`.
- `Describe(ProductDescription newDescription) : Result`
  - **Precondition:** `Status != Discontinued`.
  - **Effect:** Overwrites `Description`. Raises `ProductDescribedDomainEvent { ProductId, NewDescription, OccurredOnUtc }`.
- `Activate() : Result`
  - **Precondition:** `Status.CanTransitionTo(Active)` returns `true` (i.e., current status is `Draft`). Throws `DataIntegrityException` if called from a non-Draft state because callers should first check status.
  - **Effect:** Sets `Status = Active`. Raises `ProductActivatedDomainEvent { ProductId, OccurredOnUtc }`. Returns `Result.Ok()`.
- `Discontinue(string reason) : Result`
  - **Precondition:** `!string.IsNullOrWhiteSpace(reason)` (user-actionable — `Result.Fail(ProductErrors.ReasonRequired())` if empty), and `Status.CanTransitionTo(Discontinued)` (i.e., currently `Active`; otherwise `DataIntegrityException`).
  - **Effect:** Sets `Status = Discontinued`. Raises `ProductDiscontinuedDomainEvent { ProductId, Reason, OccurredOnUtc }`.
- `Reactivate(bool adminReactivation) : Result`
  - **Precondition:** Current `Status == Discontinued` and `Status.CanTransitionTo(Active, adminReactivation) == true`. Without `adminReactivation == true`, returns `Result.Fail(ProductErrors.ReactivationRequiresAdminFlag())` (user-actionable policy error). If the flag is `true` but status is not `Discontinued`, throws `DataIntegrityException`.
  - **Effect:** Sets `Status = Active`. Raises `ProductReactivatedDomainEvent { ProductId, OccurredOnUtc }`.

No method raises more than one domain event except `Create` (which raises exactly one).

#### Category (aggregate root)

**Purpose:** The Category aggregate is a node in a hierarchical taxonomy that organizes products. Each Category has an optional parent (root nodes have `ParentCategoryId == null`) and exposes a materialized `Path` (e.g., `/electronics/computers/laptops`) that enables prefix-based search on the `product_search_view` read model. Category is its own aggregate (not nested under Product) because: (1) its lifecycle is independent of any one product, (2) it is referenced by many products, and (3) products must not cascade-reparent. This matches Vernon's small-aggregate rule — reference other aggregates by ID, not by navigation.

**Properties**

| Name | Type | Constraint |
|------|------|------------|
| `Id` | `Guid` | Aggregate identity. `Guid.CreateVersion7()`. PK. |
| `Name` | `string` | Non-empty, max 100 chars. Case-preserved; stored verbatim. |
| `ParentCategoryId` | `Guid?` | Null means root. When non-null must reference an existing category and must not create a cycle. |
| `Path` | `CategoryPath` (VO) | Materialized path. `/` separator. Each segment lowercase slug of parent chain. Max depth = 5 segments. |
| `CreatedUtc` | `DateTimeOffset` | `IAuditableEntity`. |
| `LastModifiedUtc` | `DateTimeOffset` | `IAuditableEntity`. |

**Invariants**

- Depth of `Path` ≤ 5 segments.
- A category with children or products cannot be deleted (domain error `CategoryErrors.HasDependents`).
- Reparenting recomputes `Path` and **must revalidate depth**; reparenting a subtree whose new root would exceed depth 5 returns `Result.Fail(CategoryErrors.MaxDepthExceeded)`.
- A category cannot be its own ancestor (cycle check on reparent).
- `Path` is always consistent with `ParentCategoryId` + `Name`. Mutating either is only done via the aggregate's methods.

**Factory methods**

- `public static Result<Category> Create(string name, Guid? parentCategoryId, CategoryPath? parentPath)` — Creates a new category. If `parentCategoryId` is non-null, caller must pass the parent's `Path` (loaded via repository). Builds new `CategoryPath` as `parentPath + "/" + slug(name)` (or `"/" + slug(name)` for root). Validates new path depth ≤ 5. Returns `Result<Category>`. Raises `CategoryCreatedDomainEvent { CategoryId, Name, ParentCategoryId, Path, OccurredOnUtc }` on success.

**State-transition methods**

- `Rename(string newName) : Result`
  - **Precondition:** `newName` non-empty, ≤ 100 chars.
  - **Effect:** Rewrites `Path`'s final segment to `slug(newName)`; descendants' paths are updated by a **domain service** (not this aggregate) in a subsequent transactional step because cross-aggregate updates violate single-aggregate transactional scope. Raises `CategoryReparentedDomainEvent` (reused — `Reparented` is the generic "path changed" signal) with `OldParentId == NewParentId`.
- `Reparent(Guid? newParentCategoryId, CategoryPath? newParentPath) : Result`
  - **Precondition:** new depth ≤ 5; cycle check (caller must pre-verify via a `CategoryAncestryService`); if `newParentCategoryId == this.Id` → `Result.Fail(CategoryErrors.CannotParentToSelf)`.
  - **Effect:** Updates `ParentCategoryId` and rebuilds `Path`. Raises `CategoryReparentedDomainEvent { CategoryId, OldParentId, NewParentId, OldPath, NewPath, OccurredOnUtc }`.

### Value Objects

All value objects are `sealed record` types deriving from [`ValueObject`](../../platform/Platform.SharedKernel/Base/ValueObject.cs). Constructors are private; construction is via `Result<T>`-returning `Create` factories (pattern proven in `Money` (shared-kernel VO)).

#### Sku
- **Fields:** `Value : string`
- **Validation rules:**
  - Non-null, non-empty (trimmed).
  - Length 1–32.
  - Matches `^[A-Za-z0-9][A-Za-z0-9-]*$` (starts alphanumeric, remainder alphanumeric or dash).
  - Normalized to uppercase on create (so `abc-123` and `ABC-123` collide in the uniqueness index).
- **Errors:** `SkuErrors.Empty()`, `SkuErrors.TooLong(max: 32)`, `SkuErrors.InvalidCharacters()`.

#### Money
- **Fields:** `Amount : decimal`, `Currency : string` (ISO 4217, e.g. `"USD"`).
- **Validation rules:**
  - `Amount > 0` (strictly positive — matches the contract in `Money` (planned: `Platform.SharedKernel.ValueObjects.Money`), where `amount <= 0` is rejected).
  - `Currency` matches ISO 4217 format (3 uppercase letters). Catalog does **not** enforce a defined-code enum (unlike Ordering, which uses `CurrencyCode`) — v1 keeps Catalog permissive to avoid coupling to a shared-kernel enum. A future version may tighten this.
- **Errors:** `MoneyErrors.AmountMustBePositive()`, `MoneyErrors.InvalidCurrencyCode()`.
- **Note on units:** The CLAUDE.md hint ("minor units — e.g., cents for USD") means the integer-scaled representation is recommended at the storage layer; the domain type carries a `decimal` so consumers see `19.99 USD`, not `1999 cents`. Avro `decimal(19,4)` serialization preserves fractional precision.

#### ProductName
- **Fields:** `Value : string`
- **Validation rules:** Non-empty (trimmed), max 200 chars. Whitespace collapsed on create (multiple spaces → single space).
- **Errors:** `ProductNameErrors.Empty()`, `ProductNameErrors.TooLong(max: 200)`.

#### ProductDescription
- **Fields:** `Value : string`
- **Validation rules:** Max 4000 chars. Empty string allowed (Draft products need not be fully described). HTML is rejected by the API layer, not by the VO.
- **Errors:** `ProductDescriptionErrors.TooLong(max: 4000)`.

#### Dimensions
- **Fields:** `Length : decimal`, `Width : decimal`, `Height : decimal`, `Unit : string` (e.g., `"cm"`, `"in"`).
- **Validation rules:** All three lengths > 0. `Unit ∈ { "cm", "mm", "in" }` — small whitelist.
- **Errors:** `DimensionsErrors.NonPositiveDimension()`, `DimensionsErrors.UnsupportedUnit()`.
- **Nullability at the Product level:** `Product.Dimensions` is `Dimensions?`; the VO itself, once constructed, is always fully populated.

#### CategoryPath
- **Fields:** `Value : string` (materialized path, `/`-delimited, leading `/`, all segments lowercase slugs).
- **Validation rules:** Must match `^(/[a-z0-9][a-z0-9-]*){1,5}$` (1 to 5 segments, each ≥ 1 char). Depth invariant ≤ 5 is encoded in the regex's `{1,5}` quantifier.
- **Operations (instance methods):**
  - `Depth() : int` — returns segment count.
  - `Append(string slug) : Result<CategoryPath>` — returns a new `CategoryPath`; fails if the result would exceed depth 5.
  - `Breadcrumb(IReadOnlyDictionary<string, string> slugToName) : string` — helper used by the projection handler to build `CategoryBreadcrumb` (e.g. "Electronics > Computers > Laptops").
- **Errors:** `CategoryPathErrors.Malformed()`, `CategoryPathErrors.MaxDepthExceeded(max: 5)`.

#### ImageReference
- **Fields:** `Url : string`, `AltText : string`, `DisplayOrder : int`.
- **Validation rules:** `Url` non-empty and absolute (`Uri.TryCreate(url, UriKind.Absolute, out _)`); `AltText` non-empty, max 200 chars; `DisplayOrder ≥ 0`.
- **Errors:** `ImageReferenceErrors.InvalidUrl()`, `ImageReferenceErrors.AltTextEmpty()`, `ImageReferenceErrors.NegativeDisplayOrder()`.

#### BrandName
- **Fields:** `Value : string`
- **Validation rules:** Non-empty (trimmed), max 100 chars.
- **Errors:** `BrandNameErrors.Empty()`, `BrandNameErrors.TooLong(max: 100)`.

### SmartEnums

Built on `Ardalis.SmartEnum<T>`, following the template in [`SubscriptionTier`](../../src/Weather.Domain/Alerts/ValueObjects/SubscriptionTier.cs). Status-transition guards use a dedicated `_allowed` dictionary per SmartEnum; see `OrderStatus` in `ordering.md § 5.1` for the canonical eShop pattern.

#### ProductStatus

**Values and their properties**

| Name | Value | IsSellable | IsTerminal |
|------|-------|------------|------------|
| `Draft` | 0 | `false` | `false` |
| `Active` | 1 | `true` | `false` |
| `Discontinued` | 2 | `false` | `false` (not terminal — reactivatable with admin flag) |

- `IsSellable` — true when Basket may reference this product.
- `IsTerminal` — false for all (reactivation path keeps Discontinued non-terminal).

**Transition method**

```csharp
public bool CanTransitionTo(ProductStatus target, bool adminReactivation = false)
```

**Transition table (rows = current, columns = target):**

| From \ To | Draft | Active | Discontinued |
|-----------|-------|--------|--------------|
| **Draft** | — (no-op) | true | false |
| **Active** | false | — (no-op) | true |
| **Discontinued** | false | `adminReactivation == true` | — (no-op) |

- `Draft → Active` allowed (product launch).
- `Active → Discontinued` allowed (product end-of-life).
- `Discontinued → Active` only with `adminReactivation: true` (operator override).
- All other transitions return `false` → callers must surface a user-actionable error (for `Reactivate`) or a `DataIntegrityException` (for `Activate`/`Discontinue`) as described above.

### Internal Domain Events

All internal events are `sealed record` types deriving from [`DomainEvent`](../../platform/Platform.SharedKernel/Base/DomainEvents/DomainEvent.cs), which provides `OccurredOnUtc`. They are dispatched in-process by the `IDomainEventHandler<T>` dispatcher on aggregate save, consistent with [master design § 3](../eshop-master-design.md). No Avro schema; no Kafka.

#### ProductCreatedDomainEvent
- **Fields:** `ProductId : Guid`, `Sku : Sku`, `Name : ProductName`, `CategoryId : Guid`, `Price : Money`, `OccurredOnUtc : DateTimeOffset`.
- **Raised when:** `Product.Create(...)` succeeds.

#### ProductPriceChangedDomainEvent
- **Fields:** `ProductId : Guid`, `OldPrice : Money`, `NewPrice : Money`, `OccurredOnUtc : DateTimeOffset`.
- **Raised when:** `Product.UpdatePrice(...)` records a non-no-op change.

#### ProductDescribedDomainEvent
- **Fields:** `ProductId : Guid`, `NewDescription : ProductDescription`, `OccurredOnUtc : DateTimeOffset`.
- **Raised when:** `Product.Describe(...)` succeeds.

#### ProductActivatedDomainEvent
- **Fields:** `ProductId : Guid`, `OccurredOnUtc : DateTimeOffset`.
- **Raised when:** `Product.Activate()` succeeds (transition `Draft → Active`).

#### ProductDiscontinuedDomainEvent
- **Fields:** `ProductId : Guid`, `Reason : string`, `OccurredOnUtc : DateTimeOffset`.
- **Raised when:** `Product.Discontinue(reason)` succeeds.

#### ProductReactivatedDomainEvent
- **Fields:** `ProductId : Guid`, `OccurredOnUtc : DateTimeOffset`.
- **Raised when:** `Product.Reactivate(adminReactivation: true)` succeeds (transition `Discontinued → Active`).

#### CategoryCreatedDomainEvent
- **Fields:** `CategoryId : Guid`, `Name : string`, `ParentCategoryId : Guid?`, `Path : CategoryPath`, `OccurredOnUtc : DateTimeOffset`.
- **Raised when:** `Category.Create(...)` succeeds.

#### CategoryReparentedDomainEvent
- **Fields:** `CategoryId : Guid`, `OldParentId : Guid?`, `NewParentId : Guid?`, `OldPath : CategoryPath`, `NewPath : CategoryPath`, `OccurredOnUtc : DateTimeOffset`.
- **Raised when:** `Category.Rename(...)` or `Category.Reparent(...)` succeeds. In the Rename case, `OldParentId == NewParentId` and only the final segment of the path differs.

### External Summary Events

Per [master design § 3.3](../eshop-master-design.md), every external event is produced by a domain-event handler in the Application layer that (1) receives the internal `*DomainEvent`, (2) loads missing state from the aggregate/DbContext, (3) constructs the Avro-compiled `ISpecificRecord`, and (4) calls `_transactionalOutbox.AddOutboxMessage(topic, key, event)`. Copy the shape of [`SubscriptionActivatedOutboxPublisherDomainEventHandler`](../../src/Weather.Application/WeatherAlerts/PurchaseSubscription/SubscriptionActivatedOutboxPublisherDomainEventHandler.cs) for each.

**Message key:** always the aggregate ID as a string (Product events keyed by `ProductId`, Category events by `CategoryId`) — enables per-aggregate ordering within a Kafka partition.

**Topics:** `catalog.products` and `catalog.categories` (lowercase, dot-delimited, per master design topic-naming convention). These topics must be added to `docker-compose.yaml` during the Stage-2 event catalog wave.

#### ProductCreatedEvent
- **Triggered by:** `ProductCreatedDomainEvent`
- **Topic:** `catalog.products`
- **Key:** `ProductId` (as string)
- **Enrichment:** handler loads `Description` (truncated to 1000 chars), `Brand`, `Category.Path`, and `Status` from the `Product` aggregate + related `Category` row before emitting.
- **Consumer hints:**
  - **Inventory** — consumes to initialize a stock item at zero availability for the new `ProductId` (the only cross-BC dependency noted in the general plan: "Inventory subscribes to `catalog.products` to initialize stock items on `ProductCreatedEvent`").
  - **BFF / front-end caches** — consumes to warm product detail projections.
  - **Search indexer (future)** — consumes for a Meilisearch/Elastic projection if a full-text engine is added.

```json
{
    "type": "record",
    "name": "ProductCreatedEvent",
    "namespace": "Catalog.Products",
    "doc": "Event emitted when a new product is created in the Catalog. Enriched summary carrying all information downstream services need to initialize their own projections without calling back into Catalog.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the product aggregate."
        },
        {
            "name": "Sku",
            "type": "string",
            "doc": "Business key for the product (1-32 chars, alphanumeric + dashes, uppercase)."
        },
        {
            "name": "Name",
            "type": "string",
            "doc": "Product display name (max 200 chars)."
        },
        {
            "name": "Description",
            "type": "string",
            "doc": "Product description truncated to 1000 chars for transport. Consumers requiring full text must fetch via Catalog query API."
        },
        {
            "name": "CategoryId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Identifier of the category to which the product is assigned."
        },
        {
            "name": "CategoryPath",
            "type": "string",
            "doc": "Materialized category path (e.g., '/electronics/computers/laptops'). Enables prefix filtering downstream without a Catalog lookup."
        },
        {
            "name": "BrandName",
            "type": "string",
            "doc": "Brand name (max 100 chars)."
        },
        {
            "name": "PriceAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Product price amount."
        },
        {
            "name": "PriceCurrency",
            "type": "string",
            "doc": "ISO 4217 currency code (e.g., 'USD', 'EUR')."
        },
        {
            "name": "Status",
            "type": {
                "type": "enum",
                "name": "ProductStatus",
                "symbols": [
                    "Draft",
                    "Active",
                    "Discontinued"
                ]
            },
            "doc": "Product lifecycle status at the time of the event."
        },
        {
            "name": "CreatedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the product was created."
        }
    ]
}
```

#### ProductPriceChanged
- **Triggered by:** `ProductPriceChangedDomainEvent`
- **Topic:** `catalog.products`
- **Key:** `ProductId`
- **Consumer hints:**
  - **Basket** — may consume to invalidate basket snapshots or flag basket line items as "price changed since added" (per general plan: "Catalog → Basket: Anti-Corruption Layer — Basket stores product snapshots"). v1 implementation may prefer on-demand refresh instead; ADR-worthy.
  - **BFF** — caches are invalidated.

```json
{
    "type": "record",
    "name": "ProductPriceChanged",
    "namespace": "Catalog.Products",
    "doc": "Event emitted when a product's price is changed. Carries both old and new price so downstream consumers can detect magnitude of change without a prior snapshot.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the product whose price changed."
        },
        {
            "name": "Sku",
            "type": "string",
            "doc": "Business key of the product (denormalized for consumer convenience)."
        },
        {
            "name": "OldPriceAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Price amount before the change."
        },
        {
            "name": "NewPriceAmount",
            "type": {
                "type": "bytes",
                "logicalType": "decimal",
                "precision": 19,
                "scale": 4
            },
            "doc": "Price amount after the change."
        },
        {
            "name": "Currency",
            "type": "string",
            "doc": "ISO 4217 currency code. Same for old and new (Catalog does not support currency swap on a product)."
        },
        {
            "name": "ChangedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the price change was recorded."
        }
    ]
}
```

#### ProductDiscontinuedEvent
- **Triggered by:** `ProductDiscontinuedDomainEvent`
- **Topic:** `catalog.products`
- **Key:** `ProductId`
- **Consumer hints:**
  - **Basket** — should mark lines containing a discontinued product as non-checkoutable (surface to user "this product is no longer available").
  - **Inventory** — freezes inbound stock receipts (domain-specific policy).
  - **BFF** — removes from listings; detail page remains reachable for historical orders.

```json
{
    "type": "record",
    "name": "ProductDiscontinuedEvent",
    "namespace": "Catalog.Products",
    "doc": "Event emitted when a product is moved to the Discontinued status. Downstream services should stop offering this product for new purchases.",
    "fields": [
        {
            "name": "ProductId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the product that was discontinued."
        },
        {
            "name": "Sku",
            "type": "string",
            "doc": "Business key of the product (denormalized for consumer convenience)."
        },
        {
            "name": "Reason",
            "type": "string",
            "doc": "Free-text reason supplied by the operator (non-empty). Informational only."
        },
        {
            "name": "DiscontinuedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the product was discontinued."
        }
    ]
}
```

#### CategoryCreatedEvent
- **Triggered by:** `CategoryCreatedDomainEvent`
- **Topic:** `catalog.categories`
- **Key:** `CategoryId`
- **Consumer hints:**
  - **BFF** — builds/rebuilds the category navigation tree.
  - **Search indexer (future)** — refreshes facet navigation.

```json
{
    "type": "record",
    "name": "CategoryCreatedEvent",
    "namespace": "Catalog.Categories",
    "doc": "Event emitted when a new category node is created in the Catalog taxonomy.",
    "fields": [
        {
            "name": "CategoryId",
            "type": {
                "type": "string",
                "logicalType": "uuid"
            },
            "doc": "Unique identifier of the category."
        },
        {
            "name": "Name",
            "type": "string",
            "doc": "Category display name (max 100 chars)."
        },
        {
            "name": "ParentCategoryId",
            "type": [
                "null",
                {
                    "type": "string",
                    "logicalType": "uuid"
                }
            ],
            "default": null,
            "doc": "Optional identifier of the parent category. Null for root nodes."
        },
        {
            "name": "Path",
            "type": "string",
            "doc": "Materialized path of this category (e.g., '/electronics/computers'). Depth 1 to 5 segments."
        },
        {
            "name": "CreatedAtUtc",
            "type": {
                "type": "long",
                "logicalType": "timestamp-millis"
            },
            "doc": "UTC timestamp when the category was created."
        }
    ]
}
```

> **Deliberately NOT emitted externally (v1):** `ProductDescribedDomainEvent`, `ProductActivatedDomainEvent`, `ProductReactivatedDomainEvent`, `CategoryReparentedDomainEvent`. These are either informational to internal projections only (description changes; reactivation is rare and covered by a subsequent `ProductPriceChanged` or manual publish) or — in the case of reparenting — a heavier event that Stage 2 may introduce once cross-BC consumers actually need it. Adding them later is non-breaking.

### Pattern Showcase: CQRS Read Projection

Catalog's flagship pattern is a **denormalized read model built by an in-process projection handler from internal domain events**. The write model (the two aggregates) lives in the same database under schema `catalog`; the read view is another table in that same schema, decoupled from the write tables' normalization.

#### Read view table: `catalog.product_search_view`

| Column | Type | Notes |
|--------|------|-------|
| `ProductId` | `uuid` (PK) | Matches `Product.Id`. |
| `Sku` | `varchar(32)` | Unique index. |
| `Name` | `varchar(200)` | Full-text indexed (Postgres `tsvector` on `Name || ' ' || Description`). |
| `Description` | `text` | Truncated to 4000 chars at write. |
| `CategoryId` | `uuid` | FK to `catalog.categories`. |
| `CategoryPath` | `varchar(256)` | Btree index for prefix queries (`WHERE CategoryPath LIKE '/electronics/%'`). |
| `CategoryBreadcrumb` | `varchar(512)` | Denormalized display string, e.g. "Electronics > Computers > Laptops". Computed at projection time. |
| `BrandName` | `varchar(100)` | |
| `PriceAmount` | `numeric(19,4)` | Btree index for range queries. |
| `PriceCurrency` | `char(3)` | |
| `Status` | `smallint` | Maps to `ProductStatus.Value`; btree index (most queries filter `Status == Active`). |
| `ImagesJson` | `jsonb` | Array of `{"url": "...", "altText": "...", "displayOrder": N}`. |
| `CreatedAtUtc` | `timestamptz` | |
| `LastUpdatedAtUtc` | `timestamptz` | Updated on every projection event. |

#### Projection handler

`ProductSearchViewProjectionHandler` in `Catalog.Application.ProductSearchView.Projections` implements:

- `IDomainEventHandler<ProductCreatedDomainEvent>` — INSERT row.
- `IDomainEventHandler<ProductPriceChangedDomainEvent>` — UPDATE `PriceAmount`, `LastUpdatedAtUtc`.
- `IDomainEventHandler<ProductDescribedDomainEvent>` — UPDATE `Description`, `LastUpdatedAtUtc`.
- `IDomainEventHandler<ProductActivatedDomainEvent>` — UPDATE `Status`, `LastUpdatedAtUtc`.
- `IDomainEventHandler<ProductDiscontinuedDomainEvent>` — UPDATE `Status`, `LastUpdatedAtUtc`.
- `IDomainEventHandler<ProductReactivatedDomainEvent>` — UPDATE `Status`, `LastUpdatedAtUtc`.
- `IDomainEventHandler<CategoryCreatedDomainEvent>` — no-op on `product_search_view` (future: seed breadcrumb lookup). Retained as placeholder for completeness.
- `IDomainEventHandler<CategoryReparentedDomainEvent>` — bulk UPDATE across all products whose `CategoryPath` starts with the old path (replace prefix with new path, recompute breadcrumb). Executed in the same `SaveChangesAsync` transaction as the write-model change, so the read view never drifts.

**Transactional guarantee:** the handler writes through the **same DbContext** as the aggregate save, so the read view and write model commit atomically. No outbox, no eventual consistency **within** Catalog. Downstream BCs (Basket, Inventory, BFF) see events via Kafka and are eventually consistent, but Catalog's own search view is immediately consistent after save.

**Idempotency:** each handler uses `UPSERT` semantics keyed on `ProductId`, so replaying an event has no additional effect beyond updating `LastUpdatedAtUtc`.

#### Query side

The primary query is `SearchProductsQuery` — handled by `SearchProductsQueryHandler` that reads directly from `product_search_view` via a read-only `DbSet`.

Shape:

```csharp
public sealed record SearchProductsQuery(
    string? Text,                  // matches Name+Description via to_tsquery
    string? CategoryPathPrefix,    // e.g. "/electronics" -> WHERE CategoryPath LIKE '/electronics%'
    decimal? MinPrice,
    decimal? MaxPrice,
    string? Currency,
    ProductStatus? Status,         // usually Active
    int Page,
    int PageSize)
    : IQuery<Paged<ProductSearchItem>>;
```

Two concrete examples:

1. "All active laptops priced between 500 and 2000 USD" →
   ```sql
   SELECT * FROM catalog.product_search_view
   WHERE Status = 1
     AND CategoryPath LIKE '/electronics/computers/laptops%'
     AND PriceAmount BETWEEN 500 AND 2000
     AND PriceCurrency = 'USD'
   ORDER BY PriceAmount
   LIMIT 20 OFFSET 0;
   ```
2. "Full-text search 'wireless headphones' within /electronics/audio" →
   ```sql
   SELECT * FROM catalog.product_search_view
   WHERE Status = 1
     AND CategoryPath LIKE '/electronics/audio%'
     AND to_tsvector('english', Name || ' ' || Description)
         @@ to_tsquery('english', 'wireless & headphones');
   ```

This is the single performance layer for Catalog reads — per the success criteria, **no FusionCache is applied to Catalog writes** or Catalog reads in v1. The projection is the cache.

### Integration Points

**Consumers of Catalog external events:**
- **Basket** (general-plan context map: "Catalog → Basket: Anti-Corruption Layer") — consumes `ProductPriceChanged` and `ProductDiscontinuedEvent` to flag stale basket-line snapshots. Because Basket is Redis-backed and ephemeral, v1 implementations may rely on on-demand refresh at checkout instead of eager consumption; that tactical choice is Basket's to make, not Catalog's.
- **Inventory** (general-plan: "Inventory → Catalog: Events — stock changes update product availability", reversed for initialization) — consumes `ProductCreatedEvent` on `catalog.products` to initialize stock items at zero availability for the new product. Inventory's own events flow back the other way but do **not** mutate Catalog; Catalog remains the product-information authority.
- **EShop.BFF** — prefers synchronous HTTP query (`GET /api/catalog/products/{id}` and `/api/catalog/products/search`) for consumer-facing reads. Optional future: subscribe to `catalog.products` to warm a local cache.
- **Ordering** — does **not** consume Catalog events directly in v1. Ordering receives already-snapshotted product data in `OrderCreated` events from the Checkout saga; see [ADR-0005 — Customer Data in Ordering](../adr/0005-customer-data-in-ordering.md) for the snapshot pattern extension to product data.

**External dependencies:**
- **Inventory** — Catalog does **not** call Inventory. Stock state is external to the "what is sold" model.
- **No synchronous calls to other BCs** on the write path. All cross-BC coupling is via the outbox → Kafka.

**Context mapping patterns (for Stage 5 context map synthesis):**
- Catalog → Inventory: **Open Host Service** (Catalog publishes `catalog.products`; Inventory conforms).
- Catalog → Basket: **Open Host Service** on Catalog's side; **Anti-Corruption Layer** on Basket's side (Basket copies product data into its own snapshot VO rather than referring to Catalog types directly).
- Catalog → BFF: **Customer-Supplier** over HTTP query API.

### Infrastructure Notes

- **Storage:** PostgreSQL (moved from SqlServer per commit `0aaf53c feat: migrate SqlServer to Postgre`). Schema: `catalog`.
  - Write-model tables: `catalog.products`, `catalog.categories`, `catalog.product_images`.
  - Read-view table: `catalog.product_search_view`.
  - `catalog.outbox_messages`, `catalog.inbox_messages` — following the `Platform.ReliableMessaging.Outbox.EFCore` / `Inbox` conventions (existing `Weather.Infrastructure` usage is the template).
- **Outbox relay:** a new `Catalog.OutboxRelay` worker (cloned from the pattern of `platform/Platform.OutboxRelay*`) ships outbox messages to Kafka topics `catalog.products` and `catalog.categories`.
- **Projection handler location:** `services/Catalog/Catalog.Application/ProductSearchView/Projections/ProductSearchViewProjectionHandler.cs`.
- **External event handler location:** `services/Catalog/Catalog.Application/Products/Events/ProductOutboxPublisherDomainEventHandler.cs` (one class implementing all four `IDomainEventHandler<...>` interfaces, mirroring the Weather template).
- **FusionCache policy:** **not applied** to Catalog writes or reads in v1. The denormalized `product_search_view` is the performance layer. This is a deliberate simplification to showcase pure CQRS read projections without conflating the lesson with a distributed cache. Documented here, not in an ADR.
- **Migrations:** deterministic migrations are generated by the user per `CLAUDE.md` ("Never touch or generate EF Core/Sql script migrations"). This document specifies the intended table shape only.
- **Schema registry:** Avro schemas live under `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/Products/*.avsc` and `...Avro/Catalog/Categories/*.avsc`, materialized by downstream agents. Namespace convention: `Catalog.Products` and `Catalog.Categories` — matching master-design § 3.2.

### Error types

Catalog's error class set is the authoritative table in **[error-taxonomy.md § 1 + § 3.1](error-taxonomy.md)** (look for the `CatalogErrors` rows + the C# sketch in § 3). Single source of truth; do not duplicate here.
