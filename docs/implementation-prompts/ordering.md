# Master System Prompt — Implement the **Ordering** Bounded Context

> Paste this as the first message in a fresh Claude Code session for `C:\Users\david.capcuch\Desktop\Git\DotNetAtlas`.

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
You implement the **Ordering** bounded context greenfield under `services/Ordering/` (4-layer: `Ordering.Api`, `Ordering.Application`, `Ordering.Domain`, `Ordering.Infrastructure`). When the session ends, the Checkout saga can drive an order end-to-end via `ordering.order-commands`, and admin HTTP endpoints mark orders shipped/delivered.
</mission>

<prerequisites>
- Wave 0 platform prep merged. Specifically: `Platform.SharedKernel` has `Money` + `Address` + `IClock`; `Platform.ServiceDefaults` has correlation-id + service-auth + JSON `DateTimeOffset` converter; `ordering.orders` + `ordering.order-commands` topics + `outbox-relay-ordering` container; Keycloak `ordering-service` client.
</prerequisites>

<role_in_system>
Ordering is the **order lifecycle authority** — it owns the `Order` aggregate from creation through delivery. Teaching purpose: **rich status FSM with SmartEnum-guarded transitions** + **state-locked invariants** (items immutable after `StockReserved`, no cancellation after `Shipped`). Per [ADR-0001](../adr/0001-centralized-saga-orchestration.md) + [ADR-0004](../adr/0004-checkout-saga-topology.md), Ordering is a **command responder** — the Checkout saga orchestrates; Ordering receives commands and emits events.

Downstream: Checkout saga (state machine), Notifications (order lifecycle events), Invoicing (order-confirmed enrichment), BFF (order summary).
</role_in_system>

<contract>
LOCKED at the seams.

- 6 external events + 4 saga-issued commands under namespace `Ordering.Orders` per `events-catalog.md § 5.3 + 5.5`
- 8 internal `*DomainEvent` records per `ordering.md § 6` (invariants are yours; names are locked)
- `OrderStatus` SmartEnum transition table per `ordering.md § 5.1` (`Created → StockReserved → PaymentCompleted → Confirmed → Shipped → Delivered`; `Cancelled` / `Failed` off-ramps)
- Step ordering **stock BEFORE payment** per [ADR-0004](../adr/0004-checkout-saga-topology.md)
- Topics `ordering.orders` (infinite) + `ordering.order-commands` (7-day)
- Schemas FORWARD_TRANSITIVE (events) + FULL_TRANSITIVE (commands) per [ADR-0007](../adr/0007-avro-compatibility-modes.md)
- HTTP routes under `/api/v1/ordering/...` per ADR-0012
- `OrderingErrors` class location + names locked by `error-taxonomy.md § 3.3` (do NOT re-list them; implement to match)
- File ownership: see `<boundaries>`
</contract>

<design_open>
You own these. Justify each in your session summary.

- `Order` aggregate's concrete backing-field + collection type (`List<OrderItem>` private → `IReadOnlyCollection<OrderItem>` public) and how you enforce I-7 (items locked after `StockReserved`)
- Specification classes for `GetOrdersByBuyerQuery` (paginated)
- Saga-command consumer class organization (one consumer per command type or one multiplexer — pick, justify)
- Concurrency-token shape: explicit `RowVersion : uint` (default recommendation) vs implicit `LastModifiedUtc`
- `OrderingErrors` factory shapes + authorization-policy names (`AuthPolicies.OrderingAdmin`, etc.)
- Appendix B open-question resolutions (see `<autonomous_evolution>`)
- Additional `example-mapping/ordering.md` sessions for edge cases emerging during implementation
</design_open>

<reading_order>
1. `docs/implementation-prompts/_shared.md` — FIRST
2. `docs/bc-design/ordering.md` — **especially Appendix B (open questions to resolve)**
3. `docs/bc-design/glossary-ordering.md` + `example-mapping/ordering.md`
4. `docs/bc-design/events-catalog.md` § 5.3 + § 5.5
5. `docs/bc-design/use-cases.md` § 3 — **especially § 3.3 (saga-command intake over Kafka)**
6. `docs/bc-design/error-taxonomy.md § 3.3` — `OrderingErrors` single source of truth
7. `docs/bc-design/checkout-saga.md` § 5 + § 6 — you are the SERVICE SIDE of this saga
8. `docs/eshop-master-design.md` § 3 + § 11
9. `docs/adr/0001`, `0004`, `0005`, `0007`
10. **All ADRs in `<applicable_adrs>` below**
</reading_order>

<applicable_adrs>
Cross-cutting decisions to apply:

