# Catalog M3 — Application Layer Session Summary

> Milestone M3 per `docs/implementation-prompts/catalog.md <session_management>` — Application layer (commands, queries, validators, projection handlers, outbox publishers) + unit tests. Branch: `aaqwdqwd`. Commits `c45f53d..d071d7e` (8 commits).

## Deliverables

### Implemented (in-scope)

- **Avro contracts** (4 `.avsc` + generated `.cs`) under `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/`:
  - `Products/ProductCreatedEvent.avsc` / `.cs`
  - `Products/ProductPriceChanged.avsc` / `.cs`
  - `Products/ProductDiscontinuedEvent.avsc` / `.cs`
  - `Products/ProductStatus.cs` (avrogen-generated enum)
  - `Categories/CategoryCreatedEvent.avsc` / `.cs`
- **Application common** under `services/Catalog/Catalog.Application/Common/`:
  - `Data/ICatalogDbContext.cs` — application-owned contract extending `IOutboxDbContext` with `Products`, `Categories`, `ProductSearchView` sets.
  - `Messaging/TopicsOptions.cs` — `CatalogProducts`, `CatalogCategories` (DataAnnotations-validated; DltTopicSuffix dropped, see M4 finding).
  - `ReadModels/ProductSearchViewRow.cs` — denormalized POCO matching `catalog.md § 9` with `CorrelationId` TODO for M6.
  - `ReadModels/ProductSearchViewMapper.cs` — row → `GetProductByIdResponse` mapping + `Dimensions`/`Images` JSON (de)serialization helpers.
  - `FeatureFlags/CatalogFeatureFlags.cs` — constant `ShowDiscontinuedInSearch`.
  - `ApplicationDependencyInjection.cs` — `AddApplication()` extension (validators, CQRS handlers, domain-event handlers, behaviour chain).
- **7 commands + validators + handlers**: `CreateProduct`, `UpdateProductPrice`, `DescribeProduct`, `DiscontinueProduct`, `ReactivateProduct`, `CreateCategory`, `ReparentCategory` (self-parent guard only).
- **5 queries + response DTOs + (where applicable) validators**: `GetProductById`, `SearchProducts` (feature-flag gated via `IFeatureClient`), `GetProductsByIds` (partial-tolerant, 1–100 ids), `GetCategoryTree` (whole tree or subtree), `GetProductsByCategory` (direct or include-descendants).
- **8 projection handlers** (one per internal domain event, including `ProductActivated` added during review fix-up): live in each command feature folder, co-located with its command/handler. All run inside the command's UoW without calling `SaveChanges`.
- **4 outbox publishers**: `ProductCreated`, `ProductPriceChanged`, `ProductDiscontinued` → `catalog.products`; `CategoryCreated` → `catalog.categories`. Kafka keys = aggregate Id; Avro payloads enriched via `FindAsync` against the DbContext change tracker (no DB round-trip).
- **Domain error factories** (additive gap-fill, user-approved): `ProductErrors.NotFound`, `ProductErrors.SkuAlreadyExists`, `CategoryErrors.NotFound`, `CategoryErrors.ParentNotFound`.
- **Doc self-corrections**: `docs/bc-design/error-taxonomy.md § 3.2` enumerates the new factories with HTTP mappings and flags deferred ones; `docs/bc-design/catalog.md` lines 565/581/591 switched to the safer segment-bounded prefix form (consistent with line 529).
- **Package delta**: `services/Directory.Packages.props` pins `OpenFeature 2.12.0`; `Catalog.Application.csproj` adds one `<PackageReference>` for it.
- **Tests**: 247 unit tests in `Catalog.UnitTests` — validators, handlers, projection handlers (happy + missing-row + data-integrity-throw branches), outbox publishers (topic/key/payload assertions), feature-flag gating (off/on/status-override paths), segment-bounded prefix isolation.
- **Test helpers**: `FakeCatalogDbContext` (EF Core InMemory + `OwnsOne` VO mappings), `CatalogFactories` (Category + Product builders), `ProductSearchViewRowBuilder` (projection row builder), `FakeCatalogDbContextSmokeTests` (VO round-trip proof).

