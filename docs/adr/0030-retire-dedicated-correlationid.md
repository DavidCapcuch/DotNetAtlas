# ADR-0030: Retire the Dedicated CorrelationId — OrderId as Business Key + traceId for Telemetry

## Status

Accepted (2026-06-03) — **supersedes** [ADR-0008](0008-correlation-id-propagation.md) (Correlation-ID Propagation Rule). Amends the header note in [ADR-0007](0007-avro-compatibility-modes.md) and the PII exemption in [ADR-0011](0011-pii-handling-gdpr.md). Paired with [ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md). **Implementation is staged (Part B)** — it lands after ADR-0029 (Part A) is complete and green.

## Context

[ADR-0008](0008-correlation-id-propagation.md) introduced a dedicated, platform-wide `CorrelationId`: an `X-Correlation-Id` HTTP header / `correlation.id` Kafka header (UUID v7), threaded by middleware, stamped onto outbox rows, persisted as a `correlation_id` column in every BC's tables, and forwarded into MassTransit's saga `CorrelationId`. Its stated purpose ([ADR-0008](0008-correlation-id-propagation.md) Driver #1) was *runbook-first operability*: one durable key queryable across every BC, including pre-order and non-Kafka flows.

[ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md) re-keys the saga on a pre-assigned `OrderId`. That changes the calculus: the order flow now has a **durable, always-present business key** (`OrderId`, persisted on every order-scoped aggregate). At that point the system carries **three** identifiers for the order flow:

- `traceId` (W3C / OpenTelemetry) — telemetry; sampled, ephemeral (7-day retention in the reference config), can fork across async/retry boundaries.
- `correlationId` (ADR-0008) — a dedicated durable ops key.
- `OrderId` — the order's business identity, durable, 100% present for order flows.

For order-scoped flows, `correlationId` is now **redundant**: `OrderId` covers durable business/audit correlation and `traceId` covers cross-cutting telemetry. The dedicated `correlationId`'s only non-overlapping value is correlating **pre-order** (basket browsing/checkout-initiation before an `OrderId` exists) and **non-order** flows (catalog price changes, standalone notifications) with a durable, sampling-independent key.

A platform-wide `correlationId` is genuinely best practice for enterprise systems that need that cross-system durable key. But this is a **teaching reference solution**, and the dedicated-`correlationId` machinery (HTTP + Kafka middleware, an outbox header path, a `correlation_id` column in all 8 BCs, payload fields on commands, and a deliberately-duplicated `CorrelationIdKeys` constant pair that is itself a documented drift risk) is a large surface whose marginal value — after ADR-0029 — is the pre-order/non-order audit case alone. Breaking changes are free here (root `CLAUDE.md`).

> **Honesty note (so this is not oversold):** `traceId` is **not** equivalent to a dedicated `correlationId`. It is sampled, short-retention, and can change across logical workflow boundaries. Retiring `correlationId` is a real trade-off — see Consequences — not a free simplification. The decision is that, *for this repo's goals*, the trade is worth it.

## Decision Drivers (ranked)

1. **Eliminate the redundant third identifier** for order flows now that `OrderId` is the durable business key (ADR-0029).
2. **Reduce platform surface** — remove the correlation middleware stack, the outbox header path, the `correlation_id` columns, the payload fields, and the duplicated `CorrelationIdKeys` drift risk.
3. **Teaching clarity** — `traceId` (telemetry) + natural business keys (`OrderId`) is the model most readers will actually adopt; it is the simpler, more common shape.
4. **Breaking changes are free** — non-production reference solution.

## Considered Options

### Option 1: Keep ADR-0008 unchanged

Retain the dedicated `correlationId` alongside `OrderId` and `traceId`. Rejected: three identifiers, of which one is now redundant for the dominant (order) flow; perpetual maintenance of the correlation middleware + the duplicated constants.

### Option 2: Retire the dedicated CorrelationId — `OrderId` + `traceId` (chosen)

`traceId` (W3C/OTel) handles telemetry and cross-process propagation for **all** flows including pre-order and HTTP. `OrderId` handles durable business/audit correlation for order flows. Pre-order/non-order flows correlate **operationally via `traceId`** (accepting its sampling/retention limits). Remove the dedicated correlation infrastructure.

### Option 3: Collapse values — `correlationId = OrderId`, keep the plumbing

Set the correlation value equal to `OrderId` everywhere an order exists, but keep the header/column/middleware. Cheaper than Option 2 (no teardown) and keeps a durable cross-system key for non-order flows. Rejected as the primary decision because it retains the very concept and surface the owner set out to remove — but **recorded here as the fallback** if Part B's teardown cost proves not worth it (it can be adopted at any point by simply sourcing the correlation value from `OrderId` and halting further teardown).

## Evaluation Matrix

| Driver (ranked) | Opt 1: keep ADR-0008 | Opt 2: retire (chosen) | Opt 3: collapse values |
|---|---|---|---|
| 1. Remove redundant id | 3 ids | **2 ids** (OrderId + traceId) | 2 logical ids (value-collapsed) |
| 2. Reduce surface | Full middleware + columns + constants | **All removed** | Surface retained |
| 3. Teaching clarity | "enterprise" shape | **Common/simple shape** | Mixed |
| 4. Cost to implement | none | **High (Part B, ~platform-wide)** | Low |
| Pre-order/non-order durable key | Yes | **No (traceId only, sampled)** | Yes |

## Decision

Adopt **Option 2**. After ADR-0029 (Part A) is complete:

1. **`traceId` (W3C Trace Context / OpenTelemetry)** is the cross-process correlation/propagation key for all flows, carried in transport headers and tagged on logs/spans, exactly as today's OTel wiring already does.
2. **`OrderId`** is the durable business correlation/audit key for order-scoped flows, persisted on order-scoped aggregates and queried by the runbook.
3. **Pre-order and non-order flows** correlate operationally via `traceId`; we accept that this is sampled and short-retention (the runbook for those flows loses a durable always-present key — see Consequences).
4. **Remove the dedicated `correlationId` infrastructure**: the HTTP `CorrelationIdMiddleware` + `CorrelationIdDelegatingHandler`, the Kafka `ConsumerCorrelationIdMiddleware` + the correlation branch of `ProducerHeadersMiddleware`, `CorrelationIdKeys`, the outbox `correlation.id` header path, the Serilog correlation enricher, the `correlation_id` DB columns across all BCs, the payload `CorrelationId` fields on commands/saga-boundary events, and `PaymentByCorrelationIdSpec`. Rewrite `saga-stuck-runbook.md` queries to key on `OrderId`.

## Rationale

`traceId` and a dedicated `correlationId` genuinely differ (sampling, retention, workflow-lifetime stability), so the two are not interchangeable in general. But after ADR-0029 the *durable* correlation role for the order flow is filled by `OrderId`, and the *telemetry* role for every flow is filled by `traceId`. What the dedicated `correlationId` uniquely adds is a durable, always-present key for **pre-order and non-order** flows — a real capability, but a narrow one whose platform-wide cost (middleware in every service, a column in every BC, payload fields, and a duplicated-constant drift risk) is not proportionate for a teaching reference solution. The simpler `OrderId` + `traceId` model is also the one most readers will recognize and reuse. Option 3 (collapse-values) is retained as an explicit, cheap fallback so the teardown can stop short if its cost is not justified.

## Consequences

### Positive

- Two identifiers instead of three; the correlation middleware stack, the outbox header path, the `correlation_id` columns, and the `CorrelationIdKeys` drift risk all go away.
- The model — `traceId` for telemetry, `OrderId` for business correlation — is the common, easily-taught shape.

### Negative

- **Pre-order and non-order flows lose a durable, always-present correlation key.** Their runbook correlation now relies on `traceId`, which is **sampled** (may be absent) and **short-retention** (7 days). This is the real cost of the decision. Mitigation options if it bites: 100%-sample the critical paths, or adopt Option 3 (collapse-values) to keep a durable key cheaply.
- **Large migration (Part B):** the teardown touches every BC's persistence (column drops + migrations), messaging DI, `Program.cs`, several `.avsc` + regenerated bindings, the runbook, and ~the test suites that assert correlation propagation.

### Risks

- **Audit gaps for non-order flows** after trace retention elapses — accepted for a reference solution; flagged for any reader porting this to production (where a dedicated `correlationId`, i.e. keeping ADR-0008, is the defensible enterprise choice).
- **Staged-migration window:** between Part A and Part B the codebase briefly carries both `OrderId`-keyed sagas and the old `correlationId` plumbing; this is expected and harmless (the plumbing simply carries an unused value until removed).

## Implementation Notes (Part B inventory)

Staged after ADR-0029. High-level inventory (the greps that scoped this hit the 200-file cap):

- **Platform middleware:** delete/retire `Platform.ServiceDefaults/CorrelationId/*`, the correlation branch of `Platform.KafkaFlow.ProducerHeaders/ProducerHeadersMiddleware`, `ConsumerCorrelationIdMiddleware`, `CorrelationIdKeys`, the outbox `OutboxMessageHeaderExtensions` correlation path, the Serilog enricher.
- **Persistence:** drop `correlation_id` from every BC (Catalog, Basket, Ordering, Inventory, Payments, Invoicing, saga) — new migrations, EntityConfigurations, ModelSnapshots, and the V001 SQL scripts (per `CLAUDE.md`, generate via `dotnet ef`, fix the `Up`/`Down`, keep idempotent guards).
- **Contracts:** remove the payload `CorrelationId` fields from commands + saga-boundary `.avsc`; regenerate bindings via `generate-avro.ps1`; commit `.avsc` + `.cs` together.
- **Docs/ops:** rewrite `saga-stuck-runbook.md` queries (`WHERE correlation_id` → `WHERE order_id`); update `events-catalog.md`, `eshop-master-design.md`, `kafka-topology.md`; mark ADR-0008 superseded.
- **Tests:** remove/retarget `CorrelationIdPropagationTests`, `CorrelationIdMiddlewareTests`, `CorrelationIdServiceCollectionExtensionsTests`, `OutboxMessageHeaderExtensionsCorrelationIdTests`, etc.

## Related Decisions

- [ADR-0008: Correlation-ID Propagation Rule](0008-correlation-id-propagation.md) — **superseded** by this ADR.
- [ADR-0029: Order-Keyed Saga & Pre-Assigned OrderId](0029-order-keyed-saga-and-pre-assigned-orderid.md) — establishes `OrderId` as the durable business key this ADR relies on; must land first.
- [ADR-0007: Avro Compatibility Modes](0007-avro-compatibility-modes.md) — its note "the Kafka header is outside the Avro schema" becomes moot for correlation; amend.
- [ADR-0011: PII Handling & GDPR](0011-pii-handling-gdpr.md) — the `correlationId` OTEL-allowlist exemption is removed; `OrderId`/`traceId` take its place.
- [ADR-0015: Time & Timezone Policy](0015-time-timezone-policy.md) — unaffected; referenced only because correlated audit rows used `DateTimeOffset`.
