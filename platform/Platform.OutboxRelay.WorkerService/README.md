# Platform.OutboxRelay.WorkerService

Background worker service that polls the outbox table and publishes messages to Kafka, completing the [Transactional Outbox pattern](https://microservices.io/patterns/data/transactional-outbox.html).

## The Problem

Messages stored in the outbox table need to be reliably delivered to Kafka. This requires:

- Periodic polling to detect new messages
- Ordered delivery with at-least-once guarantees
- Graceful shutdown without message loss
- Health monitoring and observability

## The Solution

A dedicated worker service that runs independently, polling the outbox table and publishing messages to Kafka. Runs as a containerized service for easy deployment and scaling.

## Features

- 🔄 **Periodic Polling** - Configurable polling interval and batch size
- 📤 **At-Least-Once Delivery** - Only deletes messages after confirmed delivery
- 🗺️ **Producer-Driven Topic Routing** - Topic is determined by producers and stored in `OutboxMessage.TopicName`
- 🏥 **Health Checks** - Built-in health endpoints for Kubernetes probes
- 📊 **Observability** - OpenTelemetry tracing and metrics
- ⚡ **Graceful Shutdown** - Configurable flush and shutdown timeouts

## Deployment

### Production (Recommended)

Package as a Docker image and distribute via container registry:

```bash
# Build the image
docker build -f platform/Platform.OutboxRelay.WorkerService/Dockerfile -t myregistry/outbox-relay:1.0.0 .

# Push to registry
docker push myregistry/outbox-relay:1.0.0
```

Then deploy to Kubernetes, ECS, or any container orchestrator with environment-based configuration.

### Local Development

Use docker-compose and run along with the main application:

```yaml
outbox-relay-catalog:
  build:
    context: .
    dockerfile: platform/Platform.OutboxRelay.WorkerService/Dockerfile
  container_name: outbox-relay-catalog
  restart: unless-stopped
  depends_on:
    postgresdb:
      condition: service_healthy
    kafka:
      condition: service_healthy
    schema-registry:
      condition: service_healthy
  environment:
    - ConnectionStrings__Outbox=Host=postgresdb;Port=5432;Database=Catalog;Username=postgres;Password=${POSTGRES_PASSWORD}
    - KafkaProducer__BootstrapServers=broker:9092
    - KafkaProducer__ClientId=outbox-relay-catalog-worker
    # Set these per relay - see Configuration below.
    - OutboxRelay__SchemaName=catalog
    - OutboxRelay__TableName=outbox_messages
    - OutboxRelay__PollingIntervalMs=2000
    - OTEL_SERVICE_NAME=CatalogOutboxRelay
    - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
  ports:
    - "8091:8080"  # Health checks endpoint
```

## Configuration

Both options sections are validated before the host starts, so a misconfigured relay is refused
rather than left publishing under a setting nobody chose. Shipped values live in
[`appsettings.json`](./appsettings.json), overlaid for local runs by
[`appsettings.Development.json`](./appsettings.Development.json); deployments override what varies
per relay through `Section__Setting` environment variables, as the compose sample above does.

What those files cannot tell you:

- **`OutboxRelay:SchemaName` and `:TableName` carry no base-layer default on purpose** — each relay
  drains exactly one schema, so a deployment is expected to name its own rather than inherit one.
  Do not lean on that refusal, though: the Development layer does supply `catalog`, and every relay
  in `docker-compose.yaml` runs under it, so one that lost its env var binds that schema instead of
  failing. [`OutboxRelayOptions`](./OutboxRelay/Config/OutboxRelayOptions.cs) carries every relay
  setting, its bounds, and the flush-before-shutdown invariant.
- **Each producer setting the validator lists must be stated explicitly, and `EnableIdempotence`
  must be `true`** —
  [`KafkaProducerOptionsValidator`](./OutboxRelay/Config/KafkaProducerOptions.cs) names them and
  says why idempotence is the one librdkafka will not catch for you. librdkafka rejects the other
  contradictions — `Acks`, in-flight limit, retry count — when it builds the producer, so this repo
  does not re-check them.
- **Settings bind by `ProducerConfig` property name** — `MessageTimeoutMs`, not
  `message.timeout.ms`. The remarks on
  [`KafkaProducerOptions`](./OutboxRelay/Config/KafkaProducerOptions.cs) explain what a key matching
  no real setting costs.

## Health Checks

The service exposes health endpoints on port 8080:

- `/api/healthz` - Liveness probe
- `/api/readiness` - Readiness probe (includes Kafka and DB connectivity)

## Related Packages

- [Platform.ReliableMessaging.Outbox.Core](../Platform.ReliableMessaging.Outbox.Core) - Outbox entity
- [Platform.ReliableMessaging.Outbox.EFCore](../Platform.ReliableMessaging.Outbox.EFCore) - EF Core integration for adding messages
