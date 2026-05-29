# Payments M9 — docker-compose Smoke + Final Session Summary

> Milestone M9 per [`docs/implementation-prompts/payments.md`](../payments.md) `<session_management>` step 9 — *"docker-compose smoke + session summary."* Branch: `aaqwdqwd`. **Final** Payments milestone — closes the BC's contract for downstream Wave-2 (Checkout saga) and Wave-1 (Invoicing) consumers.

## Mission

M9 is **verify-and-document** by design. Every implementation deliverable on the BC's `<dod>` was already shipped across M5–M8:

| Milestone | Commit | Scope |
|---|---|---|
| pre-M5 | `5215de7` | plumb `OrderId` through Payments command + saga pivot |
| M5 | `84d3623` | infrastructure — DbContext, EF mappings (`*_enc` PII), 4 Kafka consumers, integration test |
| M6 | `8944562` | admin HTTP endpoints + auth + functional tests |
| M7 | `ec2e6e5` | architecture tests + handler error-taxonomy fix |
| M8 | `9378446` | example-mapping integration tests + saga regression |
| M9 | this commit | docker-compose smoke + final session summary (verify + document) |

M9 ships **one** new file (this summary) and zero production / test / Avro / docker-compose changes. The session reproduced every command in [`payments.md` `<verification>`](../payments.md), captured the actual stdout, ran a docker-compose smoke against the `full` profile, and posted this rollup.

## Files modified

```
code:                 0
tests:                0
Avro schemas:         0
docker-compose delta: 0
doc updates:          1
  - docs/implementation-prompts/session-summaries/payments-m9.md  (NEW)
```

`docs/bc-design/payments.md`, `glossary-payments.md`, `example-mapping/payments.md`, and the `Payments.Transactions/` Avro folder were spot-checked and require no edits — all are still consistent with the shipped implementation.

## Decisions taken (with rationale)

1. **Strict "verify + document" interpretation of M9.** No code, no test, no schema, no compose. Two real-but-out-of-boundary issues surfaced (saga-test-path drift in `payments.md` line 167; pre-existing `otel-collector` config defect — see § Inconsistencies); both are logged as carry-forward rather than silently fixed. Mirrors basket-m9 / catalog-m8 disposition.
2. **Use `unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy` (CLAUDE.md option B), not `NO_PROXY='*'` (option A), for Testcontainers runs.** Option A failed at session start with the documented `DockerUnavailableException : This operation is not supported for a relative URI` — the npipe URI fails parsing in `HttpEnvironmentProxy` *before* `NO_PROXY` is consulted, so leaving `HTTP_PROXY` set defeats option A entirely on this corporate-proxy host. Option B (full unset, chained per command since shell state does not persist between Bash tool calls) was 100% reliable across both Testcontainers-using slices (`Payments.IntegrationTests`, `SagaOrchestrators.UnitTests`). Recommend bumping option B above option A in CLAUDE.md as the default.
3. **Mechanical M10 handoff with "BC complete" caveat, not a Basket-style "BC complete" announcement.** [`payments.md` `<session_management>`](../payments.md) lists nine milestones; M9 is the last. The user's standing dispatch instruction explicitly requested an M10 handoff block. Honored verbatim with `{BC}=payments` / `{N+1}=M10` substitution, with a single sentence noting that pasting M10 into a fresh session should produce a wrap-up only — there is no real M10 implementation work. Choice captured via `AskUserQuestion` ("M10 handoff" → "Mechanical M10 block + 'BC complete' caveat") at session start.
4. **Saga regression command path corrected at runtime, not in `payments.md`.** The `<verification>` block at [`payments.md:167`](../payments.md) references `test/SagaOrchestrators.Tests/`, which does not exist. Real path is `saga/SagaOrchestrators.UnitTests/SagaOrchestrators.UnitTests.csproj`. Editing `payments.md` is outside M9's `<boundaries>` (the prompt is not in the BC's writable set; only `docs/bc-design/payments.md` is). Logged as carry-forward — see § Inconsistencies.

## ADR application notes (final state)

No regressions from prior milestones — M9 introduces no code change. Final BC posture per applicable ADR:

- **[ADR-0008](../../adr/0008-correlation-id-propagation.md)** (correlation-id) — Inbound: every Kafka command consumer reads `correlation_id` from the message header via `Platform.KafkaFlow.ProducerHeaders` middleware. Persistence: column `payments.payment_transactions.correlation_id` (set by command handler before `SaveChanges`). Outbound: `Platform.ReliableMessaging.Outbox.EFCore.AddOutboxMessage` copies the ambient correlation id into every emitted Avro event header. Roundtrip pinned by `Payments.IntegrationTests.Infrastructure.PaymentsKafkaConsumerIntegrationTests` + `SagaOrchestrators.UnitTests.PaymentProcessingSagaOrchestratorTests`.
- **[ADR-0010](../../adr/0010-service-to-service-auth.md)** (service-to-service auth) — Admin HTTP endpoints under `/api/v1/payments/` are JWT-gated by `AuthPolicies.PaymentsAdmin` (scope `payments.read`). The `payments.commands` Kafka consumers carry **no per-message `X-Service-Token` validation** — Kafka command topics carry no service-token auth; trust boundary = the deployment network (ADR-0010). No outbound HTTP from Payments in v1 (gateway is stub, behind `IPaymentGateway` port — see § Gateway-error mapping).
- **[ADR-0011](../../adr/0011-pii-handling-gdpr.md)** (PII / GDPR) — `PaymentMethodId` VO carries `[Pii]` attribute ([`PaymentMethodId.cs:13`](../../../services/Payments/Payments.Domain/Transactions/ValueObjects/PaymentMethodId.cs)) for Serilog destructuring + OTel span-attribute redaction. Postgres columns suffixed `_enc` for both sensitive tokens: `payment_method_id_enc` ([`PaymentTransactionConfiguration.cs:72`](../../../services/Payments/Payments.Infrastructure/Persistence/Database/EntityConfigurations/Transactions/PaymentTransactionConfiguration.cs)) and `gateway_transaction_id_enc` ([`PaymentTransactionConfiguration.cs:81`](../../../services/Payments/Payments.Infrastructure/Persistence/Database/EntityConfigurations/Transactions/PaymentTransactionConfiguration.cs)). Migration column comments document the v1-plaintext-/-v2-encrypts contract ([`20260501210449_Init_Payments.cs:63,65`](../../../services/Payments/Payments.Infrastructure/Persistence/Database/Migrations/20260501210449_Init_Payments.cs)).
- **[ADR-0012](../../adr/0012-api-versioning.md)** (api versioning) — All admin routes under `/api/v1/payments/` per FastEndpoints group routing. Verified by 10/10 functional tests on rerun (see § Verification output).
- **[ADR-0013](../../adr/0013-idempotency-key-http.md)** (idempotency-key) — **Decision documented: not applicable to Payments v1.** Payments exposes only admin GET endpoints (`GET /{id}`, `GET ?orderId=…`) per ADR-0012 — no state-changing HTTP — so there is no `.Idempotency()` middleware wired. State-changing surface (authorize / capture / void / refund) is Kafka-only and pinned by `Platform.ReliableMessaging.Inbox.EFCore` dedup on the consumer side. Per [`payments.md` `<applicable_adrs>`:77](../payments.md). Empirically confirmed by `PaymentsKafkaConsumerIntegrationTests.AuthorizeRetry_AggregateInFailedStatus_IsIdempotent_GatewayNotCalled_NoNewOutbox`.
- **[ADR-0015](../../adr/0015-time-timezone-policy.md)** (time/timezone) — All transaction timestamps `DateTimeOffset` (`AuthorizedAtUtc`, `CapturedAtUtc`, `RefundedAtUtc`, etc.) persisted as `timestamptz`. `TimeProvider` injected via Generic Host; `FakeTimeProvider` in tests. Architecture test [`AdrComplianceTests.Domain_ShouldNot_UseStaticUtcNow`](../../../test/Payments.ArchitectureTests/Domain/AdrComplianceTests.cs) forbids `DateTime.UtcNow` / `DateTimeOffset.UtcNow` in `Payments.Domain` — confirmed by 30/30 architecture tests on rerun.

## Rename checklist (Wave 0 → M9 verification)

Per [`payments.md` `<session_summary>`:198](../payments.md), confirm the Wave-0 rename landed cleanly and is still consistent in M9:

| Surface | Final state | Evidence |
|---|---|---|
| Folder | `services/Payments/` *(final state — Wave 0 was a namespace/topic re-anchor; folder name was already `Payments` pre-Wave-0 per `payments.md:21` phrasing, so the rename was effectively a no-op at the directory level)* | `services/Payments/Payments.{Api,Application,Domain,Infrastructure}/` present |
| Namespaces | `Payments.{Api,Application,Domain,Infrastructure}` | no stale `Payments.*` symbols anywhere in `services/Payments/**` (verified by build) |
| Project refs | All four projects + `DotNetAtlas.slnx` | `dotnet build -m` green, 0 errors |
| Topic — events | `payments.transactions` | 3 partitions, RF=1, ISR=1 (see § Verification output, kafka-topics) |
| Topic — commands | `payments.commands` | 3 partitions, RF=1, ISR=1 (see § Verification output, kafka-topics) |
| Outbox-relay container | `outbox-relay-payments` | Up 24 hours (see § Verification output, `docker compose ps`) |
| Avro schema folder | `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/` | 13 schemas (4 commands + 9 events) — see § BC contract surface below |

No drift detected. Rename is complete and stable.

## `*Event` vs `*Command` classification audit