### Deferred to an M3-followup milestone

Flagged up-front with the user; tracked via TODO comments in the source:

1. **`Product.AddImage` / `Product.RemoveImage` domain methods** and their commands `AddProductImageCommand` / `RemoveProductImageCommand` + corresponding domain events (`ProductImageAddedDomainEvent`, `ProductImageRemovedDomainEvent`) and error factories (`ProductErrors.DuplicateImageDisplayOrder`, `ProductErrors.ImageNotFound`).
2. **`DeleteCategoryCommand`** + `CategoryDeletedDomainEvent` + `CategoryErrors.HasChildren` / `HasProducts`.
3. **`CategoryAncestryService`** — cycle detection for `ReparentCategoryCommandHandler`. `ReparentCategory` in M3 only rejects direct self-parenting; reparenting A under one of A's descendants is NOT rejected. TODO at `services/Catalog/Catalog.Application/Categories/ReparentCategory/ReparentCategoryCommandHandler.cs:17-26`.
4. **`CategoryPathService`** — descendant path cascade. `CategoryReparentedProjectionHandler` is a no-op in M3 (logs Debug); stale `product_search_view.CategoryPath` rows under the old path remain stale until the cascade ships.
5. **ADR-0015 threading via `TimeProvider`** — see H1 below.

### Accepted as post-milestone follow-ups (not code change)

- **H1 (Opus review)**: `OccurredOnUtc` on Describe/Discontinue/Reactivate/Create/CategoryCreated/CategoryReparented domain events is sourced from the `DomainEvent` base-record default `DateTimeOffset.UtcNow` rather than the handler's `TimeProvider`. Only `Product.UpdatePrice` currently threads `TimeProvider` → event. Fixing this requires signature changes on `Catalog.Domain` mutators, which crosses the user-approved M3 scope (additive error factories only). M4 integration tests with `FakeTimeProvider` will make this break loudly; tracked here so the next milestone picks it up.
- **M2 (Opus review)**: `SearchProductsQueryHandler` calls `IFeatureClient.GetBooleanValueAsync(...)` without an `EvaluationContext`. ADR-0014 v1 uses the JSON-file provider which has no targeted rules, so this is a no-op today. Revisit in M6 when correlation-id/user-id are ambient on `HttpContext`.
- LOW findings L1–L4 (cosmetic): reviewed, intentional or not worth the churn.

## Design decisions taken (with rationale)

- **Projection handler organisation**: one class per internal domain event (8 classes). Matches `catalog.md <example_design_decision>`: test isolation, DI registers per closed type (mirrors natural dispatcher scan), consumer-per-event-type analogy from Kafka. Cost: ~30 LOC each, low duplication.
- **Money VO source**: shared-kernel `Platform.SharedKernel.ValueObjects.Money` (NO duplication in the Catalog BC). Confirmed via `Product.cs` imports.
- **`Catalog.Application` boundary around EF Core**: `ICatalogDbContext` lives in the Application layer; Infrastructure will implement it in M4. This avoids a direct Application→EF dependency in shape, while still allowing LINQ-against-`DbSet<T>` for query handlers.
- **Options binding**: `TopicsOptions` registered via `services.AddOptions<T>()` only — binding to `IConfiguration` and `ValidateOnStart()` are the API host's job in M6. Rationale: avoids pulling `Microsoft.Extensions.Options.ConfigurationExtensions` into `Catalog.Application` and keeps the layer free of `IConfiguration`. Documented in `ApplicationDependencyInjection.cs` remarks.
- **Outbox enrichment pattern**: publishers load the aggregate (and related Category) via `DbContext.Products.FindAsync` / `Categories.FindAsync`. When the domain-event dispatcher fires, the aggregate is in the change tracker so `FindAsync` returns instantly without DB I/O. For the Create path, the Category was loaded by the command handler for existence check, so it's also tracked. For any other path it's a single-row fetch on a bounded-size table — acceptable latency.
- **Test strategy**: unit-only in M3. The milestone text mentions "outbox integration test", but `Catalog.IntegrationTests/Placeholder.cs` explicitly says integration tests land in M4 with the real DbContext. In lieu, outbox publisher unit tests substitute `ITransactionalOutbox<ICatalogDbContext>` via NSubstitute and assert the exact topic/key/Avro payload per call. Transactional roundtrip through a real Postgres Testcontainers fixture moves to M4.
- **ReparentCategory scope-down**: self-parent guard only, no cycle detection, no descendant cascade. Spec-faithful for M3's boundary; TODOs in place; tests assert only the implemented behaviour.
- **Correlation-id seeding**: `ProductSearchViewRow.CorrelationId` set to `Guid.Empty` in M3 with a `TODO(M6)` pointer to `HttpContext.Items[CorrelationIdContextKeys.HttpContextItemsKey]` once the API layer wires the middleware.
- **Review H2 fix**: segment-bounded prefix matches use `path == prefix || path.StartsWith(prefix + "/")` in all three affected query handlers. Translates to `= $1 OR path LIKE $1 || '/%'` on Postgres; avoids the `'/electronics'` → `'/electronics-toys'` false positive. Added isolation tests in each of the three handlers.

