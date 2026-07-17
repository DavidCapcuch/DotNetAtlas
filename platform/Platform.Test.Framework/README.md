# Platform.Test.Framework

TestContainers‑based components for simplifying spinning up infrastructure setup and state management in integration tests. Components encapsulate setup, DI config, and fast state resets via simple **StartAsync/CleanDataAsync/DisposeAsync**.

- **PostgreSQL**: SQL-script migrations via Evolve, fast resets via Respawn, pre-configured ConnectionString
- **Redis**: flush-all resets, pre-configured ConfigurationOptions.
- **Kafka + Schema Registry** with config encapsulation

## Quick start

### [PostgreSQL](Database/PostgreSqlTestContainer.cs)

```csharp
using Platform.Test.Framework;
using Platform.Test.Framework.Database;

var postgres = new PostgreSqlTestContainer(
    databaseName: "Catalog",
    sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Catalog/Catalog.Infrastructure"),
    new RespawnerOptions
    {
        SchemasToInclude = ["catalog"]
    });

await postgres.StartAsync();

// Use for DI
builder.UseSetting("ConnectionStrings:Catalog", postgres.ConnectionString);

// Between tests
await postgres.CleanDataAsync();

// Teardown
await postgres.DisposeAsync();
```

### [Redis](Redis/RedisTestContainer.cs)

```csharp
using Platform.Test.Framework.Redis;

var redis = new RedisTestContainer();

await redis.StartAsync();
builder.UseSetting("ConnectionStrings:Redis", redis.ConnectionString);

// Between tests
await redis.CleanDataAsync();

// Teardown
await redis.DisposeAsync();
```

### [Kafka + Schema Registry](Kafka/KafkaTestContainer.cs)

#### CI note (Linux runners, e.g. GitHub actions):
`host.docker.internal` is not resolvable from inside Docker containers run on Linux. 
Kafka and Schema Registry use dedicated network and connect via alias to avoid hangs in CI.

```csharp
using Platform.Test.Framework.Kafka;

var kafka = new KafkaTestContainer();

await kafka.StartAsync();

// Use for DI
var options = kafka.KafkaOptions;
// e.g. builder.UseSetting("Kafka:Brokers:0", options.Brokers[0]);
// e.g. builder.UseSetting("SchemaRegistry:Url", options.SchemaRegistry.Url);

// Teardown
await kafka.DisposeAsync();
```

### [KafkaTestConsumer<TValue>](Kafka/KafkaTestConsumer.cs)

See also [KafkaTestConsumerRegistry](Kafka/KafkaTestConsumerRegistry.cs)

```csharp
using Catalog.Products;
using Platform.Test.Framework.Kafka;

// Assumes KafkaTestContainer was started and provides options
var options = kafka.KafkaOptions;

// Create during setup
var consumer = new KafkaTestConsumer<ProductCreatedEvent>(
    bootstrapServers: options.BrokersFlat,
    schemaRegistryUrl: options.SchemaRegistry.Url,
    topic: "catalog.products");

var one = consumer.ConsumeOne(TimeSpan.FromSeconds(5));
var many = consumer.ConsumeAll(TimeSpan.FromSeconds(5), maxCount: 10);

consumer.Dispose();
```

### [TestCaseTracer](Tracing/TestCaseTracer.cs)

**Why to use it:**
- Correlates each test run with an OpenTelemetry activity and surfaces failures in traces.
- Exposes TraceId for propagating context to the SUT (e.g., via HTTP headers).

**Where to use it:**
- Wrap each integration test, or create/dispose in your fixture's setup/teardown.
- Pass your test DI ServiceProvider so it uses the same tracing pipeline as the app under test.
```csharp
using Platform.Test.Framework.Tracing;

// Create in test base/fixture constructor
var tracer = new TestCaseTracer(
    serviceProvider: Scope.ServiceProvider,
    testMethodName: TestContext.Current.TestMethod!.MethodName,
    testCaseId: TestContext.Current.TestCase!.UniqueID,
    testType: "integration");

// On dispose/teardown
if (TestContext.Current.TestState?.Result == TestResult.Failed)
{
    tracer.RecordTestFailure(TestContext.Current.TestState.ExceptionMessages);
}

tracer.Dispose();
```

## Migration-script drift policy

The same `V*.sql` files are consumed by two different runners (#269):

| Runner | Where | Tracking |
|---|---|---|
| **Evolve** | `PostgreSqlTestContainer` in integration tests | `changelog` table + per-file checksum |
| **Flyway** | Single one-shot `flyway` service in `docker-compose.yaml` (loops over all BC schemas incl. saga) | `flyway_schema_history` table per schema + per-file checksum |

Both tools refuse to re-apply a `V*.sql` file whose **content has changed** after it was first recorded. This is intentional — a changed checksum is the only reliable signal that production and tests have diverged. Once a `Vnnn__Name.sql` has been merged, treat it as **immutable**.

### Symptoms

- **Evolve (tests):** `EvolveException: Validate failed: Migration checksum mismatch for migration version <n>` raised inside `PostgreSqlTestContainer.StartAsync()`. The container starts but Evolve aborts before any tests run.
- **Flyway (compose):** The `flyway` service exits non-zero with `FlywayValidateException: Validate failed: Migration checksum mismatch for migration version <n>`. Because every BC API + outbox-relay gates on `flyway: condition: service_completed_successfully`, an exit-1 there blocks the whole stack.

### Recovery

| Environment | Recovery | Rationale |
|---|---|---|
| Local dev DB (compose) | `docker compose down -v` + `up -d` to wipe the postgres volume, OR `docker run --rm ... flyway/flyway:11-alpine repair` | Local data is throwaway. Wipe-and-resync is simplest. |
| Shared dev / staging | `flyway repair` against the shared DB | Preserves data. Requires the new checksum to actually match what production will apply. |
| Production | **Incident.** Escalate. Never repair without a deliberate rollback / forward-fix plan reviewed by whoever owns the data. | Drift in prod means tests and prod are no longer reading the same migration text. |
| Integration tests | Re-emit the script from the EF migration: `dotnet ef migrations script <from> <to> --idempotent --output ...SqlScripts/Vnnn__Name.sql`. Testcontainers are throwaway, so checksum mismatch in tests is always a "regenerate the file" situation, never a "repair" situation. | |

### Prevention

- Never edit a merged `V*.sql` to fix a defect. Emit a new `Vnnn+1__Fix_*.sql` instead.
- The per-BC `DatabaseMigrationFilesTests` arch test enforces `# EF migrations == # V*.sql files`. It catches the "added an EF migration but forgot to emit the SQL" case, not content drift — content drift is caught at runtime by Evolve / Flyway.
- CI runs `dotnet ef migrations has-pending-model-changes` per BC (`build-dotnet.yml`), which catches the "edited the model but forgot to emit any migration" case earlier.

## Tips

- Start multiple containers in parallel to speed up setup:
```csharp
await Task.WhenAll(
    postgresContainer.StartAsync(),
    redisContainer.StartAsync(),
    kafkaContainer.StartAsync()
);
```
- Keep container images in sync with production; update here early when upgrading infra.

For advanced usage and troubleshooting, see Testcontainers for .NET: https://dotnet.testcontainers.org/