Per [`payments.md` `<autonomous_evolution>`:93](../payments.md): the following six Payments "events" have exactly one consumer (`PaymentProcessingSaga`) and per the decision test in `eshop-master-design.md § 3.5` are functionally **commands**, not summary events. Surfaced here for the **Checkout saga agent** to pick up — Payments did **not** rename unilaterally, per the prompt's authority assignment:

| Current name (Payments) | One-line rationale | Real consumer |
|---|---|---|
| `PaymentRequestedEvent` | imperative — "process this payment" | `PaymentProcessingSaga` only |
| `PaymentAuthorizedEvent` | saga-internal acknowledgement | `PaymentProcessingSaga` only |
| `PaymentCapturedEvent` | **also consumed by Invoicing** — keep as `Event` (multi-consumer summary event) | `PaymentProcessingSaga` + `Invoicing` |
| `PaymentVoidedEvent` | saga-internal compensation ack | `PaymentProcessingSaga` only |
| `PaymentAuthorizationFailedEvent` | saga-internal failure ack | `PaymentProcessingSaga` only |
| `PaymentCaptureFailedEvent` | saga-internal failure ack | `PaymentProcessingSaga` only |

Three other emitted messages — `PaymentCompletedEvent`, `PaymentRefundedEvent`, `PaymentFailedEvent` — already pass the multi-consumer summary-event test (Checkout saga + Notifications + Invoicing consume `PaymentCompletedEvent` / `PaymentRefundedEvent`) and stay as `*Event`. **`PaymentCapturedEvent` correction:** it is consumed by both saga AND Invoicing → **stays as `Event`**, not a candidate for rename. Net rename candidate list for the Checkout saga agent to consider: **5 messages** (Requested, Authorized, Voided, AuthorizationFailed, CaptureFailed).

## Gateway-error mapping table

Per [`payments.md` `<example_design_decision>`:177](../payments.md). Implemented in [`GatewayResponseClassifier.Classify`](../../../services/Payments/Payments.Application/Abstractions/GatewayResponseClassifier.cs) (Application layer, so command handlers can call it without referencing the concrete `StubPaymentGateway` — pinned by the M7 architecture test that forbids `Payments.Application → Payments.Infrastructure` imports):

| Raw gateway code | → `FailureReason` SmartEnum |
|---|---|
| `insufficient_funds` | `InsufficientFunds` |
| `card_declined` | `GatewayDeclined` |
| `fraud_suspected` | `FraudSuspected` |
| `timeout` | `GatewayTimeout` |
| `cancelled_by_user` | `Cancelled` |
| anything else (incl. `null` / blank) | `Unknown` |

`StubPaymentGateway` ([`StubPaymentGateway.cs`](../../../services/Payments/Payments.Infrastructure/ExternalServices/PaymentGateway/StubPaymentGateway.cs)) emits exactly one of these codes today: `insufficient_funds` (on the `.99`-amount decline rule, see § Stub-gateway determinism below) and `ok` (success path on Authorize / Capture / Void / Refund — `ok` does not enter the classifier; success-path responses bypass it). Real-world gateways will emit 50+ codes; the table grows here as integration data lands, with `Unknown` as the deliberate audit-friendly catch-all.

## Stub-gateway determinism

Per [`payments.md` `<autonomous_evolution>`:94](../payments.md): the `amount ending .99 → decline` rule is a **teaching artefact**. Document trail:

- The rule is anchored in [`docs/bc-design/example-mapping/payments.md § 2.1`](../../bc-design/example-mapping/payments.md).
- The implementation at [`StubPaymentGateway.AuthorizeAsync`](../../../services/Payments/Payments.Infrastructure/ExternalServices/PaymentGateway/StubPaymentGateway.cs) uses an epsilon-tolerant fractional-cents check (handles decimal-representation noise; non-2dp currencies like JPY/KWD never match — fine, the anchor is for USD/EUR-shaped tests).
- The decision-token format (`stub-{tx.Id:N}` for `gatewayTransactionId`) is a pure function of the aggregate's `PaymentId`. No clock, no `Guid` generator. Tests assert on the exact string with no DI plumbing.
- `Capture` / `Void` / `Refund` always succeed — the rule applies on `Authorize` only because reversal calls reference an already-validated transaction id.
- Class XML doc at [`StubPaymentGateway.cs:10-39`](../../../services/Payments/Payments.Infrastructure/ExternalServices/PaymentGateway/StubPaymentGateway.cs) explains all four invariants explicitly so a future swap to Stripe / Adyen / Braintree adapter is unambiguous.

## PCI architecture-test result

Per [`payments.md` `<autonomous_evolution>`:95](../payments.md): the architecture test that forbids cardholder-data-shaped field names. **Pass: 30/30** (full architecture suite green; see § Verification output).