## Feature-flag verification

`catalog.show-discontinued-in-search` (ADR-0014) is wired via `OpenFeature.IFeatureClient` in `SearchProductsQueryHandler`. Three unit tests in `SearchProductsQueryHandlerTests` cover the key states:

- `Given_FlagFalse_When_Searching_Then_HidesDiscontinued` — default → only Active returned.
- `Given_FlagTrue_When_Searching_Then_IncludesDiscontinued` — flag on → Active + Discontinued.
- `Given_ExplicitStatusFilter_When_Searching_Then_FlagIgnored` — explicit `Status=Discontinued` overrides the flag off.

Runtime end-to-end verification via the JSON-file provider will come once the API host wires `AddFeatureFlags(...)` in M6.

## ADR compliance

- **ADR-0002** (pricing in Catalog): kept flat + inline. `Money` is the shared-kernel VO; no Pricing BC seam introduced.
- **ADR-0007** (Avro FORWARD_TRANSITIVE): 4 `.avsc` files match `events-catalog.md § 5.1` byte-for-byte on field names, logical types, and enum symbols.
- **ADR-0008** (correlation-id): `CorrelationId` column reserved on `ProductSearchViewRow`; `TODO(M6)` comment marks the population point.
- **ADR-0012** (versioning): HTTP routes under `/api/v1/catalog/...` are the API-layer concern (M6); Application layer is route-agnostic.
- **ADR-0013** (idempotency): `.Idempotency()` filter is an API-layer concern (M6); no application-level change required.
- **ADR-0014** (feature flags): explicit consumer of `catalog.show-discontinued-in-search` via `IFeatureClient`, verified by three tests.
- **ADR-0015** (time policy): `UpdateProductPrice` flow threads `TimeProvider` correctly. H1 finding above tracks the gap on other mutators as a follow-up.
- **ADR-0016** (Redis topology): not applicable at the Application layer (Redis is Infrastructure + API output-cache territory).

## Verification output (all executed on HEAD = d071d7e)

```
$ dotnet build -m (Catalog-scoped csprojs)
  Platform.SchemaRegistry.Contracts -> ...Contracts.dll
  Catalog.Domain -> ...Catalog.Domain.dll
  Catalog.Application -> ...Catalog.Application.dll
  Catalog.Infrastructure -> ...Catalog.Infrastructure.dll (4 NU1903 warnings — pre-existing transitive vulnerability in System.Security.Cryptography.Xml, not introduced by M3)
  Catalog.Api -> ...Catalog.Api.dll
  Catalog.UnitTests -> ...Catalog.UnitTests.dll
  0 errors

$ dotnet restore --locked-mode
  Restored all projects; packages.lock.json consistent across the repo.

$ dotnet format whitespace --no-restore --verify-no-changes
  Clean.

$ dotnet format style --no-restore --verify-no-changes
  Clean.

$ dotnet test test/Catalog.UnitTests/Catalog.UnitTests.csproj --no-build
  Successful! Failed: 0, Passed: 247, Skipped: 0, Total: 247, Duration: 2s
```

