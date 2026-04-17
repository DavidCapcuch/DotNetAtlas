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
outbox-relay:
  build:
    context: .
    dockerfile: platform/Platform.OutboxRelay.WorkerService/Dockerfile
  container_name: outbox-relay
  restart: unless-stopped
  depends_on:
    mssqldb:
      condition: service_healthy
    kafka:
      condition: service_healthy
    schema-registry:
      condition: service_healthy
  environment:
    - ConnectionStrings__Outbox=Server=mssqldb,1433;Database=Weather;User Id=sa;Password=${MSSQL_SA_PASSWORD};TrustServerCertificate=true;
    - KafkaProducer__BootstrapServers=broker:9092
    - KafkaProducer__ClientId=outbox-relay-worker
    - OutboxRelay__PollingIntervalMs=2000
    - OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
  ports:
    - "8088:8080"  # Health checks endpoint
```

## Configuration

### OutboxRelay Options

| Setting              | Description                          | Default        |
| -------------------- | ------------------------------------ | -------------- |
| `PollingIntervalMs`  | How often to poll for new messages   | 1000           |
| `BatchSize`          | Max messages per batch               | 1000           |
| `SchemaName`         | Database schema for outbox table     | weather        |
| `TableName`          | Outbox table name                    | OutboxMessages |
| `FlushTimeoutMs`     | Kafka flush timeout                  | 30000          |
| `ShutdownTimeoutMs`  | Graceful shutdown timeout            | 60000          |

### KafkaProducer Options

| Setting             | Description                          | Default  |
|---------------------| ------------------------------------ | -------- |
| `BootstrapServers`  | Kafka broker addresses               | Required |
| `ClientId`          | Producer client identifier           | Required |
| `Acks`              | Acknowledgment level (None/Leader/All) | All      |
| EnableIdempotence   | Prevents duplicate messages during retries by ensuring exactly-once delivery semantics. When enabled, Kafka automatically assigns producer IDs and sequence numbers to detect and filter duplicates. Requires Acks=All. | true |
| `CompressionType`   | Message compression                  | None     |
| `LingerMs`          | Batching delay                       | 5        |

### Example appsettings.json

```json
{
  "ConnectionStrings": {
    "Outbox": "Server=localhost;Database=Weather;..."
  },
  "OutboxRelay": {
    "PollingIntervalMs": 1000,
    "BatchSize": 1000
  },
  "KafkaProducer": {
    "BootstrapServers": "localhost:9094",
    "ClientId": "outbox-relay-worker",
    "Acks": "All",
    "EnableIdempotence": true
  }
}
```

## Health Checks

The service exposes health endpoints on port 8080:

- `/api/healthz` - Liveness probe
- `/api/readiness` - Readiness probe (includes Kafka and DB connectivity)

## Related Packages

- [Platform.ReliableMessaging.Outbox.Core](../Platform.ReliableMessaging.Outbox.Core) - Outbox entity
- [Platform.ReliableMessaging.Outbox.EFCore](../Platform.ReliableMessaging.Outbox.EFCore) - EF Core integration for adding messages
