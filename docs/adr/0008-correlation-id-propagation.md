# ADR-0008: Correlation-ID Propagation Rule

## Status

**Superseded by [ADR-0030](0030-retire-dedicated-correlationid.md) (2026-06-03).**

> The dedicated platform-wide `CorrelationId` described below was retired. After
> [ADR-0029](0029-order-keyed-saga-and-pre-assigned-orderid.md) re-keyed the checkout saga on a
> pre-assigned `OrderId`, the dedicated id became redundant for order flows: `OrderId` is now the
> durable business/audit key and `traceId` (W3C Trace Context / OpenTelemetry) is the cross-process
> telemetry key. The `X-Correlation-Id` HTTP header, the `correlation.id` Kafka header + middleware,
> the `correlation_id` columns, the Avro payload fields, and the Serilog enricher were all removed.
> This ADR is retained **unchanged below as the historical record** of why the dedicated id existed
> and why it was accepted at the time; see [ADR-0030](0030-retire-dedicated-correlationid.md) for the
> trade-off analysis behind retiring it (including the accepted loss of a durable key for pre-order /
> non-order flows).

Originally accepted (2026-04-19).

## Context

The eShop reference solution spans 8 runtime components (Catalog, Basket, Ordering, Inventory, Payments, Invoicing, Notifications, Checkout saga, BFF) communicating across HTTP and Kafka. A single business workflow — "one customer's checkout" — touches every one of them. Without a deterministic, end-to-end identifier, operators cannot stitch a trace from the buyer's click to the invoice PDF and back: OpenTelemetry's `traceparent` propagates across HTTP but is weaker across Kafka; W3C baggage is not universally forwarded; and each BC generating its own identifier leaves the runbook ([saga-stuck-runbook.md](../bc-design/saga-stuck-runbook.md)) unable to query by a single key.

Two kinds of IDs already coexist in the design:
- **`traceparent` / `tracestate`** — W3C Trace Context, OpenTelemetry's native propagation, stitches spans into a trace.
- **`CorrelationId`** — a business-workflow UUID v7 minted at the boundary and threaded through DB rows, event payloads, and saga state.

This ADR specifies how **CorrelationId** is generated, propagated, and recorded — the business-level sibling of `traceparent`. Both are required; they serve different purposes (trace tooling vs. cross-system audit + runbook queries).

## Decision Drivers (ranked)

1. **Runbook-first operability** — `saga-stuck-runbook.md` must be able to `SELECT ... WHERE correlation_id = ?` across any BC and get the full picture.
2. **Cross-transport consistency** — HTTP edges and Kafka edges must not lose the identifier.
3. **Zero-surprise defaults** — developers writing a new handler should get propagation automatically without per-endpoint wiring.
4. **Tooling interop** — OpenTelemetry + structured logs must include the identifier so Jaeger/Seq can pivot by it.
5. **Not a replacement for `traceparent`** — we keep both; the correlation ID is not a substitute for distributed tracing.

## Considered Options

### Option 1: Custom `X-Correlation-Id` header + Kafka header + platform middleware

Dedicated header name, generated at the outermost edge (YARP / BFF) when absent, validated as UUID v7, propagated into:
- HttpClient outgoing calls via a `DelegatingHandler`
- Kafka producer headers via a platform extension on `Platform.KafkaFlow.ProducerHeaders`
- Kafka consumer MDC / Activity baggage on inbox dispatch
- Outbox row column (all BCs have `correlation_id` in their schemas)
- Domain events (raised with the ambient correlation)

### Option 2: Reuse `traceparent` as the sole cross-system correlation

Skip a dedicated business ID; use the OpenTelemetry `trace_id` as the business correlation. Compact at first glance; aligns with OTel conventions.

### Option 3: Each BC generates its own `CorrelationId` at entry and records the caller's in a separate field

No central header; each service computes a new ID and writes the caller's ID alongside ("CausationId" pattern). Common in some event-sourcing communities.

## Evaluation Matrix

| Driver (ranked) | Option 1: Platform-wide header + middleware | Option 2: Reuse `traceparent` | Option 3: Per-BC + CausationId |
|---|---|---|---|
| 1. Runbook operability | Single key, every table/topic/log | `trace_id` queryable but ops must know OTel internals | Must JOIN across BCs to rebuild causality chain |
| 2. Cross-transport | HTTP + Kafka + DB covered by convention | Works for HTTP; Kafka-OTel bindings vary by library | Each BC handles its own edges |
| 3. Zero-surprise defaults | DelegatingHandler + KafkaFlow middleware apply automatically | Automatic for OTel-instrumented code only | Each handler must remember to copy inbound → outbound |
| 4. Tooling interop | OTel + Seq + Jaeger all tag by a known attribute | Jaeger native; Seq via custom enricher | Multiple IDs in logs — operator confusion |
| 5. Not a `traceparent` substitute | Keeps both — explicit | Conflates two concerns; `trace_id` changes with span tree | Keeps both, but adds a third |

## Decision

We will use **Option 1: dedicated `X-Correlation-Id` header (UUID v7) with platform-level propagation middleware**, carried on HTTP requests, Kafka producer headers, outbox rows, aggregate audit columns, and log/trace attributes.

## Rationale

