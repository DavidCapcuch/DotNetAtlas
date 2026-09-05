using Confluent.SchemaRegistry;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.OutboxRelay.Benchmark.Seed;
using Platform.OutboxRelay.WorkerService.Common.Config;
using Platform.OutboxRelay.WorkerService.OutboxRelay;
using Platform.OutboxRelay.WorkerService.OutboxRelay.Config;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using Serilog;

namespace Platform.OutboxRelay.Benchmark;

/// <summary>
/// BenchmarkFixture with PostgreSQL and Kafka test containers for OutboxMessageRelay benchmarking.
/// </summary>
[DisableWafCache]
internal sealed class BenchmarkFixture : AppFixture<Platform.OutboxRelay.WorkerService.Program>
{
    /// <summary>
    /// Dedicated schema the benchmark provisions and drains. Must stay in sync with the schema created
    /// in Seed/V001__CreateOutboxMessagesTable.sql and the OutboxRelay:SchemaName supplied below.
    /// </summary>
    private const string BenchmarkSchemaName = "benchmark";

    /// <summary>
    /// Outbox table name - the platform default (snake_case). Matches V001 and OutboxRelay:TableName below.
    /// </summary>
    private const string OutboxTableName = "outbox_messages";

    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "OutboxBenchmark",
        sqlScriptsMigrationsPath:
        Path.Combine(SolutionPaths.GetSolutionRootDirectory(), "platform", "Platform.OutboxRelay.Benchmark", "Seed"),
        new RespawnerOptions
        {
            SchemasToInclude = [BenchmarkSchemaName]
        });

    private readonly KafkaTestContainer _kafkaContainer = new();

    public Platform.Test.Framework.Kafka.Config.KafkaOptions KafkaOptions { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        Log.Information("Starting benchmark infrastructure containers...");

        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _kafkaContainer.StartAsync());
        KafkaOptions = _kafkaContainer.KafkaOptions;

        await _kafkaContainer.CreateKafkaTopicsAsync([BenchmarkSeeder.SeededTopicName]);

        Log.Information("Containers started successfully");
    }

    protected override IHost ConfigureAppHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Outbox)}",
                    _dbContainer.ConnectionString)
                .UseSetting($"{KafkaProducerOptions.Section}:BootstrapServers",
                    _kafkaContainer.KafkaOptions.BrokersFlat)
                // Supplied explicitly because AppFixture runs in the "Testing" environment (no
                // appsettings.Testing.json), so these OutboxRelay keys are otherwise unbound.
                .UseSetting($"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.SchemaName)}",
                    BenchmarkSchemaName)
                .UseSetting($"{OutboxRelayOptions.Section}:{nameof(OutboxRelayOptions.TableName)}",
                    OutboxTableName);
        });

        return base.ConfigureAppHost(builder);
    }

    protected override void ConfigureApp(IWebHostBuilder builder)
    {
        builder
            .ConfigureServices(services =>
            {
                services.AddSingleton<ISchemaRegistryClient>(_ =>
                    new CachedSchemaRegistryClient(new Dictionary<string, string>
                    {
                        {
                            "schema.registry.url", KafkaOptions.SchemaRegistry.Url
                        }
                    }));
            })
            .ConfigureTestServices(services =>
            {
                // Remove OutboxRelayWorker HostedService to prevent background processing
                // OutboxMessageRelay is executed directly for benchmark
                var hostedServiceDescriptor = services
                    .FirstOrDefault(d => d.ServiceType == typeof(IHostedService) &&
                                         d.ImplementationType == typeof(OutboxRelayWorker));

                if (hostedServiceDescriptor != null)
                {
                    services.Remove(hostedServiceDescriptor);
                }
            });
    }

    protected override async ValueTask TearDownAsync()
    {
        Log.Information("Tearing down benchmark infrastructure...");

        await _dbContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();

        Log.Information("Infrastructure disposed");
    }
}
