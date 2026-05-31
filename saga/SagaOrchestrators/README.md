# SagaOrchestrators

Centralized saga worker hosting the MassTransit state machines that orchestrate cross-BC workflows for the eShop reference solution. Per [ADR-0001](../../docs/adr/0001-centralized-saga-orchestration.md), all sagas in this repo live in this single worker; per BC owns no saga code of its own.

## Hosted sagas

| State machine | Folder | Responsibility | Companion ADR |
|---|---|---|---|
| `CheckoutSaga` | [`Checkout/CheckoutSaga/`](Checkout/CheckoutSaga/) | Orders a basket end-to-end: Basket → Ordering → Inventory → Payments → confirmation, with compensation on any failure. | [ADR-0004](../../docs/adr/0004-checkout-saga-topology.md) |
| `PaymentProcessingSaga` | [`Payments/PaymentProcessingSaga/`](Payments/PaymentProcessingSaga/) | Drives a single payment lifecycle (authorize → capture → void / refund) for the Payments BC; invoked by `CheckoutSaga` via `RequestPaymentCommand`. | [ADR-0004](../../docs/adr/0004-checkout-saga-topology.md), [ADR-0023](../../docs/adr/0023-payments-event-vs-command-classification.md) |

## Stack

- **MassTransit** `MassTransitStateMachine<TState>` for declarative orchestration with EF Core persistence and optimistic concurrency via `RowVersion`.
- **PostgreSQL** (`saga` schema) for state — see [`Common/Persistence/Database/`](Common/Persistence/Database/).
- **KafkaFlow** for inbound consumers and Avro deserialization via the platform's `SchemaRegistry` integration.
- **OpenTelemetry** activities + metrics — see [`Common/Observability/`](Common/Observability/).

## Project layout

```
SagaOrchestrators/
├── Checkout/CheckoutSaga/
│   ├── Consumers/             # Kafka consumers that lift external events into the state machine
│   ├── InternalSagaEvents/    # MassTransit `Event<T>` definitions for the saga's transitions
│   ├── Observability/         # CheckoutSagaActivitySource + CheckoutSagaMetrics
│   └── Schedules/             # Per-step timeout schedules (order creation, stock, payment, confirm, compensation)
├── Payments/PaymentProcessingSaga/
│   ├── Consumers/             # Kafka consumers on payments.transactions + payments.payment-commands
│   ├── InternalSagaEvents/
│   ├── Observability/         # Activities (PaymentProcessingSagaMetrics lives under Common/)
│   └── Schedules/             # Authorization / Capture / Void / Refund / SuccessFinalization timeouts
└── Common/
    ├── Config/Kafka/          # SagaTopicsOptions, SagaConsumerGroupsOptions, SagaKafkaOptions
    ├── Observability/         # Shared SagaActivitySource + per-saga metric classes
    ├── Persistence/Database/  # SagaDbContext, interceptors, EF migrations
    ├── SagaAbstractions/      # Shared base types used by both state machines
    └── SagasDependencyInjection/ # MassTransit + KafkaFlow + EF Core wiring
```

## Consumer groups

Per the one-group-per-service rule in [`events-catalog.md § 3.1`](../../docs/bc-design/events-catalog.md), every BC owns a single Kafka consumer group named `{service}-group`. **Sagas are the documented exception:** each state machine in this worker is its own logical service per ADR-0001 and gets its own group.

| Saga | Consumer group | Subscribed topics |
|---|---|---|
| `CheckoutSaga` | `saga-checkout` | `basket.sessions`, `ordering.orders`, `inventory.reservations`, `payments.transactions` |
| `PaymentProcessingSaga` | `saga-payment-processing` | `payments.transactions`, `payments.payment-commands` (consumes `RequestPaymentCommand`) |

The two groups share `payments.transactions` but subscribe to disjoint Avro event types — no observable interleaving risk.

## Configuration

Bound from [`appsettings.json`](appsettings.json) via `Saga`, `Kafka`, `SagaHealthCheck`, and `HealthChecks` sections; all options classes live under [`Common/Config/`](Common/Config/) and validate on start. See the in-tree options classes (`SagaTopicsOptions`, `SagaConsumerGroupsOptions`, `SagaTimeoutsOptions`, `SagaHealthCheckOptions`) for the canonical schemas.

## Observability

### Metrics (OpenTelemetry)

Emitted by [`PaymentProcessingSagaMetrics`](Common/Observability/Metrics/PaymentProcessingSagaMetrics.cs) and [`CheckoutSagaMetrics`](Checkout/CheckoutSaga/Observability/CheckoutSagaMetrics.cs).

Checkout family (`saga.checkout.*`): `initiated`, `confirmed`, `failed`, `compensated`, `stuck`, `stock_reservation_failed`, `payment_failed`, plus per-step timeout counters and per-step duration histograms (`order_creation`, `stock_reservation`, `payment`, `confirmation`, `compensation`, `total`).

Payment family (`saga.payments.*`): `started`, `authorizations.{completed,failed}`, `captures.{completed,failed}`, `voids.completed`, `refunds.{requested,completed}`, `completed`, `timedout`, and a `duration` histogram.

### Tracing

[`SagaActivitySource`](Common/Observability/Tracing/SagaActivitySource.cs) hosts the shared `ActivitySource`; per-saga sources (`CheckoutSagaActivitySource`) prefix span names with their saga family. Distributed traces wrap saga lifecycle transitions, Kafka consumer pipelines, and the EF Core save units of work.

### Health checks

Exposed via `/health/ready`, `/health/live`, `/health`. The custom saga health check (see [`Common/Observability/HealthChecks/`](Common/Observability/HealthChecks/)) flags long-running non-terminal states using `SagaHealthCheck:StuckSagaThresholdMinutes` and reports `Degraded` / `Unhealthy` once `Max*` thresholds are crossed.

## Running locally

```bash
docker compose --profile full up -d   # postgres, kafka, schema-registry, ...
dotnet run --project saga/SagaOrchestrators
```

The local connection string in [`appsettings.json`](appsettings.json) points at `postgres5433` (port 5433 → containerized Postgres on 5432) and database `Saga`, matching the compose service. The `Saga` database is created by the platform's per-BC postgres init step.

## Testing

```bash
dotnet test saga/SagaOrchestrators.UnitTests
dotnet test saga/SagaOrchestrators.IntegrationTests   # Testcontainers — strip HTTP_PROXY on Windows
```

Saga state-machine tests use MassTransit's `SagaTestHarness<TState>`; integration tests spin up real Kafka + Postgres + Schema Registry via Testcontainers.

## Related docs

- [`docs/bc-design/checkout-saga.md`](../../docs/bc-design/checkout-saga.md) — Checkout saga full state machine, timeouts, compensation matrix.
- [`docs/bc-design/payments.md`](../../docs/bc-design/payments.md) — Payments BC + PaymentProcessingSaga collaboration.
- [`docs/bc-design/events-catalog.md`](../../docs/bc-design/events-catalog.md) — authoritative event / topic / consumer-group catalog.
- [`docs/bc-design/saga-stuck-runbook.md`](../../docs/bc-design/saga-stuck-runbook.md) — ops runbook for stuck sagas.
- [ADR-0001](../../docs/adr/0001-centralized-saga-orchestration.md), [ADR-0004](../../docs/adr/0004-checkout-saga-topology.md), [ADR-0023](../../docs/adr/0023-payments-event-vs-command-classification.md).
