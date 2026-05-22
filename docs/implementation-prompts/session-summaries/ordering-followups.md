# Ordering — Wave-1 Closeout Follow-ups

> Triage + fix pass over the two independent closeout reviews
> ([session-summaries/ordering-closeout.md](ordering-closeout.md) and
> [session-summaries2/ordering-closeout.md](../session-summaries2/ordering-closeout.md)).
> Reviewer findings consolidated, deduplicated, fixed inside the Ordering
> bounded-context boundary; out-of-boundary HIGH and remaining LOW items
> filed as `needs-triage` GH issues. Working tree (uncommitted): user to
> stage and commit.

## Mission

Two closeout reviewers ran in parallel against `HEAD f49d358` and produced
overlapping-but-not-identical findings. This pass:

1. Reads both closeout docs end-to-end (M8/M9 summaries in both folders are
   byte-identical; only the closeouts diverge).
2. Triages the union of findings by severity and boundary.
3. Fixes everything inside the Ordering writable set (`services/Ordering/**`,
   `test/Ordering.*/**`, `platform/...//Avro/Ordering/**`) — HIGH-leverage
   defensive rewrites and MEDIUM behavioural cleanups via TDD where the
   change is observable.
4. Files cross-cutting HIGH and remaining LOW items as `needs-triage` GH
   issues with the dispatch-mandated title prefix `ordering(wave1-followup):`.
5. Re-verifies the 4 CI gates + 4 test slices.

## Files modified

```
code:                  9 services/Ordering/**
tests (modified):      3 test/Ordering.*/**
tests (new):           9 test/Ordering.*/**
Avro schemas:          0 (no wire changes)
docker-compose delta:  0
config:                0
doc updates:           1
  - docs/implementation-prompts/session-summaries/ordering-followups.md (NEW; this file)
```

### Source changes (`services/Ordering/**`)

| File | Findings addressed |
|---|---|
| [Ordering.Domain/Orders/Order.cs](../../../services/Ordering/Ordering.Domain/Orders/Order.cs) | M-2 sum1 (drop `Currency` field site), M-4 sum2 (populate `ConfirmedAtUtc`), H-2 sum2 (`new Money(...)` → `Money.Create(...)` + defensive `Throw.If`) |
| [Ordering.Domain/Orders/ValueObjects/OrderItem.cs](../../../services/Ordering/Ordering.Domain/Orders/ValueObjects/OrderItem.cs) | H-2 sum2 (defensive `Money.Create` + `Throw.If`) |
| [Ordering.Domain/Orders/Events/OrderCreatedDomainEvent.cs](../../../services/Ordering/Ordering.Domain/Orders/Events/OrderCreatedDomainEvent.cs) | M-2 sum1 (drop `Currency` from `OrderCreatedDomainEventItem`) |
| [Ordering.Domain/Orders/Events/OrderConfirmedDomainEvent.cs](../../../services/Ordering/Ordering.Domain/Orders/Events/OrderConfirmedDomainEvent.cs) | M-4 sum2 (add `required ConfirmedAtUtc`), M-2 sum2 (doc-fix `BACKWARD` → `FORWARD_TRANSITIVE`) |
| [Ordering.Application/Orders/CreateOrder/CreateOrderCommandHandler.cs](../../../services/Ordering/Ordering.Application/Orders/CreateOrder/CreateOrderCommandHandler.cs) | H-3 sum2 (rename `*Result` locals), M-6 sum2 (wire `command.RequestedAtUtc` as `utcNow`; drop `TimeProvider` injection) |
| [Ordering.Application/Orders/ConfirmOrder/OrderConfirmedMapper.cs](../../../services/Ordering/Ordering.Application/Orders/ConfirmOrder/OrderConfirmedMapper.cs) | M-4 sum2 (read explicit `source.ConfirmedAtUtc`) |
| [Ordering.Application/Orders/MarkOrderFailed/OrderFailedMapper.cs](../../../services/Ordering/Ordering.Application/Orders/MarkOrderFailed/OrderFailedMapper.cs) | M-1 sum2 (drop dead `"Confirmed"` arm; correct throw message) |
| [Ordering.Application/Orders/CancelOrder/CancelOrderCommandHandler.cs](../../../services/Ordering/Ordering.Application/Orders/CancelOrder/CancelOrderCommandHandler.cs) | L-4 sum2 (cross-buyer log `LogInformation` → `LogWarning`) |
| [Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryHandler.cs](../../../services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryHandler.cs) | L-4 sum2 (cross-buyer log `LogInformation` → `LogWarning`) |
| [Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs) | M-1 sum1 (drop redundant `_transactionalOutbox.SaveChangesAsync`), M-3 sum2 (drop duplicate `CorrelationId` key from `BeginScope` — middleware owns the LogContext push) |