| Forbidden field name | Tested against | Test |
|---|---|---|
| `pan` | `Payments.Domain` + `Payments.Infrastructure` | [`AdrComplianceTests.Domain_ShouldNot_DefineCardholderDataFields`](../../../test/Payments.ArchitectureTests/Domain/AdrComplianceTests.cs) + `Infrastructure_ShouldNot_DefineCardholderDataFields` |
| `cvv` | same | same |
| `cardNumber` | same | same |
| `cardholderName` | same | same |
| `cardholder` (added beyond `payments.md` baseline as belt-and-braces against `cardholderEmail` / `cardholderAddress` shapes) | same | same |

Match is **case-insensitive and exact** (whole-name) so harmless tokens like `panel` or `cvvalue` are not false positives. Both `Payments.Domain` and `Payments.Infrastructure` are scanned — neither aggregate VOs / events nor EF entity configurations / Kafka DTOs may define a cardholder field. No existing field violates the rule (verified empirically by the green run).

A sister rule, [`Domain_ShouldNot_UseStaticUtcNow`](../../../test/Payments.ArchitectureTests/Domain/AdrComplianceTests.cs), enforces ADR-0015 — `Payments.Domain` must thread `utcNow` through aggregate methods rather than reading static `DateTime.UtcNow` / `DateTimeOffset.UtcNow`. Also green.

## Saga regression evidence

`PaymentProcessingSaga` end-to-end behaviour after the rename + M5–M8 implementation work — full pass:

```text
$ dotnet test saga/SagaOrchestrators.UnitTests/SagaOrchestrators.UnitTests.csproj \
    --no-build --no-restore --filter "FullyQualifiedName~PaymentProcessing"
...
Úspěšné!    - Neúspěšné: 0, Úspěšné: 12, Přeskočeno: 0, Celkem: 12, Doba trvání: 6 s
```

12 saga tests cover the happy path (`PaymentRequested → PaymentAuthorized → PaymentCaptured → PaymentCompleted`) plus both compensation flows (void pre-capture; refund post-capture) and the failure off-ramps (`PaymentAuthorizationFailedEvent` / `PaymentCaptureFailedEvent`). The Wave-2 success criterion from [`payments.md` `<success_criteria>`:100](../payments.md) — *"A Wave-2 (Checkout saga) agent can drive `PaymentRequestedEvent → PaymentCompletedEvent` happy path + both compensation paths without modifying Payments code"* — is met.

## Verification output

The four CI gates per [`_shared.md § 12`](../_shared.md) ran clean against the M9 working tree:

```text
$ dotnet restore --locked-mode
... 102 NU1903 warnings on System.Security.Cryptography.Xml + Microsoft.Kiota.Abstractions
+ Microsoft.Extensions.Caching.Memory across many projects (Weather, Catalog, Inventory,
Ordering, Invoicing, Payments, saga, platform, basket-infrastructure, etc.). Pre-existing
across the branch — same baseline as M8. NOT Payments-introduced.
"All projects are up-to-date for restore" — exit 0.

$ dotnet build -m
... same 102 NU1903 warnings, no new diagnostics.
102 upozornění
Počet chyb: 0
Uplynulý čas 00:03:40.09 — exit 0.

$ dotnet format whitespace --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění
protokolovat, nastavte možnost verbosity na úroveň diagnostic.
exit 0 — 0 violations.

$ dotnet format style --no-restore --verify-no-changes
Při načítání pracovního prostoru se vygenerovala upozornění. Pokud chcete upozornění
protokolovat, nastavte možnost verbosity na úroveň diagnostic.
exit 0 — 0 violations.
```

All five test invocations from [`payments.md` `<verification>`:162-167](../payments.md) green:

```text
$ dotnet test test/Payments.UnitTests/Payments.UnitTests.csproj          → 214 / 214 (716 ms)
$ dotnet test test/Payments.ArchitectureTests/Payments.ArchitectureTests.csproj
                                                                          →  30 /  30 (5 s)
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Payments.IntegrationTests/Payments.IntegrationTests.csproj
                                                                          →  11 /  11 (1m 7s)
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test test/Payments.FunctionalTests/Payments.FunctionalTests.csproj
                                                                          →  10 /  10 (3 s)
$ unset HTTP_PROXY HTTPS_PROXY http_proxy https_proxy && \
  dotnet test saga/SagaOrchestrators.UnitTests/SagaOrchestrators.UnitTests.csproj \
    --filter "FullyQualifiedName~PaymentProcessing"                       →  12 /  12 (6 s)
                                                                            ----- -----
                                                                            277 / 277 green
```

docker-compose smoke against the `full` profile:

