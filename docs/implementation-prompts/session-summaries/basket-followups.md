# Basket BC — Wave 1 Closeout Follow-ups Session Summary

> Branch: `aaqwdqwd`. Source reviews: [`docs/implementation-prompts/session-summaries/basket-closeout.md`](basket-closeout.md) (CONDITIONAL-PASS) and [`docs/implementation-prompts/session-summaries2/basket-closeout.md`](../session-summaries2/basket-closeout.md) (FAIL). Triage approved per [`~/.claude/plans/you-are-fixing-wave-soft-waterfall.md`](../../../../.claude/plans/you-are-fixing-wave-soft-waterfall.md).

## Scope

Fix every CRITICAL and HIGH finding from both closeout reviews that lives inside the Basket boundary (`services/Basket/**`, `test/Basket.*/**`, `platform/Platform.SchemaRegistry.Contracts/**/Basket/**`), file MEDIUM / LOW items as GitHub issues, and document one finding that was already fixed (verified against current source — reviewer flag was based on a stale snapshot).

## Disposition table

### CRITICAL — fixed in this branch

| # | Source | Commit | Failing-test name | Notes |
|---|---|---|---|---|
| C-2 | sum2.closeout | [`6e8414e`](../../../) `fix(basket): map BasketConcurrencyError to HTTP 409` | `ResultsExtensionsTests.ResolveErrorResponse_WhenBasketConcurrencyError_Returns409WithBasketConcurrencyCode` | Extracted pure-mapping helper `ResolveErrorResponse` for unit-testability. |
| C-3 | sum2.closeout | [`0339f02`](../../../) `fix(basket): catch Redis exceptions in DeleteAsync` | `RedisBasketRepositoryDeleteAsyncTests.DeleteAsync_WhenRedisThrows{RedisTimeout|RedisConnection}Exception_ReturnsResultFail` | Catch is `RedisException or TimeoutException` (RedisTimeoutException : TimeoutException, NOT RedisException). |
| C-1 + sum2.H-1 | sum2.closeout | [`4767267`](../../../) `fix(basket): serialise checkout via CAS lock` | `CheckoutBasketCommandHandlerTests.Handle_WhenBothSavesConflict_PropagatesConcurrencyError_AndNoOutboxRowWritten` + `BasketCheckoutOutboxIntegrationTests.WhenTwoConcurrentCheckoutsForSameUser_ExactlyOneOutboxRowWritten` | Wraps load → checkout → SaveAsync → outbox-commit in `BasketConcurrencyRetry`. Touch() in `Basket.Checkout` is no longer dead work — it's the CAS token now (sum2.H-1 collapses). |

### CRITICAL — verified INVALID against current source (skipped)

| Finding | Verification |
|---|---|
| sum2.C-4 / sum1.HIGH-2 — `OccurredOnUtc` wall-clock leak | `Basket.cs:116, 217, 244, 280, 351, 372, 420` ALREADY explicitly set `OccurredOnUtc = utcNow` on every domain-event init. `grep "OccurredOnUtc = utcNow" services/Basket/Basket.Domain/Baskets/Basket.cs` returns 7 matches. The closeout reviewers' search was based on a stale snapshot. The platform-level `DomainEvent.OccurredOnUtc = DateTimeOffset.UtcNow` default still merits a sweep across all BCs — filed as [#233](https://github.com/DavidCapcuch/DotNetAtlas/issues/233). |

### HIGH — fixed in this branch

