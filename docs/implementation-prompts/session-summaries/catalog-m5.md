# Catalog M5 — Architecture Tests Session Summary

> Milestone M5 per `docs/implementation-prompts/catalog.md <session_management>` step 5 — architecture tests. Branch: `aaqwdqwd`. Catalog-attributed commit: pending (this summary lands alongside the test files in a single M5 commit).

## Deliverables

A new `test/Catalog.ArchitectureTests/` project with **41 [Fact] tests across 16 source files**, mirroring the `test/Weather.ArchitectureTests/` pattern where the rule is universal (§ 1.1–1.4 of `architecture-tests.md`) and adding Catalog-specific rules on top (§§ 1.5, 1.6, 2.1 + ADR-0015).

### Files created

`test/Catalog.ArchitectureTests/`:

- `BaseTest.cs` — anchors the four layer assemblies via the existing `IAssemblyMarker` interfaces (no production-code change required); seven custom `ICustomRule` implementations.
- `CleanArchitecture/CleanArchitectureLayerTests.cs` (6 tests) — § 1.1 layer isolation.
- `Domain/AggregateRootTests.cs` (5 tests) — § 1.2 aggregate discipline (sealed, immutable-externally, private ctors, no cross-aggregate type refs, **public static factory**).
- `Domain/ValueObjectTests.cs` (3 tests) — VO discipline.
- `Domain/EntityTests.cs` (3 tests) — non-aggregate `Entity<T>` discipline; passes trivially (Catalog has no non-aggregate Entity today; file lands so the rule fires the moment one is added).
- `Domain/DomainEventTests.cs` (4 tests) — § 1.3: name suffix, sealed, immutable, namespace `^Catalog\.Domain\.\w+\.Events$`.
- `Domain/AdrComplianceTests.cs` (1 test) — ADR-0015: no `DateTime.UtcNow`/`DateTime.Now`/`DateTime.Today`/`DateTimeOffset.UtcNow`/`DateTimeOffset.Now` static getters anywhere in `Catalog.Domain`.
- `Application/CommandTests.cs` (2 tests) — § 1.4: `*Command` naming + orphan detection.
- `Application/CommandHandlerTests.cs` (2 tests) — naming + sealed.
- `Application/QueryTests.cs` (1 test) — `*Query` naming.
- `Application/QueryHandlerTests.cs` (2 tests) — naming + sealed.
- `Application/ValidatorTests.cs` (1 test) — `*Validator` naming.
- `Application/DomainEventHandlerTests.cs` (2 tests) — every `IDomainEventHandler<T>` ends with `ProjectionHandler` OR `OutboxPublisher` (Catalog's two-track convention) + sealed.
- `Application/ResultPatternTests.cs` (3 tests) — § 1.5: aggregates only throw `DataIntegrityException`; handlers don't raw-throw `ArgumentException`/`InvalidOperationException`/`ArgumentNullException`; handlers return `Task<Result>` / `Task<Result<T>>`.
- `BoundedContext/ProductTests.cs` (1 test) — § 2.1: `Product` does not reference `Catalog.Domain.Categories.Category` on any field, property, parameter, or return type (custom rule walks the type's surface).
- `BoundedContext/ProjectionHandlerTests.cs` (3 tests) — every `IDomainEventHandler<T>` that isn't an outbox publisher ends in `ProjectionHandler`; sealed; lives under `Catalog.Application.(Products|Categories).<UseCase>`.
- `BoundedContext/CrossBcReferenceTests.cs` (2 tests) — § 1.6: `Catalog.Domain` and `Catalog.Application` don't reference Basket / Ordering / Inventory / Invoicing / Payments domain or application assemblies.

### File deleted

- `test/Catalog.ArchitectureTests/Placeholder.cs` — M1 scaffold removed.

### Custom Mono.Cecil rules (in `BaseTest.cs`)

1. `PrivateConstructorsRule` — verbatim from Weather (`test/Weather.ArchitectureTests/BaseTest.cs:22`).
2. `NoStaticUtcNowRule` — IL scan for `Call`/`Callvirt` to forbidden time getters; covers all five accessors named in ADR-0015 § Decision (`DateTime.UtcNow`, `DateTime.Now`, `DateTime.Today`, `DateTimeOffset.UtcNow`, `DateTimeOffset.Now`).
3. `OnlyThrowsRule(params Type[] permitted)` — IL scan for `newobj` whose declaring type derives from `System.Exception`; cheap structural fallback (`Name.EndsWith("Exception")`) before the inheritance walk so unresolvable third-party assemblies don't silently degrade.
4. `DoesNotThrowRule(params Type[] forbidden)` — inverse of the above.
5. `HasPublicStaticFactoryMethodRule` — looks for `public static` method named `Create*` or `From*` (architecture-tests.md § 1.2 line 62).
6. `HandlerReturnsResultRule` — verifies `HandleAsync` returns `Task<Result>` or `Task<Result<T>>` (architecture-tests.md § 1.4 third sub-rule).
7. `OnlyReferencesByIdRule(params Type[] forbidden)` — surface-area check for fields, properties, method parameters, return types — for the § 2.1 Product/Category rule.

All four IL-walking rules (#2–#4) recurse into compiler-generated nested types via the `AllMethodsIncludingNested` helper. Without recursion, `DoesNotThrowRule` would silently no-op against today's 100%-async handlers — the throw IL lives in the `<HandleAsync>d__N::MoveNext` state machine, not the user-declared method body. Surfaced by the Opus pre-commit review (H2).

## Design decisions taken (with rationale)

- **`BoundedContext/` folder name (not `Catalog/`)**: A subfolder named `Catalog/` inside namespace `Catalog.ArchitectureTests` shadowed the external `Catalog.Domain`/`Catalog.Application` namespace prefix when resolving `Catalog.Domain.IAssemblyMarker` from `BaseTest.cs`, producing CS0118 `"Assembly is a namespace"` errors. Fixed by renaming + fully-qualifying `System.Reflection.Assembly` and `global::Catalog.Domain.IAssemblyMarker` for defense-in-depth.
- **Two-suffix `IDomainEventHandler<T>` convention**: Weather requires `*DomainEventHandler`. Catalog uses `*ProjectionHandler` (8 read-side, one-per-event per `catalog.md <example_design_decision>`) + `*OutboxPublisher` (4 external Avro publishers per M3.7). The `DomainEventHandlerTests` rule reads "ends in `ProjectionHandler` OR `OutboxPublisher`" — locks Catalog's two-track convention.
- **`ProjectionHandlerTests` deviates from architecture-tests.md § 2.1's literal text** which references a singular `ProductSearchViewProjectionHandler`. Catalog implemented one-class-per-event per the sanctioned `catalog.md <example_design_decision>`. M5 encodes the rule to match reality and flags the doc as a follow-up (see Open Questions).
- **`OnlyReferencesByIdRule` (custom) instead of NetArchTest's `HaveDependencyOnAny`** for the Product→Category rule. The dependency-graph check looks at all IL refs and is approximate; the surface-area check is precise — exactly the shape architecture-tests.md § 2.1 ("Product references Category solely by ID") expects.
- **`AllMethodsIncludingNested` recursion in IL-walking rules** instead of just `type.Methods`. Without it, async methods (every Catalog handler today) and lambdas are invisible — the IL lives in compiler-generated state-machine classes that NetArchTest's type loader filters out.
- **`IsExceptionType` cheap-name fallback** before Mono.Cecil's `Resolve()` chain. Defense-in-depth against unresolvable third-party assemblies that would otherwise pass through `SafeResolve == null` silently. Surfaced by Opus pre-commit review (L2).
- **No production-code change required**. Exploration (and the test run) confirmed Catalog already complies with every rule encoded — no raw throws in handlers, no `DateTime.UtcNow` in domain, no `Category` type reference inside `Product`, all 8 projection handlers already named `*ProjectionHandler` and sealed. M5 turns the conventions into CI-enforced invariants.

## ADR compliance

- **ADR-0007** (Avro FORWARD_TRANSITIVE) — n/a; M5 doesn't touch schemas.
- **ADR-0008** (correlation-id) — n/a; arch tests don't enforce ID propagation directly.
- **ADR-0010** (service-to-service auth) — n/a in M5 (API endpoints land in M6).
- **ADR-0012** (versioning) — n/a in M5.
- **ADR-0013** (idempotency) — n/a in M5.
- **ADR-0014** (feature flags) — n/a; arch tests don't enforce flag wiring.
- **ADR-0015** (time policy) — **CLOSED for `Catalog.Domain`**: `AdrComplianceTests.Domain_ShouldNot_UseStaticUtcNow` enforces all five forbidden static getters via Mono.Cecil IL scan, walking compiler-generated nested types so async/lambda IL is covered. Test passes green; H1 from M3 is now architecturally locked.
- **ADR-0016** (Redis topology) — n/a in M5.

## Verification output (executed at HEAD before commit)

```
$ dotnet restore --locked-mode
  (restored cleanly; only pre-existing NU1903 transitive vuln warnings on
   System.Security.Cryptography.Xml + Microsoft.Extensions.Caching.Memory —
   inherited from the build graph, not introduced by M5)

$ dotnet build test/Catalog.ArchitectureTests/Catalog.ArchitectureTests.csproj -m --no-restore
  Catalog.ArchitectureTests -> .../Catalog.ArchitectureTests.dll
  0 errors, 2 NU1903 warnings (transitive vuln, not introduced by M5)

$ dotnet test test/Catalog.ArchitectureTests/ --no-build
  Successful! Failed: 0, Passed: 41, Skipped: 0, Total: 41, Duration: 1s

$ dotnet test test/Catalog.UnitTests/ --no-restore
  Successful! Failed: 0, Passed: 255, Skipped: 0, Total: 255, Duration: 3s
  (sanity check — production code unchanged; M4's 255 baseline unaffected)

$ dotnet format whitespace test/Catalog.ArchitectureTests/Catalog.ArchitectureTests.csproj --no-restore --verify-no-changes
  exit 0 (clean)

$ dotnet format style test/Catalog.ArchitectureTests/Catalog.ArchitectureTests.csproj --no-restore --verify-no-changes
  exit 0 (clean)
```

## M5-not-runnable verifications (require later milestones)

- `dotnet test test/Catalog.IntegrationTests/` — needs Docker daemon (M4 baseline; no M5 change).
- `dotnet test test/Catalog.FunctionalTests/` — M6.
- `docker compose --profile full up -d` + curl smoke — M7.

## Peer-review chain

- **Opus pre-commit review** (`feature-dev:code-reviewer`, `model="opus"`) on the full test set — touched ≥ 5 files, mandatory per `_shared.md § 11`. Surfaced:
  - **CRITICAL C1** — `ProjectionHandlerTests.ProjectionHandlers_Should_HaveNameEndingWith_ProjectionHandler` was tautological (selector and assertion identical predicate). **Fixed** by inverting the selector — now scans every `IDomainEventHandler<T>` that isn't an `*OutboxPublisher` and asserts `*ProjectionHandler` suffix. Test renamed to `DomainEventHandlers_ThatAreNotOutboxPublishers_Should_HaveNameEndingWith_ProjectionHandler` to reflect what it actually checks.
  - **HIGH H1** — `NoStaticUtcNowRule` only covered `DateTime.UtcNow` + `DateTimeOffset.UtcNow`; ADR-0015 § Decision explicitly names `DateTime.Now` and `DateTime.Today` too. **Fixed** by extending the forbidden-getter list to all five accessors.
  - **HIGH H2** — Custom IL-walking rules iterated only `type.Methods`, missing async state machines / lambda closures (compiler-generated nested types). Effectively made `DoesNotThrowRule` a no-op against today's 100%-async handlers. **Fixed** by adding `AllMethodsIncludingNested` recursion and routing all three IL-scanning rules through it.
  - **MEDIUM M1** — Missing rule: handlers return `Result`/`Result<T>` (architecture-tests.md § 1.4 third sub-rule). **Fixed** by adding `HandlerReturnsResultRule` + `Handlers_Should_Return_ResultOrResultOfT` test.
  - **MEDIUM M2** — Missing rule: aggregates have public static `Create*`/`From*` factory (architecture-tests.md § 1.2 line 62). **Fixed** by adding `HasPublicStaticFactoryMethodRule` + `AggregateRoots_Should_HavePublicStaticFactoryMethod` test.
  - **MEDIUM M3** — Product test redundancy with the generic cross-aggregate dependency check. **Fixed** by replacing the dependency-graph check with the `OnlyReferencesByIdRule` custom rule that walks fields, properties, parameters, and return types directly — strictly more precise than `HaveDependencyOnAny`.
  - **LOW L1–L4** — accepted as-is (cosmetic file location, theoretical fallback paths, BC-specific suffix convention, defense-in-depth `global::` qualifier). Documented in commit body.
- **`superpowers:verification-before-completion`** — checklist applied; all four gates green; integration tests gated on Docker (no M5 change).
- **`nw-software-crafter-reviewer`** — invocation deferred to a follow-up against this summary (this file is the input).

## Open questions / improvements proposed but NOT implemented

- **Doc self-correction to `docs/bc-design/architecture-tests.md § 2.1`**. The doc references a singular `ProductSearchViewProjectionHandler` and "no projection writes outside the handler", but Catalog implemented (sanctioned by `catalog.md <example_design_decision>`) one class per event with `*ProjectionHandler` suffix. M5 encoded the rule to match the chosen design. The doc lives outside Catalog's `<boundaries>` (shared cross-BC doc), so it should be updated in a separate doc-only commit. Proposed wording: replace the singular handler reference with "Projection writes happen exclusively in `*ProjectionHandler` classes living under `Catalog.Application.(Products|Categories).<UseCase>` (one class per internal domain event, per the design decision documented in `catalog.md <example_design_decision>`). The bulk-cascade `CategoryPathService.RecomputeDescendantPathsAsync` is the documented exception (M3.5 reparent ExecuteUpdate)."
- **"No projection writes outside `*ProjectionHandler` classes" rule** — not cleanly encodable because `Catalog.Application.Categories.ReparentCategory.CategoryPathService` is the documented bulk-cascade exception (M3.5) that legitimately writes to `ProductSearchView` via `ExecuteUpdateAsync`. Could be encoded with a per-class allowlist, but the brittleness outweighs the benefit. Reviewer task at PR time, not arch-test task.
- **Apply the same arch-test set to Basket/Ordering/Inventory/Invoicing/Payments**. Their `*.ArchitectureTests` projects are still scaffold-only (`Placeholder.cs`). Cross-BC harmonization (especially the `IDomainEventHandler<T>` suffix conventions, which differ per BC) needs a separate cross-BC commit when those teams' DDs land.
- **`ProjectionHandlerTests.ProjectionHandlers_Should_LiveUnder_AggregateUseCaseNamespace`** — currently asserts `^Catalog\.Application\.(Products|Categories)\.\w+$`. If Catalog grows a third aggregate (e.g., a Pricing seam per ADR-0002 v2), the regex needs updating. Documented inline so reviewers can spot the brittleness; alternative encoding (any `Catalog.Application.<two-segments>` pattern) was rejected as too lax.

## File-touch audit (nothing outside Catalog M5 boundary)

Per `<boundaries>` in `docs/implementation-prompts/catalog.md`:

- `test/Catalog.*.Tests/**` ✓ — all 16 new files + 1 deletion (Placeholder.cs) under `test/Catalog.ArchitectureTests/`
- `docs/implementation-prompts/session-summaries/catalog-m5.md` ✓ — this file
- `services/Catalog/**` — **untouched**; no production-code change required
- `services/Directory.Packages.props` — **untouched**; `NetArchTest.eNhancedEdition` 1.4.5, `Mono.Cecil`, `xunit.v3`, `AwesomeAssertions` already in `test/Directory.Packages.props`
- `docs/bc-design/architecture-tests.md` — **untouched** (out of catalog `<boundaries>`; doc divergence flagged in Open Questions for a future doc-only commit)

## Ready state

- 41/41 Catalog architecture tests green (was 0/0 — Placeholder.cs was the M1 scaffold).
- 255/255 Catalog unit tests still green (sanity confirmed; no production-code change in M5).
- Catalog.ArchitectureTests builds clean; format whitespace + format style green.
- Hand-off block for M6 follows in the session's closing message.
