# DotNetAtlas.Test.Framework

TestContainers‑based components for simplyfing spinning up infrastructure setup and state management in integration and functional tests. Components encapsulate setup, DI config, and fast state resets via simple **StartAsync/CleanDataAsync/DisposeAsync**.

- **SQL Server**: Flyway-style migrations via Evolve, fast resets via Respawn, pre-configured ConnectionString
- **Redis**: flush-all resets, pre-configured ConfigurationOptions.
- **Kafka + Schema Registry** with config encapsulation

## Quick start

### [SQL Server](Database/SqlServerTestContainer.cs)

```csharp
using DotNetAtlas.Test.Framework.Database;

var sqlServer = new SqlServerTestContainer(
    databaseName: "Weather",
    flywayMigrationsPath: SolutionPaths.FlywayMigrationsDirectory,
    new RespawnerOptions
    {
        SchemasToInclude = ["weather", "HangFire"]
    });

await sqlServer.StartAsync();

// Use for DI
builder.UseSetting("ConnectionStrings:Weather", sqlServer.ConnectionString);

// Between tests
await sqlServer.CleanDataAsync();

// Teardown
await sqlServer.DisposeAsync();
```

### [Redis](Redis/RedisTestContainer.cs)

```csharp
using DotNetAtlas.Test.Framework.Redis;

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
using DotNetAtlas.Test.Framework.Kafka;

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
using DotNetAtlas.Test.Framework.Kafka;
using Weather.Contracts;

// Assumes KafkaTestContainer was started and provides options
var options = kafka.KafkaOptions;

// Create during setup
var consumer = new KafkaTestConsumer<ForecastRequestedEvent>(
    bootstrapServers: options.BrokersFlat,
    schemaRegistryUrl: options.SchemaRegistry.Url,
    topic: "forecast-requested");

var one = consumer.ConsumeOne(TimeSpan.FromSeconds(5));
var many = consumer.ConsumeAll(TimeSpan.FromSeconds(5), maxCount: 10);

consumer.Dispose();
```

### [TestCaseTracer](Tracing/TestCaseTracer.cs)

**Why to use it:**
- Correlates each test run with an OpenTelemetry activity and surfaces failures in traces.
- Exposes TraceId for propagating context to the SUT (e.g., via HTTP headers).

**Where to use it:**
- Wrap each integration/functional test, or create/dispose in your fixture's setup/teardown.
- Pass your test DI ServiceProvider so it uses the same tracing pipeline as the app under test.
```csharp
using DotNetAtlas.Test.Framework.Tracing;

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

## Tips

- Start multiple containers in parallel to speed up setup:
```csharp
await Task.WhenAll(
    sqlContainer.StartAsync(),
    redisContainer.StartAsync(),
    kafkaContainer.StartAsync()
);
```
- Keep container images in sync with production; update here early when upgrading infra.

For advanced usage and troubleshooting, see Testcontainers for .NET: https://dotnet.testcontainers.org/