- [ADR-0008](../adr/0008-correlation-id-propagation.md) — every saga-command handler reads `CorrelationId` from the Kafka header; persists into `ordering.orders.correlation_id` column; outbox publisher copies into emitted Avro events
- [ADR-0010](../adr/0010-service-to-service-auth.md) — inbound JWT validation for admin endpoints (scope `ordering.commands.*`); saga-command Kafka consumer validates the `X-Service-Token` header from `checkout-saga` client (per Wave 0 inbox middleware); no outbound HTTP from Ordering in v1
- [ADR-0011](../adr/0011-pii-handling-gdpr.md) — `ShippingAddress` + `BillingAddress` are PII; columns named `*_enc` per the convention (V1 stores plaintext; v2 will encrypt with per-buyer DEK); arch test forbids logging `Address`-typed parameters; OTEL allowlist forbids tagging spans with address fields
- [ADR-0012](../adr/0012-api-versioning.md) — admin routes under `/api/v1/ordering/...`
- [ADR-0013](../adr/0013-idempotency-key-http.md) — apply FastEndpoints `.Idempotency()` to `POST /api/v1/ordering/orders/{id}/cancel` (admin double-click guard) backed by `redis-cache`
- [ADR-0015](../adr/0015-time-timezone-policy.md) — every timestamp `DateTimeOffset` (persisted as `timestamptz`); inject `IClock` for `Order.PlacedAt`, `ShippedAt`, etc.; arch test forbids `DateTime.UtcNow` in `Ordering.Domain`
</applicable_adrs>

<skills>
Universal skills per `_shared.md § 7`. Ordering-specific:

| Phase | Skill | When |
|---|---|---|
| Designing admin HTTP endpoints | `backend-development:api-design-principles` | `MarkOrderShipped` / `MarkOrderDelivered` + authorization |
| Classifying saga-command consumer composition | `superpowers:brainstorming` | one consumer per command vs one multiplexer — explore before committing |
</skills>

<autonomous_evolution>
Ordering-specific triggers:

- **Appendix B questions (all 6)** — pick sensible defaults, document rationale:
  - B.1 Saga → Ordering transport → **Kafka** (already locked in events-catalog)
  - B.2 Weather-remnant fate → **N/A** (Weather was fully removed pre-dispatch)
  - B.3 Concurrency token → **explicit `RowVersion`** (default unless reason)
  - B.4 Pagination → **offset/limit** (keyset is v2+)
  - B.5 Cancellation policy → buyer may cancel up to `Confirmed`; admin may cancel up to `Confirmed`; NO ONE after `Shipped` (I-12)
  - B.6 Delivery confirmation → admin-only `MarkOrderDeliveredCommand` (no auto-timer in v1)
- **If implementation surfaces a missing rule** (e.g., "what happens when a saga's `CancelOrderCommand` arrives AFTER the buyer's HTTP cancel?"): add a session to `example-mapping/ordering.md` before implementing.
- **Address-PII storage** — confirm `*_enc` column naming per ADR-0011 even though v1 stores plaintext. Future v2 migration depends on the column convention being correct now.
</autonomous_evolution>

<success_criteria>
- A Wave-2 (Checkout saga) agent can drive the full happy-path (`CreateOrder → StockReserved → PaymentCompleted → Confirmed`) without modifying Ordering code.
- Admin HTTP endpoints (`MarkOrderShipped`, `MarkOrderDelivered`) work end-to-end with admin authorization.
- A buyer can query their own orders via `GetOrdersByBuyerQuery` but cannot see other buyers' orders (verified by integration test).
- `OrderingErrors` matches `error-taxonomy.md § 3.3` byte-for-byte (names, namespace, factory signatures).
</success_criteria>

<dod>
Concrete deliverables. Extends `_shared.md § 12`.

- [ ] 4-layer solution structure scaffolded under `services/Ordering/`, `.slnx` updated, `dotnet build -m` green
- [ ] 6 external Avro events + 4 saga-command schemas + 8 internal `*DomainEvent` records + 4 outbox publishers + 4 saga-command Kafka consumers (with inbox dedup + service-token validation)
- [ ] Admin HTTP endpoints under `/api/v1/ordering/` — `MarkOrderShipped`, `MarkOrderDelivered`, `Cancel` + authorization policies + `.Idempotency()` on cancel
- [ ] Queries: `GetOrderById` (with buyer-or-admin authorization check), `GetOrdersByBuyer` (paginated)
- [ ] Appendix B decisions all documented in session summary
- [ ] `OrderingErrors` implemented to match `error-taxonomy.md § 3.3` (names, namespace, factory shapes)
- [ ] All timestamps use `DateTimeOffset` (persisted as `timestamptz`); no `DateTime.UtcNow` in domain code (architecture test)
- [ ] PII column naming `*_enc` for `ShippingAddress` / `BillingAddress` (ADR-0011 convention; v1 plaintext)
- [ ] Correlation-id propagation: Kafka header → handler → DB column → outbox row → emitted event Avro header (integration test)
- [ ] Integration tests cover all sessions in `example-mapping/ordering.md` + admin-cancel idempotency
- [ ] All `<applicable_adrs>` enforced (architecture tests + verification commands)
- [ ] Peer-review chain (`_shared.md § 11`) executed; HIGH findings fixed
</dod>

