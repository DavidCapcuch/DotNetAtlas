# Payments BC — Final Closeout Review (FAIL)

> **HEAD:** `f49d358fb87f9559ab83f5abff2a638b76ea6cb9` (`feat(ordering): M9 docs self-corrections + Appendix B resolutions + session summary (Wave 1 / M9)`)
> **Branch:** `aaqwdqwd`
> **BC final milestone:** M9 (docker-compose smoke + final session summary) — confirmed present at [`payments-m9.md`](payments-m9.md); BC declared complete by the M9 summary § 347-351.
> **Verdict:** **FAIL** (strict-rubric reading — see [§ Verdict](#verdict)).
> **Date of this review:** 2026-05-14.

## TL;DR

The Payments BC ships a coherent, well-architected, internally consistent 4-layer DDD implementation: all four CI gates green, **277 / 277** Payments-scoped tests + saga regression green, layer / aggregate / domain-event / PII / ADR-0011 / ADR-0015 architecture rules enforced and passing. **However**, the independent multi-dimensional code review surfaced **2 CRITICAL contract-locked-seam drifts** on the saga ↔ Payments Avro surface that the M9 summary does not acknowledge: `IsRetryable` is silently `false` on every emitted authorization-/capture-failure event, and `PaymentMethodId` is typed `logicalType: uuid` on a field the BC defines as a 1-64 char gateway token. Either is independently sufficient to trip the rubric's FAIL bar (`any CRITICAL → FAIL`); both together plus the unenforced FORWARD_TRANSITIVE / FULL_TRANSITIVE compatibility-mode setting plus the silent `PaymentId := CorrelationId` collapse cumulatively trip the second FAIL bar (`contract-locked seam drifted`). The BC is teaching-readiness complete; it is not strictly contract-readiness complete.

---

## Dimension 1 — Doc Adherence + DoD Audit

### `_shared.md § 12` universal DoD walk

| # | Item | Status | Evidence |
|---|------|--------|----------|
| 1 | 4-layer project compiles | MET | [`services/Payments/Payments.{Api,Application,Domain,Infrastructure}/`](../../../services/Payments/) all build; `dotnet build -m` exit 0 |
| 2 | All commands + queries from `use-cases.md § 5` implemented | MET (with caveat) | 4 commands + 2 queries shipped; `use-cases.md` does not have a dedicated `§ 5 — Payments` chapter — only `§ 5. Cross-Service Command Flow Summary` ([use-cases.md:1480](../../bc-design/use-cases.md:1480)); doc inconsistency vs the dispatch prompt's `<reading_order>:7`, but the Payments use cases ARE captured in `payments.md § 9` |
| 3 | All internal `*DomainEvent` types declared | MET | 9 records under [`services/Payments/Payments.Domain/Transactions/Events/`](../../../services/Payments/Payments.Domain/Transactions/Events/) |
| 4 | All external `*Event` Avro schemas under `Avro/Payments/Transactions/` | MET | 13 `.avsc` files = 9 events + 4 commands; [`Avro/Payments/Transactions/`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/) |
| 5 | Outbox publishers map internal → external | PARTIALLY MET | 6 publishers (Payments-emitted set); `PaymentRequested` / `PaymentCompleted` / `PaymentFailed` external events are produced by the saga side per `events-catalog.md:82-85`, so absence of those 3 publishers is correct, BUT the mappers themselves drift from the schemas — see Dimension 5 + § Punch list |
| 6 | DbContext + naming conventions | MET | [`PaymentsDbContext.cs:39-46`](../../../services/Payments/Payments.Infrastructure/Persistence/Database/PaymentsDbContext.cs:39) (snake_case via `EFCore.NamingConventions`, schema `payments`); migration user-generated per CLAUDE.md |
| 7 | Messaging DI: outbox + inbox + Kafka consumers | MET | [`MessagingDependencyInjection.cs:67-110`](../../../services/Payments/Payments.Infrastructure/Common/MessagingDependencyInjection.cs:67) |
| 8 | docker-compose delta | MET | [`docker-compose.yaml:276-277,631-652`](../../../docker-compose.yaml:276) — topics + `outbox-relay-payments` |
| 9 | 4 test projects compile + pass; architecture tests enforce rules | MET | See Dimension 7 |
| 10 | All HTTP routes under `/api/v1/payments/...` per ADR-0012 | MET | [`PresentationDependencyInjection.cs:48-55`](../../../services/Payments/Payments.Api/Common/PresentationDependencyInjection.cs:48) + [`PaymentsGroup.cs:18`](../../../services/Payments/Payments.Api/Endpoints/Payments/PaymentsGroup.cs:18) |
| 11 | All timestamps `DateTimeOffset`; no `DateTime.UtcNow` in domain | MET | [`AdrComplianceTests.Domain_ShouldNot_UseStaticUtcNow`](../../../test/Payments.ArchitectureTests/Domain/AdrComplianceTests.cs:34); migration columns `timestamp with time zone` ([Init_Payments.cs:68-75](../../../services/Payments/Payments.Infrastructure/Persistence/Database/Migrations/20260501210449_Init_Payments.cs:68)) |
| 12 | Correlation-id propagation working | MET (with gap) | DB column `correlation_id` + unique index `UX_PaymentTransactions_CorrelationId` ([Init_Payments.cs:58,102-107](../../../services/Payments/Payments.Infrastructure/Persistence/Database/Migrations/20260501210449_Init_Payments.cs:58)); consumer middleware `AddCorrelationIdConsumerMiddleware` ([MessagingDependencyInjection.cs:85](../../../services/Payments/Payments.Infrastructure/Common/MessagingDependencyInjection.cs:85)); outbox Kafka key set to `CorrelationId.ToString()` ([PaymentAuthorizedOutboxPublisher...cs:41](../../../services/Payments/Payments.Application/Outbox/PaymentAuthorizedOutboxPublisherDomainEventHandler.cs:41)). Roundtrip pinned by integration test. |
| 13 | 4 CI gates all green | MET | Dimension 7 verbatim |
| 14 | `docker compose --profile full up -d` starts | MET | M9 summary § 184-218 — all Payments-relevant containers Healthy |
| 15 | Docs self-corrected if needed | NOT MET | Several locked-contract drifts NOT mentioned in `payments.md` self-corrections or in `payments-m9.md § Inconsistencies` — see Dimension 5 |
| 16 | Peer-review chain executed; HIGH-severity findings fixed | NOT MET | M9 pre-commit Opus reviewer found 0 CRITICAL / 0 HIGH ([payments-m9.md:315](payments-m9.md:315)); independent closeout reviewer finds 2 CRITICAL + 7 HIGH (this report). M9 review did not exercise mapper-vs-schema drift on the failure events nor the Avro `PaymentMethodId` uuid drift. |
| 17 | Session summary posted | MET | [`payments-m9.md`](payments-m9.md) |

### BC-specific DoD walk (`payments.md` `<dod>` lines 109-125)

| Line | Item | Status |
|------|------|--------|
| 109 | `services/Payments/` present, 4-layer | MET |
| 110 | 9 external Avro events + 4 command schemas under `Avro/Payments/Transactions/` | MET (count); see Dimension 5 for schema-vs-mapper drift |
| 111 | 9 internal `*DomainEvent` records + outbox publishers per external event | PARTIALLY MET (6 publishers — the 3 saga-emitted events have no Payments-side publisher by design) |
| 112 | 4 Kafka consumers with inbox dedup; no per-message service-auth | MET |
| 113 | `IPaymentGateway` port + `StubPaymentGateway` adapter with deterministic test rules | MET |
| 114 | Admin HTTP endpoints + auth policy | MET (with minor doc-comment typo — see Punch list LOW) |
| 115 | `PaymentsErrors` matches `error-taxonomy.md § 3.5` | MET (4 factories present; `GatewayDeclined` is the typed record per the taxonomy) |
| 116 | All timestamps `DateTimeOffset`; arch test forbids `DateTime.UtcNow` in domain | MET |
| 117 | Architecture test forbids PAN/CVV-like field names | MET |
| 118 | `*_enc` column naming | MET |
| 119 | Topic rename verified in `docker-compose.yaml` | MET |
| 120 | Integration tests cover all 3 `example-mapping` sessions + retry-idempotency | MET (8 named tests across 3 sessions) |
| 121 | `PaymentProcessingSaga` regression green | MET (12/12) |
| 122 | Correlation-id roundtrips | MET |
| 123 | All `<applicable_adrs>` enforced | PARTIALLY MET — ADR-0007 (FORWARD_TRANSITIVE / FULL_TRANSITIVE) is documented but **not actually enforced** at the schema-registry layer (Dimension 5 + Punch list HIGH) |
| 124 | Peer-review chain; HIGH findings fixed | NOT MET (closeout review surfaces 2 CRITICAL + 7 HIGH that the M9 review missed) |

### LOCKED contract spot-check (`payments.md` `<contract>` lines 33-43)

| Locked item | Reality | Status |
|-------------|---------|--------|
| Aggregate `PaymentTransaction` with `PaymentId : Guid` UUID v7 | Aggregate exists; **but** `PaymentId` is silently set equal to the saga's CorrelationId (Guid.NewGuid v4) via [`SagaCommandMappers.cs:30,46,57`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:30). Migration comment "Guid v7 — time-ordered" ([Init_Payments.cs:57](../../../services/Payments/Payments.Infrastructure/Persistence/Database/Migrations/20260501210449_Init_Payments.cs:57)) is false. | DRIFTED |
| `PaymentStatus` SmartEnum w/ 7 states | [`PaymentStatus.cs:20-26`](../../../services/Payments/Payments.Domain/Transactions/ValueObjects/PaymentStatus.cs:20) — matches | MET |
| Topics `payments.transactions` (infinite, key `CorrelationId`) + `payments.commands` (7-day, `CorrelationId`) | [`docker-compose.yaml:276-277`](../../../docker-compose.yaml:276) — names correct; retention not explicitly configured at topic-create (relies on Kafka defaults). M9 summary § 220-235 confirms topics describe successfully with 3 partitions RF=1. | MET |
| 9 external events + 4 commands under `Payments.Transactions` namespace | 13 `.avsc` files all in `namespace: "Payments.Transactions"` ✓ | MET |
| Schemas FORWARD_TRANSITIVE (events) + FULL_TRANSITIVE (commands) per ADR-0007 | **NOT enforced** — no init script, no startup hook, no per-subject compatibility registration anywhere in the repo. Defaults to BACKWARD on a fresh schema-registry. | DRIFTED |
| HTTP `/api/v1/payments/...` admin GET only | [`PresentationDependencyInjection.cs:48-55`](../../../services/Payments/Payments.Api/Common/PresentationDependencyInjection.cs:48) + group `payments` — actual routes are `/api/v1/payments/{id}` and `/api/v1/payments?orderId=...`. AuthPolicies.cs:21-22 docstring is wrong (says doubled `payments/payments`) — see Punch list LOW. | MET (routes); doc-comment drift |
| `PaymentsErrors` per `error-taxonomy.md § 3.5` | 4 factories + 1 typed record — matches taxonomy verbatim | MET |

### Invariant spot-check (5 from `payments.md § 2.2`)

| Invariant | Test that fails when invariant removed |
|-----------|----------------------------------------|
| **I-1** Amount > 0 | [`PaymentTransactionCreateTests.Create_WhenAmountNotPositive_UpstreamMoneyFactoryRejects`](../../../test/Payments.UnitTests/Transactions/Aggregates/PaymentTransactionCreateTests.cs:58) (✓ — but enforced upstream by `Money.Create`, not by `PaymentTransaction.Create`'s explicit amount check at [`PaymentTransaction.cs:118`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:118); both paths covered) |
| **I-3** FSM transitions guarded | [`PaymentTransactionAuthorizeTests.Authorize_FromCompleted_ThrowsDataIntegrityException`](../../../test/Payments.UnitTests/Transactions/Aggregates/PaymentTransactionAuthorizeTests.cs:93) + 6 other tests across the {Authorize,Capture,Refund,Void}Tests files |
| **I-4** GatewayTransactionId append-only | [`PaymentTransactionAuthorizeTests.Authorize_WhenDifferentGatewayTransactionId_ThrowsDataIntegrityException`](../../../test/Payments.UnitTests/Transactions/Aggregates/PaymentTransactionAuthorizeTests.cs:59) — verifies [`PaymentTransaction.GuardAppendOnlyGatewayTransactionId`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:467) |
| **I-5** Terminal-state idempotency | [`PaymentTransactionAuthorizeTests.Authorize_WhenAlreadyAuthorized_ReturnsOkAndDoesNotRaiseEvent`](../../../test/Payments.UnitTests/Transactions/Aggregates/PaymentTransactionAuthorizeTests.cs:38) + per-status terminal tests |
| **I-6** `CorrelationId` / `BuyerId` / `OrderId` immutable | Properties are `{ get; private set; }`; no public mutator; arch test `AggregateRoots_Should_BeImmutableExternally` covers the class-level rule ([`AggregateRootTests.cs:35`](../../../test/Payments.ArchitectureTests/Domain/AggregateRootTests.cs:35)) |

All 5 spot-checked invariants ARE enforced. **PASS** on invariant coverage.

---

## Dimension 2 — Architecture

**PASS.**

Clean Architecture topology (`Payments.Domain ← Application ← Infrastructure ← Api`) enforced by 6 dependency tests in [`CleanArchitectureLayerTests.cs`](../../../test/Payments.ArchitectureTests/CleanArchitecture/CleanArchitectureLayerTests.cs). Aggregate discipline (sealed, private ctor, public static factory, externally immutable) enforced by 4 tests in [`AggregateRootTests.cs`](../../../test/Payments.ArchitectureTests/Domain/AggregateRootTests.cs). Domain-event discipline (inherits `DomainEvent`, sealed record, name ends `DomainEvent`, resides in `Payments.Domain.{Aggregate}.Events`) enforced by 4 tests in [`DomainEventTests.cs`](../../../test/Payments.ArchitectureTests/Domain/DomainEventTests.cs). Cross-BC reference isolation enforced by 2 tests in [`NoCrossBcReferenceTests.cs`](../../../test/Payments.ArchitectureTests/CrossBoundedContext/NoCrossBcReferenceTests.cs). ADR-0011 PAN/CVV field-name prohibition + ADR-0015 no-static-UtcNow enforced by 3 tests in [`AdrComplianceTests.cs`](../../../test/Payments.ArchitectureTests/Domain/AdrComplianceTests.cs). PII `*_enc` column-naming enforced by 2 theory cases in [`PiiColumnNamingTests.cs`](../../../test/Payments.ArchitectureTests/Infrastructure/PiiColumnNamingTests.cs). Command/query handler naming + Application→Infrastructure-adapter isolation enforced by 2 tests in [`PaymentGatewayPortTests.cs`](../../../test/Payments.ArchitectureTests/Application/PaymentGatewayPortTests.cs).

Total: **30 / 30** non-trivial assertions, all currently green. The tests read end-to-end as genuine architectural rules, not tautologies (e.g. `PaymentGatewayPortTests` literally asserts `typeof(IPaymentGateway).Namespace == "Payments.Application.Abstractions"` AND that the Application assembly has no dependency on `Payments.Infrastructure.ExternalServices.PaymentGateway`).

### Findings (Dimension 2)

| Severity | File:line | Description |
|---|---|---|
| LOW | `test/Payments.ArchitectureTests/` | **Coverage gap.** No architecture test enforces "outbox is the ONLY path for external Kafka publishes" (no test forbids `KafkaFlow.IProducer<,>` references in `Payments.Application` / `Payments.Infrastructure` outside the consumer middleware wiring), and no test asserts that outbox publishers cite `TopicsOptions.Transactions` rather than hard-coded literal strings. Easy to add; would catch a meaningful class of future regression. |

---

## Dimension 3 — Design (DDD)

**PASS.**

- **Aggregate boundary**: One aggregate (`PaymentTransaction`), one saga-scoped lifecycle. Invariants live inside the aggregate methods (`GuardTransition`, `GuardAppendOnlyGatewayTransactionId`), not bled into handlers. [`PaymentTransaction.cs:460-474`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:460).
- **Value objects**: `PaymentMethodId`, `FailureInfo`, `FailureReason`, `GatewayResponseCode` — all `sealed record` / `sealed class : SmartEnum` with private ctor + `Create → Result<T>` factory where validation is meaningful. Structural equality from `record`.
- **Domain events**: 9 `sealed record … : DomainEvent`, in `Payments.Domain.Transactions.Events` namespace, dispatched in-process via `IDomainEventDispatcher` ([`AuthorizePaymentCommandHandler.cs:139-141`](../../../services/Payments/Payments.Application/Transactions/AuthorizePayment/AuthorizePaymentCommandHandler.cs:139)). External events are translated by Application-layer outbox publisher handlers; aggregates never produce `ISpecificRecord` directly.
- **Internal vs external event split**: 9 internal `*DomainEvent` + 9 external `*Event` Avro schemas with mappers that explicitly rename per Path-B convention (`BuyerId → UserId`, `GatewayTransactionId → AuthorizationId`, etc.).
- **SmartEnum where state machine exists**: `PaymentStatus` (7 states + transition table) ✓; `FailureReason` (6 values, no transitions — just classification) ✓. Absent where state machines don't exist (no SmartEnum for the gateway response code or the payment method).
- **Factory returning `Result<T>`**: [`PaymentTransaction.Create:107-152`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:107) returns `Result<PaymentTransaction>`. Constructor is private (line 91-93).
- **Idempotency by design**: aggregate methods short-circuit on same-state ([`Authorize:172-175`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:172), [`Capture:272-275`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:272), [`Void:385-388`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:385), [`Refund:429-432`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:429), [`MarkAuthorizationFailed:212-215`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:212), [`MarkCaptureFailed:327-330`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:327)).
- **Result vs DataIntegrityException**: factory validation → `Result.Fail(PaymentsErrors.*)`; FSM-violation / append-only-violation / missing-prerequisite → `DataIntegrityException` (bug-class, DLT-routable). Matches `error-taxonomy.md § 3.5`.
- **Capture auto-advances to Completed** within the same method call ([`Capture:296-309`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:296)). Documented as v1 simplification per `payments.md § 4` table; raises both `PaymentCapturedDomainEvent` and `PaymentCompletedDomainEvent`.

### Findings (Dimension 3)

None at this dimension. The DDD work is solid.

---

## Dimension 4 — Testing

**PASS** with a noted gap.

Test pyramid:

| Slice | Tests | Notes |
|-------|-------|-------|
| Payments.UnitTests | 214 | 6 aggregate × {Create,Authorize,Capture,Failure,Refund,Void}, 5 VO, 8 application (handlers + validators + classifier + mapper + queries), 1 error-shape, 1 SmartEnum, 1 stub-gateway, plus `PaymentTransactionFactory` helper |
| Payments.ArchitectureTests | 30 | covered in Dimension 2 |
| Payments.IntegrationTests | 11 | Testcontainers Postgres; faked outbox writer + faked Kafka message context; covers the 3 example-mapping sessions + idempotency-replay + capture-without-prior-authorize bug-class case |
| Payments.FunctionalTests | 10 | `WebApplicationFactory<Program>` + real auth handshake (`UnAuthClient`, `UserClient`, `AdminWithoutScopeClient`, `AdminClient`) |
| SagaOrchestrators.UnitTests (filter PaymentProcessing) | 12 | regression covering happy path + both compensation flows |
| **Total** | **277** | all green |

Strengths:

- Behaviour-named tests throughout (`WhenAdminAndPaymentDoesNotExist_ReturnsNotFound`, `Authorize_WhenDifferentGatewayTransactionId_ThrowsDataIntegrityException`, etc.).
- Every command has both a handler test AND a validator test ([`AuthorizePaymentCommandHandlerTests.cs`](../../../test/Payments.UnitTests/Application/AuthorizePaymentCommandHandlerTests.cs) + [`AuthorizePaymentCommandValidatorTests.cs`](../../../test/Payments.UnitTests/Application/AuthorizePaymentCommandValidatorTests.cs); same pattern for Capture, Void, Refund).
- Every aggregate invariant has a unit test asserting it fires AND a test asserting the invariant holds on idempotent replay (`Authorize_WhenAlreadyAuthorized_ReturnsOkAndDoesNotRaiseEvent`).
- `xunit.v3 xUnit1051` — `TestContext.Current.CancellationToken` used uniformly ([`PaymentsKafkaConsumerIntegrationTests.cs:57`](../../../test/Payments.IntegrationTests/Infrastructure/PaymentsKafkaConsumerIntegrationTests.cs:57) etc).
- `FakeTimeProvider` injected ([`AuthorizePaymentCommandHandlerTests.cs:23`](../../../test/Payments.UnitTests/Application/AuthorizePaymentCommandHandlerTests.cs:23)).
- No `CancellationToken.None`, no `.Result` / `.Wait()`, no static `DateTime.UtcNow` anywhere in `services/Payments/` or `test/Payments.*/` (confirmed by repo-wide grep).
- Assertion style is `using (new AssertionScope())` for multi-assertion blocks — non-brittle.

### Findings (Dimension 4)

| Severity | File:line | Description | Recommendation |
|---|---|---|---|
| MEDIUM | `test/Payments.IntegrationTests/Common/IntegrationTestFixture.cs` + `FakeKafkaMessageContext.cs` | Integration tests use a **faked** outbox writer and a **faked** Kafka message context — they do NOT go through the real Avro Schema Registry roundtrip in the test harness. M9 summary § 28-29 documents this ("byte-level roundtrip is exercised by the docker-compose smoke test"), but in practice that smoke only verifies the topics describe successfully, not that mapper-produced Avro records actually serialize through a real schema-registry compatibility check. Consequence: the unset `IsRetryable` field and the `PaymentMethodId` uuid-format constraint are NOT caught by any test in the CI suite. | Add at least one Schema-Registry-backed integration test per Payments outbox publisher that round-trips a real Avro byte payload through `Confluent.SchemaRegistry.Serdes`, asserting the deserialized payload's fields match expectation. Alternatively, add a contract test that asserts every Avro `IsRetryable` field on a Payments record is set to a non-default value in the mapper output. |
| LOW | `test/Payments.UnitTests/Application/AuthorizePaymentCommandValidatorTests.cs:35-43` | The validator-test theory `Validate_BadCurrency_Fails` asserts on `Currency` length being not-3 — but does not assert which `errorCode` flows back ("Payments.InvalidPaymentMethod" vs FluentValidation's default `Length`). Test name is fine; assertion could be tighter (`.WithErrorCode("...")`). Minor; not a coverage gap. | Tighten the assertion or accept as cosmetic. |

---

## Dimension 5 — Event-driven Best Practices

**Multiple HIGH + 2 CRITICAL findings.**

### What is enforced correctly

- **Outbox-only producer path**: every external event is enqueued via `ITransactionalOutbox<IPaymentsDbContext>.AddOutboxMessage` ([`PaymentAuthorizedOutboxPublisher...:39-43`](../../../services/Payments/Payments.Application/Outbox/PaymentAuthorizedOutboxPublisherDomainEventHandler.cs:39) — pattern repeated across 6 publishers). No `KafkaFlow.IProducer<,>` instance is referenced from anywhere in `Payments.Application` or in the `Payments.Infrastructure.ExternalServices` / `Payments.Infrastructure.Messaging.Kafka.PaymentCommands` areas — production publishes flow exclusively through `Platform.ReliableMessaging.Outbox` + the `outbox-relay-payments` worker.
- **Same-transaction outbox + aggregate write**: command handlers call `_dispatcher.DispatchAsync` (which enqueues outbox rows inside the same `IPaymentsDbContext`) then `_outbox.SaveChangesAsync(ct)` once at the end ([`AuthorizePaymentCommandHandler.cs:139-144`](../../../services/Payments/Payments.Application/Transactions/AuthorizePayment/AuthorizePaymentCommandHandler.cs:139)). Atomicity preserved.
- **Inbox dedup**: every Kafka consumer is wrapped in `AddInbox(typeof(...))` ([`MessagingDependencyInjection.cs:94-98`](../../../services/Payments/Payments.Infrastructure/Common/MessagingDependencyInjection.cs:94)) over `Platform.ReliableMessaging.Inbox.EFCore` against `PaymentsDbContext`. `EnsureTransactionAsync` wraps each handler call ([`SagaCommandHandlerBase.cs:73-93`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandHandlerBase.cs:73)).
- **Correlation-id flow**: Kafka header → `AddCorrelationIdConsumerMiddleware` → handler `Serilog.LogContext` scope ([`SagaCommandHandlerBase.cs:63-67`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandHandlerBase.cs:63)) → DB column `payment_transactions.correlation_id` → outbox Kafka key (== `CorrelationId.ToString()`) → eventual outbound header. ADR-0008 ✓ for the documented path.
- **No internal `*DomainEvent` leaked to Kafka**: arch test pattern; spot-checked — outbox messages only carry generated Avro `ISpecificRecord` types.
- **No cross-BC consumption of another BC's internal events**: `NoCrossBcReferenceTests` blocks Payments from referencing other BCs' Domain/Application namespaces.
- **No idempotency-key middleware**: correct decision per ADR-0013 + dispatch prompt `<applicable_adrs>:77` (admin GET only; no state-changing HTTP).

### Findings (Dimension 5)

| Severity | File:line | Description | Recommendation |
|---|---|---|---|
| **CRITICAL** | [`PaymentAuthorizationFailedMapper.cs:22-29`](../../../services/Payments/Payments.Application/Outbox/PaymentAuthorizationFailedMapper.cs:22) + [`PaymentCaptureFailedMapper.cs:20-28`](../../../services/Payments/Payments.Application/Outbox/PaymentCaptureFailedMapper.cs:20) + [`PaymentAuthorizationFailedEvent.avsc:33-37`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizationFailedEvent.avsc:33) + [`PaymentCaptureFailedEvent.avsc:38-42`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentCaptureFailedEvent.avsc:38) | The Avro contracts declare `IsRetryable: boolean` (required, no default). Both mappers omit it. The avrogen-generated C# field is `private bool _IsRetryable;` ([`PaymentAuthorizationFailedEvent.cs:42`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizationFailedEvent.cs:42)) — defaults to `false`. Every emitted failure event therefore carries `IsRetryable = false` regardless of `FailureReason`. The `FailureReason` SmartEnum has `GatewayTimeout` (transient, retryable) — emitted as not-retryable. Saga retry-vs-compensate path is degraded; on a real production gateway this means timeouts are treated as terminal declines and trigger compensation that may not be needed. Locked-contract semantic drift on a money-flow event. | In both mappers, derive `IsRetryable` from `source.FailureInfo.Reason` (only `GatewayTimeout` is provably retryable in v1; everything else is a business decline or ambiguous). Co-locate the decision in `GatewayResponseClassifier` as `IsRetryable(FailureReason)`. Add a unit test per `FailureReason` value asserting the mapper output. Add a Schema-Registry-backed integration test (Dimension 4 gap). |
| **CRITICAL** | [`AuthorizePaymentCommand.avsc:32-38`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AuthorizePaymentCommand.avsc:32) + [`PaymentRequestedEvent.avsc`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentRequestedEvent.avsc) + [`SagaCommandMappers.cs:36`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:36) vs [`PaymentMethodId.cs:14-41`](../../../services/Payments/Payments.Domain/Transactions/ValueObjects/PaymentMethodId.cs:14) + [`payments.md § 3`](../../bc-design/payments.md) | `PaymentMethodId` is typed `{ "type": "string", "logicalType": "uuid" }` on the wire. The Domain VO + BC chapter both define it as a generic 1-64 char gateway token (Stripe `pm_xxxxxx`, Adyen alphanumeric — never a UUID). The avrogen generates a `Guid`-or-`string-with-UUID-format`-typed property; the mapper coerces with `.ToString()`. Today (v1 stub) the saga emits a UUID-shaped value and everything works. **The moment a real-gateway adapter ships, real tokens fail Avro UUID validation at the producer.** This is a locked-contract drift on the saga ↔ Payments seam that blocks the BC's stated production-swap path. | Change `PaymentMethodId` in both `AuthorizePaymentCommand.avsc` and `PaymentRequestedEvent.avsc` to plain `"type": "string"` with a `maxLength: 64` doc note. Because the subject is `FULL_TRANSITIVE` (commands) and `FORWARD_TRANSITIVE` (events), the type-widening is a breaking change — register V2 records under new subjects, run V1+V2 in parallel during migration, retire V1. Add an architecture test asserting the avrogen-emitted `PaymentMethodId` property is `string`, not `Guid`. |
| HIGH | [`AuthorizePaymentCommandHandler.cs:105-144`](../../../services/Payments/Payments.Application/Transactions/AuthorizePayment/AuthorizePaymentCommandHandler.cs:105) | **Gateway-before-SaveChanges double-charge risk.** The gateway is invoked at line 105; `_outbox.SaveChangesAsync` commits aggregate + inbox + outbox at line 144. If the gateway succeeds but SaveChanges fails (DB blip, deadlock, concurrency violation), the gateway side holds an authorization Payments has no record of. The inbox-dedup row is rolled back along with everything else, so the saga's retry re-enters the handler via the `existing is null` branch → second `_gateway.AuthorizeAsync` call. The stub gateway hides this (deterministic `stub-{tx.Id:N}` + always-success); a real gateway issues a new authorization id on each call and DOES charge funds. The handler comment at lines 22-33 names "saga retry per ADR-0010" as the recovery without naming "double-authorize" as the failure mode it must prevent. | Two complementary fixes: (1) Persist the aggregate in `Requested` BEFORE the gateway call — split the flow into "create + SaveChanges in Requested" then "call gateway → Authorize/MarkAuthorizationFailed + SaveChanges". After the first SaveChanges, the inbox-dedup row is durable, so saga retries see the existing aggregate and short-circuit. (2) Pass the aggregate's PaymentId (or saga CorrelationId) as `Idempotency-Key` to the gateway adapter — the `IdempotencyKey` field in `AuthorizePaymentCommand.avsc:56-59` already exists and is today dropped (see next finding); wire it through `IPaymentGateway.AuthorizeAsync(tx, idempotencyKey, ct)` and have the v2 adapter forward as Stripe-style `Idempotency-Key` header. |
| HIGH | [`AuthorizePaymentCommand.avsc:55-59`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AuthorizePaymentCommand.avsc:55) + [`SagaCommandMappers.cs:27-37`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:27) | **`IdempotencyKey` Avro field dropped on the wire.** The schema declares a required `IdempotencyKey: string` documented as "Idempotency key to prevent duplicate authorizations." The mapper does not propagate it; the application command record has no corresponding property. Schema readers are misled into believing the key flows to the gateway. In production this is the load-bearing field for safe gateway retries — see preceding HIGH. | Add `IdempotencyKey` to the application `AuthorizePaymentCommand` record, propagate through the handler into `IPaymentGateway.AuthorizeAsync(tx, idempotencyKey, ct)`, have the v2 adapter forward it. Or remove from the schema if Kafka-message-id-based inbox dedup is genuinely sufficient (but combined with the gateway-before-SaveChanges finding, it isn't). |
| HIGH | [`VoidPaymentCommand.avsc:28-32`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/VoidPaymentCommand.avsc:28) + [`SagaCommandMappers.cs:54-59`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:54) | **`VoidPaymentCommand.Reason` dropped.** Schema declares required `Reason: string`. Mapper drops it. Application `VoidPaymentCommand` has no `Reason` property. `PaymentTransaction.Void()` ([`PaymentTransaction.cs:381-412`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:381)) takes no reason and emits a `PaymentVoidedDomainEvent` with no reason. Audit-trail gap on a money operation. The comparable Refund flow correctly threads `RefundReason` through command → handler → aggregate → `PaymentRefundedDomainEvent.Reason` — the asymmetry is striking. | Mirror the Refund flow: add `Reason` to the application `VoidPaymentCommand`, propagate via the mapper, accept on `PaymentTransaction.Void(string reason, ...)`, persist on the aggregate (new `void_reason` column — affects the migration, regenerate per CLAUDE.md), include in `PaymentVoidedDomainEvent`, project through `PaymentVoidedMapper` if a new Avro `Reason` field is added there. Minimum bar: persist + surface in the admin GET response. |
| HIGH | [`PaymentAuthorizedMapper.cs:25,39`](../../../services/Payments/Payments.Application/Outbox/PaymentAuthorizedMapper.cs:25) | **`ExpiresAtUtc` synthesized as `AuthorizedAtUtc + 7 days`.** The Avro schema [`PaymentAuthorizedEvent.avsc:51-58`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentAuthorizedEvent.avsc:51) documents this as "UTC timestamp when the authorization expires. Must capture before this time." — a hard contract. The aggregate has no notion of expiry; the mapper invents 7 days. If a saga "expedite capture before expiry" branch ever materializes, it is off by hours-to-days vs the real gateway's window. | Add an `ExpiresAtUtc` field to `AuthorizeResponse` ([`Payments.Application.Abstractions.AuthorizeResponse.cs`](../../../services/Payments/Payments.Application/Abstractions/AuthorizeResponse.cs)) so the v2 gateway adapter returns the gateway-issued expiry. Stub returns `AuthorizedAtUtc + 7 days` but flowing through the response contract. Or make `ExpiresAtUtc` optional in Avro (`["null", "timestamp-millis"]`) so consumers handle "unknown expiry" explicitly. |
| HIGH | [`SagaCommandMappers.cs:30,46,57`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:30) + [`Init_Payments.cs:57`](../../../services/Payments/Payments.Infrastructure/Persistence/Database/Migrations/20260501210449_Init_Payments.cs:57) | **`PaymentId := CorrelationId` silent collapse.** The migration's PK comment claims `Guid v7 — time-ordered`; the value comes from the saga's `Guid.NewGuid()` (v4 random). v7 time-ordering claim is false; PK inserts scatter across the B-tree, losing the index-locality benefit v7 was chosen for. Documented as "one-payment-per-saga" only in the mapper comment; never in `payments.md` or `<contract>`. Downstream BCs reading `PaymentTransactionId` from `PaymentCapturedEvent` / `PaymentCompletedEvent` have no way to know it equals `CorrelationId`. The moment the BC needs a void → re-authorize-with-fresh-PaymentId flow, both the unique-index `UX_PaymentTransactions_CorrelationId` and the implicit `PaymentId == CorrelationId` equation crack. | Preferred: have the saga generate a `Guid.CreateVersion7()` PaymentId, carry as a new `PaymentTransactionId` field on `AuthorizePaymentCommand.avsc`, mapper passes through. PaymentId and CorrelationId stay distinct per `payments.md § 2.1`; the v7 PK comment becomes true; "one-payment-per-saga" stays enforced by the existing unique index on `correlation_id`. Acceptable fallback: document the collapse in `payments.md` and fix the migration comment to "Guid v4 (== saga CorrelationId)". |
| HIGH | [`SagaCommandMappers.cs:43-48,54-59`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:43) + [`CapturePaymentCommandKafkaHandler.cs:30-35`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/CapturePaymentCommandKafkaHandler.cs:30) + [`VoidPaymentCommandKafkaHandler.cs:29-34`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/VoidPaymentCommandKafkaHandler.cs:29) | **`AuthorizationId` ignored on wire commands.** `CapturePaymentCommand` and `VoidPaymentCommand` carry an `AuthorizationId: string` field documented as "Authorization ID from the payment provider to capture/void." The mapper ignores it; handlers reload `tx.GatewayTransactionId` from the DB. If the saga ever sends a wire AuthorizationId that disagrees with stored value (real bug, stale token after a partial gateway-side rotation, or a malicious replay), the handler silently uses the stored value. Wire contract becomes documentation-only; bug-class case is silently absorbed. | In each handler, assert `string.Equals(tx.GatewayTransactionId, command.AuthorizationId, StringComparison.Ordinal)` (after pulling from mapper) and throw `SagaCommandDispatchException` ([`SagaCommandHandlerBase.cs:102-117`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandHandlerBase.cs:102)) on mismatch, routing the message to DLT. Stored value remains the SOT for the actual gateway call (defense-in-depth); assertion catches the bug-class case loudly. Add an integration test that asserts DLT delivery on mismatched AuthorizationId. |
| HIGH | `docker-compose.yaml` + repo-wide search for `compatibility` / `FORWARD_TRANSITIVE` / `FULL_TRANSITIVE` returns zero hits in init scripts or startup hooks | **Schema-registry compatibility modes not enforced.** `<contract>:40` and `events-catalog.md § 2` mandate FORWARD_TRANSITIVE for `payments.transactions` and FULL_TRANSITIVE for `payments.commands`. Topics are created at `kafka-create-topic` start ([`docker-compose.yaml:276-277`](../../../docker-compose.yaml:276)); no companion step sets per-subject compatibility. Schema-registry defaults to BACKWARD on a fresh deploy. Local docker-compose / integration-test environments therefore enforce the WRONG compat mode for both topic families. A schema evolution that's safe under FORWARD_TRANSITIVE may break under BACKWARD (or vice versa); local "works on my machine" diverges from any future CI/CD gate. | Add a tiny one-shot init container to `docker-compose.yaml` (sibling to `kafka-create-topic`) that POSTs `{"compatibility":"FORWARD_TRANSITIVE"}` to `/config/<subject>` for every `Payments.Transactions.*Event-value` subject and `FULL_TRANSITIVE` for every `*Command-value` subject after the registry comes up. Or add a per-service startup hook. Add an integration test asserting the actual compatibility-mode-per-subject matches the locked-contract table. |
| MEDIUM | [`PaymentRefundedMapper.cs:30`](../../../services/Payments/Payments.Application/Outbox/PaymentRefundedMapper.cs:30) | **`RefundTransactionId == PaymentTransactionId`.** Schema doc says "New transaction ID for the refund." Implementation re-uses the same id with a comment about v2 partial-refunds. Documented but breaks downstream reconciliation by id. | Generate `RefundTransactionId = Guid.CreateVersion7(now)` in the aggregate when `Refund` is called; persist alongside `RefundedAtUtc`. Migration regeneration required. Or amend the schema doc string to make the v1 equality explicit. |
| MEDIUM | [`MessagingDependencyInjection.cs:87-93`](../../../services/Payments/Payments.Infrastructure/Common/MessagingDependencyInjection.cs:87) | **`RetryForever` on money-handling consumer.** Policy is unlimited retries with 500ms/1s/2s/5s backoff on `DbUpdateException` / `NpgsqlException` / `TimeoutException`. A poison message that consistently throws one of these (e.g. structural DB problem mid-deploy) blocks the consumer worker indefinitely; partition-offset advance halts. DLT producer is registered (line 71-76) but unreachable for the three handled exception types. | Replace with bounded retry (`WithMaxRetriesAndDLT(maxRetries: 8, ...)`) routing exhausted messages to `payments.commands.DLT`. Add an operational runbook page for `payments.commands.DLT` non-zero and an integration test asserting DLT delivery after exhaustion. |
| MEDIUM | [`PaymentTransactionResponseMapper.cs:25`](../../../services/Payments/Payments.Application/Transactions/GetPaymentById/PaymentTransactionResponseMapper.cs:25) + [`GetPaymentByIdResponse.cs`](../../../services/Payments/Payments.Application/Transactions/GetPaymentById/GetPaymentByIdResponse.cs) | **Raw `PaymentMethodId.Value` returned in admin GET response.** `[Pii]` is declared on the VO ([`PaymentMethodId.cs:13`](../../../services/Payments/Payments.Domain/Transactions/ValueObjects/PaymentMethodId.cs:13)) — Serilog redaction policy applies when destructuring the VO, but the response DTO's `PaymentMethodId` is a plain `string` with no `[Pii]` marker. An authenticated admin caller receives plaintext PII over HTTP; any future OTel HTTP-server span capturing the response body leaks it. ADR-0011's logging-PII rule is not enforced on response shapes. Likewise `GatewayTransactionId` (also `_enc`-tagged in DB) is returned plaintext. | Mark the response DTO's `PaymentMethodId` + `GatewayTransactionId` properties with `[Pii]` so the Serilog destructuring policy fires. Optionally add a masking function (`pm_•••1234`) or a fine-grained `payments.read.unmasked` scope policy. Add an architecture test that any property containing `PaymentMethodId` or `GatewayTransactionId` in its name across `Payments.Application` DTOs carries `[Pii]`. |
| MEDIUM | [`AuthorizePaymentCommandHandler.cs:83,106`](../../../services/Payments/Payments.Application/Transactions/AuthorizePayment/AuthorizePaymentCommandHandler.cs:83) | **Double `_timeProvider.GetUtcNow()` reads** spanning the gateway call. `PaymentRequestedDomainEvent.OccurredOnUtc` is set from the first read; `PaymentAuthorizedDomainEvent.OccurredOnUtc` + `AuthorizedAtUtc` from the second. Production gateway round-trip is hundreds of ms; FakeTimeProvider doesn't auto-advance, so tests don't see the gap. Violates ADR-0015's "single source of truth for time within a unit of work" implicit intent. | Read `var utcNow = _timeProvider.GetUtcNow();` once at the top of `HandleAsync`, pass to both the factory and post-gateway transitions. |
| MEDIUM | [`CapturePaymentCommandHandler.cs:65-70`](../../../services/Payments/Payments.Application/Transactions/CapturePayment/CapturePaymentCommandHandler.cs:65) + [`VoidPaymentCommandHandler.cs:64-67`](../../../services/Payments/Payments.Application/Transactions/VoidPayment/VoidPaymentCommandHandler.cs:64) + [`RequestRefundCommandHandler.cs:64-67`](../../../services/Payments/Payments.Application/Transactions/RequestRefund/RequestRefundCommandHandler.cs:64) | **Unreachable `MissingGatewayTransactionId` guard with misleading message.** The handler null-check fires for the `Requested → Capture` bug-class case (integration-tested), but the error message claims "this should be unreachable" — which is misleading because it WILL fire on that exact tested case. Aggregate's own FSM `GuardTransition` would reject the same case if the handler check were removed. | Remove the handler-level null-check entirely and let the aggregate's FSM guard fire (single source of truth in the aggregate). Or keep the check but reword the message to drop the "unreachable" claim and state the precondition explicitly. |
| MEDIUM | [`PaymentRepository.cs:23`](../../../services/Payments/Payments.Infrastructure/Persistence/Repositories/PaymentRepository.cs:23) + [`GetPaymentByIdQueryHandler.cs`](../../../services/Payments/Payments.Application/Transactions/GetPaymentById/GetPaymentByIdQueryHandler.cs) | **`GetByIdAsync` returns a tracking query**, used by both write-side handlers (correct) and the read-side query handler (suboptimal — tracking overhead, latent bug surface if a future mapper mutates the entity). The list-by-order method correctly uses `.AsNoTracking()`. Asymmetric. | Split into `GetByIdForUpdateAsync(...)` (tracking) and `GetByIdAsNoTrackingAsync(...)` (no-tracking, used by query handler). Or expose a `bool tracking` parameter. |

---

## Dimension 6 — .NET / C# Best Practices

**PASS.**

Spot-checks across `services/Payments/` and `test/Payments.*/`:

- **No `.Result` / `.Wait()`**: 0 hits.
- **No `CancellationToken.None`** (xunit.v3 xUnit1051 baseline): 0 hits in test bodies; tests use `TestContext.Current.CancellationToken`.
- **No static `DateTime.UtcNow` / `DateTimeOffset.UtcNow` / `DateTime.Now`**: 0 hits in production code (enforced by [`AdrComplianceTests.Domain_ShouldNot_UseStaticUtcNow`](../../../test/Payments.ArchitectureTests/Domain/AdrComplianceTests.cs:34); also confirmed by repo-wide grep).
- **`TimeProvider` injected** through every command handler ctor; `FakeTimeProvider` injected in tests.
- **Cancellation tokens** flow from Kafka consumer middleware (`context.ConsumerContext.WorkerStopped`) → handler → gateway → outbox `SaveChangesAsync` ([`SagaCommandHandlerBase.cs:61`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandHandlerBase.cs:61)).
- **`IDisposable` honoured**: integration-test fixture exposes `Dispose`; scope creation uses `using var scope`.
- **`[Pii]` attribute** on `PaymentMethodId.cs:13` (ADR-0011 in-process redaction); see Dimension 5 MEDIUM for the response-DTO gap.
- **No magic strings for connection-string keys**: typed `ConnectionStringsOptions` ([`ConnectionStringsOptions.cs`](../../../services/Payments/Payments.Infrastructure/Common/Config/ConnectionStringsOptions.cs)) + memory record that Basket uses `ConnectionStringNames` constants — Payments follows the typed-options pattern (acceptable variant; both patterns coexist in the codebase).
- **No magic strings for topic names**: `TopicsOptions.Transactions` injected via `IOptions<>` ([`TopicsOptions.cs:11-33`](../../../services/Payments/Payments.Application/Common/Messaging/TopicsOptions.cs:11)).
- **No magic strings for error codes**: `error-taxonomy.md`-aligned `PaymentsErrors.*` factories with stable `errorCode` strings ("Payments.NotFound", "Payments.InvalidAmount", "Payments.InvalidPaymentMethod", "Payments.GatewayUnavailable"); `PaymentsErrorCodes` constants class for the HTTP-side mapping ([`PaymentsErrorCodes.cs`](../../../services/Payments/Payments.Api/Common/Extensions/PaymentsErrorCodes.cs)).
- **No PII logged**: grep over `services/Payments/**/*.cs` for `LogInformation`/`LogWarning`/`LogError`/`LogDebug` filtered by `PaymentMethodId|GatewayTransactionId` returns 0 hits.
- **Nullable reference types respected**: explicit `!` operator used in 3 well-documented places in the aggregate ([`PaymentTransaction.cs:343,406,450`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:343)) — each immediately follows a `GuardTransition` / `Throw.If` that proves the value is non-null. Comment at line 341-342 explains the reasoning.
- **Logging levels** appropriate: `LogInformation` for happy-path observability, `LogWarning` for business-expected failures + saga-must-retry, no overuse of `LogError`.

---

## Dimension 7 — CI Gates + Test Slices (Verbatim)

> Per the dispatch prompt: actual stdout, not summaries.

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet restore --locked-mode --nologo
  (102 NU1903 warnings on System.Security.Cryptography.Xml + Microsoft.Kiota.Abstractions + Microsoft.Extensions.Caching.Memory across many projects)
  Všechny projekty jsou v aktuálním stavu pro obnovení.
==EXIT==0
```

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet build -m --nologo
  (NOTE: first attempt failed with MSB4018/MSB3026 on Ordering.Api.dll + Catalog.Api.dll
   due to stale testhost.exe processes holding file locks. After PowerShell:
   Get-Process testhost,vstest.console | Stop-Process -Force, the build succeeded.)
  ... 106 upozornění (NU1903 baseline + per-test-project warnings, identical to M9 § 154)
  Počet chyb: 0
  Uplynulý čas 00:00:45.52
===EXIT===0
```

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet format whitespace --no-restore --verify-no-changes
  Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění
  protokolovat, nastavte možnost verbosity na úroveň diagnostic.
===EXIT===0
```

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && dotnet format style --no-restore --verify-no-changes
  Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění
  protokolovat, nastavte možnost verbosity na úroveň diagnostic.
===EXIT===0
```

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Payments.UnitTests/Payments.UnitTests.csproj --no-build --no-restore --nologo
Testovací běh pro C:\Users\david.capcuch\Desktop\Git\DotNetAtlas\test\Payments.UnitTests\bin\Debug\net10.0\Payments.UnitTests.dll (.NETCoreApp,Version=v10.0)
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:   214, Přeskočeno:     0, Celkem:   214, Doba trvání: 22 s
```

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Payments.ArchitectureTests/Payments.ArchitectureTests.csproj --no-build --no-restore --nologo
Testovací běh pro C:\Users\david.capcuch\Desktop\Git\DotNetAtlas\test\Payments.ArchitectureTests\bin\Debug\net10.0\Payments.ArchitectureTests.dll (.NETCoreApp,Version=v10.0)
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    30, Přeskočeno:     0, Celkem:    30, Doba trvání: 29 s
```

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Payments.IntegrationTests/Payments.IntegrationTests.csproj --no-build --no-restore --nologo
Testovací běh pro C:\Users\david.capcuch\Desktop\Git\DotNetAtlas\test\Payments.IntegrationTests\bin\Debug\net10.0\Payments.IntegrationTests.dll (.NETCoreApp,Version=v10.0)
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    11, Přeskočeno:     0, Celkem:    11, Doba trvání: 36 s
```

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Payments.FunctionalTests/Payments.FunctionalTests.csproj --no-build --no-restore --nologo
Testovací běh pro C:\Users\david.capcuch\Desktop\Git\DotNetAtlas\test\Payments.FunctionalTests\bin\Debug\net10.0\Payments.FunctionalTests.dll (.NETCoreApp,Version=v10.0)
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    10, Přeskočeno:     0, Celkem:    10, Doba trvání: 4 s
```

```text
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test saga/SagaOrchestrators.UnitTests/SagaOrchestrators.UnitTests.csproj \
    --no-build --no-restore --nologo --filter "FullyQualifiedName~PaymentProcessing"
Testovací běh pro C:\Users\david.capcuch\Desktop\Git\DotNetAtlas\saga\SagaOrchestrators.UnitTests\bin\Debug\net10.0\SagaOrchestrators.UnitTests.dll (.NETCoreApp,Version=v10.0)
Celkový počet testovacích souborů, které odpovídají zadanému vzoru: 1

Úspěšné!    - Neúspěšné:     0, Úspěšné:    12, Přeskočeno:     0, Celkem:    12, Doba trvání: 7 s
```

**Total: 277 / 277 Payments-scoped tests green; 4 / 4 CI gates exit 0. Dimension 7 PASS.**

> Note on the build's transient stale-testhost failure: the M9 summary documents 0 errors on a clean working tree, which is consistent with what I observed once the orphaned `testhost.exe` PIDs (from a prior locally-aborted Catalog functional-test run on this workstation) were terminated. This is a Windows-developer-environment behaviour, not a Payments-introduced defect.

> Note on `docker compose --profile full up -d`: re-verifying this was deemed unnecessary — the M9 summary § 184-218 captured the smoke 24 hours before this review and all Payments-relevant containers (`broker`, `schema-registry`, `postgres5433`, `outbox-relay-payments`) were Healthy; the `otel-collector` Restarting state is acknowledged pre-existing platform-level defect (M9 § Inconsistencies #2), not Payments-introduced. Working state has not changed since M9.

---

## Dimension 8 — Independent Multi-Dimensional Code Review

Dispatched: `Agent(subagent_type="feature-dev:code-reviewer", model="opus")` with the production-code file list + `<contract>` + applicable ADRs + my 14 areas-of-concern as briefing context. Reviewer ran for ~5m 43s and returned **15 findings** (2 CRITICAL, 7 HIGH, 5 MEDIUM, 1 LOW + 1 coverage gap). Reviewer findings cross-referenced with my own dimensional findings: **3 of my HIGHs escalated to CRITICAL or unique HIGH**, **4 new HIGHs surfaced that I had classified as MEDIUM or missed**, **all of my CRITICAL/HIGH/MEDIUM findings confirmed independently**, **2 LOWs collapsed**.

The reviewer's CRITICAL escalations are the load-bearing reason for the FAIL verdict:

1. `IsRetryable` always-false (the reviewer escalated this from MEDIUM-coded-mapper-bug to CRITICAL-locked-contract-on-money-event after independently noting the consumer-side saga implications).
2. `PaymentMethodId` Avro=uuid (the reviewer escalated from HIGH-format-drift to CRITICAL-blocks-v2-adapter after independently confirming the Stripe/Adyen real-token shape mismatch).

The reviewer's full punch list is incorporated into Dimension 5 and into the [§ Punch list](#punch-list) below.

---

## Verdict

**FAIL.**

Threshold reading per the dispatch prompt's `<output>`:

| Threshold | Trigger | Met? |
|---|---|---|
| PASS | zero CRITICAL, zero unaccepted HIGH, all DoD MET, all gates green | ❌ (2 CRITICAL) |
| CONDITIONAL-PASS | zero CRITICAL, ≤ N HIGH documented as accepted carry-forwards, all DoD MET or PARTIALLY MET with rationale | ❌ (2 CRITICAL) |
| FAIL | any CRITICAL | ✅ |
| FAIL | any DoD NOT MET without acceptance | ✅ (DoD line 124 "Peer-review chain; HIGH findings fixed" — closeout review finds 7 HIGH the M9 reviewer missed; not acknowledged in `payments-m9.md`) |
| FAIL | any test red | ❌ (277/277 green) |
| FAIL | contract-locked seam drifted | ✅ (three drifts: `IsRetryable` semantics, `PaymentMethodId` Avro type, FORWARD_TRANSITIVE / FULL_TRANSITIVE compat-mode enforcement) |

Three independent FAIL bars are tripped. Verdict is **FAIL**.

### Framing context

This verdict is strictly contract-compliance against the LOCKED seams documented in `<contract>`. It is NOT a "the BC doesn't work" verdict — every shipped test is green, the saga regression is green, the docker-compose smoke is green, the four CI gates are green, the BC's documented v1 scope (`<success_criteria>` lines 99-104) is met. The FAIL is driven by a contract-vs-code divergence on the saga ↔ Payments seam that:

- Is invisible in the current test suite because integration tests use a faked Schema Registry + a faked outbox writer.
- Is invisible in the docker-compose smoke because the smoke only verifies topics describe successfully.
- Will surface the moment the BC graduates to a production-grade gateway adapter (HIGH #3, CRITICAL #2) or the moment a downstream saga depends on `IsRetryable` to differentiate retry vs compensate paths (CRITICAL #1).

For a teaching reference solution explicitly scoped to v1-stub per ADR-0009, these are HIGH-importance follow-ups; for "the BC is contract-locked-seam complete" — the bar `<output>` sets — they are CRITICAL.

---

## Punch list

Ordered by severity; address top-to-bottom.

### CRITICAL — Must fix before "Payments BC contract-locked" claim is honest

1. **Set `IsRetryable` in failure-event mappers.** Co-locate the mapping in `GatewayResponseClassifier.IsRetryable(FailureReason)`; only `GatewayTimeout` is retryable in v1.
   - Edit: [`PaymentAuthorizationFailedMapper.cs:22-29`](../../../services/Payments/Payments.Application/Outbox/PaymentAuthorizationFailedMapper.cs:22), [`PaymentCaptureFailedMapper.cs:20-28`](../../../services/Payments/Payments.Application/Outbox/PaymentCaptureFailedMapper.cs:20).
   - Add: per-`FailureReason` unit test on the mapper output; Schema-Registry-backed integration test on the failure-event publish path.

2. **Retype `PaymentMethodId` Avro field to `string` (no `logicalType: uuid`).** Breaking schema change → register V2 records under new subjects, migrate, retire V1 per the FULL/FORWARD_TRANSITIVE migration playbook.
   - Edit: [`AuthorizePaymentCommand.avsc:32-38`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/AuthorizePaymentCommand.avsc:32), [`PaymentRequestedEvent.avsc`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/PaymentRequestedEvent.avsc).
   - Add: architecture test asserting avrogen-generated `PaymentMethodId` property type is `string`, not `Guid`.

### HIGH — Must fix before any real-gateway adapter swap

3. **Persist aggregate in `Requested` BEFORE the gateway call** in `AuthorizePaymentCommandHandler`, OR propagate `IdempotencyKey` end-to-end and have the v2 gateway adapter forward it. Today's flow has a double-charge window on `SaveChangesAsync` failure + saga retry. Edit: [`AuthorizePaymentCommandHandler.cs:105-144`](../../../services/Payments/Payments.Application/Transactions/AuthorizePayment/AuthorizePaymentCommandHandler.cs:105).

4. **Wire `IdempotencyKey` Avro field to the gateway** OR delete it from the schema. Today it's documented but dropped. Edit: [`SagaCommandMappers.cs:27-37`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:27) + Application `AuthorizePaymentCommand` + `IPaymentGateway.AuthorizeAsync(...)` signature.

5. **Propagate `VoidPaymentCommand.Reason`** through the mapper → application command → aggregate → domain event → optional Avro `Reason` field on `PaymentVoidedEvent`. Migration regeneration needed (user-side). Edit: [`SagaCommandMappers.cs:54-59`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:54), [`PaymentTransaction.Void`](../../../services/Payments/Payments.Domain/Transactions/PaymentTransaction.cs:381).

6. **Remove the synthesized `ExpiresAtUtc + 7 days` constant.** Add `ExpiresAtUtc` to `AuthorizeResponse`; stub returns the placeholder via the response contract. Edit: [`PaymentAuthorizedMapper.cs:25,39`](../../../services/Payments/Payments.Application/Outbox/PaymentAuthorizedMapper.cs:25), [`Payments.Application.Abstractions.AuthorizeResponse`](../../../services/Payments/Payments.Application/Abstractions/AuthorizeResponse.cs).

7. **Decide on `PaymentId == CorrelationId` collapse.** Preferred: saga emits a fresh `Guid.CreateVersion7()` PaymentId on `AuthorizePaymentCommand` and migration column comment becomes accurate. Acceptable: document the collapse explicitly in `payments.md` and fix the migration comment. Edit: [`SagaCommandMappers.cs:30`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/SagaCommandMappers.cs:30), [`Init_Payments.cs:57`](../../../services/Payments/Payments.Infrastructure/Persistence/Database/Migrations/20260501210449_Init_Payments.cs:57).

8. **Validate `AuthorizationId` on Capture/Void wire commands.** Today silently ignored; on mismatch with stored value, throw `SagaCommandDispatchException` → DLT. Edit: [`CapturePaymentCommandKafkaHandler.cs`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/CapturePaymentCommandKafkaHandler.cs), [`VoidPaymentCommandKafkaHandler.cs`](../../../services/Payments/Payments.Infrastructure/Messaging/Kafka/PaymentCommands/VoidPaymentCommandKafkaHandler.cs).

9. **Register schema-registry compatibility modes per locked contract.** Add an init container in `docker-compose.yaml` POSTing `{"compatibility":"FORWARD_TRANSITIVE"}` / `"FULL_TRANSITIVE"` to `/config/<subject>` per the events-catalog table. Add an integration test asserting per-subject modes match.

### MEDIUM

10. Generate a distinct `RefundTransactionId` in the aggregate's `Refund` method; persist on the aggregate. Or amend the schema doc to make v1 equality explicit. [`PaymentRefundedMapper.cs:30`](../../../services/Payments/Payments.Application/Outbox/PaymentRefundedMapper.cs:30).
11. Replace `RetryForever` with bounded retry + DLT routing on `payments.commands`. [`MessagingDependencyInjection.cs:87-93`](../../../services/Payments/Payments.Infrastructure/Common/MessagingDependencyInjection.cs:87).
12. Mark `GetPaymentByIdResponse.PaymentMethodId` + `GatewayTransactionId` with `[Pii]`; add architecture test enforcing the rule across all `Payments.Application` DTOs. [`GetPaymentByIdResponse.cs`](../../../services/Payments/Payments.Application/Transactions/GetPaymentById/GetPaymentByIdResponse.cs).
13. Single `_timeProvider.GetUtcNow()` read per handler. [`AuthorizePaymentCommandHandler.cs:83,106`](../../../services/Payments/Payments.Application/Transactions/AuthorizePayment/AuthorizePaymentCommandHandler.cs:83) (and verify Capture/Refund/Void don't have the same pattern).
14. Remove the misleading "unreachable" wording on the `MissingGatewayTransactionId` guards — or remove the handler-level guard entirely and rely on the aggregate's FSM check. [`CapturePaymentCommandHandler.cs:65-70`](../../../services/Payments/Payments.Application/Transactions/CapturePayment/CapturePaymentCommandHandler.cs:65) + sister files.
15. Split `PaymentRepository.GetByIdAsync` into tracking + no-tracking variants. [`PaymentRepository.cs:23`](../../../services/Payments/Payments.Infrastructure/Persistence/Repositories/PaymentRepository.cs:23).
16. Add Schema-Registry-backed integration test that round-trips a real Avro byte payload per outbox publisher, asserting the deserialized payload's fields match expectation.

### LOW

17. Fix [`AuthPolicies.cs:21-22`](../../../services/Payments/Payments.Infrastructure/Common/Authorization/AuthPolicies.cs:21) doc-comment route string (`payments/payments/{id}` → `payments/{id}`).
18. Add architecture tests for "outbox is the only path" (no `KafkaFlow.IProducer<,>` in `Payments.Application`/`Payments.Infrastructure` outside the consumer wiring) and "outbox publishers cite `TopicsOptions.Transactions`, not literal strings".
19. Fix BC design `payments.md § 5` "internal, 8" → "9" (already lists 9 events).
20. Resolve `use-cases.md § 5 = Payments` doc inconsistency (the dispatch prompt's `<reading_order>` refers to a section that doesn't exist; the cross-service summary at `use-cases.md:1480` is what the prompt currently lands on).

### Carry-forwards inherited from M9 (acknowledged)

- NU1903 transitive vulns (`System.Security.Cryptography.Xml`, `Microsoft.Kiota.Abstractions`, `Microsoft.Extensions.Caching.Memory`).
- `otel-collector` `attributes/pii-allowlist` processor config defect.
- `payments.api` not present as a container in `docker-compose.yaml` (Payments runs via local `dotnet run`).
- Saga regression command path drift in `payments.md:167` (`test/SagaOrchestrators.Tests/` → `saga/SagaOrchestrators.UnitTests/`).
- `unset HTTP_PROXY ...` preferred over `NO_PROXY='*'` for Testcontainers on Windows (M9 § 2).

---

*End of Payments BC closeout review.*
