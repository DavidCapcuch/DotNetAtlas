# ADR-0020: Summary Events for Cross-BC Aggregate Snapshots

## Status

Accepted (2026-05-02)

## Context

The eShop publishes integration events on Kafka topics that downstream
BCs consume to build their own state. Most events declared in
[events-catalog.md](../bc-design/events-catalog.md) are **delta events**:
they describe a transition (`OrderStockReservedEvent`,
`PaymentRefundedEvent`) and carry only the data needed to identify the
aggregate plus the change. The downstream then either queries the
producer back over HTTP for the full state or reassembles state from a
sequence of deltas.

Wave 1's M6 implementation surfaced a problem with this default in one
specific path: **invoice issuance**.
[Invoicing.Domain.Invoices.Invoice.Create](../../services/Invoicing/Invoicing.Domain/Invoices/Invoice.cs:82)
needs `BillingAddress`, `Lines`, `TotalAmount`, and `Currency` from the
order at the moment of confirmation. The contract today —
`OrderConfirmedEvent.avsc` carrying `(OrderId, CorrelationId, BuyerId,
ConfirmedAtUtc)` — forces Invoicing to either:

1. **Sync HTTP back to Ordering** for the order body. Read-path coupling
   on a fiscal-record path with 10-year retention; production fragility
   (Ordering down → no invoices); doesn't work in integration tests
   (Ordering isn't running in the Invoicing fixture).
2. **Subscribe to `OrderCreatedEvent` separately** and converge a third
   stream alongside `OrderConfirmedEvent` and `PaymentCapturedEvent`.
   `OrderCreatedEvent` does carry `Items` and `Total` but **not**
   `BillingAddress` (PII redaction at the Created step per Wave 0
   intent), so an extra `OrderCreatedConsumer` plus a third payload
   column still wouldn't fully solve the problem on its own.
3. **Promote `OrderConfirmedEvent` to a Summary Event** — embed the
   aggregate's full state at the transition, so downstream BCs need
   nothing else.

Verraes' "Patterns for Decoupling Distributed Systems: Summary Event"
([https://verraes.net/2019/05/patterns-for-decoupling-distsys-summary-event/](https://verraes.net/2019/05/patterns-for-decoupling-distsys-summary-event/))
is the canonical articulation: *"a Summary Event is an event that
contains a complete summary of the state of an entity at a particular
moment, rather than only the delta of a single change."* Trade-off:
larger payload + the redundancy of carrying the aggregate body on every
emission, in exchange for downstream decoupling, replay completeness on
infinite-retention topics, and audit-trail self-sufficiency.

## Decision Drivers (ranked)

1. **Audit-trail completeness on long-retention topics** — `ordering.orders`
   has Infinite retention; `invoicing.invoices` is governed by a 10-year
   regulatory retention. A consumer rebuilding state from offset 0 must
   not need a synchronous round-trip to the producer for fields that
   could simply be in the event.
2. **Cross-BC test isolation** — Invoicing's integration tests must not
   require Ordering to be running. The event payload must be a complete
   stand-alone source of truth for the consumer's projection.
3. **Read-path resilience** — invoice issuance is a fiscal step. It must
   not depend on Ordering's HTTP availability.
4. **Avro contract evolution discipline** — the change must satisfy the
   `FORWARD_TRANSITIVE` policy that
   [ADR-0007](0007-avro-compatibility-modes.md) sets on event-log topics
   (including `ordering.orders`), so all historical messages remain
   readable across every future schema version.
5. **Cost of the pattern** — the larger payload (≈ a few hundred bytes
   added per confirmed-order event) is acceptable on a topic emitting
   one event per confirmed order.

## Considered Options

### Option 1: Promote `OrderConfirmedEvent` to a summary (chosen)

Add `Items`, `TotalAmount`, `Currency`, `BillingAddress` to the existing
LOCKED Avro schema as backward-compatible additions. The Ordering
producer reads these off the `Order` aggregate snapshot when emitting.
Invoicing's M6 projection handler captures them into
`pending_invoices.OrderPayload` (jsonb — no migration). M7's
`IssueInvoiceCommandHandler` reads them straight off the converged row.

### Option 2: Sync HTTP ACL from Invoicing back to Ordering

A new `IOrderingClient` in Invoicing.Infrastructure; M7's handler
queries `/api/v1/ordering/orders/{id}` for the body fields. Rejected:
read-path coupling on a fiscal path; production fragility; integration
tests would need a fake Ordering HTTP host.

### Option 3: Consume `OrderCreatedEvent` separately

Subscribe Invoicing to `ordering.orders.OrderCreatedEvent`; converge a
third stream alongside `OrderConfirmedEvent` + `PaymentCapturedEvent`.
Rejected: extra consumer, extra payload column, extra convergence
state-machine, AND `OrderCreatedEvent` lacks `BillingAddress` so it
doesn't fully solve the problem.

## Evaluation Matrix

| Driver (ranked) | Option 1: Summary Event | Option 2: HTTP ACL | Option 3: OrderCreated stream |
|---|---|---|---|
| 1. Audit-trail completeness | ✅ payload IS the audit record | ❌ requires producer up | ⚠️ split across two events |
| 2. Cross-BC test isolation | ✅ self-sufficient payload | ❌ needs HTTP fake | ⚠️ two consumers in fixture |
| 3. Read-path resilience | ✅ no upstream dep at issuance | ❌ Ordering = SPOF | ✅ no synchronous dep |
| 4. Avro evolution discipline | ✅ BACKWARD-compatible additions | ✅ no Avro change at all | ⚠️ no schema change but adds consumer |
| 5. Cost of pattern | ⚠️ +0.5 KB/event on `ordering.orders` | ✅ no payload growth | ⚠️ +1 consumer's runtime cost |

## Decision

We will adopt Verraes' **Summary Event** pattern for cross-BC
integration events whose downstream consumer needs the full aggregate
state at the transition. The general rule:

> **An integration event SHOULD be a Summary Event when** any
> downstream consumer needs the producer aggregate's full state at the
> transition AND that state is not reconstructible from the consumer's
> own prior projections. Otherwise, prefer a delta event.

### Adopters

- **`OrderConfirmedEvent` (Wave 1.5)** — first adopter. Drives Invoicing's
  M7 invoice-issuance path; Items / TotalAmount / Currency / BillingAddress
  travel with the event. See [Implementation Notes](#orderconfirmedevent-field-defaults-wave-15).
- **`OrderCancelledEvent` (Wave 1.6)** — second adopter. Drives Invoicing's
  M8 credit-note path; same four enrichment fields, locally-named inline
  records (`OrderItemCancelled`, `OrderCancellationBillingAddress`) to
  satisfy the per-`.avsc`-file avrogen constraint. Compensation consumers
  (Inventory, Payments, Notifications, BFF, checkout saga) keep reading
  the original Reason / AtStatus delta payload only. See
  [Implementation Notes](#ordercancelledevent-field-defaults-wave-16).
  `OriginalInvoiceId` was deliberately NOT added — Ordering has no
  knowledge of invoices; M8 looks the original invoice up by buyer +
  correlation on the Invoicing side.

### Future candidates

Decided per-event at the relevant wave, not preemptively here:

- `OrderShippedEvent` — Notifications' shipped-confirmation email needs
  the buyer's display name and shipment carrier; if those grow beyond
  what BFF can re-fetch cheaply, a summary promotion is the answer.

## Rationale

**The 10-year retention on `invoicing.invoices` is the load-bearing
driver.** A consumer replaying historical events to rebuild state must
not need an HTTP call to a producer that may have been re-architected
or decommissioned in the intervening years. The event payload is the
audit record. This is the same logic that drove ADR-0006 (event
sourcing for inventory) and ADR-0007 (FORWARD_TRANSITIVE compatibility):
the historical event log is canonical truth.

**Compatibility per ADR-0007 is preserved.** `ordering.orders` runs
under `FORWARD_TRANSITIVE` (ADR-0007 § Decision). Adding fields with
defaults satisfies that policy: every prior consumer compiled against
v1 keeps reading v2 messages because the new fields are optional / have
defaults; every future consumer reading historical v1 messages applies
those same defaults. The change is in fact bidirectionally compatible
(`FULL`-equivalent) thanks to the defaults, but the load-bearing rule
on this topic is `FORWARD_TRANSITIVE` and that's the gate the schema is
designed to pass. Production producers ship the populated fields from
day one; the defaults exist for compatibility, not as a runtime
fallback.

**The pattern does not generalize to all events.** Forcing every event
to be a summary inflates the topic and couples consumers to fields they
don't need (the `BillingAddress` is irrelevant to BFF cache
invalidation, for example). The decision rule deliberately gates
adoption on whether *any* downstream consumer needs full state — for
the events where no consumer does, the existing delta shape stays.

## Consequences

### Positive

- Invoicing M7's `IssueInvoiceCommandHandler` can construct
  `Invoice.Create(...)` from the converged `pending_invoices` row alone
  — no HTTP, no second consumer.
- Audit replay from offset 0 produces complete state; no out-of-band
  enrichment required.
- Cross-BC integration tests stay self-contained — Invoicing's fixture
  doesn't need Ordering running.
- The pattern, once documented, becomes a recognised primitive for
  future cross-BC contract design.

### Negative

- Payload size grows. For `OrderConfirmedEvent` the additional fields
  add ≈ 200–500 bytes per event (one address + N items × ≈ 80 bytes).
  Acceptable on a topic with one event per confirmed order; would not
  be acceptable on a high-throughput partial-update stream.
- The producer must read the aggregate's full state at emission time.
  For `OrderConfirmedEvent` the aggregate is in memory (loaded by
  `ConfirmOrderCommandHandler`), so this is free — but for events
  raised from a thinner code path, populating a summary requires
  loading data the producer didn't otherwise need.
- Schema is harder to evolve later — every field added to the summary
  becomes part of the BACKWARD-compatibility surface.

### Risks

- **Stale snapshot risk** — a summary event captures aggregate state at
  emission time. If the aggregate mutates after emission, downstream
  state diverges. Mitigation: only emit summaries on transitions where
  the snapshotted state is also frozen on the producer side
  (`OrderConfirmedEvent` is good — confirmed orders' lines are
  immutable per invariant I-2).
- **PII duplication** — embedding `BillingAddress` in the event means
  PII traverses topics that consumers other than Invoicing might
  receive. Mitigation: per ADR-0011, `ordering.orders` consumers are
  enumerated in `events-catalog.md`; new subscribers are gated by
  review.
- **Default-encoding fragility on bytes/decimal fields** — Avro decimal
  defaults are encoded as JSON strings of bytes (e.g., `" "` for
  unscaled zero). The repo had no precedent for a `bytes/decimal`
  default before this ADR. Mitigation: where the default would be
  fragile, the field is declared as a nullable union
  `["null", {bytes, decimal}]` with `default: null` instead. New
  producers always populate; consumers null-check defensively. See
  [implementation notes](#implementation-notes) for the per-field
  choice on `OrderConfirmedEvent`.

## Implementation Notes

### `OrderConfirmedEvent` field defaults (Wave 1.5)

| Field | Avro type | Default | Why |
|---|---|---|---|
| `Items` | `array<OrderItemConfirmed>` | `[]` | Empty array is well-encoded; invariant I-7 forbids empty in real data |
| `TotalAmount` | `["null", {bytes, decimal:19,4}]` | `null` | No repo precedent for bytes/decimal defaults; nullable union avoids encoding fragility |
| `Currency` | `["null", string]` | `null` | Covaries with `TotalAmount` — kept symmetric |
| `BillingAddress` | `["null", OrderBillingAddress]` | `null` | Optional in the schema; populated for confirmed orders |

Producers (Ordering's outbox publisher) always populate all four. The
nullable shape is for backward compatibility with the LOCKED v1
schema's pre-Wave-1.5 messages, not for runtime omission.

### `OrderBillingAddress` is locally defined

Avro JSON allows cross-record reuse by full name *within a single
file*, but `generate-avro.ps1` runs avrogen per-file — each `.avsc` is
processed in isolation. Referencing `Basket.Sessions.CheckoutAddress`
across files would emit a class collision. Wave 1.5 therefore inlines a
locally-named record `Ordering.Orders.OrderBillingAddress` with the
same six fields as `Basket.Sessions.CheckoutAddress` (Street1, Street2,
City, State, PostalCode, CountryCode). The duplication is intentional;
the conceptual mapping is one-to-one.

### `OrderCancelledEvent` field defaults (Wave 1.6)

| Field | Avro type | Default | Why |
|---|---|---|---|
| `Items` | `array<OrderItemCancelled>` | `[]` | Empty array is well-encoded; invariant I-7 forbids empty in real data |
| `TotalAmount` | `["null", {bytes, decimal:19,4}]` | `null` | No repo precedent for bytes/decimal defaults; nullable union avoids encoding fragility |
| `Currency` | `["null", string]` | `null` | Covaries with `TotalAmount` — kept symmetric |
| `BillingAddress` | `["null", OrderCancellationBillingAddress]` | `null` | Optional in the schema; populated for cancelled orders |

Producers (Ordering's `OrderCancelledOutboxPublisherDomainEventHandler`)
always populate all four. The nullable shape is for FORWARD_TRANSITIVE
compatibility with the v1 (pre-Wave-1.6) schema's existing messages,
not for runtime omission. Compensation consumers (Inventory, Payments,
Notifications, BFF, checkout saga) keep reading only the original
`Reason` / `AtStatus` delta fields; only Invoicing's
`OrderCancelledCreditNoteProjectionKafkaHandler` consumes the
enrichment fields, persisting them into `pending_credit_notes.OrderPayload`
(`jsonb` — no migration) for M8's credit-note issuance.

### Locally-named records in `OrderCancelledEvent.avsc`

For the same per-`.avsc`-file avrogen reason given above for
Wave 1.5's `OrderBillingAddress`, Wave 1.6 inlines two locally-named
records inside `OrderCancelledEvent.avsc`:

- `Ordering.Orders.OrderItemCancelled` — six fields identical to
  Wave 1.5's `OrderItemConfirmed`. A different name (not reuse) because
  avrogen would emit a duplicate class definition when generating both
  events' `.cs` siblings.
- `Ordering.Orders.OrderCancellationBillingAddress` — six fields identical
  to Wave 1.5's `OrderBillingAddress` and `Basket.Sessions.CheckoutAddress`.
  Same per-file avrogen constraint; the third locally-named copy of the
  same conceptual record. Future cleanup would consolidate via a shared
  `.avsc` namespace + a multi-file avrogen run, but that is platform
  work, not a wave concern.

### Future migrations

Tightening a default-null field to required in a future schema version
is a BACKWARD-incompatible change per ADR-0007 — it would require a
new subject (e.g., `OrderConfirmedEventV2`). Wave 1.5's nullable
defaults are therefore semi-permanent. Producers should populate them
unconditionally to keep the *de facto* contract tight even though the
*de jure* schema permits null.

## Related Decisions

- [ADR-0006: Event Sourcing for Inventory](0006-event-sourcing-for-inventory.md)
  — establishes the audit-replay requirement that drives this ADR's
  driver #1.
- [ADR-0007: Avro Schema Compatibility Modes](0007-avro-compatibility-modes.md)
  — the `FORWARD_TRANSITIVE` rule on event-log topics (including
  `ordering.orders`) under which Wave 1.5's field additions register
  cleanly.
- [ADR-0011: PII Handling & GDPR](0011-pii-handling-gdpr.md) — gates
  which consumers may subscribe to topics now carrying address PII.
- [events-catalog.md § 5.3.2](../bc-design/events-catalog.md) — the
  `OrderConfirmedEvent` schema's canonical reference.
- [events-catalog.md § 5.3.3](../bc-design/events-catalog.md) — the
  `OrderCancelledEvent` schema's canonical reference (Wave 1.6 promotion).
- [docs/bc-design/invoicing.md](../bc-design/invoicing.md) — Invoicing
  M6/M7/M8 — the consumer that drove the pattern's adoption.
