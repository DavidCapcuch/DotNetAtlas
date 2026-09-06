# Platform.Kafka.TopicGuard

Reports whether the Kafka topics a service names actually exist, as a readiness check.

## The Problem

The broker runs with `auto.create.topics.enable=false`, because the `kafka-create-topic` block *is*
the topology — partitions and retention are chosen per topic class (`docs/kafka-topology.md`). With
auto-create off, producing to a name that was never provisioned does not fail fast: librdkafka
queues the message and retries metadata until `message.timeout.ms` expires, then throws
`Local: Message timed out`. That message names neither the topic nor the reason, and it arrives
minutes after startup, on whichever request happened to trigger the publish.

## The Solution

A readiness check comparing the declared set against cluster metadata:

| Broker | Topics | Result |
|---|---|---|
| answered | all present | `Healthy`, and latched — the broker is never contacted again |
| answered | some absent | `Unhealthy` naming **all** of them |
| silent | unknown | `Unhealthy`, carrying the `KafkaException` as the cause |

The process is not killed, because nothing here produces to Kafka on the request path — every
publish goes through the transactional outbox. A missing topic degrades async messaging, not HTTP,
so a service that stays up serving reads while reporting red is the more useful failure.

**Latched**, so the cost is one admin call per process, not one per probe: topic existence does not
change under a running service. A topic deleted at runtime therefore goes unnoticed until the next
restart — accepted deliberately.

## Quick Start

```csharp
// Health-checks builder, beside the service's other readiness checks
services.AddHealthChecks()
    .AddKafkaTopicsExistenceHealthCheck(
        kafkaOptions.BrokersFlat,
        topicsOptions.GetAllTopics(),
        name: "Kafka topics",
        tags: [ServiceDefaultHealthCheckTags.ReadinessTag]);
```

## The One Constraint

The check must never create the topic it is checking for. `KafkaTopicProbe` documents why the
all-topics metadata overload is the only safe one; do not "optimise" it to the per-topic overload,
which auto-creates on any broker that allows it and would then always pass.

## Related Packages

- `Platform.KafkaFlow.DeadLetter` — owns the `<topic><suffix>` dead-letter naming each
  `GetAllTopics()` mirrors for its consumed topics.
- `Platform.ServiceDefaults` — maps `/api/readiness` and owns the readiness/liveness tags.
