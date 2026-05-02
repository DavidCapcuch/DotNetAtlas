# Catalog — Example Mapping Sessions

> Format: Matt Wynne's Example Mapping (Story / Rules / Examples / Questions) with BDD Given/When/Verify/Then for Examples. Each session corresponds to one non-trivial business rule or invariant on the BC's aggregate. These sessions are the seed for executable acceptance-test specs (SpecFlow / Reqnroll) during implementation.
>
> Color legend (echoing the reference images):
> - 📖 Yellow = Story
> - 📐 Blue = Rule (business invariant)
> - 🌱 Green = Example ("The one where...")
> - ❓ Pink = Question (open issue)
> - 💬 White = Answer / Note (resolved question)

---

## Session 1: Reparent category without orphaning children or exceeding max depth

### 📖 Story
As a **catalog administrator** I want **to move a category (and its descendants) under a new parent** so that **the taxonomy reflects merchandising changes without losing product associations or breaking the breadcrumb navigation**.

### 📐 Rules
- **R1** — `CategoryPath` depth is capped at 5 segments; any reparent whose resulting subtree would exceed depth 5 must be rejected.
- **R2** — A category cannot be reparented under itself or any of its own descendants (cycle prevention).
- **R3** — Reparenting recomputes the `Path` of the target category and every descendant; the cross-aggregate descendant update is a domain-service operation (outside the aggregate boundary) executed in the same transactional step.
- **R4** — The destination parent must exist; reparenting to a missing `ParentCategoryId` is a user-actionable failure.
- **R5** — A root category (`ParentCategoryId == null`, `Path == "/slug"`) may be reparented; the new path is `{parentPath}/{slug}`.

### 🌱 Examples

#### The one where the target depth is still within limits

- **Given** category `Laptops` at path `/electronics/computers/laptops` (depth 3) and destination `Portable Devices` at path `/electronics/portable-devices` (depth 2)
- **When** `ReparentCategoryCommand(Laptops, newParentId=PortableDevicesId)` is handled
- **Verify** R1, R2, R3
- **Then** `Laptops.Path` becomes `/electronics/portable-devices/laptops`, every descendant's `Path` prefix is rewritten by the domain service, and `CategoryReparentedDomainEvent(OldPath, NewPath)` is raised.

#### The one where the new parent would push depth past 5

- **Given** category `Wireless Mice` at path `/electronics/computers/peripherals/mice/wireless` (depth 5) and destination `Accessories Portable Devices Gaming` at path `/electronics/accessories/portable/gaming` (depth 4)
- **When** `ReparentCategoryCommand(WirelessMice, newParentId=GamingId)` is handled
- **Verify** R1
- **Then** the command returns `Result.Fail(CategoryErrors.MaxDepthExceeded(max: 5))`, no path changes, no `CategoryReparentedDomainEvent` is raised.

#### The one where the destination is the category's own descendant

- **Given** category `Electronics` at `/electronics` with descendant `Laptops` at `/electronics/computers/laptops`
- **When** `ReparentCategoryCommand(Electronics, newParentId=LaptopsId)` is handled
- **Verify** R2
- **Then** the command returns `Result.Fail(CategoryErrors.ReparentCreatesCycle(category.Id, newParentId))`, no path changes, no event raised. The cycle is detected by `CategoryAncestryService` before the aggregate runs (see § Questions below).

#### The one where a root category is moved under a real parent

- **Given** root category `Accessories` at path `/accessories` (depth 1) and destination `Electronics` at `/electronics` (depth 1)
- **When** `ReparentCategoryCommand(Accessories, newParentId=ElectronicsId)` is handled
- **Verify** R1, R3, R5
- **Then** `Accessories.Path` becomes `/electronics/accessories`, descendants' paths are re-prefixed by the domain service, and `CategoryReparentedDomainEvent(OldPath="/accessories", NewPath="/electronics/accessories")` is raised.

### ❓ Questions
*(None — cycle pre-check is explicitly the responsibility of `CategoryAncestryService` before calling the aggregate, and the destination-existence check is an application-layer concern.)*

---

## Session 2: Reactivate discontinued product requires admin flag

### 📖 Story
As a **catalog administrator** I want **to reactivate a previously discontinued product** so that **a temporarily withdrawn SKU can re-enter the catalog without losing its identity, SKU, or historical order associations**.

### 📐 Rules
- **R1** — The only legal transition out of `Discontinued` is back to `Active`, and only via `Product.Reactivate(adminReactivation: true)`.
- **R2** — Without `adminReactivation == true`, `Reactivate` returns `Result.Fail(ProductErrors.ReactivationRequiresAdminFlag())` — this is a user-actionable policy error, not a bug.
- **R3** — Successful reactivation raises `ProductReactivatedDomainEvent` (internal only in v1; not published to Kafka).
- **R4** — Historical order references are preserved — reactivation neither changes the `ProductId` nor rewrites past order lines; orders continue to show the item as it was at purchase.
- **R5** — Calling `Reactivate` with `adminReactivation: true` when the product is not `Discontinued` is a bug (the UI should gate the button); the method throws `DataIntegrityException`.

### 🌱 Examples

#### The one where admin reactivates a discontinued product

- **Given** product `WH-1000XM5` with `Status = Discontinued`
- **When** `Product.Reactivate(adminReactivation: true)` is called
- **Verify** R1, R3, R4
- **Then** `Status` becomes `Active`, `ProductReactivatedDomainEvent(ProductId, OccurredOnUtc)` is raised, no external Kafka event is emitted, and existing orders referencing this product are untouched.

#### The one where a non-admin caller forgets the flag

- **Given** product `WH-1000XM5` with `Status = Discontinued`
- **When** `Product.Reactivate(adminReactivation: false)` is called
- **Verify** R2
- **Then** the method returns `Result.Fail(ProductErrors.ReactivationRequiresAdminFlag())`, `Status` remains `Discontinued`, no domain event is raised.

#### The one where reactivate is called on an already-active product

- **Given** product `WH-1000XM5` with `Status = Active`
- **When** `Product.Reactivate(adminReactivation: true)` is called
- **Verify** R5
- **Then** the method throws `DataIntegrityException` (the caller reached an unreachable branch; the admin UI must not offer "Reactivate" on an Active product).

### ❓ Questions
*(None.)*
