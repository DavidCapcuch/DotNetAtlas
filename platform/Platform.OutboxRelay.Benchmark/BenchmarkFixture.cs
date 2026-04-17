using Confluent.SchemaRegistry;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Platform.OutboxRelay.WorkerService.Common.Config;
using Platform.OutboxRelay.WorkerService.OutboxRelay;
using Platform.OutboxRelay.WorkerService.OutboxRelay.Config;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using Serilog;
using Weather.Infrastructure.Messaging.Kafka.Config;
using Weather.Infrastructure.Persistence.Database;

namespace Platform.OutboxRelay.Benchmark;

/// <summary>
/// BenchmarkFixture with PostgreSQL and Kafka test containers for OutboxMessageRelay benchmarking.
/// </summary>
[DisableWafCache]
internal sealed class BenchmarkFixture : AppFixture<Platform.OutboxRelay.WorkerService.Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "OutboxBenchmark",
        sqlScriptsMigrationsPath:
        Path.Combine(SolutionPaths.GetSolutionRootDirectory(), "platform", "Platform.OutboxRelay.Benchmark", "Seed"),
        new RespawnerOptions
        {
            SchemasToInclude = [WeatherDbContext.DefaultSchemaName]
        });

    private readonly KafkaTestContainer _kafkaContainer = new();

    public KafkaOptions KafkaOptions { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        Log.Information("Starting benchmark infrastructure containers...");

        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _kafkaContainer.StartAsync());
        KafkaOptions = _kafkaContainer.KafkaOptions;

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
                    _kafkaContainer.KafkaOptions.BrokersFlat);
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
