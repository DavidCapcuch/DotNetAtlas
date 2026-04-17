# ADR-0001: Centralized Saga Orchestration

## Status

Accepted

## Context

DotNetAtlas is a distributed system with multiple services that must coordinate multi-step business processes:

- **Order service** — manages alert subscription purchase and extension orders
- **Payments service** — executes payment commands (authorize, capture, void, refund)
- **Weather.Alerts service** — activates and extends alert subscriptions

Business workflows like "purchase an alert subscription" span all three services: create an order, process payment, activate the subscription — with compensation (refund) if activation fails. These distributed transactions require reliable orchestration with timeout handling and compensation flows.

The system currently has three sagas:

1. **AlertSubscriptionPurchaseSaga** — Order → Payment → Activation → Completion (or refund on failure)
2. **AlertSubscriptionExtensionSaga** — Order → Payment → Extension → Completion (or refund on failure)
3. **PaymentProcessingSaga** — Authorize → Capture → Complete (with void/refund compensation and retry logic)

We needed to decide where this orchestration logic lives.

## Decision Drivers (ranked)

1. **Clear ownership of orchestration logic** — workflow coordination must have a single owner to avoid split-brain scenarios
2. **Avoid circular dependencies between services** — sagas touch Order, Finance, and Weather; embedding them in one service would create coupling to the others
3. **Services remain autonomous command responders** — each service should own its domain logic without needing to know about multi-step workflows
4. **Centralized observability for distributed transactions** — tracing a purchase flow across services is easier when the orchestrator is a single, observable process
5. **Independent scaling** — saga processing load differs from API or consumer load

## Considered Options

### Option 1: Centralized Saga Service

A dedicated `saga/SagaOrchestrators` worker service hosts all MassTransit state machines. Services communicate via Kafka events; the saga service consumes events from all domains and publishes commands/events back.

Sagas are organized by initiating domain (`Orders/`, `Finance/`) but deployed as a single unit. Each saga uses the consumer-adapter pattern: Kafka consumers transform Avro events into internal MassTransit saga events.

### Option 2: Sagas Embedded in the Initiating Service

Each saga lives in the service that initiates the workflow. The AlertSubscriptionPurchaseSaga would live in the Order service since it starts with a purchase order. PaymentProcessingSaga would live in the Payments service.

### Option 3: Choreography (No Orchestrator)

Remove explicit orchestration entirely. Each service reacts to events and publishes its own events. The workflow emerges from the chain of event handlers across services.

## Evaluation Matrix

| Driver (ranked)                    | Centralized Saga | Embedded in Services | Choreography |
|------------------------------------|------------------|----------------------|--------------|
| 1. Clear orchestration ownership   | ✅ Single owner   | ⚠️ Split across services | ❌ No owner   |
| 2. No circular dependencies        | ✅ Saga is the hub | ❌ Order must know Finance/Weather | ✅ No direct deps |
| 3. Services stay autonomous        | ✅ Services are command responders | ❌ Initiating service gains orchestration duty | ✅ Fully autonomous |
| 4. Centralized observability       | ✅ Single process to trace | ⚠️ Distributed across services | ❌ Must correlate across all services |
| 5. Independent scaling             | ✅ Scales separately | ❌ Tied to service scaling | ✅ Each service scales independently |

## Decision

We will use a **centralized saga service** (`saga/SagaOrchestrators`) hosting all MassTransit state machines, with Kafka as the event backbone and SQL Server for saga state persistence.

## Rationale

**Centralized orchestration wins on the highest-priority drivers.** The purchase and extension sagas each coordinate three services — no single service is a natural owner. Embedding them in the Order service would force it to consume Finance and Weather events, creating tight coupling. Choreography would scatter the workflow logic across services, making it nearly impossible to reason about the end-to-end flow or implement reliable compensation.

**The PaymentProcessingSaga is intentionally layered as a sub-saga.** The Purchase and Extension sagas delegate payment processing by publishing `PaymentRequestedEvent`, which triggers the PaymentProcessingSaga to orchestrate authorize → capture → complete. This creates a saga-within-saga pattern that:

- Keeps payment orchestration logic (retries, void compensation) in one place
- Allows reuse if future workflows need payment processing
- Keeps the Purchase/Extension sagas focused on their business flow

This layering adds indirection (two saga instances per purchase) but is justified by the separation of concerns. If payment processing were inlined into each business saga, retry logic and void/refund compensation would be duplicated.

## Consequences

### Positive

- Workflow logic for distributed transactions lives in one place — easy to understand, test, and modify
- Services remain simple command responders with no knowledge of multi-step flows
- Saga state is persisted to SQL Server with optimistic concurrency — durable and recoverable
- Each saga has configurable timeouts at every step, preventing indefinite hangs
- Compensation flows (refund, void) are explicit in the state machine, not scattered across services
- Single deployment unit for all orchestration simplifies operational monitoring

### Negative

- Extra deployment unit (the saga worker service) to operate and monitor
- The saga service becomes a coupling point — it must be updated when cross-service workflows change
- Saga-within-saga pattern (PaymentProcessingSaga) adds complexity when debugging end-to-end flows
- All sagas share a single SQL Server database for state; high saga volume could create contention

### Risks

- **Saga service as bottleneck**: If saga throughput becomes a concern, the service can be scaled horizontally since MassTransit supports concurrent saga processing with configurable concurrency limits
- **Schema evolution**: Adding new sagas or modifying existing ones requires deploying the saga service. Mitigation: sagas are isolated in their own folders with no shared state between them
- **PaymentProcessingSaga reuse assumption**: If Purchase and Extension remain the only two payment workflows, the extra layering may not pay off. Mitigation: the pattern is already implemented and working; removing it later is a simplification, not a migration

## Implementation Notes

- Sagas use MassTransit `MassTransitStateMachine<TState>` with EF Core persistence
- Kafka consumers use the consumer-adapter pattern: Avro events → internal saga events via `IPublishEndpoint`
- Consumer groups are per-saga: `saga-alert-subscription-purchase`, `saga-alert-subscription-extension`, `saga-payment-processing`
- Outbox pattern ensures saga state changes and published messages are transactionally consistent
- Saga folders: `Orders/AlertSubscriptionPurchaseSaga/`, `Orders/AlertSubscriptionExtensionSaga/`, `Finance/PaymentProcessingSaga/`

## Related Decisions

- Transactional Outbox pattern (platform library) — ensures reliable message delivery from saga state changes
- Avro serialization with Confluent Schema Registry — contract format for all cross-service events