The runbook is the acid test. `saga-stuck-runbook.md § 2 Triage queries` is written assuming a single column exists in every relevant table (`saga_checkout_states.correlation_id`, `ordering.orders.correlation_id`, `inventory.reservations.correlation_id`, `payments.transactions.correlation_id`, `invoicing.invoices.correlation_id`). Option 2's `trace_id` is ephemeral to the OTel pipeline — it lives in span exporters, not in DB rows that auditors need ten years later. Option 3 creates a per-BC ID per hop, which is pedagogically valuable (causation is richer than correlation) but operationally doubles the number of IDs that must be remembered at 3 AM.

Option 1 also aligns with patterns learners will encounter in production. Stripe uses `Stripe-Request-Id`, AWS uses `X-Amzn-Trace-Id`, most enterprise platforms carry a similar business-ID header. Teaching a custom platform-wide `X-Correlation-Id` + middleware pipeline is a direct transfer of skill.

## Consequences

### Positive

- `saga-stuck-runbook.md § 2` queries work across all BCs with a single predicate.
- Jaeger traces can be correlated to DB rows by sharing the CorrelationId as a span attribute.
- New handlers inherit propagation for free — `AddHttpClient(...)` auto-wires the DelegatingHandler from `Platform.ServiceDefaults`.
- Outbox → Kafka header → inbox pipeline copies the ID without manual plumbing.
- Audit trail is reconstructible long after traces are purged (trace retention is 7 days in reference config; DB rows live 10 years).

### Negative

- Two identifiers to teach — `traceparent` AND `X-Correlation-Id`. Docs must explain when to use which.
- Middleware is platform-level, so a bug in the header writer affects every service. Mitigation: architecture tests + a dedicated platform integration test.
- Header size adds ~40 bytes per HTTP request and per Kafka message. Negligible at reference-solution throughput.

### Risks

- **Missing ID on ingress** — external callers may not send the header. Mitigation: YARP / BFF auto-generates a UUID v7 on first contact; rejecting the request would break public API. Logged-but-accepted.
- **ID forged by a client** — accepted in v1 since public endpoints are authenticated; the CorrelationId is an operational key, not a security boundary.
- **Non-standard header name** — future libraries may expect `traceparent` only. Mitigation: document the standard in `_shared.md`.

## Implementation Notes

- Header name: `X-Correlation-Id` (HTTP), `correlation.id` (Kafka)
- Format: UUID v7 — time-sortable, good for DB index locality
- Generation boundary: YARP (for external requests) and BFF (for internal BFF-originated workflows)
- Platform surface:
  - `Platform.ServiceDefaults` adds `AddCorrelationId()` extension → registers ASP.NET middleware that reads/validates/generates and sets `Activity.Current?.SetTag("correlation.id", value)` plus `LogContext.PushProperty("CorrelationId", value)`.
  - `DelegatingHandler` (also in `Platform.ServiceDefaults`) reads from the ambient context and copies onto outbound HttpClient calls.
  - `Platform.KafkaFlow.ProducerHeaders.ProducerHeadersMiddleware` writes the `correlation.id` Kafka header on every direct produce, sourcing from `Activity.Current` tag or generating a fresh v7 when originating a new workflow.
  - `Platform.KafkaFlow.ProducerHeaders.ConsumerCorrelationIdMiddleware` reads the `correlation.id` Kafka header on consume, validates it as UUID v7, and republishes onto `Activity.Current` + Serilog `LogContext` for the duration of the handler dispatch. Missing / malformed header → generates a fresh v7 (logged at Debug).
  - `Platform.ReliableMessaging.Outbox.Core.OutboxMessageHeaderExtensions.BuildOtelHeadersFromActivity` injects `correlation.id` as a **top-level** key in the outbox row's headers JSON alongside the OTel propagation headers (`traceparent`, `tracestate`, `baggage`). The `Platform.OutboxRelay.WorkerService.OutboxRelay.OutboxMessageRelay` then copies the row's headers verbatim onto the produced Kafka message, so outbox-routed messages carry the same canonical top-level `correlation.id` header as direct-producer messages. Cross-cutting wave1-followup #256 promoted this from baggage-only encoding to a top-level header so the consumer side `Headers.GetString("correlation.id")` one-liner works for outbox-routed events too.
- DB convention: every aggregate that can be part of a cross-BC workflow has a `correlation_id uuid NOT NULL` column. Indexed per BC choice.
- Domain events: `IDomainEvent` base carries `CorrelationId`; factory helpers copy from ambient `HttpContext` / saga state.
- Architecture test: outbox rows written by `Platform.ReliableMessaging.Outbox.EFCore` must include the CorrelationId header in the produced Kafka message (integration-test assertion).
- Logging: Serilog enricher adds `CorrelationId` to every log line. Seq dashboards pivot on it.
- The identifier is **distinct from** MassTransit's built-in `CorrelationId` on saga contracts; when both are present, we forward our header value into MassTransit's binding so they remain identical.

## Related Decisions

- [ADR-0001: Centralized Saga Orchestration](0001-centralized-saga-orchestration.md) — saga state tables carry the CorrelationId
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md) — Kafka header is outside the Avro schema; schema evolution does not affect propagation
- [ADR-0011: PII Handling & GDPR Article 17 Path](0011-pii-handling-gdpr.md) — CorrelationId is NOT PII and is exempt from the OTEL attribute allowlist
- [ADR-0015: Time & Timezone Policy](0015-time-timezone-policy.md) — timestamps in correlated audit rows use `DateTimeOffset`