H-1 sum1 / M-7 sum2 (symmetric `IsAdmin` guard on `GetOrderByIdQueryValidator`)
was already in the working tree from the closeout author and is preserved verbatim.

### Tests (`test/Ordering.*/**`)

New:

- `test/Ordering.UnitTests/Application/Orders/MarkOrderFailed/OrderFailedMapperTests.cs` — 9 facts, pins `MapStatus` against the FSM.
- `test/Ordering.UnitTests/Application/Orders/CancelOrder/CancelOrderCommandValidatorTests.cs` — 6 facts.
- `test/Ordering.UnitTests/Application/Orders/ConfirmOrder/ConfirmOrderCommandValidatorTests.cs` — 2 facts.
- `test/Ordering.UnitTests/Application/Orders/MarkOrderShipped/MarkOrderShippedCommandValidatorTests.cs` — 5 facts.
- `test/Ordering.UnitTests/Application/Orders/MarkOrderDelivered/MarkOrderDeliveredCommandValidatorTests.cs` — 2 facts.
- `test/Ordering.UnitTests/Application/Orders/MarkOrderFailed/MarkOrderFailedCommandValidatorTests.cs` — 5 facts.
- `test/Ordering.UnitTests/Application/Orders/MarkOrderStockReserved/MarkOrderStockReservedCommandValidatorTests.cs` — 3 facts.
- `test/Ordering.UnitTests/Application/Orders/MarkOrderPaymentCompleted/MarkOrderPaymentCompletedCommandValidatorTests.cs` — 3 facts.
- `test/Ordering.ArchitectureTests/Rules/DoesNotLogPiiAddressRule.cs` + `test/Ordering.ArchitectureTests/Domain/NoAddressInLogArgumentsTests.cs` — IL-scan rule + 3 facts (Application / Infrastructure / API assemblies).

Modified:

- `test/Ordering.UnitTests/Application/Orders/CreateOrder/CreateOrderCommandHandlerTests.cs` — +1 fact pinning M-6 (`command.RequestedAtUtc` becomes `Order.CreatedAtUtc`); drop the `TimeProvider` ctor arg.
- `test/Ordering.UnitTests/Application/Orders/CreateOrder/OrderCreatedOutboxPublisherDomainEventHandlerTests.cs` — drop the `Currency` argument from `OrderCreatedDomainEventItem` literal (M-2 sum1 cascade).
- `test/Ordering.UnitTests/Application/Orders/ConfirmOrder/OrderConfirmedOutboxPublisherDomainEventHandlerTests.cs` — populate `ConfirmedAtUtc` on the test domain event (M-4 sum2 cascade).
- `test/Ordering.IntegrationTests/Persistence/OrderPersistenceTests.cs` — drop the `TimeProvider` ctor arg from `new CreateOrderCommandHandler(...)` (M-6 cascade).

Net delta: **+36 unit facts, +3 architecture facts, 0 integration / functional delta**.

## Triage table (consolidated, deduplicated)

Severity from each reviewer's calibration. Disposition columns: `fixed` (this
session, in-boundary), `landed-prior` (already in the working tree from the
closeout author), `filed` (gh issue under `needs-triage`), or `carry-forward`
(documented across prior milestones, no new issue needed).

