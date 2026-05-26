# wave1-followup Session 3 — Closeout

**Scope:** Resolve issue [#217](https://github.com/DavidCapcuch/DotNetAtlas/issues/217) — apply the Catalog arch-test set to Basket / Ordering / Inventory / Invoicing. Payments slice was already done.

**Outcome:** Four commits landed directly on `aaqwdqwd`, no PRs. Issue **#217 is NOT closed** — leave for the human.

## Commits on `aaqwdqwd`

| BC | Commit | Tests after |
|---|---|---|
| Inventory | `a643673` test(Inventory): apply Catalog arch-test set (#217) | 44/44 arch, 66/66 unit, 43/44 int |
| Basket    | `038c361` test(Basket): apply Catalog arch-test set (#217)    | 42/42 arch, 166/166 unit, 4/4 int |
| Invoicing | `f910b10` test(Invoicing): apply Catalog arch-test set (#217) | 33/33 arch, 104/104 unit, 41/41 int |
| Ordering  | `fe8e4e0` test(Ordering): apply Catalog arch-test set (#217)  | 33/33 arch + 2 pre-existing skips, 179/179 unit, 15/19 int |

> First-pass workflow opened 4 draft PRs (#270–#273) per the original dispatch prompt; the user re-routed mid-session and asked for the work as commits on `aaqwdqwd` directly. The PRs were closed (GitHub doesn't allow PR deletion) with their remote branches removed and "Superseded" comments. The four commits above were cherry-picked onto `aaqwdqwd`.

## Planning premise that turned out to be wrong

The session-3 dispatch prompt claimed the 4 target BCs were "scaffold-only (Placeholder.cs)". In reality each BC already had a substantial arch-test suite (Inventory carried 5/7 of the Catalog rules; Ordering & Invoicing used a parallel file-per-rule pattern under `Rules/`; only Basket was genuinely thin). The mismatch was surfaced before any branch creation and the user picked **gap-fill** over wholesale-replace.

## Per-BC structure (recap of what landed)

| BC | Pre-existing pattern | Gap filled | Migrations |
|---|---|---|---|
| Inventory | Catalog-style (nested classes in BaseTest), 5/7 rules + Inventory's own `PublicMethodsAreSubsetOfRule` | `HasPublicStaticFactoryMethodRule` + `OnlyReferencesByIdRule` added; 2 new Facts in `AggregateRootTests` | — |
| Basket | Minimal BaseTest (`PrivateConstructorsRule` only) + BC-specific `TimePolicyTests` | 6 rules added to BaseTest; new `Domain/AdrComplianceTests.cs` + `Application/ResultPatternTests.cs`; 1 new Fact in `AggregateRootTests` | — |
| Invoicing | File-per-rule under `Rules/` + helper in BaseTest | `OnlyThrowsRule`, `DoesNotThrowRule`, `HandlerReturnsResultRule`, `OnlyReferencesByIdRule` added; new `Application/ResultPatternTests.cs`; 5 test files updated to reference BaseTest-nested rules | 3 migrated `Rules/` files deleted (the Invoicing-specific `NoForbiddenActivityTagKeysRule.cs` stays) |
| Ordering | File-per-rule under `Rules/`, minimal BaseTest | `OnlyThrowsRule`, `DoesNotThrowRule`, `HandlerReturnsResultRule`, `OnlyReferencesByIdRule` added; new `Application/ResultPatternTests.cs`; 2 test files updated | 3 migrated `Rules/` files deleted (the Ordering-specific `DoesNotLogPiiAddressRule.cs` stays) |

## Documented divergences

Two BC-specific adaptations made it into the BaseTest `<remarks>` blocks (per the user's "adapt or drop, document the divergence" guidance):

- **Inventory** — `HasPublicStaticFactoryMethodRule` accepts a third prefix `Fold` in addition to `Create` / `From`. `StockItem.Fold(events)` is the canonical event-sourcing factory (left-fold over the rehydrated event stream); renaming to `From` would lose the ES semantics.
- **Invoicing** — `OnlyReferencesByIdRule` is exposed in BaseTest for symmetry but the matching `AggregateRoots_ShouldNot_ReferenceOtherAggregatesByType` Fact is intentionally **not** added. `CreditNote.Create(Invoice originalInvoice, ...)` is a justified by-type coupling — refactoring to by-Id-only would force the snapshot logic (status check, line copy, total inversion) out of the aggregate into a domain service, sacrificing cohesion for rule-set uniformity. The rationale is in `BaseTest.cs`; a future contributor adding a second cross-aggregate type can revisit explicitly.

## Pre-existing failures observed (none caused by this work)

- **Inventory integration** — `ReserveStockCommandKafkaHandlerTests.HappyPath_AvroCommandTranslatedAndDispatched` fails on a `CorrelationId` assertion comparing a `Guid.NewGuid()` to a UUID v7. This is fallout from commit `ab58264` ([PR #118](https://github.com/DavidCapcuch/DotNetAtlas/pull/118) "Kafka consumers read CorrelationId from header per ADR-0008") landing on main — the test expects the old consumer-regenerated-correlation-id behavior.
- **Ordering integration** — 3 failures in `HappyPathIntegrationTests` of similar shape (saga/wire-protocol mismatch).

Both are independent of #217 — these commits touch no production code; they only add or relocate arch-test files.

## What's next

1. Close #217 with a comment listing the four commit SHAs (`a643673`, `038c361`, `f910b10`, `fe8e4e0`).
2. Investigate the pre-existing integration failures separately — likely a "wave1-followup: tests catch up to ADR-0008 CorrelationId header path" ticket.