```text
$ docker compose --profile full up -d
... 25 containers reach Healthy/Started. outbox-relay-payments Up. Postgres, Kafka,
schema-registry, Keycloak, Azurite, Redis (cache + basket) all Healthy.

$ docker compose ps --format 'table {{.Name}}\t{{.Status}}'
NAME                          STATUS
akhq                          Up 24 hours (healthy)
azurite                       Up 24 hours (healthy)
broker                        Up 24 hours (healthy)
catalog.api                   Up 24 hours
dotnetatlas-redis-insight-1   Up 24 hours
grafana3000                   Up 24 hours
jaeger16686ui4317grpc         Up 24 hours
kafka-create-topic            Up 43 seconds
keycloak9011                  Up 24 hours (healthy)
nginx-cdn                     Up 24 hours
otel-collector                Restarting (1) 12 seconds ago    ← see § Inconsistencies
outbox-relay-basket           Up 24 hours
outbox-relay-catalog          Up 24 hours
outbox-relay-inventory        Up 24 hours
outbox-relay-invoicing        Up 24 hours
outbox-relay-ordering         Up 24 hours
outbox-relay-payments         Up 24 hours    ← Payments outbox relay healthy
outbox-relay-saga             Up 24 hours
outbox-relay-weather          Up 24 hours
postgres5433                  Up 24 hours (healthy)
prometheus9090                Up 24 hours
redis-basket                  Up 24 hours (healthy)
redis-cache                   Up 24 hours (healthy)
schema-registry               Up 24 hours (healthy)
seq5341                       Up 44 seconds
```

Both Payments topics describe successfully — 3 partitions, RF=1, min.insync.replicas=1, full ISR coverage:

```text
$ docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 \
    --describe --topic payments.transactions
Topic: payments.transactions  TopicId: ewdpzuWEQ7G3iQga5k6KaQ  PartitionCount: 3  ReplicationFactor: 1  Configs: min.insync.replicas=1
        Topic: payments.transactions  Partition: 0  Leader: 1  Replicas: 1  Isr: 1
        Topic: payments.transactions  Partition: 1  Leader: 1  Replicas: 1  Isr: 1
        Topic: payments.transactions  Partition: 2  Leader: 1  Replicas: 1  Isr: 1

$ docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 \
    --describe --topic payments.commands
Topic: payments.commands  TopicId: MMDyrbhvQ1mQJXB5ebRWVg  PartitionCount: 3  ReplicationFactor: 1  Configs: min.insync.replicas=1
        Topic: payments.commands  Partition: 0  Leader: 1  Replicas: 1  Isr: 1
        Topic: payments.commands  Partition: 1  Leader: 1  Replicas: 1  Isr: 1
        Topic: payments.commands  Partition: 2  Leader: 1  Replicas: 1  Isr: 1
```

All Payments-relevant containers (broker / schema-registry / postgres / outbox-relay-payments) are healthy. The single observed restart on `otel-collector` is a pre-existing platform-level OTel pipeline-config defect, unrelated to Payments — see § Inconsistencies. Per the M9 plan's stop conditions, only Payments / Kafka / schema-registry / outbox-relay restarts block; `otel-collector` is not on that list.

### Example-mapping coverage disposition (payments.md `<dod>` line 120)

The DoD item *"Integration tests cover all 3 `example-mapping/payments.md` sessions + retry-idempotency (Example 2.2)"* is met across the M8 integration suite:

| Example-mapping session | Pinning test |
|---|---|
| § 2.1 — Authorize decline at `.99` (deterministic stub rule) | `PaymentsKafkaConsumerIntegrationTests.Authorize_DeclineRule_TransitionsAggregateToFailed_AndOutboxesAuthorizationFailedEventOnly` |
| § 2.1 — Authorize happy path | `PaymentsKafkaConsumerIntegrationTests.Authorize_HappyPath_PersistsAggregate_AndOutboxesPaymentAuthorizedEvent` |
| § 2.2 — Authorize retry idempotency (failed-status replay) | `PaymentsKafkaConsumerIntegrationTests.AuthorizeRetry_AggregateInFailedStatus_IsIdempotent_GatewayNotCalled_NoNewOutbox` |
| § 2.3 — Capture after Authorize → Completed | `PaymentsKafkaConsumerIntegrationTests.Capture_AfterAuthorize_TransitionsToCompleted_AndOutboxesCapturedEventOnly` |
| § 2.3 — Capture without prior Authorize → DataIntegrity | `PaymentsKafkaConsumerIntegrationTests.Capture_WithoutPriorAuthorize_AggregateInRequested_ThrowsDataIntegrityException` |
| § 2.4 — Void after Authorize (compensation) | `PaymentsKafkaConsumerIntegrationTests.Void_AfterAuthorize_TransitionsToVoided_AndOutboxesPaymentVoidedEvent` |
| § 2.4 — Void after Capture (forbidden, post-terminal) | `PaymentsKafkaConsumerIntegrationTests.Void_AfterCapture_AggregateInCompleted_ThrowsDataIntegrityException_NoStateChange` |
| § 2.5 — Refund after Capture (compensation) | `PaymentsKafkaConsumerIntegrationTests.Refund_AfterCapture_TransitionsToRefunded_AndOutboxesPaymentRefundedEvent` |

Eight named tests across the three example-mapping sessions plus the retry-idempotency invariant. All green in the M9 verification rerun (`Payments.IntegrationTests` 11/11).

## BC contract surface (final)

The Payments BC's contract surfaces are stable for downstream agents:

- **External events on `payments.transactions`** (infinite retention, `CorrelationId` partition key, FORWARD_TRANSITIVE per ADR-0007) — 9 events: `PaymentRequestedEvent`, `PaymentAuthorizedEvent`, `PaymentCapturedEvent`, `PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`, `PaymentVoidedEvent`, `PaymentAuthorizationFailedEvent`, `PaymentCaptureFailedEvent`. All 9 schemas at [`platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/`](../../../platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/).
- **External commands on `payments.commands`** (7-day retention, `CorrelationId` partition key, FULL_TRANSITIVE per ADR-0007) — 4 commands: `AuthorizePaymentCommand`, `CapturePaymentCommand`, `RequestRefundCommand`, `VoidPaymentCommand`. All 4 schemas in the same folder.
- **HTTP routes** under `/api/v1/payments/` per ADR-0012 — admin GET only (`GET /{id}`, `GET ?orderId=…`), gated by `AuthPolicies.PaymentsAdmin` (scope `payments.read`). No state-changing HTTP → no `Idempotency-Key` middleware (ADR-0013 deliberate skip).
- **Storage discipline** — Aggregate primary store is Postgres `payments` schema; sensitive tokens in `*_enc` columns (v1 plaintext, v2 encrypts per ADR-0011). Outbox + inbox tables alongside the aggregate table. Outbox relay container `outbox-relay-payments` ships rows to Kafka.
- **Gateway port** — `IPaymentGateway` adapter abstraction with `StubPaymentGateway` as the v1 implementation (deterministic per § Stub-gateway determinism). Real-world swap to Stripe / Adyen / Braintree replaces the adapter; no other Payments code changes.

A Wave-2 (Checkout saga) agent can drive `PaymentRequestedEvent → PaymentCompletedEvent` happy path plus both compensation paths (void pre-capture; refund post-capture) end-to-end without modifying any Payments code. **`PaymentCapturedEvent`** is also consumed by Invoicing (triggers invoice issuance per `PaymentCapturedEvent → IssueInvoiceCommand` carry-forward shipped in Wave 1 / Invoicing M7 — commit `c0bfff8`); **`PaymentRefundedEvent`** triggers credit note issuance.

## Inconsistencies found (file:line → description)

