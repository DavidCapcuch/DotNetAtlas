using Catalog.Infrastructure.Common.Config;
using Catalog.Infrastructure.Persistence.Database;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Redis;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Catalog.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// Boots the real <c>Program.cs</c> host inside an <see cref="AppFixture{TEntryPoint}"/>
/// against Postgres + Redis + Kafka Testcontainers (containers mirror what
/// <see cref="Common.MessagingDependencyInjection"/> validates at startup; the
/// KafkaFlow consumer block is registered but Catalog's <c>Program.cs</c> intentionally
/// omits the <c>kafkaBus.StartAsync()</c> call so no consumer poll loop runs in tests).
/// Schema comes from the same idempotent V*.sql scripts Flyway runs in compose (#269).
/// Replaces the production <see cref="IOutboxWriter"/> with <see cref="FakeOutboxWriter"/>
/// so command-handler outbox assertions don't require a Schema Registry round-trip; pins
/// <see cref="TimeProvider"/> to <see cref="Now"/> so <c>OccurredOnUtc</c> +
/// <c>LastUpdatedAtUtc</c> assertions stay deterministic across CI runs.
/// </summary>
[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    /// <summary>Stable test clock — matches <c>Catalog.FunctionalTests.ApiTestFixture.Now</c>.</summary>
    public static readonly DateTimeOffset Now =
        new(2026, 04, 25, 12, 00, 00, TimeSpan.Zero);

    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Catalog",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Catalog/Catalog.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [CatalogDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    /// <summary>Test-controlled <see cref="FakeTimeProvider"/> — call <c>Advance(...)</c> per scenario.</summary>
    public FakeTimeProvider TimeProvider { get; } = new(Now);

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            var redisConfig = _redisContainer.ConfigurationOptions;
            webBuilder
                .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Catalog)}",
                    _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", redisConfig.ToString())
                .UseKafkaSettings(_kafkaContainer.KafkaOptions);
        });

        return base.ConfigureAppHost(a);
    }

    protected override void ConfigureApp(IWebHostBuilder a)
    {
        a
            .UseEnvironment("Testing")
            .ConfigureServices((context, services) =>
            {
                var injectableTestOutputSink = new InjectableTestOutputSink();
                services.AddSingleton<IInjectableTestOutputSink>(injectableTestOutputSink);
                services.AddSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .MinimumLevel.Debug()
                        .ReadFrom.Configuration(context.Configuration)
                        .WriteTo.InjectableTestOutput(injectableTestOutputSink)
                        .Enrich.FromLogContext();
                }, true, true);
            })
            .ConfigureTestServices(services =>
            {
                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter with a
                // fake. Avro byte fidelity is asserted in AvroByteFidelityTests with its own
                // Schema-Registry container; the rest of the suite stays fast.
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());

                // Pin the wall-clock so deterministic OccurredOnUtc / LastUpdatedAtUtc
                // assertions match across CI runs (mirrors ApiTestFixture).
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider));
            });
    }

    /// <summary>Creates a per-test DI scope; caller disposes.</summary>
    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>Connection string for tests that bypass the DbContext.</summary>
    public string ConnectionString => _dbContainer.ConnectionString;

    /// <summary>Wipes every table in the Catalog schema between tests and flushes Redis.
    /// Note: <see cref="TimeProvider"/> is intentionally NOT rewound — <see cref="FakeTimeProvider.SetUtcNow"/>
    /// rejects going backwards in time, and tests that advance the clock with
    /// <c>TimeProvider.Advance(...)</c> use relative offsets. The singleton lives for the
    /// fixture lifetime; tests are isolated by Postgres/Redis cleanup, not by clock rewind.</summary>
    public async Task ResetFixtureStateAsync()
    {
        await Task.WhenAll(
            _dbContainer.CleanDataAsync(),
            _redisContainer.CleanDataAsync()
        );
    }

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
