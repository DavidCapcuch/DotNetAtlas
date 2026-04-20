# Master System Prompt — Implement the **Payments** Bounded Context

> Paste this as the first message in a fresh Claude Code session for `C:\Users\dcapc\Desktop\Git\DotNetAtlas`.

<thinking_first>
Before writing any code, do these in your **first response** — explicitly, in order:

1. **Read every file under `<reading_order>`** in order. State your understanding of what's locked vs open.
2. **Verify prerequisites.** List anything in `<prerequisites>` that isn't satisfied. STOP and ask if any.
3. **Surface contradictions** (`file:line`).
4. **Confirm applicable ADRs.** For each ADR in `<applicable_adrs>`, name what it implies for THIS BC's code.
5. **State your plan.** Group `<dod>` items into commit milestones. Confirm with the user before starting code.
6. **Acknowledge stop conditions** from this prompt and `_shared.md § 9`.
</thinking_first>

<mission>
You implement the **Payments** bounded context under `services/Payments/` (4-layer). This is **not greenfield** — it is a **rename + re-chapter** of the existing `services/Payments/` folder, with a few cleanups, plus the Kafka topics `payments.payments` → `payments.transactions` / `payments.payment-commands` → `payments.commands` (already renamed in Wave 0). When the session ends, the Checkout saga (via `PaymentProcessingSaga`) drives a payment from `Requested → Completed` through the authorize-capture flow, and compensation paths (void + refund) work.
</mission>

<prerequisites>
- Wave 0 platform prep merged. Specifically: `services/Payments/` renamed to `services/Payments/`; topics `payments.payments` → `payments.transactions` + `payments.payment-commands` → `payments.commands`; `outbox-relay-payments` container (renamed from existing relay); Keycloak `payments-service` client; `Platform.ServiceDefaults` has correlation-id + service-auth.
</prerequisites>

<role_in_system>
Payments is the **authority for money movement state** — it owns the `PaymentTransaction` aggregate and the only path to the external payment gateway (`IPaymentGateway` port). Commands in from `PaymentProcessingSaga`; terminal events out to Checkout saga, Notifications, and Invoicing. Teaching purpose: **saga sub-orchestration** + **PCI scope minimization via tokenization**.

Downstream consumers of `payments.transactions` events:
- Checkout saga (happy-path and compensation confirmation)
- Notifications (refund confirmation email)
- Invoicing (`PaymentCapturedEvent` → trigger invoice issuance; `PaymentRefundedEvent` → trigger credit note)
</role_in_system>

<contract>
LOCKED at the seams.

- Aggregate: `PaymentTransaction` (one aggregate, `PaymentId` UUID v7)
- `PaymentStatus` SmartEnum: `Requested → Authorized → Captured → Completed`; off-ramps `Failed`, `Voided`, `Refunded`
- Topics: `payments.transactions` (infinite, partition key `CorrelationId`) + `payments.commands` (7-day, `CorrelationId`)
- 9 external events + 4 commands per `events-catalog.md § 2` (under `Payments.Transactions` namespace)
- Schemas FORWARD_TRANSITIVE (events) + FULL_TRANSITIVE (commands) per [ADR-0007](../adr/0007-avro-compatibility-modes.md)
- HTTP (admin-only) routes under `/api/v1/payments/...` per ADR-0012 — two GET endpoints (lookup by id, lookup by orderId)
- `PaymentsErrors` per `error-taxonomy.md § 3.5` — implement to match
- File ownership: see `<boundaries>`
</contract>

<design_open>
You own these. Justify each in your session summary.

