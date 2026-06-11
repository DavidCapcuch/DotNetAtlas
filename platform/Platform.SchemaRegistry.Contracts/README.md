# Platform.SchemaRegistry.Contracts

Schema definitions for Kafka messages with C# class generation support.

## Why Schema Registry?

Kafka messages need a contract between producers and consumers. Without schema management:

- Message formats drift between services
- Breaking changes cause runtime failures
- No validation of message structure

**Solution:** Avro schemas define message contracts. This package contains `.avsc` schema files and generates C# classes from them. Schemas are registered in [Confluent Schema Registry](https://docs.confluent.io/platform/current/schema-registry/index.html) for validation and evolution.

## Quick Start

## Adding a New Schema (Local flow only)

1. **Create `.avsc` file** in the `platform/Platform.SchemaRegistry.Contracts` folder
2. **Run the generator** - the script moves the file to the correct location based on the namespace:

   ```bash
   ./generate-avro.ps1 SubscriptionExtensionActivationFailedEvent.avsc
   ```

### 1. Add Project Reference

```xml
<ProjectReference Include="..\..\platform\Platform.SchemaRegistry\Platform.SchemaRegistry.Contracts.csproj" />
```

### 2. Configure Schema Registry

Add to your `appsettings.json`:

```json
{
  "Kafka": {
    "SchemaRegistry": {
      "Url": "http://localhost:8081"
    },
    "AvroSerializer": {
      "SubjectNameStrategy": "Record",
      "AutoRegisterSchemas": false,
      "NormalizeSchemas": true
    }
  }
}
```

### 3. Register with KafkaFlow

```csharp
services.AddKafka(kafka => kafka
    .AddCluster(cluster => cluster
        .WithBrokers(kafkaOptions.Brokers)
        .WithSchemaRegistry(config => config.Url = kafkaOptions.SchemaRegistry.Url)
        .AddProducer<MyProducer>(producer => producer
            .AddMiddlewares(m => m
                .AddSchemaRegistryAvroSerializer(kafkaOptions.AvroSerializer)))));
```

## How It Works

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              PRODUCTION FLOW                                  │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  CI/CD Pipeline                      Schema Registry                         │
│  ┌─────────────────┐                ┌─────────────────┐                      │
│  │ 1. Validate     │───────────────▶│ 2. Pre-register │                      │
│  │    schema       │   compatible?  │    schema       │                      │
│  └─────────────────┘                └────────┬────────┘                      │
│                                              │                               │
│                                              ▼                               │
│  ┌─────────────────┐                ┌─────────────────┐                      │
│  │ 3. Producer     │───────────────▶│ 4. Consumer     │                      │
│  │    serializes   │    Kafka       │    deserializes │                      │
│  └─────────────────┘                └─────────────────┘                      │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Production:** Schemas are pre-registered via CI/CD pipeline *before* deployment. Auto-registration is disabled (`AutoRegisterSchemas: false`). This ensures schema compatibility is validated before any code changes are deployed.

**Local Development:** For convenience, auto-registration can be enabled to allow rapid iteration without manual schema registration.

| Environment | Auto-Register | Governance |
|-------------|---------------|------------|
| Production  | ❌ Disabled   | CI/CD validates and pre-registers schemas |
| Local       | ✅ Enabled    | None (development convenience) |