| ID | Severity | Source | Disposition | Notes |
|---|---|---|---|---|
| H-1 sum1 / M-7 sum2 | HIGH | [GetOrderByIdQueryValidator.cs:10](../../../services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryValidator.cs) | landed-prior | `BuyerId.NotEmpty().When(q => !q.IsAdmin)` symmetric with `CancelOrderCommandValidator`; 4 unit facts in [GetOrderByIdQueryValidatorTests.cs](../../../test/Ordering.UnitTests/Application/Orders/GetOrderById/GetOrderByIdQueryValidatorTests.cs). |
| H-1 sum2 | HIGH | [platform/Platform.ServiceDefaults/Auth/JwtBearerConfigurator.cs:46-55](../../../platform/Platform.ServiceDefaults/Auth/JwtBearerConfigurator.cs) | filed (#234) | Keycloak `roles` claim mapping; platform-level, cross-BC parity with `CAT-SEC-003`. Fails closed (admin denied, not buyer-escalated). |
| H-2 sum2 | HIGH | [OrderItem.cs:60](../../../services/Ordering/Ordering.Domain/Orders/ValueObjects/OrderItem.cs) + [Order.cs:159](../../../services/Ordering/Ordering.Domain/Orders/Order.cs) | fixed | `new Money(...)` → `Money.Create(...)` + `Throw.If(result.IsFailed, new DataIntegrityException(...))`. Defensive symmetry against future `Money` invariants. |
| H-3 sum2 | HIGH (cosmetic) | [CreateOrderCommandHandler.cs:59-61,79-80](../../../services/Ordering/Ordering.Application/Orders/CreateOrder/CreateOrderCommandHandler.cs) | fixed | Renamed `currencyResult` / `shippingAddressResult` / `billingAddressResult` → `currency` / `shippingAddress` / `billingAddress`. They aren't `Result<T>`. |
| M-1 sum1 / M-5 sum2 | MEDIUM | [SagaCommandHandlerBase.cs:88](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs) | fixed | Dropped the redundant `_transactionalOutbox.SaveChangesAsync(cancellationToken)`. Inner app handlers own SaveChanges; `EnsureTransactionAsync` wraps. Re-verified by integration slice 18/18. |
| M-2 sum1 | MEDIUM | [OrderCreatedDomainEvent.cs:37](../../../services/Ordering/Ordering.Domain/Orders/Events/OrderCreatedDomainEvent.cs) + [Order.cs:188](../../../services/Ordering/Ordering.Domain/Orders/Order.cs) | fixed | Dropped dead per-item `Currency` field — top-level `Total.Currency` is the I-9 single source of truth; mapper already ignored it. |
| M-1 sum2 | MEDIUM | [OrderFailedMapper.cs:34](../../../services/Ordering/Ordering.Application/Orders/MarkOrderFailed/OrderFailedMapper.cs) | fixed | TDD: red fact in `OrderFailedMapperTests.MapStatus_Confirmed_ThrowsBecauseConfirmedToFailedIsFsmForbidden` → drop the `"Confirmed"` arm → green. FSM forbids `Confirmed → Failed`, so the arm was unreachable. |
| M-2 sum2 | MEDIUM | [OrderConfirmedDomainEvent.cs:42](../../../services/Ordering/Ordering.Domain/Orders/Events/OrderConfirmedDomainEvent.cs) | fixed | XML-doc said "BACKWARD compatibility per ADR-0020"; actual mode is `FORWARD_TRANSITIVE` per ADR-0007 + ADR-0020. Now reads correctly. |
| M-3 sum2 | MEDIUM | [SagaCommandHandlerBase.cs:63-67](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandHandlerBase.cs) + [ConsumerCorrelationIdMiddleware.cs:40](../../../platform/Platform.KafkaFlow.ProducerHeaders/ConsumerCorrelationIdMiddleware.cs) | fixed | Dropped the duplicate `["CorrelationId"] = correlationId` push from the handler base; middleware owns the LogContext push from the Kafka header. `OrderId` scope retained. |
| M-4 sum2 | MEDIUM | publisher mappers (`OrderCreatedMapper:32` / `OrderConfirmedMapper:29` / `OrderCancelledMapper`) | fixed | Added `required DateTimeOffset ConfirmedAtUtc` to `OrderConfirmedDomainEvent`; populated from `Order.Confirm`; `OrderConfirmedMapper` reads it explicitly. Mapper timestamp sourcing now consistent across the three publishers. |
| M-6 sum2 | MEDIUM | [CreateOrderCommand.cs:42](../../../services/Ordering/Ordering.Application/Orders/CreateOrder/CreateOrderCommand.cs) + [CreateOrderCommandHandler.cs:73](../../../services/Ordering/Ordering.Application/Orders/CreateOrder/CreateOrderCommandHandler.cs) | fixed | TDD: red fact `Handle_UsesCommandRequestedAtUtc_AsOrderCreatedAtUtc_NotHandlerWallClock` → switch handler to pass `command.RequestedAtUtc` to `Order.CreateFromBasket` → green. Drops `TimeProvider` from the handler ctor. ADR-0015 trace fidelity. |
| M-8 sum2 | MEDIUM | (regression-guard, no production-code change today) | fixed | New `DoesNotLogPiiAddressRule` (Mono.Cecil IL-scan) + `NoAddressInLogArgumentsTests` (3 facts across Application / Infrastructure / API assemblies). Production grep is clean; this is the regression guard. |
| M-9 sum2 | MEDIUM | `test/Ordering.UnitTests/Application/Orders/` (no test files for 7 validators) | fixed | Added 5 new validator-test files: Cancel / Confirm / MarkOrderShipped / MarkOrderDelivered / MarkOrderFailed / MarkOrderStockReserved / MarkOrderPaymentCompleted. 26 new facts in total (note: `MarkOrderStockReserved` + `MarkOrderPaymentCompleted` were missing from the M-9 reviewer count; both have tests now). |
| L-1 sum1 | LOW | [GetOrdersByBuyerQueryHandler.cs:53-56](../../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs) | filed (#235) | Throw style consistency. |
| L-2 sum1 | LOW | (DLT routing runtime smoke) | filed (#236) | Verify `SagaCommandDispatchException` terminates correctly under `RetryForever` policy. |
| L-1 sum2 | LOW | [SagaCommandMappers.cs:38-40,66](../../../services/Ordering/Ordering.Infrastructure/Messaging/Kafka/SagaCommands/SagaCommandMappers.cs) | filed (#237) | Items collection double-iteration. |
| L-2 sum2 | LOW | [GetOrdersByBuyerQueryHandler.cs:35](../../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQueryHandler.cs) | filed (#238) | Client-side projection after full owned-collection materialise. |
| L-3 sum2 | LOW | publisher mappers under `Ordering.Application.Orders.*` | filed (#239) | Decorative `[Mapper]` / `[UserMapping]` attributes on hand-written mappers. |
| L-4 sum2 | LOW | [CancelOrderCommandHandler.cs:42-46](../../../services/Ordering/Ordering.Application/Orders/CancelOrder/CancelOrderCommandHandler.cs) + [GetOrderByIdQueryHandler.cs:43-47](../../../services/Ordering/Ordering.Application/Orders/GetOrderById/GetOrderByIdQueryHandler.cs) | fixed (log level) + filed (#240) | Log level bumped `Information` → `Warning` in-session; `ordering.authz.cross_buyer_attempt` counter filed for a future meter pass. |
| L-5 sum2 | LOW | [GetOrdersByBuyerQuery.cs:15-17](../../../services/Ordering/Ordering.Application/Orders/GetOrdersByBuyer/GetOrdersByBuyerQuery.cs) | filed (#241) | Defensive `Skip` / `Take` guard inside the handler. |
| L-6 sum2 | LOW | [Order.cs:71-72](../../../services/Ordering/Ordering.Domain/Orders/Order.cs) | filed (#242) | `CreatedUtc` (audit) vs `CreatedAtUtc` (business) projection-typo risk. Cross-BC rename candidate. |
| F6 | MEDIUM | [ProductSnapshotContractTests.cs:19-41](../../../test/Ordering.ArchitectureTests/ProductSnapshotContractTests.cs) | filed (#243) | Cross-BC `ProductSnapshot.CapturedAtUtc` 10-step chain. 2 `[Fact(Skip)]` remain; out of Ordering boundary. |
| `use-cases.md § 3` drift | LOW | docs/bc-design/use-cases.md (5 occurrences) | filed (#244) | Cross-BC doc drift; shared with Basket / Inventory / Payments / Invoicing / BFF. |

## Verification — actual output

The 4 CI gates per [`_shared.md § 12`](../_shared.md):

```text
$ dotnet restore --locked-mode
... (transitive NU1903 listing — pre-existing branch baseline, NOT Ordering-introduced)
exit 0.

$ dotnet build -m
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:04:15.36
exit 0.

(Pre-existing build-cache corruption in services/Invoicing/Invoicing.Infrastructure
and test/Invoicing.IntegrationTests required `Remove-Item -Recurse -Force obj/ bin/`
on those two projects to clear; the analyzer was reading a stale source snapshot.
Not Ordering-introduced; documented here for traceability.)

$ dotnet format whitespace --no-restore --verify-no-changes
exit 0 — 0 violations.

$ dotnet format style --no-restore --verify-no-changes
exit 0 — 0 violations.
(One IDE0066 surfaced on the new DoesNotLogPiiAddressRule.cs switch statement;
converted to a switch expression and re-verified clean.)
```

The four Ordering test slices (dispatch step 6 requires Unit + Integration;
the closeout-baseline-recommended Architecture + Functional re-run was also
executed to catch any ripple from M-1 / M-3 / M-4 / M-6):

```text
$ dotnet test test/Ordering.UnitTests/Ordering.UnitTests.csproj --no-build --no-restore
Passed!  - Failed:     0, Passed:   179, Skipped:     0, Total:   179, Duration: 2 s
exit 0.
  ↑ baseline 143 (sum1 closeout post-fix); +36 new facts:
     - OrderFailedMapperTests: 9
     - CreateOrderCommandHandlerTests (M-6 RequestedAtUtc): +1
     - 7 new validator test files: 26 facts (3+3+2+5+5+2+6)

$ dotnet test test/Ordering.ArchitectureTests/Ordering.ArchitectureTests.csproj --no-build --no-restore
[xUnit.net]   Ordering.ArchitectureTests.ProductSnapshotContractTests.OrderingProductSnapshot_IsStructuralSupersetOfBasketProductSnapshot [SKIP]
[xUnit.net]   Ordering.ArchitectureTests.ProductSnapshotContractTests.OrderingProductSnapshot_HasCapturedAtUtc [SKIP]
Passed!  - Failed:     0, Passed:    30, Skipped:     2, Total:    32, Duration: 2 s
exit 0.
  ↑ baseline 27 (+2 F6 SKIP); +3 new facts from NoAddressInLogArgumentsTests (one per assembly).

$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Ordering.IntegrationTests/Ordering.IntegrationTests.csproj --no-build --no-restore
[xUnit.net]   Ordering.IntegrationTests.Sessions.ItemImmutabilityIntegrationTests.Placeholder_ItemMutationGuard_NotApplicableInV1 [SKIP]
Passed!  - Failed:     0, Passed:    18, Skipped:     1, Total:    19, Duration: 13 s
exit 0.
  ↑ 18/18 (+1 expected SKIP); no behaviour regression from dropping SaveChangesAsync (M-1 sum1)
     or from the M-3 / M-4 / M-6 wiring changes. Same as M9 baseline.

$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Ordering.FunctionalTests/Ordering.FunctionalTests.csproj --no-build --no-restore
Passed!  - Failed:     0, Passed:    21, Skipped:     0, Total:    21, Duration: 5 s
exit 0.
  ↑ 21/21; same as M9 baseline.

TOTAL: 248 / 248 (+3 SKIP) post-fix.
       baseline was 209 / 209 (+3 SKIP) at closeout-sum1 head (post-H-1 fix).
       +39 new facts:
         +36 unit (OrderFailedMapper 9, CreateOrderHandler M-6 1, validator tests 26)
         +3 architecture (PII-log regression guard, one per assembly)
```

## Issues filed (`gh issue create --label needs-triage`)

11 issues total. All use the dispatch-mandated title prefix
`ordering(wave1-followup):`.

| # | Severity | Title |
|---|---|---|
| [#234](https://github.com/DavidCapcuch/DotNetAtlas/issues/234) | HIGH | Keycloak roles claim mapping in Platform.ServiceDefaults |
| [#235](https://github.com/DavidCapcuch/DotNetAtlas/issues/235) | LOW | GetOrdersByBuyer status-parse throw style |
| [#236](https://github.com/DavidCapcuch/DotNetAtlas/issues/236) | LOW | DLT routing runtime smoke for SagaCommandDispatchException |
| [#237](https://github.com/DavidCapcuch/DotNetAtlas/issues/237) | LOW | SagaCommandMappers.ResolveUniformCurrency double-iteration |
| [#238](https://github.com/DavidCapcuch/DotNetAtlas/issues/238) | LOW | GetOrdersByBuyer client-side projection after owned-collection materialise |
| [#239](https://github.com/DavidCapcuch/DotNetAtlas/issues/239) | LOW | Mapperly `[Mapper]`/`[UserMapping]` attributes are decorative |
| [#240](https://github.com/DavidCapcuch/DotNetAtlas/issues/240) | LOW | emit `ordering.authz.cross_buyer_attempt` counter metric |
| [#241](https://github.com/DavidCapcuch/DotNetAtlas/issues/241) | LOW | Skip/Take defensive guard inside GetOrdersByBuyerQueryHandler |
| [#242](https://github.com/DavidCapcuch/DotNetAtlas/issues/242) | LOW | Order.CreatedUtc (audit) vs CreatedAtUtc (business) projection-typo risk |
| [#243](https://github.com/DavidCapcuch/DotNetAtlas/issues/243) | MEDIUM | F6 ProductSnapshot.CapturedAtUtc 10-step cross-BC chain |
| [#244](https://github.com/DavidCapcuch/DotNetAtlas/issues/244) | LOW | use-cases.md §3 stale FailOrderCommand / MarkOrderStockReservedCommand-as-Kafka names |

## ADR application notes

No new ADRs applied. Several existing ADRs strengthened or re-pinned:

- **[ADR-0007](../../adr/0007-avro-compatibility-modes.md)** — `OrderConfirmedDomainEvent` XML-doc now correctly cites FORWARD_TRANSITIVE (was BACKWARD); wire schema unchanged.
- **[ADR-0008](../../adr/0008-correlation-id.md)** — `CorrelationId` LogContext push consolidated to the platform middleware (single source of truth from the Kafka header). Handler base no longer duplicates from the Avro payload.
- **[ADR-0011](../../adr/0011-pii-handling-gdpr.md)** — new architecture test forbids `Address`-typed arguments in `ILogger.Log*` calls across Application / Infrastructure / API assemblies. Regression guard.
- **[ADR-0013](../../adr/0013-idempotency-key-http.md)** — `CreateOrderCommandHandler` now uses `command.RequestedAtUtc` instead of `_timeProvider.GetUtcNow()`; idempotency on `CorrelationId` is unaffected (already pinned by `Handle_ReplayWithSameCorrelationId_ReturnsExistingId_NoDuplicate`).
- **[ADR-0015](../../adr/0015-time-timezone-policy.md)** — `Order.CreatedAtUtc` now reflects saga-issued time (M-6 sum2). `Order.Confirm` populates an explicit `OrderConfirmedDomainEvent.ConfirmedAtUtc` instead of routing through `OccurredOnUtc` (M-4 sum2). `DomainEventTests` re-ran green; the IL-level `DoesNotCallStaticUtcNowRule` arch test passes.

## Boundary discipline

In-bounds writes per [`ordering.md` `<boundaries>`:138-142](../ordering.md):

- 10 modified source files under `services/Ordering/**`.
- 4 modified test files under `test/Ordering.*/**`.
- 9 new test files under `test/Ordering.UnitTests/**` (validators + mapper) and `test/Ordering.ArchitectureTests/**` (PII rule + facts).
- This summary at `docs/implementation-prompts/session-summaries/ordering-followups.md`.

NOT touched:

- `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/**` — no schema changes (wire-compatibility-preserving session).
- `platform/Platform.ServiceDefaults/**` — Keycloak roles claim fix is filed as #234 (out of Ordering boundary).
- `docker-compose.yaml`, `Directory.Packages.props`, `DotNetAtlas.slnx` — no infrastructure changes.
- Other BCs' code, tests, schemas, docs.
- `docs/bc-design/use-cases.md` — cross-BC drift filed as #244.
- Pre-existing untracked closeout files under `docs/implementation-prompts/session-summaries/` and `docs/implementation-prompts/session-summaries2/` — read-only consumed for this triage; targeted writes only.

## Out-of-scope findings (reported back per dispatch step 8)

- **Pre-existing dirty working tree** at session start: Catalog/Inventory/Invoicing/Payments/Basket files in `M`/`??` state, plus the un-staged H-1 sum1 fix from the closeout author. Untouched by this session.
- **Pre-existing build-cache corruption** in `services/Invoicing/Invoicing.Infrastructure` and `test/Invoicing.IntegrationTests`. Initial solution-wide build threw `IDE0060` ("Remove unused parameter 'isDeployedEnvironment'") on `InfrastructureDependencyInjection.cs:26` — but that line declares `enableSensitiveDataLogging`, and `isDeployedEnvironment` lives in `AuthDependencyInjection.cs:37` where it IS used. The analyzer was reading a stale obj/ cache. Workaround: `Remove-Item -Recurse -Force services/Invoicing/Invoicing.Infrastructure/{obj,bin} test/Invoicing.IntegrationTests/{obj,bin}` then rebuild. Worth a `dotnet clean` CI step before `dotnet build -m` on Windows hosts. Documented here so the next maintainer doesn't chase the false IDE0060.
- **`Platform.ServiceDefaults/Auth/JwtBearerConfigurator.cs`** Keycloak roles claim mapping (#234) is the single highest-leverage cross-BC fix in the carry-forward set. Recommend a Wave-1.7 platform pass that resolves Ordering + Catalog (`CAT-SEC-003`) + sibling BCs in one PR.
- **F6 `ProductSnapshot.CapturedAtUtc` 10-step chain** (#243) is now formally tracked — was a documented carry-forward across M7/M8/M9 but not previously filed.

## What "done" looks like for the wave-1 follow-up pass

- [x] Both closeout docs read end-to-end (both folders, M8/M9/closeout).
- [x] Findings triaged, deduplicated, severity-aligned (single triage table above).
- [x] HIGH (in-boundary): 2/2 fixed.
- [x] HIGH (out-of-boundary): 1/1 filed (#234).
- [x] MEDIUM (in-boundary): 8/8 fixed.
- [x] MEDIUM (cross-BC carry-forward): 1/1 filed (#243).
- [x] LOW (in-boundary): 1/1 fixed (log-level bump).
- [x] LOW (filed): 9/9 (#235-#242 + #244).
- [x] 4 CI gates green (build / restore --locked-mode / format whitespace / format style).
- [x] 4 Ordering test slices green: 248 / 248 (+3 SKIP) — Unit 179, Architecture 30 (+2 F6 SKIP), Integration 18 (+1 placeholder SKIP), Functional 21.
- [x] TDD discipline applied to behavioural changes (M-1 sum2, M-4 sum2, M-6 sum2, M-1 sum1 via existing integration tests).
- [x] Summary posted at this path.

## Open questions

None blocking. Wave-1.7 platform pass (Keycloak roles claim + F6 cross-BC
chain + `use-cases.md` doc cleanup) is the natural next step but is out of
Ordering's writable boundary.
