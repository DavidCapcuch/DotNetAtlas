# Avro Schema Registry

This directory contains Avro schema definitions (.avsc) and scripts
to generate C# classes from them.
Avro schemas serve as a **contract** between a Producer and Consumer and
define the structure of events published to Kafka. \
Avro schemas are defined in json, but serialized and transported as binary.
You won't be able to see the payload in human readable form unless deserialized, e.g. in AKHQ Kafka UI.

## Quick Start

### Windows PowerShell

```bash
# Generate C# class from a avsc schema file
./generate-avro.ps1 ForecastRequestedEvent.avsc
```

# Overview

## Kafka Integration

### Schema Registry Relationship

Schemas in this directory are registered in the Kafka Schema Registry,
which serves as the central authority for Avro schema validation and evolution.
The relationship works as follows:

1. **Producer Side**: Applications serialize messages using the schema definition
2. **Schema Registry**: Stores and validates schemas, ensuring compatibility
3. **Consumer Side**: Applications deserialize messages using the registered schema

In a local environment like this, auto registration of avro schemas to schema registry is enabled for ease of development.
(controlled by `AutoRegisterSchemas` in Kafka [appsettings.json](../../src/DotNetAtlas.Api/appsettings.json) config) \
In a production environment, it is highly discouraged to have the auto registration enabled, 
as it leads to uncontrolled schema evolution and lack of governance. (See [Best Practices for Confluent Schema Registry](https://www.confluent.io/blog/best-practices-for-confluent-schema-registry/))

Instead of auto-registration, the recommended practice for production environments is to use a centralized repository
for managing and deploying schemas. The repository:

1. Has CI pipeline for validating Avro Schema evolutions according to the specified compatibility
   type https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html#compatibility-types
2. CD pipelile for pre-registration of avsc to the Schema Registry with REST API
3. Centrally generates language-specific data classes, not limited to C# only (e.g., C# NuGet packages, Java JARs) 
for use by producer and consumer applications.

Therefore, both producers and consumers use pre-registered schemas which are governed in a single place.

### Configuration

Schema Registry connection is configured in [appsettings.json](../../src/DotNetAtlas.Api/appsettings.json):

```json
{
  "Kafka": {
    "SchemaRegistry": {
      "Url": "http://localhost:8081"
    }
  }
}
```

## Example workflow: Adding new Schema

### 1. Create Avro Schema

Create a new `.avsc` file in same directory as this readme:

```json
{
  "type": "record",
  "name": "YourEventName",
  "namespace": "Weather.Contracts",
  "doc": "Description of your event",
  "fields": [
    {
      "name": "EventId",
      "type": {
        "type": "string",
        "logicalType": "uuid"
      },
      "doc": "Unique event identifier"
    },
    {
      "name": "OccurredOnUtc",
      "type": {
        "type": "long",
        "logicalType": "timestamp-millis"
      },
      "doc": "UTC timestamp when event occurred"
    }
  ]
}
```

### 2. Generate C# Class

Run the generation script with your schema file:

```bash
# Windows PowerShell
./generate-avro.ps1 YourEventName.avsc
```

### 3. Verify Generated File

Check that `YourEventName.cs` was created with the `ISpecificRecord` interface and correct namespace.

## Script Details

1. **Validates input**: Check that schema file exists
2. **Auto-install avrogen**: Installs Apache.Avro.Tools if not present
3. **Generate class**: Runs `avrogen` for the specified schema
4. **Output to same directory**: Generated .cs file is placed next to .avsc file

## Manual Generation (Without the PS script)

If you prefer to generate manually:

```bash
# Install avrogen
dotnet tool install --global Apache.Avro.Tools --version 1.12.0

# Generate for specific schema
avrogen -s FeedbackChangedEvent.avsc .
```

## Best Practices

### EventId Field

**Include for:**

- **Idempotent message processing**: Enable consumer-side deduplication using the Inbox pattern. Consumers persist
  incoming message IDs to track which events have been processed, preventing duplicate processing.
- **Debugging & tracing**: Each message has a unique identifier for easier troubleshooting and correlation across
  services.

```json
{
  "name": "EventId",
  "type": {
    "type": "string",
    "logicalType": "uuid"
  },
  "doc": "Unique event identifier for idempotent processing and debugging."
}
```

**Inbox Implementation Example (Consumer Side):**

```csharp
// Check if event already processed
if (await _processedEvents.ContainsAsync(message.EventId))
{
    return; // Skip duplicate
}

using var transaction = await dbContext.Database.BeginTransactionAsync();

// Logic
await ProcessEvent(message);

// Mark as processed
await _processedEvents.AddAsync(message.EventId);

await transaction.CommitAsync();
```

### OccurredOnUtc Field

**Include for:**

- **Audit trails**: Maintain accurate business event timelines for compliance and analytics.
- **Replaying events**: Support event sourcing by providing the original occurrence time.

```json
{
  "name": "OccurredOnUtc",
  "type": {
    "type": "long",
    "logicalType": "timestamp-millis"
  },
  "doc": "UTC timestamp when the event originally occurred. Critical for temporal ordering and audit trails."
}
```

**Important**: Use `timestamp-millis` (milliseconds since Unix epoch)

### Renaming Fields

- Don't rename - add alias instead:

```json
{
  "name": "NewName",
  "aliases": [
    "OldName"
  ],
  "type": "string"
}
```

## Recommended Resources

- [Apache Avro docs](https://avro.apache.org/docs/)
- [Schema Evolution docs](https://docs.confluent.io/platform/current/schema-registry/fundamentals/schema-evolution.html#schema-evolution)
- [Practical Schema Evolution with Avro](https://medium.com/expedia-group-tech/practical-schema-evolution-with-avro-c07af8ba1725)
- [Handling Incompatible Schema Changes with Avro](https://medium.com/expedia-group-tech/handling-incompatible-schema-changes-with-avro-2bc147e26770)