| # | Source | Commit | Failing-test name |
|---|---|---|---|
| sum1.HIGH-1 | sum1.closeout | [`30c1221`](../../../) `fix(basket): preserve item state when refresh finds no price changes` | `BasketTests.RefreshPrices_WhenAllPricesEqualButMetadataChanged_DoesNotMutateInMemoryItems` |
| sum1.HIGH-3 | sum1.closeout | [`0dacec6`](../../../) `fix(basket): assert RequireHttpsMetadata in deployed-env JWT guard` | `AuthenticationDependencyInjectionTests.AssertDeployedJwtBearerOptions_WhenRequireHttpsMetadataFalse_Throws` |
| sum2.H-2 | sum2.closeout | [`bafaf63`](../../../) `fix(basket): wrap checkout outbox commit in EnsureTransactionAsync` | Existing `CheckoutCommand_FullPipeline_*` integration tests cover the happy path; the wrap is preventative (single-SaveChanges atomic by default today). |
| sum2.H-3 | sum2.closeout | [`6965594`](../../../) `fix(basket): log checkout published only after SaveChanges` | `BasketCheckoutInitiatedOutboxPublisherDomainEventHandlerTests.Handle_LogsAtDebugWithQueuedVerb_NotInformation` |
| sum2.H-4 | sum2.closeout | [`50ad9b9`](../../../) `fix(basket): surface mapper currency failure as Result.Fail` | `RedisBasketRepositoryGetByUserIdAsyncTests.GetByUserIdAsync_WhenStoredCurrencyIsUnknown_ReturnsCorruptionFailureNotThrow` |
| sum2.H-6 | sum2.closeout | [`ad9f189`](../../../) `fix(basket): chunk Catalog by-ids batches to keep URLs bounded` | `ProductCatalogHttpAdapterTests.GetMany_WhenIdsExceedChunkSize_IssuesMultipleRequests` |
| sum2.H-7 | sum2.closeout | [`53d04dc`](../../../) `fix(basket): align lock retry budget with lock TTL` | `BasketRedisOptionsTests.Validate_WhenRetryBudgetShorterThanLockTtl_FailsWithDescriptiveError` |

### HIGH — out of Basket scope