> 📚 See [Best Practices for Confluent Schema Registry](https://www.confluent.io/blog/best-practices-for-confluent-schema-registry/) for production governance strategies.

## Event Design

Designing events is one of the most important architectural decisions in an event-driven system. Events cross service boundaries and become part of your public API - changing them affects all consumers.

> 📚 This section is based on Confluent's [Designing Events and Event Streams](https://developer.confluent.io/courses/event-design/intro/) course and [Martin Fowler's Event-Driven taxonomy](https://martinfowler.com/articles/201701-event-driven.html).

### The Four Dimensions of Event Design

When designing events, consider these four dimensions:

| Dimension | Question | Trade-off |
|-----------|----------|-----------|
| **Facts vs. Deltas** | Record the whole state or just what changed? | Facts are self-contained but larger; deltas are smaller but require reconstruction |
| **Normalized vs. Denormalized** | Include related data or reference it? | Denormalized is self-contained but duplicates data; normalized requires joins |
| **Single vs. Multiple Types per Topic** | One event type per topic or many? | Single is simpler to consume; multiple reduces topic proliferation |
| **Discrete vs. Continuous** | One-off events or part of a workflow? | Affects how consumers correlate and process events |

### Data on the Inside vs. Outside

A critical concept: **data on the inside** (your internal models) differs from **data on the outside** (your public events).

- **Inside:** Fluid, can change frequently, optimized for your application's needs
- **Outside:** Stable, purpose-built for sharing, changes affect all consumers

Design your events as **Data Transfer Objects (DTOs)** - purpose-built for inter-process communication, not direct reflections of your internal models.

### Normalized vs. Denormalized Events

**Normalized events** force consumers to understand the producer's internal domain model:

```
❌ NORMALIZED: Consumers must join data from multiple events/calls

Producer emits separate events:
  UserCreatedEvent { UserId: "user-456" }
  SubscriptionPurchasedEvent { UserId: "user-456", Tier: "Pro" }

Consumer must:
  ❌ Track UserCreatedEvent to get user details
  ❌ Join with SubscriptionPurchasedEvent
  ❌ Know producer's internal entity structure
  ❌ Break if producer refactors internal model
```

**Denormalized events** are self-contained business facts. Consider a `SubscriptionPurchasedEvent`:

```json
// Billing.Subscriptions.SubscriptionPurchasedEvent (example schema)
{
  "UserId": "user-456",
  "PaymentTransactionId": "txn-789",
  "Tier": "Pro",
  "DurationDays": 30,
  "OccurredOnUtc": 1704067200000
}

// Consumer (Alerts Service):
// ✅ Has everything needed to activate subscription
// ✅ PaymentTransactionId for saga correlation if activation fails
// ✅ No knowledge of Billing Service's internal structure
```

**What to denormalize:**

| Denormalize (Include) | Keep Normalized (Reference) |
|-----------------------|-----------------------------|
| Commonly accessed data | Large, infrequently needed data |
| Stable, rarely-changing data | Frequently-updated data (e.g., inventory counts) |
| Data needed for immediate consumer action | Data consumers can tolerate fetching async |
| Data from within your bounded context | External data you don't own |

### Event Ordering and Topic Design

Kafka guarantees ordering **only within a single partition**. Use aggregate ID as the Kafka key:

```
✅ SINGLE TOPIC with aggregate ID key: Ordering guaranteed

Topic: reviews.feedbacks (key = FeedbackId)
  Partition 0: [
    FeedbackCreatedEvent(id=abc-123) @ offset 100,
    FeedbackChangedEvent(id=abc-123) @ offset 101
  ]

Consumer receives events in correct order for each aggregate.
```

For example, an OutboxRelay can map both `FeedbackCreatedEvent` and `FeedbackChangedEvent` to the same topic:

```json
// OutboxRelay TypeTopicMappings (example)
{
  "FeedbackCreatedEvent": "reviews.feedbacks",
  "FeedbackChangedEvent": "reviews.feedbacks"
}
```

**Topic design guidance:**

| Scenario | Topic Strategy | Reason |
|----------|----------------|--------|
| Aggregate lifecycle (Created/Changed) | Single topic | Ordering critical for state reconstruction |
| Saga/workflow events | Single topic | Ordering critical for compensation |
| Cross-bounded-context state transfer | Separate topics per event type | Different consumers, independent processing |
| Analytics/metrics | Separate topics | Loss-tolerant, no ordering requirements |

### Integration Events vs. Domain Events

Events crossing bounded context boundaries are **integration events** - they differ from internal domain events:

| Aspect | Domain Event | Integration Event            |
|--------|--------------|------------------------------|
| **Scope** | Within bounded context | Across bounded contexts      |
| **Audience** | Internal handlers | External services            |
| **Coupling** | Can reference internal types | Must be self-contained       |
| **Serialization** | Optional (in-memory) | Required (e.g. Avro)   |
| **Schema** | C# record | Avro schema + generated class |

**Transform domain events to integration events at the boundary:**

1. Domain raises internal `FeedbackCreatedDomainEvent`
2. Domain event handler transforms to `FeedbackCreatedEvent` (Avro)
3. Integration event added to Outbox table
4. Outbox processor publishes to Kafka

This keeps your domain model free to evolve while integration events remain stable contracts.

### Event Ownership

Events should be owned and published by the bounded context where the business fact originates:

| Event | Owner (Namespace) | Reason |
|-------|-------------------|--------|
| `SubscriptionPurchasedEvent` | Billing Service (`Billing.Subscriptions`) | Payment completed in Billing |
| `SubscriptionExtendedEvent` | Billing Service (`Billing.Subscriptions`) | Extension payment completed in Billing |
| `SubscriptionActivationFailedEvent` | Alerts Service (`Alerts.Subscriptions`) | Activation failed in Alerts |
| `FeedbackCreatedEvent` | Reviews Service (`Reviews.Feedback`) | Feedback created in Reviews |

**Avoid "God Events"** that contain data from multiple domains - include only data owned by your bounded context.

### Event Fundamentals

| Principle | Description |
|-----------|-------------|
| **Events are facts** | Represent something that happened. Use past tense: `OrderPlaced`, `FeedbackCreated` |
| **Events are immutable** | Cannot be changed once published. Design for extensibility from the start |
| **Events are self-describing** | Include enough context for consumers to process without additional lookups |
| **Use logical types** | Prefer `uuid`, `timestamp-millis`, `date` over raw strings/longs |

### Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Schema name | `{Entity}{Action}Event` | `FeedbackCreatedEvent` |
| Namespace | `{Domain}.{Subdomain}` | `Reviews.Feedback` |
| Field names | PascalCase | `UserId`, `OccurredOnUtc` |

### Schema Example

A well-designed event schema with all essential elements highlighted:

```json
{
  "type": "record",
  "name": "FeedbackCreatedEvent",                                    // ← Past tense naming
  "namespace": "Reviews.Feedback",                                   // ← Domain.Subdomain
  "doc": "Emitted when user creates new feedback.",                  // ← Schema-level documentation
  "fields": [
    {
      "name": "FeedbackId",                                          // ← Aggregate identifier
      "type": { "type": "string", "logicalType": "uuid" },           // ← Logical type, not raw string
      "doc": "Unique identifier of the feedback aggregate."          // ← Field-level documentation
    },
    {
      "name": "OccurredOnUtc",                                       // ← Required timestamp
      "type": { "type": "long", "logicalType": "timestamp-millis" }, // ← Logical type for timestamps
      "doc": "UTC timestamp when the feedback was created."
    },
    {
      "name": "Rating",
      "type": "int",
      "doc": "Feedback rating from 1 (poor) to 5 (excellent)."
    }
  ]
}
```

## Schema Evolution

Schema evolution is one of the most critical architectural decisions you'll make. The compatibility mode you choose determines what schema changes are allowed and has permanent implications for your system.

> ⚠️ An incorrectly chosen compatibility mode can prevent you from reading historical data. For example, if you're using event sourcing and need to replay the entire event history, you **must** use either **BACKWARD_TRANSITIVE** or **FULL_TRANSITIVE** mode.

### Compatibility Modes

Compatibility modes dictate what changes are allowed when evolving Avro, Protobuf, or JSON schemas. Schema Registry enforces these rules automatically, preventing incompatible changes from being registered.

> 📚 For format-specific allowed changes, see [Schema Evolution and Compatibility](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html#compatibility-types).

| Mode                  | Description                                                 | Example                                                                                | Upgrade First |
|-----------------------|-------------------------------------------------------------|----------------------------------------------------------------------------------------|---------------|
| `BACKWARD` (default)  | New consumer can read data from the previous version        | If v10 consumer, can read v9 *(but not guaranteed to read older v1-v8 and future v11)* | Consumers     |
| `BACKWARD_TRANSITIVE` | New consumer can read data from **all** historical versions | If v10 consumer, can read v1-v9 *(but not guaranteed to read future v11)*              | Consumers     |
| `FORWARD`             | Previous consumer can read data from the new version        | If v10 consumer, can read v11 *(but not guaranteed to read old v1-v9 and future v12)*  | Producers     |
| `FORWARD_TRANSITIVE`  | All historical consumers can read new data                  | If v10 consumer, can read all future v11+ (but not guaranteed to read old v1-v9)       | Producers     |
| `FULL`                | Both backward and forward compatible with the last version  | If v10, can read v9; if v9, can read v10                                               | Either        |
| `FULL_TRANSITIVE`     | Full compatibility with all versions (strictest)            | All versions can read all bidirectionally                                              | Either        |
| `NONE`                | ⚠️ No compatibility checking - use only for development     | No guarantees                                                                          | Coordinate    |

### Transitive vs Non-Transitive Modes

The key difference between transitive and non-transitive modes is **permanence of schema history**:

| Aspect | Non-Transitive (`BACKWARD`, `FORWARD`, `FULL`) | Transitive (`BACKWARD_TRANSITIVE`, `FORWARD_TRANSITIVE`, `FULL_TRANSITIVE`) |
|--------|------------------------------------------------|----------------------------------------------------------------------------|
| **Compatibility check** | Against the last version only | Against ALL previous versions                                              |
| **Field removal** | Can remove fields after one version | Fields must be kept forever (can deprecate, not remove)                    |
| **Historical replay** | Only guaranteed for adjacent versions | Guaranteed for entire history                                              |
| **Schema cleanup** | Easier to evolve over time | Schema can accumulate deprecated fields                                    |

**Example:** In `BACKWARD_TRANSITIVE`, if you add a field in v2, you can never remove it - even in v10 - because v10 must still be able to read v1 data that doesn't have that field.

### Choosing a Compatibility Mode

Per [Confluent's documentation](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html#backward-compatibility):

> "The main reason that BACKWARD compatibility mode is the default, and preferred for Kafka, is so that you can rewind consumers to the beginning of the topic."

**Recommendation by use case:**

| Use Case | Recommended Mode | Why |
|----------|------------------|-----|
| Most Avro schemas | `BACKWARD` (default) | Simple, allows gradual evolution |
| Event sourcing / full replay needed | `BACKWARD_TRANSITIVE` | Must read entire event history |
| Long-lived topics with many versions | `BACKWARD_TRANSITIVE` | Guarantees rewind to any point |
| Independent producer/consumer deployments | `FULL` or `FULL_TRANSITIVE` | No deployment order constraints |

### Handling Breaking Changes

When you must make an incompatible change, you have two options:

**Option 1: Multi-step compatible migration** (preferred)

1. Add a new field with a default value
2. Populate both old and new fields during transition
3. Communicate to consumers to migrate
4. Deprecate (but don't remove) the old field

**Option 2: New topic**

Create a new topic with the new schema. Simpler but requires coordinating the cutover.

> 📚 **Deep Dive:** For detailed examples, see Elliot West's excellent Expedia articles:
> - [Practical Schema Evolution with Avro](https://medium.com/expedia-group-tech/practical-schema-evolution-with-avro-c07af8ba1725) - Common evolution scenarios
> - [Handling Incompatible Schema Changes with Avro](https://medium.com/expedia-group-tech/handling-incompatible-schema-changes-with-avro-2bc147e26770) - Breaking change strategies

## Related Packages

- [Platform.Messaging.Abstractions](../Platform.Messaging.Abstractions) - Message header constants (`message.id`)
- [Platform.KafkaFlow.ProducerHeaders](../Platform.KafkaFlow.ProducerHeaders) - Auto-adds `message.id` header for idempotency
- [Platform.KafkaFlow.Inbox.EFCore](../Platform.KafkaFlow.Inbox.EFCore) - Consumer-side deduplication using `message.id`
- [Platform.KafkaFlow.DeadLetter](../Platform.KafkaFlow.DeadLetter) - Routes failed messages to Dead Letter Topics
- [Platform.ReliableMessaging.Outbox.EFCore](../Platform.ReliableMessaging.Outbox.EFCore) - Transactional outbox for reliable publishing

## Resources

### Official Documentation
- [Apache Avro Specification](https://avro.apache.org/docs/)
- [Confluent Schema Registry - Schema Evolution](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html)
- [Best Practices for Confluent Schema Registry](https://www.confluent.io/blog/best-practices-for-confluent-schema-registry/)

### Event Design & Architecture
- [Designing Events and Event Streams](https://developer.confluent.io/courses/event-design/intro/) - Confluent's comprehensive course on event design
- [What do you mean by "Event-Driven"?](https://martinfowler.com/articles/201701-event-driven.html) - Martin Fowler's taxonomy of event patterns
- [Event Sourcing](https://martinfowler.com/eaaDev/EventSourcing.html) - Martin Fowler on event sourcing fundamentals

### Practical Guides
- [Practical Schema Evolution with Avro](https://medium.com/expedia-group-tech/practical-schema-evolution-with-avro-c07af8ba1725) - Expedia's FAQ on schema evolution
- [Handling Incompatible Schema Changes with Avro](https://medium.com/expedia-group-tech/handling-incompatible-schema-changes-with-avro-2bc147e26770) - Migration strategies for breaking changes