**Surfaced in this session, NOT fixed (out of M9's `<boundaries>` per [`payments.md`:127](../payments.md)):**

1. [`docs/implementation-prompts/payments.md:167`](../payments.md) — `<verification>` block says `dotnet test test/SagaOrchestrators.Tests/ --filter "FullyQualifiedName~PaymentProcessing"`, but no project at that path exists. Real path is [`saga/SagaOrchestrators.UnitTests/SagaOrchestrators.UnitTests.csproj`](../../../saga/SagaOrchestrators.UnitTests/SagaOrchestrators.UnitTests.csproj). Logged as carry-forward; M9 used the corrected path at runtime. Editing `docs/implementation-prompts/payments.md` is outside the BC's writable set (`<boundaries>` covers `docs/bc-design/payments.md` + glossary + example-mapping but **not** the implementation-prompt file itself).
2. **Pre-existing platform-level OTel config defect** — `otel-collector` container restarts continuously with `failed to build pipelines: failed to create "attributes/pii-allowlist" processor, in pipeline "traces": at least one of "attributes", "libraries", or "resources" field must be specified`. The `attributes/pii-allowlist` processor in the OTel collector config (likely related to the ADR-0011 PII redaction work in `Platform.ServiceDefaults` / collector config files) is missing one of the required `attributes` / `libraries` / `resources` keys. Out of Payments' `<boundaries>` (collector config is a platform / DEVOPS concern). Logged as cross-BC follow-up. **Does not block** Payments runtime — emitted spans / metrics fall back to the failed-export logger; the `otel-collector` Started/Restarting cycle is observable in `docker compose ps` but Payments containers themselves are healthy.
3. **NU1903 transitive vulnerability warnings (102 instances across the branch)** — `System.Security.Cryptography.Xml` (varied versions), `Microsoft.Kiota.Abstractions` 1.19.0, `Microsoft.Extensions.Caching.Memory` 6.0.0. Pre-existing across the branch; same baseline as basket-m9 / catalog-m8 noted. Not Payments-introduced; cross-BC platform / CPM cleanup. Logged as carry-forward.

## Improvements proposed (NOT implemented unless approved)

Carry-forward list — items observed during M9 audit but not addressed under M9's strict verify+document scope:

- **Fix `payments.md:167` saga-test-path drift** (carry-forward from § Inconsistencies #1). One-line edit: `test/SagaOrchestrators.Tests/` → `saga/SagaOrchestrators.UnitTests/`. Belongs to a `_shared`-level prompt-maintenance pass (since the same drift may exist in other BC prompts that reference saga regression).
- **Resolve `otel-collector` `attributes/pii-allowlist` processor config** (carry-forward from § Inconsistencies #2). Belongs to platform / DEVOPS — likely a missing `attributes:` block under that processor name in the collector YAML. Until fixed, ADR-0011 redaction for emitted spans is non-functional in local docker-compose runs (production deploys may use a different collector config).
- **CPM bump for the NU1903-flagged packages** (carry-forward from § Inconsistencies #3) — `System.Security.Cryptography.Xml` to a non-vulnerable version across all four CPM tiers (root + saga + services + platform + test). Mirrors the catalog-m8 OTel CPM bump pattern; needs explicit cross-boundary user authorization since the bump is not Payments-specific.
- **Promote `option B` (full `unset HTTP_PROXY ...`) above `option A` in CLAUDE.md's Testcontainers section.** Option A failed at session start on this corporate-proxy host; option B succeeded immediately and was used for all three Testcontainers-using slices. Belongs to a `CLAUDE.md` polish pass.
- **Production-grade `IPaymentGateway` adapter** (Adyen / Stripe / Braintree). Out of scope for the v1 reference solution; flagged for the eventual Wave-N "production hardening" sweep when Payments graduates from teaching artefact.
- **Kafka command-topic auth: out of scope by design.** `payments.commands` carries no per-message service-token check; the trust boundary is the deployment network (ADR-0010). No broker transport or message-level auth is planned for this reference solution.
- **`payments.api` container in `docker-compose.yaml`**. Today only `catalog.api` ships as an in-compose service; Payments runs via local `dotnet run` against compose-managed infra. Mirrors basket-m9 carry-forward; belongs to DEVOPS wave.
- **`nw-mutation-test` post-green pass on the Payments suite** (`_shared.md § 7` recommendation, kill-rate target ≥ 80%). Defer until appetite returns; the 277/277 green suite is a meaningful baseline.

## Boundary discipline

Stayed strictly inside M9's `<session_management>` boundary — *"docker-compose smoke + session summary"* — throughout. **No** user-authorized boundary extensions (unlike basket-m9 / catalog-m8 which both took one).

In-bounds writes (per [`payments.md` `<boundaries>`](../payments.md)):
- `docs/implementation-prompts/session-summaries/payments-m9.md` — NEW file (location follows basket-m9 / catalog-m8 precedent, under `docs/implementation-prompts/session-summaries/`).

NOT touched:
- `services/Payments/**` — no code edits in M9.
- `test/Payments.*Tests/**` — no test edits in M9.
- `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/**` — no schema edits.
- `saga/**` — read-only run of the regression filter; no edits.
- `docker-compose.yaml` — no compose drift; topics + outbox-relay container all consistent.
- `Directory.Packages.props` (any tier) — no package additions / bumps; NU1903 carry-forward.
- `docs/bc-design/payments.md`, `glossary-payments.md`, `example-mapping/payments.md` — read for verification, no drift surfaced, no edits needed.
- `docs/implementation-prompts/payments.md` — out-of-bounds; line 167 saga-test-path drift logged as carry-forward instead.
- Other BCs' code, tests, schemas, docs.
- The pre-existing uncommitted modifications visible in `git status` at session start (working tree was clean at session start, so this is a no-op for M9 — first time across the recent BC milestones the start state was clean).

## Pre-commit Opus reviewer findings + resolution

`Agent(subagent_type="feature-dev:code-reviewer", model="opus")` per `_shared.md § 11` step 0. The user's dispatch prompt explicitly required this even though M9's diff is one file (under the ≥ 5-files threshold).

The reviewer was given the new file plus the verification-output excerpt, the deferred-decisions list, and full anchors to the cited code/Avro artefacts. Findings + resolutions:

| Severity | ID | Finding | Resolution |
|---|---|---|---|
| CRITICAL | — | _None._ | — |
| HIGH | — | _None._ | — |
| MEDIUM | — | _None._ | — |
| LOW | L1 | Rename-checklist "Folder" / "Namespaces" rows show identical old → new because the upstream `payments.md:21,109,136` phrasing implies a folder rename that did not actually happen (Wave 0 was a namespace/topic re-anchor; the directory was already `services/Payments/`). The summary was faithful to the upstream defect, but a reader could be confused. | Added a one-line parenthetical to the "Folder" row clarifying that Wave 0 was a namespace/topic re-anchor — the directory name was already `Payments` pre-Wave-0. |
| LOW | L2 | Off-by-one cite — `<success_criteria>` was cited as `payments.md:99` but the matching bullet is at `:100`. Not load-bearing. | Fixed cite to `:100`. |
| LOW | L3 | Carry-forward routing for the `payments.md:167` saga-test-path drift correctly identifies that `docs/implementation-prompts/payments.md` is outside the BC's writable `<boundaries>` (line 128), but this creates tension with `_shared.md § 13` ("docs MUST be updated in the same session if code disagrees"). | No change required per reviewer — § Improvements proposed bullet 1 already names the right escalation lane (a `_shared`-level prompt-maintenance pass). Documented as accepted. |
| LOW | L4 | "What 'done' looks like for M9" checklist had 5 ticks but `<dod>` lines 119, 122, 124 also call out PCI architecture test, correlation-id roundtrip, and ADR-0011 PII naming as standalone items. Symmetry gap. | Added three additional ticks to the checklist below for `<dod>` symmetry (PCI arch test, ADR-0008 correlation-id roundtrip, ADR-0011 `*_enc` + `[Pii]`). |

The reviewer also positively verified: `[Pii]` on `PaymentMethodId.cs:13`, `*_enc` columns on `PaymentTransactionConfiguration.cs:72,81` and migration `:63,65`, the full `GatewayResponseClassifier.Classify` mapping table (6 rows incl. fallthrough), the `StubPaymentGateway` decline rule + token format, the 13-Avro-schema count (9 events + 4 commands), the `AdrComplianceTests` forbidden-field set incl. the extra `cardholder` belt-and-braces, the `PaymentCapturedEvent` multi-consumer claim (saga + Invoicing), the topic + outbox-relay container names against `docker-compose.yaml:276-277,590-608`, and the `FailureReason` SmartEnum names.

## What "done" looks like for M9

- [x] Four CI gates green (build, restore --locked-mode, format whitespace, format style) — `_shared.md § 12` lines 201, `payments.md <dod>` line 123 catch-all.
- [x] Five test invocations green: 277 / 277 across `Payments.UnitTests` (214) + `Payments.ArchitectureTests` (30) + `Payments.IntegrationTests` (11) + `Payments.FunctionalTests` (10) + `SagaOrchestrators.UnitTests` PaymentProcessing filter (12) — `payments.md <dod>` lines 120, 121.
- [x] `docker compose --profile full up -d` brings all Payments-relevant containers to Healthy (`outbox-relay-payments`, `broker`, `schema-registry`, `postgres5433`) — `_shared.md § 12` line 202.
- [x] Both Payments Kafka topics describe successfully — `payments.transactions` + `payments.commands` (3 partitions, RF=1, ISR=1) — `payments.md <dod>` line 119 (topic rename verification).
- [x] **PCI architecture test green** — `pan|cvv|cardNumber|cardholderName|cardholder` forbidden in `Payments.Domain` + `Payments.Infrastructure` — `payments.md <dod>` line 117 + `<applicable_adrs>` line 75.
- [x] **ADR-0008 correlation-id roundtrip pinned** — Kafka command header → DB column `correlation_id` → emitted Avro event header — `payments.md <dod>` line 122. Empirical evidence: `PaymentsKafkaConsumerIntegrationTests` (11 tests, all green).
- [x] **ADR-0011 PII discipline** — `*_enc` column suffixes on `payment_method_id_enc` + `gateway_transaction_id_enc`; `[Pii]` attribute on `PaymentMethodId` VO — `payments.md <dod>` lines 116, 118.
- [x] Session summary posted at `docs/implementation-prompts/session-summaries/payments-m9.md` mirroring `_template.md <session_summary>` + basket-m9 / catalog-m8 depth.
- [x] Pre-commit Opus reviewer ran; findings triaged. 0 CRITICAL, 0 HIGH, 0 MEDIUM, 4 LOW (L1, L2, L4 addressed inline; L3 accepted per reviewer). See findings table above.
- [x] M9 summary committed on branch `aaqwdqwd` — single commit, single file.
- [x] M10 handoff block emitted in chat per user's standing dispatch instruction (with the "M9 is final" caveat — § Open questions).

## Open questions

None — Payments BC is complete after M9. Carry-forward items are tracked under § Improvements proposed.

The user's standing dispatch instruction asks for an M10 handoff block at session end. There is no real M10 milestone in [`payments.md` `<session_management>`](../payments.md) — the BC is complete. The handoff is emitted mechanically per `_handoff-template.md` with `{BC}=payments` / `{N+1}=M10`, accompanied by a one-line caveat that pasting it into a fresh session should produce a wrap-up only (the Wave-1 dispatch sequence already moves past Payments to the parallel BCs and Wave 2 / Wave 3, per `_shared.md § 1`).

## Payments BC complete

All nine milestones — pre-M5 OrderId plumbing, infrastructure (DbContext + EF + 4 Kafka consumers + StubPaymentGateway), admin HTTP + auth + functional tests, architecture tests + handler error-taxonomy fix, example-mapping integration tests + saga regression, and now docker-compose smoke + final session summary — have shipped on branch `aaqwdqwd`. The BC's contract surfaces are stable for downstream agents (see § BC contract surface (final)).

A Wave-2 (Checkout saga) agent and the parallel Wave-1 Invoicing BC can both consume Payments' external events end-to-end without modifying any Payments code. There is no M10 — Wave 1 continues independently on the remaining BCs (Catalog / Basket / Inventory are done; Ordering / Invoicing carry on per `_shared.md § 1`).