- `PaymentTransaction` aggregate's backing-field layout (EF mapping choices: owned entity for `FailureInfo`? value-converter for `FailureReason`?)
- `IPaymentGateway` adapter selection — implement `StubPaymentGateway` for v1 with deterministic responses per `example-mapping/payments.md § 2.1` (amount ending `.99` declines)
- Kafka consumer class organization — one consumer per command type (recommended) vs multiplexer (justify)
- `PaymentsErrors` factory shapes + authorization-policy name (`AuthPolicies.PaymentsAdmin`)
- Gateway response error taxonomy — mapping gateway codes to `FailureReason` SmartEnum values
- Additional `example-mapping/payments.md` sessions if edge cases surface (e.g., gateway timeout mid-authorize)
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/payments.md` — full BC spec
3. `docs/bc-design/glossary-payments.md` + `example-mapping/payments.md`
4. `docs/bc-design/events-catalog.md` § 2 (rows with topic `payments.transactions` / `payments.commands`)
5. `docs/bc-design/error-taxonomy.md § 3.5` (`PaymentsErrors` SSOT)
6. `docs/bc-design/use-cases.md § 5` (command handlers + admin queries)
7. `docs/bc-design/checkout-saga.md` § 5 + § 6 — understand the caller side (PaymentProcessingSaga)
8. `docs/adr/0001-centralized-saga-orchestration.md` + `0004` + `0007`
9. **All ADRs in `<applicable_adrs>` below**
10. **Existing code:** `services/Payments/**` (post-rename) — READ end-to-end. Much of the authorize/capture handler shape carries over from Payments.
</reading_order>

<applicable_adrs>
Cross-cutting decisions to apply:

- [ADR-0008](../adr/0008-correlation-id-propagation.md) — every command handler reads `CorrelationId` from the Kafka header; `payments.transactions.correlation_id` column persists it; outbox publisher copies it into emitted Avro events
- [ADR-0010](../adr/0010-service-to-service-auth.md) — inbound JWT validation for admin endpoints (scope `payments.read`); `payments.commands` consumer validates the `X-Service-Token` header from `PaymentProcessingSaga`; no outbound HTTP from Payments in v1 (gateway is stub, behind `IPaymentGateway`)
- [ADR-0011](../adr/0011-pii-handling-gdpr.md) — `PaymentMethodId` + `GatewayTransactionId` are sensitive; columns named `*_enc` per convention (v1 plaintext, v2 encrypts); **architecture test forbids PAN/CVV-like field names** (`pan`, `cvv`, `cardNumber`, `cardholderName` — any of these in `Payments.Domain` or `Payments.Infrastructure` fails the build); Serilog `[PII]` attribute on `PaymentMethodId` VO
- [ADR-0012](../adr/0012-api-versioning.md) — admin routes under `/api/v1/payments/...`
- [ADR-0013](../adr/0013-idempotency-key-http.md) — **NOT required on Payments HTTP endpoints** (they are admin GET only; no state-changing HTTP). Document the decision in the session summary.
- [ADR-0015](../adr/0015-time-timezone-policy.md) — all transaction timestamps `DateTimeOffset` (`AuthorizedAtUtc`, `CapturedAtUtc`, etc.); persist as `timestamptz`; inject `IClock`; arch test forbids `DateTime.UtcNow` in `Payments.Domain`
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. Payments-specific:

| Phase | Skill | When |
|---|---|---|
| Designing the gateway abstraction | `backend-development:api-design-principles` | `IPaymentGateway` port shape + retry/timeout semantics |
| Gateway-error taxonomy classification | `superpowers:brainstorming` | mapping real-world gateway response codes → `FailureReason` is non-obvious; explore before committing |
</skills>

<autonomous_evolution>
Payments-specific triggers:

- **`*Event` vs `*Command` naming debt** — per `eshop-master-design.md § 3.5`, several Payments "events" (`PaymentRequestedEvent`, `PaymentAuthorizedEvent`, `PaymentCapturedEvent`, `PaymentVoidedEvent`, `PaymentAuthorizationFailedEvent`, `PaymentCaptureFailedEvent`) have exactly one consumer (`PaymentProcessingSaga`) and per the decision test are actually commands. The **Checkout saga agent** has primary authority to propose renames; as the Payments agent, **surface the current list in your session summary** but do not rename unilaterally.
- **Stub-gateway determinism** — the stub rule (`amount ending .99 → decline`) is a test convenience. Document it clearly in `StubPaymentGateway.cs` with a comment explaining why. A real gateway replaces this adapter in production.
- **PCI architecture test** — author the arch test that forbids `pan|cvv|cardNumber|cardholderName` string-field names. This is a teaching artifact; flag it loudly if you find any existing Weather/Payments code that violates it.
- **If integration surfaces a missing case** (e.g., "what if gateway times out mid-authorize?"): add an example-mapping session before implementing.
</autonomous_evolution>

<success_criteria>
- A Wave-2 (Checkout saga) agent can drive `PaymentRequestedEvent → PaymentCompletedEvent` happy path + both compensation paths (void pre-capture; refund post-capture) without modifying Payments code.
- Invoicing receives `PaymentCapturedEvent` and produces an invoice (verified by integration test running Payments + Invoicing together).
- `StubPaymentGateway` is deterministic enough that integration tests don't flake (amount-last-digit rules documented).
- PCI architecture test passes — no cardholder-data-shaped fields anywhere in Payments.
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] `services/Payments/` present (renamed from `services/Payments/` in Wave 0); 4-layer projects + namespaces consistent
- [ ] 9 external Avro events + 4 command schemas under `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/`
- [ ] 9 internal `*DomainEvent` records + outbox publishers for each external event
- [ ] 4 Kafka consumers for commands (with inbox dedup via `Platform.ReliableMessaging.Inbox.EFCore` + service-auth token validation)
- [ ] `IPaymentGateway` port + `StubPaymentGateway` adapter with deterministic test rules
- [ ] Admin HTTP endpoints under `/api/v1/payments/` — `GET /{id}`, `GET ?orderId=…` + authorization policy
- [ ] `PaymentsErrors` implemented to match `error-taxonomy.md § 3.5`
- [ ] All timestamps `DateTimeOffset`; architecture test forbids `DateTime.UtcNow` in domain code
- [ ] Architecture test forbids PAN/CVV-like field names in domain layer
- [ ] `*_enc` column naming for `PaymentMethodId` / `GatewayTransactionId` (per ADR-0011 convention)
- [ ] Topic rename verified: `docker-compose.yaml` references `payments.transactions` + `payments.commands`
- [ ] Integration tests cover all 3 `example-mapping/payments.md` sessions + retry-idempotency (Example 2.2)
- [ ] `PaymentProcessingSaga` still functional end-to-end after rename (run existing saga tests)
- [ ] Correlation-id roundtrips: Kafka command header → DB column → emitted event header
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:** `services/Payments/**`, `test/Payments.*.Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Payments/**`, `docker-compose.yaml` (touch only if topic / relay drifted from Wave 0), `DotNetAtlas.slnx` (project path updates if not done in Wave 0), `Directory.Packages.props` (Payments-specific), `docs/bc-design/payments.md` + glossary + example-mapping (self-correction only), `saga/SagaOrchestrators/Payments/PaymentProcessingSaga/` (existing saga — you may update references but do not rewrite the state machine).

**Do not touch:** other BCs' services, other BCs' Avro schemas, Checkout saga (Wave 2), Invoicing (separate Wave-1 BC), Weather, platform libraries.
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- `services/Payments/` still exists (means Wave 0 rename was incomplete — escalate).
- Topic names still say `payments.payments` / `payments.payment-commands` in `docker-compose.yaml` (Wave 0 rename incomplete).
- The PCI architecture test finds existing `pan` / `cvv` string fields in Payments/Payments code (possible legacy — escalate; do not silently sanitize).
- The `PaymentProcessingSaga` regression tests fail after Payments changes (regression — fix before proceeding).
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Suggested commit milestones:

1. Scaffold confirms rename + project references; `dotnet build` green
2. Domain layer (`PaymentTransaction`, `PaymentStatus`, VOs, internal events) + unit tests
3. `IPaymentGateway` port + `StubPaymentGateway` adapter + deterministic rule tests
4. Application layer (command handlers, query handlers, outbox publishers) + integration test
5. Infrastructure layer (DbContext, EF mappings with `*_enc`, Kafka consumers × 4) + integration test
6. Admin HTTP endpoints + authorization + functional tests
7. Architecture tests (PCI no-cardholder, no `DateTime.UtcNow`, no direct gateway imports in Application)
8. Integration tests for `example-mapping` sessions + saga regression
9. docker-compose smoke + session summary
</session_management>

<verification>
```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Payments.UnitTests/
dotnet test test/Payments.ArchitectureTests/
dotnet test test/Payments.IntegrationTests/
dotnet test test/Payments.FunctionalTests/
# Saga regression — PaymentProcessingSaga should still work after rename
dotnet test test/SagaOrchestrators.Tests/ --filter "FullyQualifiedName~PaymentProcessing"
docker compose --profile full up -d
# Verify topics
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic payments.transactions
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic payments.commands
```

Paste actual output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
**Question:** Gateway-response error taxonomy — how do gateway response codes map to `FailureReason` SmartEnum values?

**Bad answer:** "Map `insufficient_funds` to `InsufficientFunds` and everything else to `Unknown`."

**Good answer:** "Explicit mapping table in `StubPaymentGateway.ClassifyResponse(code)`. Mapping: `insufficient_funds`→`InsufficientFunds`, `card_declined`→`GatewayDeclined`, `fraud_suspected`→`FraudSuspected`, `timeout`→`GatewayTimeout`, `cancelled_by_user`→`Cancelled`; anything else → `Unknown` + log `WARN` with full code so ops can grow the table. Reasons: (1) deterministic — each integration test asserts exact `FailureReason`; (2) auditable — the mapping is one place to change when real gateway codes reveal new categories; (3) telemetry — `payments.gateway.latency.seconds` metric tagged `reason` is meaningful only with a stable taxonomy. Trade-off accepted: real gateways have 50+ response codes; we map 5 now and grow as integration data lands. Verified by `PaymentFailureReasonMappingTests`."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`. Payments-specific notes:

- Rename checklist (folder, namespaces, project refs, topics, outbox-relay container name)
- `*Event` vs `*Command` classification audit — list any Payments messages that should be renamed (for Checkout saga agent to pick up)
- Gateway-error mapping table — which gateway codes map to which `FailureReason` values
- PCI architecture-test result — pass/fail with code examples of what's tested
- ADR-0013 idempotency decision documented (no HTTP state-changing endpoints → no `.Idempotency()` in v1)
- ADR-0011 PII — `*_enc` column naming applied; Serilog `[PII]` on `PaymentMethodId`
- Saga regression evidence (`PaymentProcessingSaga` tests still green)

Proceed.
</session_summary>
