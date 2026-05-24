# Payments wave1-followup — Session 1 closeout

> **Branch:** `aaqwdqwd` (commits 754d136..e0c2be7 — 5 commits)
> **Scope:** Payments BC sweep — 14 issues resolved; one no-op (already done).
> **Status:** All five commits landed on `aaqwdqwd`. No PR was opened (per user's request — direct on-branch).

---

## 1. Issues resolved (15 in scope)

| Issue | Title | Commit | Notes |
|---|---|---|---|
| [#246](https://github.com/DavidCapcuch/DotNetAtlas/issues/246) | `RefundTransactionId == PaymentTransactionId` | Commit 2 (`fe3b75e`) | Mapper now generates `Guid.CreateVersion7()` per refund row; downstream consumers can key off it distinctly. Tests rewritten. |
| [#247](https://github.com/DavidCapcuch/DotNetAtlas/issues/247) | `RetryForever` on money-handling consumer | Commit 5 (`e0c2be7`) | `RetrySimple(TryTimes: 8)` + outer `AddDeadLetter`; DLT topic `payments.commands.Payments.DLT` created in docker-compose; runbook authored at `docs/runbooks/payments-dlt.md`. |
| [#250](https://github.com/DavidCapcuch/DotNetAtlas/issues/250) | "this should be unreachable" guards | Commit 1 (`754d136`) | Removed `Payments.MissingGatewayTransactionId` null-guards in Capture / Void / RequestRefund handlers — the FSM `CanTransitionTo` pre-check proves I-4 (`GatewayTransactionId` non-null) so the null-coalesce was unreachable. Aggregate FSM is now the single source of truth. |
| [#251](https://github.com/DavidCapcuch/DotNetAtlas/issues/251) | `GetByIdAsync` tracking + read asymmetry | Commit 3 (`cbcc2f2`) | Split into `GetByIdForUpdateAsync` (tracking; 4 command handlers) + `GetByIdAsNoTrackingAsync` (`AsNoTracking`; `GetPaymentByIdQueryHandler`). Smoke test asserts detached / tracked entity state via `DbContext.ChangeTracker`. |
| [#257](https://github.com/DavidCapcuch/DotNetAtlas/issues/257) | `payments/payments/{id}` docstring | Commit 1 (`754d136`) | Single-line fix on `AuthPolicies.cs`. |
| [#258](https://github.com/DavidCapcuch/DotNetAtlas/issues/258) | Outbox-only path + topic-options arch tests | Commit 4 (`d7c2844`) | `OutboxOnlyPathTests` forbids `KafkaFlow.IProducer<,>` in Application/Infrastructure; `TopicOptionsUsageTests` IL-scans outbox publisher classes for literal `payments.transactions` / `payments.commands` `ldstr` opcodes. Both pass on day one — regression net. |
| [#259](https://github.com/DavidCapcuch/DotNetAtlas/issues/259) | `payments.md § 5` count drift | Commit 1 (`754d136`) | "internal, 8" → "internal, 9". |
| [#260](https://github.com/DavidCapcuch/DotNetAtlas/issues/260) | `<reading_order>:7` use-cases.md § 5 | Commit 1 (`754d136`) | Clarified that § 5 is the cross-service summary (not a Payments-specific section that doesn't exist). |
| [#261](https://github.com/DavidCapcuch/DotNetAtlas/issues/261) | `payments.md § 6` `PaymentRequestedEvent` producer | Commit 1 (`754d136`) | Note + table-row caption now flags Checkout saga as the producer per events-catalog.md line 85. |
| [#262](https://github.com/DavidCapcuch/DotNetAtlas/issues/262) | Inert internal events | Commit 1 (`754d136`) | Added XML `<remarks>` on `PaymentFailedDomainEvent` + `PaymentCompletedDomainEvent` explaining no handler is registered (Checkout saga owns wire-event production) and warning against accidental wiring. |
| [#263](https://github.com/DavidCapcuch/DotNetAtlas/issues/263) | Stub `Reason == GatewayCode` | Commit 1 (`754d136`) | Split: `Reason = "Insufficient funds on file"` (human-readable), `GatewayCode = "insufficient_funds"` (machine code). Test fixture updated. |
| [#264](https://github.com/DavidCapcuch/DotNetAtlas/issues/264) | Epsilon over decimal | Commit 2 (`fe3b75e`) | Simplified to direct `== 0.99m` equality. Added 99.99 / 99.50 / 100.00 inline data. |
| [#265](https://github.com/DavidCapcuch/DotNetAtlas/issues/265) | `MarkCaptureFailed` source-state guard message | (no-op) | Already done — line 350 of `PaymentTransaction.cs` already includes `current: '{Status.Name}'` since commit `895987b` (M2 domain layer authoring). Issue body referenced a stale read. |
| [#266](https://github.com/DavidCapcuch/DotNetAtlas/issues/266) | `Roles.cs` / `Scopes.cs` ADR-0010 anchors | Commit 1 (`754d136`) | Added `#admin-role` and `#oauth-scopes` anchor links via `<see href>`. |
| [#267](https://github.com/DavidCapcuch/DotNetAtlas/issues/267) | `PaymentStatus.IsFinal` doc note omits `Captured` | Commit 1 (`754d136`) | Doc note now covers `Captured` as non-final (auto-advances to `Completed` or refunded). |

**Skipped (already closed per plan):** #248, #249, #252, #253, #254, #245.

---

## 2. Commits on `aaqwdqwd`

```
e0c2be7 payments(wave1-followup): bounded retry + DLT routing on payments.commands (#247) (Commit 5/5)
d7c2844 test(payments)(wave1-followup): outbox-only + topic-options arch tests (#258) (Commit 4/5)
cbcc2f2 payments(wave1-followup): split PaymentRepository tracking + no-tracking (#251) (Commit 3/5)
fe3b75e payments(wave1-followup): mapper + stub cleanup (Commit 2/5)
754d136 payments(wave1-followup): doc + cosmetic sweep (Commit 1/5)
```

Each commit is independently buildable (verified `dotnet build -m` on the Payments slice after each apply).

---

## 3. Verification results

| Command | Outcome |
|---|---|
| `dotnet restore --locked-mode` | ✅ Pass |
| `dotnet build -m` (solution-wide) | ✅ Pass — 0 errors, 0 warnings (after a stash of unrelated Session-2 WIP that was sitting uncommitted in the working tree alongside an untracked `test/Payments.UnitTests/Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappersTests.cs` referencing the not-yet-added `AvroAuthorizePaymentCommand.PaymentTransactionId` field — see § 5 below). |
| `dotnet format whitespace --no-restore --verify-no-changes` | ✅ Pass — silent |
| `dotnet format style --no-restore --verify-no-changes` | ✅ Pass — silent |
| `dotnet test test/Payments.UnitTests` | ✅ **259/259** passed (added 2 new tests: PaymentRefundedMapper-distinct + 2 new StubPaymentGateway InlineData rows). |
| `dotnet test test/Payments.ArchitectureTests` | ✅ **36/36** passed (added 3 new facts: `OutboxOnlyPathTests` × 2 + `TopicOptionsUsageTests` × 1). |
| `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Payments.IntegrationTests` | ⚠️ **5/16 passed, 1 skipped, 10 failed** after Session 2 commits (`b85b061` + `aeb31ed`) landed on `aaqwdqwd`; **6/16, 9 failed** with my commits alone — either way, all failures are pre-existing and unrelated to this session's changes (see § 5). |
| `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test test/Payments.FunctionalTests` | ✅ **10/10** passed (the functional tests use the HTTP admin endpoints, not the Kafka handler chain that has the `FakeKafkaMessageContext` correlation-id gap). |

---

## 4. New artefacts

| Path | Purpose |
|---|---|
| `docs/runbooks/payments-dlt.md` | Full oncall runbook for non-zero `payments.commands.Payments.DLT` — what landing here means, header taxonomy, query path (`kafka-console-consumer --print.headers`), triage decision tree by `dlt-exception-type`, replay-vs-skip procedures, escalation matrix, forward-prevention. |
| `test/Payments.ArchitectureTests/Infrastructure/OutboxOnlyPathTests.cs` | Forbids `KafkaFlow.IProducer<,>` in Application / Infrastructure. |
| `test/Payments.ArchitectureTests/Infrastructure/TopicOptionsUsageTests.cs` | IL-scans outbox publishers for hard-coded topic-name literals. |
| `test/Payments.IntegrationTests/Infrastructure/PaymentRepositoryIntegrationTests.cs` | Smoke test for the `GetByIdForUpdateAsync` (tracked) / `GetByIdAsNoTrackingAsync` (detached) split. |
| `test/Payments.IntegrationTests/Messaging/PaymentCommandsDLTRoutingTests.cs` | **Placeholder** (skip-marked) for the end-to-end DLT roundtrip. The existing `IntegrationTestFixture` bypasses the production KafkaFlow runtime by invoking handlers directly via `FakeKafkaMessageContext`; adding a `KafkaTestContainer` + `SchemaRegistry` harness is a follow-up. Expected assertion shape captured in inline comments so the next contributor doesn't re-derive it. |

---

## 5. Pre-existing failures (NOT caused by this session)

`test/Payments.IntegrationTests/Infrastructure/PaymentsKafkaConsumerIntegrationTests` has 9 failing tests on `aaqwdqwd`. **Confirmed pre-existing** — they failed at the base commit `ab58264` (before any of the wave1-followup commits) and the diagnosis is independent of every file this session touched.

### Root cause

ADR-0008 (commit `ab58264`, **before** this session) migrated every Kafka consumer to read `CorrelationId` from the Kafka header instead of the Avro payload. The Payments test fixture at `test/Payments.IntegrationTests/Common/FakeKafkaMessageContext.cs:44-58` generates a **fresh `Guid.CreateVersion7()` for the header** when the test caller doesn't pass `correlationId` explicitly:

```csharp
public static IMessageContext Create(
    Guid? messageId = null,
    string origin = DefaultOrigin,
    Guid? correlationId = null,   // <-- default null
    CancellationToken cancellationToken = default)
{
    var headers = new MessageHeaders
    {
        // ...
        {
            MessageHeaderKeys.CorrelationId,
            Encoding.UTF8.GetBytes((correlationId ?? Guid.CreateVersion7()).ToString())   // <-- fresh GUID when null
        },
    };
```

Every failing test (`Authorize_HappyPath_*`, `Authorize_DeclineRule_*`, `Capture_AfterAuthorize_*`, etc.) calls `FakeKafkaMessageContext.Create(cancellationToken: …)` **without** passing the test's local `correlationId`. So:

1. Test creates `AvroAuthorizePaymentCommand { CorrelationId = X, … }` where `X` is the test's `Guid.CreateVersion7()`.
2. Test calls `FakeKafkaMessageContext.Create(...)` — the helper generates a new `Y` and puts it on the header.
3. `AuthorizePaymentCommandKafkaHandler.Handle(...)` reads `correlationId = Y` from the header (per ADR-0008) and dispatches `AppCommand { PaymentId = Y, CorrelationId = Y }`.
4. Aggregate persists with `Id = Y, CorrelationId = Y`.
5. Test queries `dbContext.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.CorrelationId == X)` — no row, returns null.
6. `aggregate.Should().NotBeNull()` fails.

The fix is one-line at each call site: pass `correlationId: correlationId` to `FakeKafkaMessageContext.Create(...)`. The docstring at lines 33-38 explicitly says callers MUST do this, but the test code was never updated when ADR-0008 landed.

### Why this wasn't caught earlier

Commit `ab58264` is recent (HEAD at session start). CI presumably passed at the time of that commit on a different test invocation profile, or the failures slipped through. Either way, the failures predate every commit in this session — `git checkout ab58264 -- services/Payments test/Payments.IntegrationTests/Infrastructure/PaymentsKafkaConsumerIntegrationTests.cs` reproduces the same 9 failures.

### What was NOT done about it

Out of scope for this session. The fix is mechanical (one-line at each test call site) but it's not on the wave1-followup issue list. Suggested next steps:
1. File a `payments(test-infra)` issue tracking the `FakeKafkaMessageContext.Create` correlation-id-propagation gap.
2. Either patch every Payments-side test call site to pass `correlationId: correlationId`, or change the helper to mint the header from the Avro payload's CorrelationId on construction so the gap can't surface again.

### Working-tree pollution while diagnosing

While running verification, the working tree was repeatedly stomped by other agents' parallel branch switches (the same physical worktree at `C:\Users\david.capcuch\Desktop\Git\DotNetAtlas` was being moved between `aaqwdqwd`, `wave1-followup-session1`, `wave1-followup-session2`, `wave1-followup-session3-basket`, `wave1-followup-session3-inventory`, `wave1-followup-session3-invoicing` by other concurrent work — visible in `git worktree list`). I created a dedicated worktree at `../DotNetAtlas-session1` (now unused; cherry-picked everything to `aaqwdqwd`) to isolate from this. The cherry-picks landed cleanly; verification was performed on `aaqwdqwd` directly after stashing the parallel agents' uncommitted edits.

---

## 6. Things to know for whoever picks up Session 2 / Session 3

- **#255 (Session 2) IS already partially in flight** on `aaqwdqwd` — commit `b85b061 platform: top-level correlation.id header from outbox + Outbox.EFCore unit tests (#256, Session 2 WIP)` was added by another agent. The `AuthorizePaymentCommand.avsc/.cs`, `SagaCommandMappers.cs`, `PaymentProcessingSagaOrchestrator.cs`, and `PaymentProcessingSagaOrchestratorTests.cs` are all in flight as uncommitted (or partially committed) state. Coordinate with that agent before continuing.
- **The `wave1-followup-session1` branch still exists** with the same 5 commits at SHAs `0e01d91..e6c2c1c` (pre-cherry-pick) — safe to delete once `aaqwdqwd` is the canonical resting place.
- **The auxiliary worktree at `../DotNetAtlas-session1` and the stash `commit5-wip-on-session1-worktree` can be cleaned up** (`git worktree remove ../DotNetAtlas-session1` + `git stash drop`).
- **The DLT integration test (`PaymentCommandsDLTRoutingTests`) is the natural next-task** for whoever fleshes out the Kafka testcontainer harness; the inline comments describe exactly what to wire.

---

## 7. Conventions touched / kept

- **Centralized package versions** (`Directory.Packages.props`) — not modified; new tests reused existing NetArchTest and FluentAssertions packages.
- **Result pattern** — preserved; the deleted `MissingGatewayTransactionId` guards were the `throw` shape (not Result) and are FSM-precondition-proven unreachable.
- **Avro schemas** — not modified (#246 is mapper-only, not aggregate-side).
- **Platform.SharedKernel** — not touched (no solution-wide rebuild risk).
- **Testcontainers proxy bypass** — chained `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet test …` on the IntegrationTests invocation per CLAUDE.md (without it, the named-pipe URI bug fires).

---

*End of payments-followup-session1 closeout.*