<boundaries>
**You may write:** `services/Ordering/**`, `test/Ordering.*.Tests/**`, `platform/Platform.SchemaRegistry.Contracts/Avro/Ordering/**`, `docker-compose.yaml` (touch only if topic / relay drifted from Wave 0), `DotNetAtlas.slnx` (project path additions if not done in Wave 0), `Directory.Packages.props` (Ordering-specific), `docs/bc-design/ordering.md` + glossary + example-mapping (self-correction only).

**Do not touch:** other services, saga, platform code (except `.avsc`), other BCs' Avro schemas, other BCs' topic entries.
</boundaries>

<stop_conditions>
STOP and ask the user (in addition to `_shared.md § 9` universal stops) if:

- `services/Order/` still exists (means Weather cleanup was not done — escalate).
- `Platform.SharedKernel.Address` doesn't exist (Wave 0 prerequisite missing).
- `events-catalog.md § 5.3` lists a different number of events than `ordering.md § 6`.
- The saga-command Kafka consumer middleware (Wave 0) doesn't validate the service-auth token — this is a security-relevant gap, escalate.
</stop_conditions>

<session_management>
Per `_shared.md § 10`. Suggested commit milestones:

1. Scaffold 4 layers + project references; `dotnet build` green
2. Domain layer (`Order`, `OrderItem`, `OrderStatus` SmartEnum, VOs, internal events) + unit tests; FSM transition tests
3. Application layer (saga-command handlers, query handlers, outbox publishers) + outbox integration test
4. Infrastructure layer (DbContext, EF mappings with `*_enc` columns, Kafka consumers) + integration test
5. Admin HTTP endpoints with authorization + `.Idempotency()` + functional tests
6. Architecture tests (PII naming, no `DateTime.UtcNow`, no cross-BC refs)
7. Integration tests for all `example-mapping/ordering.md` sessions
8. docker-compose smoke + Avro schema registration
9. Docs self-corrections + Appendix B resolutions + session summary
</session_management>

<verification>
```bash
dotnet build -m
dotnet restore --locked-mode
dotnet format whitespace --no-restore --verify-no-changes
dotnet format style --no-restore --verify-no-changes
dotnet test test/Ordering.UnitTests/
dotnet test test/Ordering.ArchitectureTests/
dotnet test test/Ordering.IntegrationTests/
dotnet test test/Ordering.FunctionalTests/
docker compose --profile full up -d
# Verify topics + retention
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic ordering.orders
docker compose exec kafka kafka-topics --bootstrap-server kafka:9092 --describe --topic ordering.order-commands
# Smoke: query order endpoint (after seeding via integration test)
curl -s "http://localhost:8080/api/v1/ordering/orders/00000000-0000-0000-0000-000000000001" -H "Authorization: Bearer ..."
```

Paste actual output (pass/fail per command) into your session summary.
</verification>

<example_design_decision>
**Question:** Saga-command consumer class organization — one consumer per command type or one multiplexer?

**Bad answer:** "One multiplexer to keep the consumer count low."

**Good answer:** "One consumer per command type (4 classes: `CreateOrderCommandConsumer`, `ConfirmOrderCommandConsumer`, `CancelOrderCommandConsumer`, `MarkOrderFailedCommandConsumer`). Reasons: (1) test isolation — each consumer has its own integration test fixture, no cross-pollution; (2) DLT routing per `kafka-dlq-strategy.md` is per-class — a multiplexer would require manual classification of which command failed; (3) idiomatic KafkaFlow `IMessageHandler<T>` pattern, matches the Weather reference. Trade-off accepted: 4 small classes (~25 LOC each) instead of 1 multiplexer; the duplication is the inbox-dedup + handler dispatch, which is one helper method extracted into `Common/SagaCommandHandlerBase`. Verified by `OrderingSagaCommandConsumerTests` covering each command type independently."

That's the depth expected for **every** `<design_open>` resolution.
</example_design_decision>

<peer_review>
Per `_shared.md § 11`. Before declaring DoD met:

1. Run every command in `<verification>`; paste pass/fail output in session summary.
2. Invoke `superpowers:verification-before-completion`.
3. Invoke `nw-software-crafter-reviewer` with your session summary as input — fix any HIGH-severity findings.
</peer_review>

<session_summary>
Use the template in `_template.md § session_summary`. Ordering-specific notes:

- All 6 Appendix B resolutions + rationale (defaults above acceptable unless deviated)
- Saga-command consumer organization decision + why
- Concurrency-token decision (`RowVersion` vs `LastModifiedUtc`) + why
- ADR-0011 PII verification — `*_enc` column naming applied; arch test in place
- ADR-0008 correlation-id roundtrip — which test verifies Kafka header → DB column → outbox → emitted event
- ADR-0013 idempotency on admin cancel verified

Proceed.
</session_summary>