**Repo-wide `dotnet build -m` caveat**: 18 errors in `test/Basket.UnitTests/Baskets/Application/ValidatorsTests.cs` (SA1025 whitespace). These files are **unstaged Basket drift** introduced before this M3 session started — not touched by any of the 8 Catalog M3 commits (confirmed via `git show --stat` on each). They will block `dotnet build -m` at the repo level until the Basket work lands or the drift is stashed.

**M3-not-runnable verifications** (require later milestones):

- `dotnet test test/Catalog.ArchitectureTests/` — needs M5.
- `dotnet test test/Catalog.IntegrationTests/` — needs M4 DbContext (confirmed by `Placeholder.cs`).
- `dotnet test test/Catalog.FunctionalTests/` — needs M6 API endpoints.
- `docker compose --profile full up -d` + curl smoke — needs M4 + M6 + M7.

## Peer-review chain

- **Opus pre-commit review** (`feature-dev:code-reviewer`, model=opus): verdict *approve-with-fixes*. 0 CRITICAL, 2 HIGH (one fixed H2 + segment-bounded prefix, one deferred H1 + `TimeProvider` threading on Domain mutators), 4 MEDIUM (M1, M3, M4 fixed; M2 deferred), 4 LOW (accepted). Pre-commit fix commit `d071d7e` addresses H2 + M1 + M3 + M4 with added test coverage.
- `superpowers:verification-before-completion`: executed inline — all 4 gates run with captured output (see above).
- `nw-software-crafter-reviewer`: **NOT invoked** per `_shared.md § 11` step 3. The Opus pre-commit pass (Sonnet is explicitly weaker per § 7 footnote; Opus caught two HIGH findings) is primary; Haiku complement deferred. Document as a follow-up for the M4 close-out.

## Open questions / improvements proposed but NOT implemented

- **H1 — ADR-0015 threading**: `DomainEvent.OccurredOnUtc` default fires `DateTimeOffset.UtcNow` at record construction. Fix needs `utcNow` parameter on `Product.Discontinue/Reactivate/Describe/Create` + `Category.Create/Reparent` + handler plumbing of `TimeProvider`. Cross-cutting signature change on Domain mutators; out of scope for M3 (user approved additive error factories only). Must land before M4 integration tests with `FakeTimeProvider`.
- **`ProductActivatedDomainEvent`** has no HTTP command in v1 (M3 can create Drafts but not activate them). Either add an `ActivateProductCommand` in a follow-up or document as a Wave-1 limitation.

## File-touch audit (nothing outside M3 boundary)

Each commit's `git show --stat` confirms only the following paths were touched:

- `platform/Platform.SchemaRegistry.Contracts/Avro/Catalog/**` (Avro contracts — explicitly permitted).
- `services/Catalog/Catalog.Application/**` (owning BC).
- `services/Catalog/Catalog.Domain/Products/Errors/ProductErrors.cs`, `Categories/Errors/CategoryErrors.cs` (additive error factories — user-approved gap-fill).
- `services/Catalog/Catalog.Application/Catalog.Application.csproj`, `packages.lock.json`.
- `services/Directory.Packages.props` (one `PackageVersion` add: `OpenFeature`).
- `test/Catalog.UnitTests/**` (owning BC's unit tests).
- `test/Catalog.UnitTests/packages.lock.json` (restored on reference graph change).
- `docs/bc-design/error-taxonomy.md`, `docs/bc-design/catalog.md` (self-corrections only).
- `docs/implementation-prompts/session-summaries/catalog-m3.md` (this file).

No files outside `<boundaries>` were staged.

## Ready state

- 8 commits on `aaqwdqwd` from `c45f53d` to `d071d7e`.
- 247/247 unit tests green.
- 4 gates green for the Catalog scope (Domain + Application + UnitTests + API + Infrastructure + SchemaRegistry).
- Hand-off block for M4 follows in the session's closing message.