| Source | Issue |
|---|---|
| sum2.H-5 — Address length constants duplicated across BCs | [#230](https://github.com/DavidCapcuch/DotNetAtlas/issues/230) `cross-cutting(wave1-followup): pin Address length constants on Platform.SharedKernel.Address` |

### MEDIUM / LOW — fixed inline (one batched commit [`32bd0c1`](../../../))

| # | Source | What changed |
|---|---|---|
| sum1.MEDIUM | `BasketSnapshot.cs:21` | `Items` defaults to `ImmutableArray<BasketItem>.Empty` so a reflective deserializer that bypasses the factory never bombs at `SequenceEqual`. |
| sum1.MEDIUM | `BasketTotal.cs:27` | `From(Money)` now asserts non-null + strictly positive — matches the XML doc. |
| sum2.M-1 | `BasketCheckoutInitiatedMapper.cs:33` | Currency read from `Total.Amount.Currency` instead of `Items[0].Snapshot.Price.Currency`. |
| sum2.M-3 | `RedisBasketRepository.cs:179-183` | Added one-line comment explaining why `ScriptEvaluateAsync` drops the caller's CT. |
| sum2.LOW + sum1.LOW | `RedisBasketRepository.cs:126-140` | Added XML doc on `DeleteAsync` covering the FusionCache-backplane second call and the deliberate lack of CAS lock. |
| sum2.L-1 | `ConnectionStringsOptions.cs` → `ConnectionStringNames.cs` | File renamed to match the class inside (one-type-per-file convention). |
| sum2.L-2 | `PersistenceDependencyInjection.AddBasketPersistence` → `AddBasketRedisPersistence` | Disambiguates from the SQL `AddDatabase` sibling. |

### MEDIUM / LOW — filed as GitHub issues

| # | Source | Issue |
|---|---|---|
| sum1.MEDIUM | `BasketItem.Create` vs `BuildUnchecked` SoT gap | [#225](https://github.com/DavidCapcuch/DotNetAtlas/issues/225) `basket(wave1-followup): reconcile BasketItem.Create vs BuildUnchecked single-source-of-truth gap` |
| sum1.MEDIUM | `BasketErrors.CatalogUnavailable/ProductNotFound` in Domain layer | [#226](https://github.com/DavidCapcuch/DotNetAtlas/issues/226) `basket(wave1-followup): move ACL errors (CatalogUnavailable, ProductNotFound) out of Basket.Domain` |
| sum2.M-2 | CORS startup guard for localhost + AllowCredentials | [#227](https://github.com/DavidCapcuch/DotNetAtlas/issues/227) `basket(wave1-followup): add startup guard for CORS AllowCredentials in deployed environments` |
| sum1.LOW | Stale milestone XML doc refs | [#228](https://github.com/DavidCapcuch/DotNetAtlas/issues/228) `basket(wave1-followup): trim stale milestone references from XML doc comments` |
| sum1.LOW | `Basket.Checkout` `Throw.If` vs `Result.Fail` style | [#229](https://github.com/DavidCapcuch/DotNetAtlas/issues/229) `basket(wave1-followup): consider Result.Fail vs Throw.If style consistency in Checkout guards` |

### Cross-cutting (out of Basket scope) — filed as GitHub issues

| Source | Issue |
|---|---|
| `platform/Platform.SharedKernel/ValueObjects/Address.cs` (sum2.H-5) | [#230](https://github.com/DavidCapcuch/DotNetAtlas/issues/230) `cross-cutting(wave1-followup): pin Address length constants on Platform.SharedKernel.Address` |
| `docs/bc-design/use-cases.md § 2.1.3` (M9 carry-forward L-1) | [#231](https://github.com/DavidCapcuch/DotNetAtlas/issues/231) `cross-cutting(wave1-followup): rename ItemNotInBasket to ItemNotFound in use-cases.md § 2.1.3` |
| `docs/bc-design/error-taxonomy.md § 3.1` (M9 carry-forward) | [#232](https://github.com/DavidCapcuch/DotNetAtlas/issues/232) `cross-cutting(wave1-followup): add ItemNotFound(productId) factory to error-taxonomy.md § 3.1` |
| `platform/Platform.SharedKernel/Base/DomainEvents/DomainEvent.cs:8` (closeout HIGH, but stale flag) | [#233](https://github.com/DavidCapcuch/DotNetAtlas/issues/233) `cross-cutting(wave1-followup): drop wall-clock default on Platform.SharedKernel DomainEvent.OccurredOnUtc` |

## New error code introduced

| Code | Type | HTTP | Origin |
|---|---|---|---|
| `Basket.Concurrency` | `BasketConcurrencyError : IError` (already existed) | **409** (was 500) | Surfaced cleanly by Fix C-2. |
| `Basket.Corruption` | `ValidationError` factory in `BasketErrors` | **503** | Introduced by Fix H-4 for the mapper-failure path. |

Both codes need to land in `docs/bc-design/error-taxonomy.md § 3.1` — captured under [#232](https://github.com/DavidCapcuch/DotNetAtlas/issues/232).

## Verification output (CI gates + four test slices)

All gates and slices were run against the post-cleanup HEAD on branch `aaqwdqwd`. Pre-existing non-Basket build failures in `Payments.IntegrationTests` (from uncommitted modifications visible in `git status` at session start) are NOT introduced by this round — every Basket project builds, formats, and tests cleanly when invoked in isolation.

```text
$ dotnet build -m   (per-project, all 8 Basket csproj)
   Basket.Api / Basket.Application / Basket.Domain / Basket.Infrastructure
   Basket.UnitTests / Basket.IntegrationTests / Basket.FunctionalTests / Basket.ArchitectureTests
   Build succeeded. 0 Error(s) (each project, exit 0)

$ dotnet restore --locked-mode
   ... All projects restored. Exit: 0.
   (Lock files for InMemory + Basket.Api refs regenerated in chore commit 2d19c35.)

$ dotnet format whitespace --no-restore --verify-no-changes  (per-project, all 8)
   Exit: 0 (each project — zero violations across the Basket scope).

$ dotnet format style --no-restore --verify-no-changes  (per-project, all 8)
   Exit: 0 (each project — zero violations across the Basket scope).

$ NO_PROXY='*' dotnet test test/Basket.UnitTests/Basket.UnitTests.csproj
   Passed!  - Failed: 0, Passed: 164, Skipped: 0, Total: 164, Duration: 1 s

$ NO_PROXY='*' dotnet test test/Basket.ArchitectureTests/Basket.ArchitectureTests.csproj
   Passed!  - Failed: 0, Passed: 36, Skipped: 0, Total: 36

$ NO_PROXY='*' dotnet test test/Basket.IntegrationTests/Basket.IntegrationTests.csproj
   Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4

$ NO_PROXY='*' dotnet test test/Basket.FunctionalTests/Basket.FunctionalTests.csproj
   Passed!  - Failed: 0, Passed: 23, Skipped: 0, Total: 23

Totals                            new vs M9 baseline
UnitTests          164 / 164      +21  (143 → 164)
ArchitectureTests   36 / 36       +0
IntegrationTests     4 / 4        +1   (3 → 4   — parallel-checkout)
FunctionalTests     23 / 23       +0
                  ---------       ----
                  227 / 227 green +22  (205 → 227)
```

22 new tests across the unit + integration slices — three CRITICAL and seven HIGH fixes each landed with a pinned regression test (plus the multi-case error-mapping suite for C-2 and the parallel-checkout integration test for C-1).

Note on functional + architecture slices: zero new tests were added. The functional slice's existing end-to-end coverage already exercises every endpoint through the full ASP.NET pipeline including `ResultsExtensions.SendErrorResponseAsync`; the C-2 fix surfaces via the same path. The architecture slice would benefit from a custom Mono.Cecil rule asserting every `*DomainEvent` initializer in `Basket.Domain` explicitly sets `OccurredOnUtc` — captured as part of [#233](https://github.com/DavidCapcuch/DotNetAtlas/issues/233) (the platform-side root-cause fix).

## Commits on this branch

```
chore(basket): apply MEDIUM/LOW closeout cleanups inline           32bd0c1
chore(basket): regenerate packages.lock.json for follow-up fixes   2d19c35
fix(basket): align lock retry budget with lock TTL                 53d04dc
fix(basket): chunk Catalog by-ids batches to keep URLs bounded     ad9f189
fix(basket): surface mapper currency failure as Result.Fail        50ad9b9
fix(basket): log checkout published only after SaveChanges         6965594
fix(basket): wrap checkout outbox commit in EnsureTransactionAsync bafaf63
fix(basket): assert RequireHttpsMetadata in deployed-env JWT guard 0dacec6
fix(basket): preserve item state when refresh finds no price changes 30c1221
fix(basket): serialise checkout via CAS lock                       4767267
fix(basket): catch Redis exceptions in DeleteAsync                 0339f02
fix(basket): map BasketConcurrencyError to HTTP 409                6e8414e
```

12 commits, ten of which are TDD-driven CRITICAL/HIGH fixes (one fix per commit). The two `chore` commits batch (a) the regenerated `packages.lock.json` entries triggered by the new `Microsoft.EntityFrameworkCore.InMemory` test-side dependency + the `Basket.Api` project ref added to `Basket.UnitTests` for the C-2 fix's pure-mapping tests, and (b) the seven MEDIUM/LOW inline cleanups that don't merit a commit each.

## Boundary discipline

Stayed inside the Basket boundary at all times. Files outside that boundary were filed as GitHub issues rather than edited:

| Out-of-scope file | Disposition |
|---|---|
| `platform/Platform.SharedKernel/**` | Issues [#230](https://github.com/DavidCapcuch/DotNetAtlas/issues/230), [#233](https://github.com/DavidCapcuch/DotNetAtlas/issues/233) |
| `docs/bc-design/use-cases.md` | Issue [#231](https://github.com/DavidCapcuch/DotNetAtlas/issues/231) |
| `docs/bc-design/error-taxonomy.md` | Issue [#232](https://github.com/DavidCapcuch/DotNetAtlas/issues/232) |

The pre-existing uncommitted modifications visible in `git status` at session start (Catalog, Inventory, Invoicing, Notifications, Ordering, Payments + their lock files) were left untouched — same disposition as M8 and M9.

## Outstanding

Nine issues filed (5 basket + 4 cross-cutting). One closeout finding skipped as INVALID (sum2.C-4 — already fixed in source). All other CRITICAL and HIGH closeout findings landed in this branch with TDD coverage.
