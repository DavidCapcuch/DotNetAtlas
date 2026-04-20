# Master System Prompt — Implement the **Checkout Saga**

> Paste this as the first message in a fresh Claude Code session for `C:\Users\david.capcuch\Desktop\Git\DotNetAtlas`.

<thinking_first>
Before writing any code, do these in your **first response** — explicitly, in order:

1. **Read every file under `<reading_order>`** in order. State your understanding of what's locked vs open.
2. **Verify prerequisites.** List anything in `<prerequisites>` that isn't satisfied. STOP and ask if any.
3. **Surface contradictions** (`file:line`).
4. **Confirm applicable ADRs.** For each ADR in `<applicable_adrs>`, name what it implies for THIS orchestrator's code.
5. **State your plan.** Group `<dod>` items into commit milestones. Confirm with the user before starting code.
6. **Acknowledge stop conditions** from this prompt and `_shared.md § 9`.
</thinking_first>

<mission>
You implement the **Checkout saga** in `saga/SagaOrchestrators/Checkout/`. This is NOT a bounded context — it's an **orchestrator** that drives the Checkout workflow across Basket → Ordering → Inventory → Payments (via `PaymentProcessingSaga`) → Notifications. Your output is a new MassTransit state machine registered alongside the existing `PaymentProcessingSaga`, fully wired with Kafka consumer adapters, EF Core persistence, timeout schedules, and compensation paths. When the session ends, `BasketCheckoutInitiatedEvent` triggers the saga end-to-end, the happy path reaches `Confirmed`, every compensation path reaches its correct terminal state, and `CompensationStuck` fires the documented ops alert.
</mission>

<prerequisites>
- **Wave 1 BCs scaffolded** (Catalog, Basket, Ordering, Inventory, Payments, Invoicing). At minimum their Avro schemas registered — the saga consumes events + publishes commands against all of them.
- Wave 0 platform prep merged. Specifically: `Platform.ServiceDefaults` has correlation-id + service-auth (crucial — saga publishes commands on multiple command topics); Keycloak `checkout-saga` service client with scopes `ordering.commands.*`, `inventory.commands.*`, `payments.commands.*`.
</prerequisites>

<role_in_system>
Per [ADR-0001](../adr/0001-centralized-saga-orchestration.md) (centralized saga service) and [ADR-0004](../adr/0004-checkout-saga-topology.md) (Checkout-specific topology with stock-BEFORE-payment), the Checkout saga is a **central orchestrator** that turns a `BasketCheckoutInitiatedEvent` into either a `Confirmed` order or a fully-compensated rollback. Teaching purpose: **multi-step saga with exhaustive compensation** (11 states incl. `CompensationStuck` abnormal terminal) + **sub-saga reuse** (`PaymentProcessingSaga` unchanged).
</role_in_system>

<contract>
LOCKED at the seams.

