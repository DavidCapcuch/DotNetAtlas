# SagaOrchestrators

Centralized saga worker hosting the MassTransit state machines that orchestrate cross-BC workflows for the eShop reference solution. Per [ADR-0001](../../docs/adr/0001-centralized-saga-orchestration.md), all sagas in this repo live in this single worker; per BC owns no saga code of its own.

## Hosted sagas

| State machine | Folder | Responsibility | Companion ADR |
|---|---|---|---|
| `CheckoutSaga` | [`Checkout/CheckoutSaga/`](Checkout/CheckoutSaga/) | Orders a basket end-to-end: Basket → Ordering → Inventory → Payments → confirmation, with compensation on any failure. | [ADR-0004](../../docs/adr/0004-checkout-saga-topology.md) |
| `PaymentProcessingSaga` | [`Payments/PaymentProcessingSaga/`](Payments/PaymentProcessingSaga/) | Drives a single payment lifecycle for the Payments BC — authorize, then a capture-approval gate, then capture. A declined authorization fails outright; only a failure *after* authorization compensates via void. Invoked by `CheckoutSaga` via `RequestPaymentCommand`. | [ADR-0004](../../docs/adr/0004-checkout-saga-topology.md), [ADR-0023](../../docs/adr/0023-payments-event-vs-command-classification.md) |

## Stack

- **MassTransit** `MassTransitStateMachine<TState>` for declarative orchestration with EF Core persistence and optimistic concurrency via `RowVersion`.
- **PostgreSQL** (`saga` schema) for state — see [`Common/Persistence/Database/`](Common/Persistence/Database/).
- **KafkaFlow** for inbound consumers and Avro deserialization via the platform's `SchemaRegistry` integration.
- **OpenTelemetry** activities + metrics — see [`Common/Observability/`](Common/Observability/).

## Project layout

Each saga owns a folder under its BC name (`Checkout/CheckoutSaga/`, `Payments/PaymentProcessingSaga/`) holding its orchestrator and state class. Three subfolders are worth telling apart: `Consumers/` is the KafkaFlow edge lifting **external** events into the state machine, `InternalSagaEvents/` holds the MassTransit `Event<T>` types for the saga's **own** transitions, and `Schedules/` carries one timeout per step that can stall. `CheckoutSaga` additionally carries `Snapshots/` — the JSON shapes frozen into its saga state at checkout initiation, so the run no longer depends on the originating basket still existing.

Everything the two sagas share — config, persistence, observability, and the MassTransit + KafkaFlow + EF Core wiring — lives under [`Common/`](Common/).

## Consumer groups

Every BC owns a single Kafka consumer group; **sagas are the documented sole exception**, because each state machine in this worker is its own logical service per ADR-0001 and so gets its own group. The rule, the exception, and the group names live in [`events-catalog.md § 3.1`](../../docs/bc-design/events-catalog.md).

The runtime values are bound from the `Kafka:Topics` and `Kafka:ConsumerGroups` sections of [`appsettings.json`](appsettings.json) and subscribed in [`Common/SagasDependencyInjection/`](Common/SagasDependencyInjection/).

## Configuration

Bound from [`appsettings.json`](appsettings.json). The options classes live under [`Common/Config/`](Common/Config/) (Kafka's under [`Common/Config/Kafka/`](Common/Config/Kafka/)) and each names the section it binds. Validation happens on start, so a bad value fails the host rather than surfacing at the first message — but only for classes actually registered in [`SagaDependencyInjection`](Common/SagaDependencyInjection.cs); a class present in `Common/Config/` and absent there binds nothing.

## Observability

### Metrics (OpenTelemetry)

Each saga has its own metric family, named for it: `saga.checkout.*` from [`CheckoutSagaMetrics`](Checkout/CheckoutSaga/Observability/CheckoutSagaMetrics.cs) and `saga.payments.*` from [`PaymentProcessingSagaMetrics`](Common/Observability/Metrics/PaymentProcessingSagaMetrics.cs), each instrument declared there with its description. The stuck-saga gauge is the exception — it is published by [`SagaStateMachineHealthCheck`](Common/Observability/HealthChecks/SagaStateMachineHealthCheck.cs), next to the health check that computes it rather than in a metrics class.

### Tracing

[`SagaActivitySource`](Common/Observability/Tracing/SagaActivitySource.cs) hosts the shared `ActivitySource`; per-saga sources (`CheckoutSagaActivitySource`) prefix span names with their saga family. Distributed traces wrap saga lifecycle transitions, Kafka consumer pipelines, and the EF Core save units of work.

### Health checks

Endpoint paths are not owned here. [`Program.cs`](Program.cs) calls `MapPlatformHealthCheckEndpoints` and `UsePlatformHealthChecksPrometheusExporter` from [`Platform.ServiceDefaults`](../../platform/Platform.ServiceDefaults/WebApplicationExtensions.cs); the paths, and the readiness-vs-liveness tag split deciding which checks each endpoint evaluates, are declared in [`ServiceDefaultHealthCheckTags`](../../platform/Platform.ServiceDefaults/Config/ServiceDefaultHealthCheckTags.cs).

**Those constants are not the only copy.** [`docker-compose.yaml`](../../docker-compose.yaml)'s `x-readiness-healthcheck` anchor, the Prometheus scrape config, and this project's `launchSettings.json` each hardcode the path rather than deriving it, so changing a constant silently breaks them until they are changed too.

The custom saga health check ([`SagaStateMachineHealthCheck`](Common/Observability/HealthChecks/SagaStateMachineHealthCheck.cs)) flags long-running non-terminal states against the `SagaHealthCheck` thresholds ([`SagaHealthCheckOptions`](Common/Config/SagaHealthCheckOptions.cs) is its schema). It is registered `Degraded`, which the platform still serves as **200** — a stuck saga raises the alarm without pulling the instance out of rotation, since a restart would not unstick it. Only the DB and Kafka checks fail readiness outright.

## Running locally

```bash
docker compose --profile full up -d   # postgres, kafka, schema-registry, ...
dotnet run --project saga/SagaOrchestrators
```

The `ConnectionStrings:Saga` entry in [`appsettings.json`](appsettings.json) reaches the containerized Postgres over the host port published by the `postgresdb` service in [`docker-compose.yaml`](../../docker-compose.yaml). The database itself is created by the platform's per-BC postgres init step.

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
