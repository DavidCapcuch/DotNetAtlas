# Ordering Wave-1 Follow-ups — Multi-Session Plan

Verified open scope as of 2026-05-24. Closed issues (#240 counter, #236 DLT smoke, #242 audit-vs-business rename) excluded.

| Session | Issues | Scope | Cross-BC auth |
|---|---|---|---|
| [S1](#s1--keycloak-roles-claim-mapping-platform-fix) | #234 | Platform JWT roles claim — HIGH | n/a (platform file, all BCs benefit) |
| [S2](#s2--ordering-tidy-bundle) | #235, #237, #239, #241 | 4 LOW mechanical fixes inside Ordering BC | no |
| [S3](#s3--ordering-getordersbybuyer-sql-side-projection) | #238 | EF projection rewrite | no |
| [S4](#s4--f6-productsnapshotcapturedatutc-cross-bc-chain) | #243 | 10-step chain across Basket + saga + Ordering | **yes** |
| [S5](#s5--use-casesmd-3-stale-kafka-routing) | #244 | Cross-BC doc fix | **yes** |
| [S6](#s6--apply-catalog-arch-test-set-to-five-bcs) | #217 | Mirror Catalog arch tests into 5 sibling BCs | **yes** (touches Basket/Inventory/Invoicing/Payments test trees) |

Run order: **S1 first** (production-blocking auth bug). S2–S6 are independent of each other and can run in parallel sessions if you have the appetite.

Each prompt below is self-contained — copy from `<prompt>` to `</prompt>` and paste as the first message of a fresh Claude Code session in `C:\Users\david.capcuch\Desktop\Git\DotNetAtlas`.

---

## S1 — Keycloak roles claim mapping (platform fix)

**Why now:** issue #234 is HIGH-severity. Real Keycloak admin tokens are rejected by `Policies(AuthPolicies.OrderingAdmin)` because Keycloak emits realm roles in the flat `roles` claim and `TokenValidationParameters.RoleClaimType` is never set. Fails closed (no buyer privilege escalation) but breaks production admin ship/deliver/cancel across Ordering + Catalog + every sibling BC. Functional tests pass only because `FakeTokenCreator` emits `ClaimTypes.Role` directly.

<prompt>

# Fix Keycloak `roles` claim mapping in `Platform.ServiceDefaults` (#234)

## Context

Keycloak's realm-export (`src/keycloak/realm-export.json:243-257`) configures `oidc-usermodel-realm-role-mapper` with `claim.name=roles`, so real admin tokens carry roles in the flat `roles` array. The shared platform JWT configurator at `platform/Platform.ServiceDefaults/Auth/JwtBearerConfigurator.cs` does NOT set `TokenValidationParameters.RoleClaimType` and registers no `OnTokenValidated` transformer, so `User.IsInRole(Roles.Admin)` (which looks at `ClaimTypes.Role` / `"role"`) returns false for real Keycloak admin tokens. Authorization policies that check role membership (e.g. `AuthPolicies.OrderingAdmin`, `AuthPolicies.CatalogAdmin`) reject all admin operations in production. The bug is masked in functional tests because `FakeTokenCreator` issues `ClaimTypes.Role` directly.

The accepted carry-forward note is in `docs/implementation-prompts/session-summaries/catalog-closeout.md` (CAT-SEC-003). A precedent Weather implementation exists that sets up an `OnTokenValidated` transformer — find and review it before deciding between (a) `RoleClaimType = "roles"` vs (b) a transformer.

## Goal

Real Keycloak admin tokens must satisfy `User.IsInRole(Roles.Admin)` across every BC that consumes `AddPlatformJwtBearer`. Pin with a functional test that mints a token with the `roles` claim (no manual `ClaimTypes.Role`) and exercises an admin-only endpoint.

## Files

- **Edit:** [platform/Platform.ServiceDefaults/Auth/JwtBearerConfigurator.cs](platform/Platform.ServiceDefaults/Auth/JwtBearerConfigurator.cs) — the only production file in scope.
- **Add/extend test:** pick one BC's functional test fixture (Ordering or Catalog) and assert the `roles`-only path. Likely candidates:
  - `test/Ordering.FunctionalTests/Common/TestClientInfrastructure/FakeTokenCreator.cs` (modify to emit `roles`, drop `ClaimTypes.Role`).
  - `test/Ordering.FunctionalTests/Orders/CancelOrder/AdminCancelOrderTests.cs` (or similar) to verify the admin path resolves.
- **Inspect for reference:** any existing `JwtBearerEvents.OnTokenValidated` in the Weather BC (`services/Weather/**`) per the issue's note.
- **Do NOT touch:** Keycloak realm export (`src/keycloak/realm-export.json`) — the claim mapping there is correct; the consumer side is what's wrong.

## CLAUDE.md guardrails

This edits `Platform.SharedKernel`-tier code that downstream BCs compile against (the Configurator's `TokenValidationParameters` shape is observable from every BC's `*.Api`). Verification path per CLAUDE.md:

```bash
dotnet build -m
dotnet restore --locked-mode
```

A slice build (Platform-only) will NOT surface CS9035 violations in downstream BC trees — repeat solution-wide before committing.

## Acceptance

1. `platform/Platform.ServiceDefaults/Auth/JwtBearerConfigurator.cs` either sets `TokenValidationParameters.RoleClaimType = "roles"` OR wires an `OnTokenValidated` event that re-maps `roles` → `ClaimTypes.Role`. Pick one and justify in the commit message.
2. The `PostConfigure<JwtBearerOptions>` block at lines 69-76 still owns the final word on signed-token / issuer / audience / lifetime validation (#223 lockdown is preserved).
3. New functional test mints a token whose ONLY role claim is in `roles`, calls an admin-only endpoint, and gets 200/204 (not 403).
4. Adjust one BC's `FakeTokenCreator` to verify the production claim shape works through the test fixture too (no `ClaimTypes.Role` cheat).
5. `dotnet build -m` clean solution-wide.
6. `dotnet format whitespace --no-restore --verify-no-changes` and `dotnet format style --no-restore --verify-no-changes` pass.
7. Comment + close issue: `gh issue close 234 --comment "Fixed in <sha> — RoleClaimType wired in Platform.ServiceDefaults; pinned by functional test <test-name>."`

## Skills

- `superpowers:test-driven-development` — drive the fix from a failing functional test that mints a `roles`-only token.
- `superpowers:verification-before-completion` — before closing #234, paste actual build + test output, do not claim success without it.

</prompt>

---

## S2 — Ordering tidy bundle

**Why grouped:** all four are LOW-severity, all bounded to `services/Ordering/**`, all mechanical. Single PR, one commit per issue.

<prompt>

# Ordering wave-1 tidy bundle — #235, #237, #239, #241

## Context

Four low-severity follow-ups from Ordering wave-1 closeout. All inside the Ordering BC writable set. Single session, separate commits so each maps cleanly to one GH issue.

### Issue #235 — `GetOrdersByBuyer` status-parse throw style

**File:** [services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs:53-56](services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs:53)

Throw uses free-form string concat. `DataIntegrityException` is bug-class; `OrderingErrors` is user-visible. Aligning naming style only — no behavioural change.

**Approach:** hoist a private static helper (either local to the handler or in a sibling `DomainExceptions`/`OrderingErrors` partial) that returns a `DataIntegrityException` with a fixed error code `"OrdersByBuyer.InvalidStatus"`. Keep the failure shape identical so existing tests stay green.

### Issue #237 — `SagaCommandMappers.ResolveUniformCurrency` double-iteration

**File:** [services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandMappers.cs:36-81](services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandMappers.cs:36)

`avro.Items.Select(ToItemInput).ToArray()` plus separate `ResolveUniformCurrency` pass walks the items collection twice per dispatch. Bounded by basket size; correctness is fine.

**Approach:** fuse into a single loop that builds the mapped list AND validates per-item currency against `items[0].UnitPriceCurrency` as it goes. Preserve the `DataIntegrityException("Ordering.MultipleCurrencies", …)` throw shape (existing tests pin it). Watch for the `avro.Items is null || Count == 0` edge — `string.Empty` must still be returned to keep the empty-basket branch unchanged.

### Issue #239 — Mapperly `[Mapper]` / `[UserMapping]` attributes decorative

**Files:**
- [services/Ordering/Ordering.Application/Orders/CreateOrder/OrderCreatedMapper.cs:18](services/Ordering/Ordering.Application/Orders/CreateOrder/OrderCreatedMapper.cs:18)
- [services/Ordering/Ordering.Application/Orders/ConfirmOrder/OrderConfirmedMapper.cs:18](services/Ordering/Ordering.Application/Orders/ConfirmOrder/OrderConfirmedMapper.cs:18)
- [services/Ordering/Ordering.Application/Orders/CancelOrder/OrderCancelledMapper.cs:22](services/Ordering/Ordering.Application/Orders/CancelOrder/OrderCancelledMapper.cs:22)

All three are `public static partial class …Mapper` decorated with `[Mapper]` and `[UserMapping]`, but the methods have hand-written bodies (no `partial` declarations) — Mapperly generates nothing. A maintainer would reasonably assume source-gen is running.

**Approach: pick ONE convention BC-wide.** Two valid choices:

(a) **Drop the attributes** + the `partial` modifier + the `using Riok.Mapperly.Abstractions;` from all three files. Cheapest, no runtime risk.

(b) **Convert one to real source-gen** (`public static partial OrderCreatedEvent ToOrderCreatedEvent(this OrderCreatedDomainEvent source);`) — keep the others or convert them too. More work; requires verifying scale-4 `AvroDecimal` conversion still uses `ToAvroDecimal(Scale)` (Mapperly's default conversion does not know the scale).

Go with (a) unless there's a reason in the existing codebase to keep Mapperly here — the issue body and the file comments already prefer "simple mapping is clearer". Justify the choice in the commit message.

### Issue #241 — Skip/Take defensive guard inside handler

**Files:**
- [services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQuery.cs](services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQuery.cs)
- [services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs](services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs)
- [services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryValidator.cs:11-12](services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryValidator.cs:11) — validator already has `Skip >= 0`, `Take in [1, 100]`.

Defence-in-depth: if `ValidationBehavior` is bypassed (test path or a future pipeline reorder), `Take=0` silently returns an empty page and negative `Skip` surfaces as an EF exception.

**Approach:** at the top of `HandleAsync`, throw `DataIntegrityException("OrdersByBuyer.OutOfRange", …)` if `Skip < 0 || Take <= 0 || Take > MaxPageSize`. Use the same `100` upper bound the validator uses; define `MaxPageSize` as a private const in the handler. Add ONE unit test that constructs the handler directly (no pipeline) and asserts the bug-class throw. Pattern mirror is `Order.CreateFromBasket` per the issue body.

## Boundaries

**You may write:** `services/Ordering/**`, `test/Ordering.*/**`. **Do not touch:** other BCs, platform, Avro, saga, EF migrations (per CLAUDE.md).

## Verification

```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Ordering.UnitTests/
dotnet test test/Ordering.ArchitectureTests/
dotnet test test/Ordering.IntegrationTests/
dotnet test test/Ordering.FunctionalTests/
```

If `dotnet test` against `Ordering.IntegrationTests` fails inside the fixture with `DockerUnavailableException` on a corporate-proxy host, prefix with the workaround from CLAUDE.md (`unset HTTP_PROXY …`).

## Close issues

One commit per issue, then:

```bash
gh issue close 235 --comment "Fixed in <sha> — DataIntegrityException factory helper applied."
gh issue close 237 --comment "Fixed in <sha> — fused mapping + currency-uniformity into a single walk."
gh issue close 239 --comment "Fixed in <sha> — dropped decorative Mapperly attributes (chose convention <a or b>)."
gh issue close 241 --comment "Fixed in <sha> — handler-level Skip/Take guard added with unit pin."
```

## Skills

- `superpowers:test-driven-development` for #241 (new guard needs a test-first failing case).
- `superpowers:verification-before-completion` before closing any issue.

</prompt>

---

## S3 — Ordering `GetOrdersByBuyer` SQL-side projection

**Why standalone:** #238 has a real EF translation risk (partial-null projection on `Cancellation`/`Failure`/`Shipment` optional VOs historically trips EF) that deserves its own focused PR + integration test.

<prompt>

# Move `GetOrdersByBuyer` to SQL-side projection (#238)

## Context

[services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs:28-38](services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs:28) materialises full `Order` aggregates (six owned tables × N orders) then runs `OrderProjection.ToResponse` client-side. `UseQuerySplitting=true` is configured on the `IOrderingDbContext` so the join graph does NOT cartesian-explode, but every column still travels to the client.

`GetOrderById` (single order, [services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryHandler.cs](services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryHandler.cs)) is acceptable as-is — the saving is on the paginated list path only.

The known EF gotcha called out in the issue: `Cancellation`, `Failure`, `Shipment` are optional owned VOs on `Order` — partial-null projection in `Select` has historically broken EF translation. Either project conditionally per-VO inside the `Select`, or accept that those three fields stay materialised and only the scalar columns + items collection move to SQL projection.

## Files

- **Edit:** [services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs](services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs)
- **Optional new:** a paginated-list-specific projection method (do NOT touch `OrderProjection.ToResponse` — it's shared with `GetOrderById` and must produce a byte-identical shape for both paths).
- **Tests:** [test/Ordering.IntegrationTests/Orders/GetOrdersByBuyer/**](test/Ordering.IntegrationTests/Orders/GetOrdersByBuyer/) — add a fact that creates orders in each of the three optional-VO states (cancelled / failed / shipped / none) and asserts the projection returns identical content vs the existing client-side path.

## Boundaries

**You may write:** `services/Ordering/Ordering.Application/**`, `test/Ordering.IntegrationTests/**`. **Do not touch:** the `Order` aggregate or its EF configuration (changing owned VO mappings would expand scope into EF migration territory — out of scope here).

## Acceptance

1. The paginated list path issues a SQL projection that selects only the columns the `GetOrderByIdResponse` (and `OrderItemDto`, `AddressDto`, `CancellationDto`, `FailureDto`, `ShipmentDto`) need.
2. Either the optional-VO triplet stays materialised AND that decision is documented in a one-line comment on the handler, OR they're conditionally projected AND a regression test pins the three null shapes.
3. Existing functional tests for `GetOrdersByBuyer` stay green without modification — the response body is byte-identical.
4. New integration test asserts: an order in each of `{none, cancelled, failed, shipped}` returns matching JSON between the old and new code paths. (Capture old output once before the rewrite, then assert equality after.)
5. `dotnet build -m`, format checks, and all four `Ordering.*Tests` slices pass.

## Close

```bash
gh issue close 238 --comment "Fixed in <sha> — SQL-side projection on the list path; optional-VO triplet handled as <chose: materialised | conditional projection>; pinned by integration test <test-name>."
```

## Skills

- `superpowers:test-driven-development` — write the byte-identity test first, then move the projection.
- `superpowers:verification-before-completion` before closing.

</prompt>

---

## S4 — F6 `ProductSnapshot.CapturedAtUtc` cross-BC chain

**Why standalone + needs auth:** issue #243 spells out a 10-step chain that touches `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/**`, the saga's `CreateOrderConsumer`, and Basket's mapper — all forbidden by Ordering's `<boundaries>`. This is a mini-milestone, not an Ordering follow-up.

<prompt>

# F6 ProductSnapshot.CapturedAtUtc cross-BC chain (#243)

## Authorization required

This session crosses bounded-context writes. You will modify:

- `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc`
- Basket Application code (mapper)
- The checkout-saga `CreateOrderConsumer`
- Ordering Domain + Infrastructure + EF mapping
- All four BCs' test trees

The user must confirm authorization for this cross-BC pass in their reply BEFORE you touch any non-Ordering file. Per the precedent in commits `e206653` / `01540c3`, the cross-BC mini-milestone lands as a single PR.

## Context

The two architecture-test facts `OrderingProductSnapshot_HasCapturedAtUtc` and `OrderingProductSnapshot_IsStructuralSupersetOfBasketProductSnapshot` at [test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs:59,80](test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs:59) are `[Fact(Skip = PendingChainSkip)]`. The class-level remarks (lines 17-41) spell out the 10 ordered steps required to unskip them. Tracking: `docs/bc-design/ordering.md`, `docs/implementation-prompts/ordering.md:124-134`, ADR-0002 (frozen snapshots).

Audit fidelity: today the "when did the user see this price?" answer is dropped at the Basket → Ordering ACL boundary. Needed for chargebacks + price-change disputes.

## Steps (preserve this order)

1. Add nullable `CapturedAtUtc` field with default to `BasketCheckoutItem` record in `platform/Platform.SchemaRegistry.Contracts/Avro/Basket/Sessions/BasketCheckoutInitiatedEvent.avsc`. Schema compat per ADR-0007 = `FORWARD_TRANSITIVE` — the field MUST be nullable with default.
2. Re-run avrogen.
3. Propagate the field through Basket's `BasketCheckoutInitiatedMapper` (Application layer) — populate from a `TimeProvider` (no `DateTime.UtcNow`).
4. Add `CapturedAtUtc` to `CreateOrderCommand.OrderItemInput` and propagate via the saga's `CreateOrderConsumer`.
5. Add `CapturedAtUtc` to `Ordering.Domain.Baskets.BasketSnapshotItem`.
6. Add `CapturedAtUtc` to `Ordering.Domain.Orders.ValueObjects.ProductSnapshot` with `required init` + validation in `Create` (must be UTC, must be `> DateTimeOffset.MinValue`).
7. Thread the value through `Order.CreateFromBasket`.
8. Add EF column mapping in `services/Ordering/Ordering.Infrastructure/Persistence/Database/EntityConfigurations/Orders/OrderConfiguration.cs`. **User generates the migration** — per CLAUDE.md, never hand-write the `.cs` migration. After `dotnet ef migrations add`, inspect the `Up()` / `Down()` and accept defaults only if EF chose `AddColumn` (no `DropColumn` here).
9. Update unit / integration tests to construct snapshots with timestamps.
10. Remove the `Skip` argument on both `[Fact]`s at `test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs:59,80`. The structural-superset fact reflectively loads `Basket.Domain` — the test project must `ProjectReference` Basket.Domain (the assembly co-locates via bin output, see the class-level comment).

## Verification

Per CLAUDE.md — Platform.SharedKernel-tier change with Avro schema motion. MUST run solution-wide:

```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Ordering.UnitTests/
dotnet test test/Ordering.IntegrationTests/
dotnet test test/Ordering.FunctionalTests/
dotnet test test/Ordering.ArchitectureTests/  # the two facts must now run (and pass)
dotnet test test/Basket.UnitTests/
dotnet test test/Basket.IntegrationTests/
dotnet test  # full solution
```

## Close

```bash
gh issue close 243 --comment "Fixed in <sha> — F6 chain complete; both ProductSnapshotContractTests facts unskipped and passing. Migration: <name>."
```

## Skills

- `superpowers:test-driven-development` — drive from the two skipped facts; unskip last per step 10.
- `superpowers:verification-before-completion` — DO NOT close #243 without pasting actual output of all eight verification commands.

</prompt>

---

## S5 — `use-cases.md` §3 stale Kafka routing

**Why standalone + needs auth:** issue #244 edits a cross-BC doc (read by Basket / Inventory / Payments / Invoicing / BFF). Per the issue's last paragraph, requires explicit user authorisation. **Scope already trimmed by my 2026-05-24 comment on #244** — `FailOrderCommand` claim is dead; remaining work is the two stale Kafka routings.

<prompt>

# Fix `use-cases.md` §3 stale Kafka routings (#244)

## Authorization required

This edits `docs/bc-design/use-cases.md`, which is read by every BC + BFF. User must confirm authorisation for this cross-BC doc change in their reply.

## Context

`docs/bc-design/use-cases.md` § 3 presents `MarkOrderStockReservedCommand` and `MarkOrderPaymentCompletedCommand` as Kafka-arriving commands on `ordering.order-commands`. They are NOT — in v1 they are application-layer only, dispatched in-process by the saga. The Kafka wire is verified by the actual `services/Ordering/Ordering.Infrastructure/Common/MessagingDependencyInjection.cs:89-99` consumer registration which lists only `AvroCreateOrderCommand`, `AvroConfirmOrderCommand`, `AvroCancelOrderCommand`, `AvroMarkOrderFailedCommand`. The same precedent fix (with the same renames + trigger-column clarifications) was already applied to `docs/bc-design/ordering.md` and `docs/bc-design/example-mapping/ordering.md` during Ordering M9.

## Lines to fix

| Line | Today | Fix |
|---|---|---|
| 814 | `#### 3.1.2 MarkOrderStockReservedCommand` with `HTTP: None — from saga via ordering.order-commands` | Either reclassify section as application-layer-only command (remove "via `ordering.order-commands`"), OR delete the §3.1.2 stanza entirely and roll it into a single saga-internal-commands subsection — match whatever convention §3.1 already uses for in-process commands. |
| 837 | Same shape on `#### 3.1.3 MarkOrderPaymentCompletedCommand` | Same fix as 814. |
| 999-1000 | Routing table: `Ordering.Commands.MarkOrderStockReservedCommand` / `…PaymentCompletedCommand` listed with `MarkOrder…KafkaHandler` | Remove both rows from the table. The four remaining rows (`CreateOrderCommand`, `ConfirmOrderCommand`, `MarkOrderFailedCommand`) match the actual Kafka wiring. Verify by grep `KafkaHandler` in `services/Ordering/**` — only four should exist. |
| 1488 | Step 3a in the saga sequence table: `ordering.order-commands` for `MarkOrderStockReservedCommand` | Change "Topic" column from `ordering.order-commands` to `(in-process)` or equivalent. |
| 1491 | Step 5a: same shape for `MarkOrderPaymentCompletedCommand` | Same fix as 1488. |

The `FailOrderCommand` claim in the original issue body is already resolved (no matches in current `use-cases.md`); ignore that part.

## Boundaries

**You may write:** `docs/bc-design/use-cases.md` only. **Do not touch:** any production code, any other doc, any test. If you find yourself wanting to "fix the code too," stop and surface it as a separate finding — this session is doc-cleanup only.

## Verification

No build/test impact — pure doc. After the edits:

```bash
# Sanity: zero references to MarkOrderStockReservedCommand / MarkOrderPaymentCompletedCommand
# claiming Kafka routing should remain.
grep -n "MarkOrderStockReservedCommand" docs/bc-design/use-cases.md
grep -n "MarkOrderPaymentCompletedCommand" docs/bc-design/use-cases.md
grep -n "ordering.order-commands" docs/bc-design/use-cases.md  # remaining hits must be CreateOrder/Confirm/Cancel/MarkOrderFailed only
```

Cross-check by comparing the final routing table against the actual `AddInbox(...)` registration at `services/Ordering/Ordering.Infrastructure/Common/MessagingDependencyInjection.cs:89-99`. Paste both into the close comment.

## Close

```bash
gh issue close 244 --comment "Fixed in <sha> — use-cases.md §3 reclassified MarkOrderStockReservedCommand / MarkOrderPaymentCompletedCommand as application-layer only; routing tables and saga sequence updated to match MessagingDependencyInjection wiring."
```

</prompt>

---

## S6 — Apply Catalog arch-test set to five BCs

**Why standalone + needs auth:** issue #217 touches `test/Basket.ArchitectureTests/**`, `test/Inventory.ArchitectureTests/**`, `test/Invoicing.ArchitectureTests/**`, `test/Payments.ArchitectureTests/**`, AND `test/Ordering.ArchitectureTests/**`. Five BCs' test trees in one session — needs explicit user authorisation.

<prompt>

# Mirror Catalog arch-test set into 5 sibling BCs (#217)

## Authorization required

This edits FIVE bounded contexts' test directories. User must confirm authorisation in their reply.

## Context

`test/Catalog.ArchitectureTests/` holds 17 architecture-test files organised into `Domain/`, `Application/`, `BoundedContext/`, `CleanArchitecture/`. The siblings (`test/Basket.ArchitectureTests/`, `test/Inventory.ArchitectureTests/`, `test/Invoicing.ArchitectureTests/`, `test/Payments.ArchitectureTests/`, and partially `test/Ordering.ArchitectureTests/`) ship only a `BaseTest.cs` scaffold. Ordering already has the F6 cross-BC contract test (`ProductSnapshotContractTests.cs`) but lacks the rule set.

Reference inventory of what to copy (from `test/Catalog.ArchitectureTests/`):

```
Domain/ValueObjectTests.cs
Domain/EntityTests.cs
Domain/AdrComplianceTests.cs
Domain/DomainEventTests.cs
Domain/AggregateRootTests.cs
Application/CommandHandlerTests.cs
Application/QueryTests.cs
Application/QueryHandlerTests.cs
Application/ValidatorTests.cs
Application/DomainEventHandlerTests.cs
Application/ResultPatternTests.cs
Application/CommandTests.cs
BoundedContext/CrossBcReferenceTests.cs
BoundedContext/ProjectionHandlerTests.cs
BoundedContext/ProductTests.cs                 -- Catalog-specific; rename or skip per BC
CleanArchitecture/CleanArchitectureLayerTests.cs
```

Each file's rules per the issue body: `NoStaticUtcNow`, `OnlyThrows`, `DoesNotThrow`, `HasPublicStaticFactoryMethod`, `HandlerReturnsResult`, `OnlyReferencesById`.

## Approach

For each of Basket, Inventory, Invoicing, Payments, Ordering:

1. Copy the file layout above into the BC's `*.ArchitectureTests/` directory.
2. Rebind namespaces (`Catalog.ArchitectureTests` → `<BC>.ArchitectureTests`).
3. Update the `BaseTest.cs` assembly references to point at the BC's `Domain` / `Application` / `Infrastructure` / `Api` assemblies (mirror the existing per-BC `BaseTest.cs` skeletons — they already declare the right `DomainAssembly` / `ApplicationAssembly` etc.).
4. Rename `BoundedContext/ProductTests.cs` to the BC's aggregate (e.g. `BoundedContext/BasketTests.cs`, `BoundedContext/OrderTests.cs`) and rebind the type checks. If the BC has no exact 1:1 aggregate (e.g. Payments has both `Payment` and `PaymentTransaction`), keep one file per aggregate.
5. Resolve violations: any rule that flags real code in a sibling BC is either a real defect (fix in a follow-up issue, NOT here) or a justified exception (add `.Where(...)` to the `Types.InAssembly(…)` filter with a one-line comment).

Resolve violations by mark-and-defer: if a test fails because of a real but pre-existing pattern in the BC's code (not a regression introduced by this PR), add the exception filter, file a `needs-triage` issue with title `<bc>(arch-test-debt): <rule> exception for <type>` and reference it in the test's filter comment. Do NOT silently delete rules. Do NOT fix the underlying code in this session.

## Boundaries

**You may write:** `test/{Basket,Inventory,Invoicing,Payments,Ordering}.ArchitectureTests/**`. **Do not touch:** any production code in `services/**` or `platform/**`. The only Ordering arch test that pre-exists (`ProductSnapshotContractTests.cs`) must remain unchanged.

## Verification

```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Basket.ArchitectureTests/
dotnet test test/Inventory.ArchitectureTests/
dotnet test test/Invoicing.ArchitectureTests/
dotnet test test/Payments.ArchitectureTests/
dotnet test test/Ordering.ArchitectureTests/
dotnet test  # full solution — assert no other test slice broke
```

Each per-BC arch-test invocation must report a non-zero passing test count (each BC must run at least the equivalent of Catalog's set, minus any rules that legitimately don't apply to that BC's domain — e.g. an event-only BC may not need `AggregateRootTests` if it has no aggregates).

## Close

```bash
gh issue close 217 --comment "Fixed in <sha> — Catalog arch-test rule set replicated into Basket/Inventory/Invoicing/Payments/Ordering with per-BC aggregate renames. Pre-existing violations filed as <list of needs-triage issues>."
```

## Skills

- `superpowers:verification-before-completion` — each per-BC test run must show actual output before closing.

</prompt>