- MassTransit `MassTransitStateMachine<CheckoutSagaState>` — state class implements `SagaStateMachineInstance`
- EF Core repository, PostgreSQL `saga` schema, optimistic `RowVersion`
- Consumer group: **`saga-checkout`** (unique; don't collide with existing groups)
- `CorrelationId` = `BasketCheckoutInitiatedEvent.BasketCorrelationId` (UUID v7) — per ADR-0008 this is THE workflow correlation id threaded through every downstream command
- **11 states**, **18 transitions**, full compensation matrix per `checkout-saga.md § 3 + § 4 + § 6`
- Timeouts per `checkout-saga.md § 7` (defaults: Order 30s / Stock 60s / Payment 90s / Confirm 30s / Compensation 300s)
- Step ordering: **stock BEFORE payment** (ADR-0004)
- Fan-out: one `ReserveStockCommand` per distinct `ProductId` per `checkout-saga.md § 5`
- **No saga-terminal Kafka events** per `eshop-master-design.md § E.2` — observability via OTel activities + counters only
- **No new Avro schemas** — you only CONSUME events/commands already defined by Ordering/Inventory/Payments BCs
- File ownership: see `<boundaries>`
</contract>

<design_open>
You own these. Justify each in your session summary.

- State class property layout (every field named in `checkout-saga.md § 2` must be present; column types and JSON-vs-columns for complex fields — your call)
- Internal saga event records (one per consumer adapter; shape is yours)
- Consumer-adapter class organization (one per external event — mirror the existing `PaymentProcessingSaga` consumers)
- Timeout schedule classes (one per timeout — match existing `Schedules/` folder pattern)
- Fan-out tracking (ReservationId minting order — you mint them client-side so idempotency survives transport retries; confirm)
- Observability instrumentation density: activities per-transition, counter names (`checkout-saga.md § 11` gives names; you implement)
- Test-harness strategy: `SagaTestHarness<CheckoutSagaState>` coverage per transition + multi-item fan-out happy + one failure per compensation branch
- Additional `example-mapping/` entries if you discover missing saga transitions
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/checkout-saga.md` — **the full spec; every section matters**
3. `docs/adr/0001-centralized-saga-orchestration.md` + `0004-checkout-saga-topology.md`
4. `docs/bc-design/events-catalog.md` — rows for every event you consume or publish (Ordering + Inventory + Payments + Basket)
5. `docs/bc-design/saga-stuck-runbook.md` — your `CompensationStuck` state triggers this
6. `docs/bc-design/kafka-dlq-strategy.md`
7. `docs/eshop-master-design.md` § 3 + § 11.7 + § E.2 + § 11 (cross-cutting)
8. **All ADRs in `<applicable_adrs>` below**
9. **Existing sagas — READ ALL END-TO-END:**
   - `saga/SagaOrchestrators/Payments/PaymentProcessingSaga/` — your PRIMARY template + the sub-saga you delegate payment to
   - `saga/SagaOrchestrators/Common/SagaDependencyInjection.cs` — you EXTEND this
   - `saga/SagaOrchestrators/appsettings.json` — you ADD a `SagaOptions.Checkout` section
</reading_order>

<applicable_adrs>
Cross-cutting decisions to apply:

- [ADR-0008](../adr/0008-correlation-id-propagation.md) — saga state's `CorrelationId` is the workflow correlation id; every outbound command carries it in the Kafka header; every inbound response event is correlated by `CorrelationId` (not `OrderId`, which arrives only after `OrderCreated`)
- [ADR-0010](../adr/0010-service-to-service-auth.md) — **saga is the primary consumer of service-auth** — every command publish attaches an `X-Service-Token` Kafka header via `Platform.ServiceDefaults.AddServiceAuth("checkout-saga")`; the Keycloak `checkout-saga` client must hold scopes for every command topic the saga writes to
- [ADR-0011](../adr/0011-pii-handling-gdpr.md) — addresses transit through saga state (`BasketCheckoutInitiatedEvent` carries shipping/billing addresses); **persist the addresses ONLY for the duration the saga needs them** — on `Confirmed` or terminal-failure, null out the address columns in `saga_checkout_states` (don't keep PII beyond workflow lifetime); OTEL allowlist forbids tagging spans with address fields; `correlation.id` is allowlisted
- [ADR-0014](../adr/0014-feature-flags-openfeature.md) — **explicit consumer of `checkout.payment-then-stock` flag** — reads via `IFeatureClient` at the initial transition guard; default OFF (ADR-0004 locks stock-then-payment as v1 default); flag ON demonstrates the alternative topology without changing the ADR decision
- [ADR-0015](../adr/0015-time-timezone-policy.md) — saga state timestamps `DateTimeOffset`; MassTransit scheduler timeouts use `IClock.UtcNow` where possible (MassTransit's own scheduler uses `DateTime.UtcNow` internally — acceptable; your saga's own timestamp columns use `DateTimeOffset`)
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. Saga-specific:

| Phase | Skill | When |
|---|---|---|
| Before designing the state machine | `backend-development:saga-orchestration` | FIRST — MassTransit state-machine patterns, state/event/schedule/activity lifecycle |
| Workflow pattern depth | `backend-development:workflow-orchestration-patterns` | for compensation patterns + saga-within-saga + state-persistence strategies |
| Fan-out / multi-item design | `superpowers:brainstorming` | the fan-out algorithm has subtle race conditions; explore options before committing |
| When debugging state transitions | `superpowers:systematic-debugging` | saga debugging is ABOVE AVERAGE difficulty — use this skill more than in a BC |
</skills>

<autonomous_evolution>
Saga-specific triggers:

- **ReservationId minting location** — `checkout-saga.md § 5` says saga mints them; confirm. If Inventory mints them, trace the race where saga needs to know the ID to later release.
- **Payments event-named-commands redesign — YOU HAVE AUTHORITY.** Per `eshop-master-design.md § 3.5`, you are authorised to classify each of `PaymentRequestedEvent`, `PaymentAuthorizedEvent`, `PaymentCapturedEvent`, `PaymentVoidedEvent`, `PaymentAuthorizationFailedEvent`, `PaymentCaptureFailedEvent` and propose renames + topic moves (e.g., `PaymentRequestedEvent` → `RequestPaymentCommand` on `payments.commands`). **Workflow:**
  1. Read every consumer of each event (`saga/SagaOrchestrators/Payments/PaymentProcessingSaga/Consumers/` + any consumers that land in the new CheckoutSaga).
  2. Classify each per `eshop-master-design.md § 3.5` decision test (one-known-consumer + expected-feedback = command).
  3. Compile a proposal table in the session summary: `{currentName} → {proposedName}, {currentTopic} → {proposedTopic}, consumers: [...]`.
  4. Do NOT implement any renames until the user approves the proposal in-session. The rename spans `Platform.SchemaRegistry.Contracts/Avro/Payments/Transactions/*.avsc`, `PaymentProcessingSaga`, the CheckoutSaga, and any downstream — it's a coordinated change.
  5. Genuine multi-consumer events (`PaymentCompletedEvent`, `PaymentFailedEvent`, `PaymentRefundedEvent`) stay as events — don't propose renaming those.
- **Compensation path gap** — if there's a `(state, event)` combination in the transition table not explicitly handled, it's a silent bug. Validate exhaustively.
- **Saga timeout vs Inventory TTL** — sum of happy-path timeouts (30+60+90+30 = 210s) is well under Inventory TTL (900s). If you tune timeouts higher, the buffer may shrink — flag.
- **Observability for the runbook** — does `CompensationStuck` emit enough context for `saga-stuck-runbook.md § 3` investigation? If integration tests reveal a missing log field, add it.
- **Feature-flag verification** — confirm `checkout.payment-then-stock` flag reads cleanly via `IFeatureClient` and that flipping it in the JSON file actually changes the initial state-machine transition.
</autonomous_evolution>

<success_criteria>
- A Wave-3 (BFF) agent can call the full happy path (BFF `POST /api/v1/bff/checkout` → basket checkout → saga drives → order `Confirmed`) without modifying the saga.
- Every `(state, event)` combination in `checkout-saga.md § 3 transition table` is either implemented or explicitly marked "unreachable by construction".
- `CompensationStuck` fires the documented ops alert on compensation timeout; runbook-level telemetry (correlation_id, last_state, stuck_since_utc, failure_reason) is present in logs + spans.
- Multi-item fan-out works under partial failure — 1 of 3 stock reservations fails → the other 2 are released; order cancelled; saga terminal `Failed`.
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12` adapted (no `services/` folder — just `saga/SagaOrchestrators/Checkout/`):

- [ ] `CheckoutSagaOrchestrator` + `CheckoutSagaState` + internal saga events + consumer adapters + schedules + observability folder created
- [ ] Registered in `SagaDependencyInjection.cs` with EF repository + Kafka endpoints (consumer group `saga-checkout`)
- [ ] 11 states + 18 transitions implemented; transition table covers every `(state, event)` combination
- [ ] Fan-out handles partial failure correctly (one line fails → compensate all prior successes)
- [ ] Every compensation path reaches its terminal state; `CompensationStuck` counter increments on CompensationTimeout
- [ ] All 5 timeouts configured via MassTransit scheduler
- [ ] `SagaTestHarness<CheckoutSagaState>` tests: happy path + each compensation branch + multi-item fan-out + timeout firing
- [ ] Integration test (Testcontainers) runs end-to-end: `POST /api/v1/bff/checkout` (simulated) → saga reaches `Confirmed`
- [ ] Saga state persists + resumes across container restart (kill container mid-flow, verify state restored)
- [ ] Every outbound command carries correlation-id + service-auth token in Kafka headers
- [ ] Addresses nulled out in `saga_checkout_states` on terminal (Confirmed / Failed / Compensated / CompensationStuck) per ADR-0011 retention rule
- [ ] `checkout.payment-then-stock` flag read via `IFeatureClient`; OFF path (default) verified; ON path stubbed + marked experimental
- [ ] `SagaOptions.Checkout` configured in all 3 appsettings files
- [ ] `docker compose --profile full up -d` starts saga container; AKHQ shows `saga-checkout` consumer group; healthcheck passes
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:** `saga/SagaOrchestrators/Checkout/**` (new folder), `saga/SagaOrchestrators/Common/SagaDependencyInjection.cs` (extend), `saga/SagaOrchestrators/appsettings*.json` (add Checkout section), `test/SagaOrchestrators.Tests/**` (new test project), `docs/bc-design/checkout-saga.md` (self-correction only).

**Do not touch:** `services/*`, other saga folders (`Payments/PaymentProcessingSaga/` — you consume from it, never modify), `platform/*`, Weather, **any `.avsc` file** (schemas belong to the producing BC; if one's missing, STOP and ASK).
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- Any Wave-1 BC (Catalog / Basket / Ordering / Inventory / Payments / Invoicing) has not registered its Avro schemas — you can't consume what doesn't exist.
- The Keycloak `checkout-saga` client doesn't have the required scopes (Wave 0 may have missed them).
- `checkout-saga.md § 3 transition table` has a `(state, event)` cell marked "TBD" — escalate; saga cannot ship with holes.
- `PaymentProcessingSaga` regression tests fail after your DI changes.
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Suggested commit milestones:

1. Scaffold saga folder structure + DI registration + `dotnet build` green
2. `CheckoutSagaState` + internal saga events + Correlation strategy
3. Consumer adapters (one per external event — `BasketCheckoutInitiated`, `OrderCreated`, `StockReserved`, `StockReservationFailed`, `PaymentCompleted`, `PaymentFailed`, `PaymentRefunded`, `ReservationConfirmed`, `ReservationReleased`, `OrderConfirmed`)
4. State machine definitions (states + transitions + activities) + unit tests per transition
5. Schedule classes + timeout tests (`SagaTestHarness` with time-skip)
6. Fan-out tracking + multi-item integration test
7. Compensation branches + `CompensationStuck` path + observability emit
8. Feature-flag integration + flag-on/off tests
9. Integration test end-to-end with all Wave-1 BCs running (Testcontainers)
10. docker-compose smoke + session summary
</session_management>

<verification>
```bash
dotnet build -m
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/SagaOrchestrators.Tests/
docker compose --profile full up -d
# Manual end-to-end smoke:
# 1. POST /api/v1/basket/items (seed items)
# 2. POST /api/v1/basket/checkout (with Idempotency-Key header)
# 3. Watch: BasketCheckoutInitiated → CreateOrderCmd → OrderCreated → N × ReserveStockCmd → N × StockReserved → PaymentRequested → PaymentCompleted → ConfirmOrderCmd → OrderConfirmed → saga.checkout.confirmed++
# 4. Query saga_checkout_states — row with CurrentState='Confirmed'
# 5. Re-POST with same CorrelationId → dedup path (no duplicate execution)
# 6. Simulate Payments fail → compensation fires → saga reaches Failed or Compensated
# 7. Query saga_checkout_states on terminal — address columns nulled (PII retention per ADR-0011)
```

Paste actual output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
**Question:** Fan-out tracking — where is `ReservationId` minted (saga vs Inventory)?

**Bad answer:** "Saga mints them so compensation has a handle."

**Good answer:** "Saga mints `ReservationId` as UUID v7, client-side, one per `ReserveStockCommand`. Reasons: (1) idempotency across Kafka retries — if the saga retries the same command, Inventory's inbox dedup uses the MessageId (KafkaFlow) but the `ReservationId` is a business-level key that survives even full saga-restart scenarios; (2) compensation correlation — `CompensatingStockReservations → ReleaseReservationCommand` needs the ID; having the saga mint it avoids a roundtrip to read Inventory's generated ID; (3) per `checkout-saga.md § 5` the mint-client-side is the documented decision. Trade-off accepted: Inventory must accept a provided ID (factory signature) instead of generating one; architecture test in Inventory forbids Inventory-internal `ReservationId.CreateNew()` outside tests. Verified by `CheckoutSagaFanOutTests.PartialFailure_ReleasesSuccesses_UsingSagaMintedIds`."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`, plus saga-specifics:

- Every state + every transition (list any gaps)
- Fan-out design: who mints ReservationId, with what guarantees
- Timeout test coverage (which timeouts fired in which tests)
- Race conditions encountered (e.g., `PaymentRefunded` arriving while in `CompensationStuck`)
- Runbook verification: does `CompensationStuck` emit enough context for investigation?
- Payments event-named-commands proposal table (if you surfaced one for user review)
- ADR-0008 correlation roundtrip — evidence saga correlates across every event/command
- ADR-0010 service-auth — every outbound command carries a validated token; screenshot / excerpt of one verified flow
- ADR-0011 PII retention — addresses nulled on terminal; integration test verified
- ADR-0014 feature-flag — both flag states tested

Proceed.
</session_summary>
