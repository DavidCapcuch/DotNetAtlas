using System.Threading.Channels;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using SagaOrchestrators.Common.Config;
using SagaOrchestrators.Common.Config.Kafka;
using SagaOrchestrators.Common.Persistence.Database;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace SagaOrchestrators.IntegrationTests.Common;

/// <summary>
/// Test collection for saga integration tests sharing the fixture.
/// </summary>
[CollectionDefinition(nameof(SagaTestCollection))]
public sealed class SagaTestCollection : ICollectionFixture<SagaIntegrationTestFixture>;

/// <summary>
/// Integration test fixture for saga tests using WebApplicationFactory and real PostgreSQL container.
/// Provides MassTransit test harness with EF Core saga persistence and real Kafka consumers.
/// </summary>
public sealed class SagaIntegrationTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlTestContainer _dbContainer;
    private readonly KafkaTestContainer _kafkaContainer = new();

    public KafkaTestProducer KafkaProducer { get; private set; } = null!;

    private IBusControl? _busControl;

    public SagaIntegrationTestFixture()
    {
        var migrationsPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(), "saga", "SagaOrchestrators", "Common", "Persistence", "Database",
            "Migrations", "SqlScripts");

        _dbContainer = new PostgreSqlTestContainer(
            databaseName: "Saga",
            sqlScriptsMigrationsPath: migrationsPath,
            new RespawnerOptions
            {
                SchemasToInclude = [SagaDbContext.DefaultSchemaName]
            });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment("Testing")
            .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Saga)}", _dbContainer.ConnectionString)
            .UseKafkaSettings(_kafkaContainer.KafkaOptions)
            .ConfigureServices(services =>
            {
                var testOutputSink = new InjectableTestOutputSink();
                services.AddSingleton<IInjectableTestOutputSink>(testOutputSink);
                services.AddSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .MinimumLevel.Debug()
                        .WriteTo.InjectableTestOutput(testOutputSink)
                        .Enrich.FromLogContext();
                }, true, true);
            });
    }

    public async ValueTask InitializeAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _kafkaContainer.StartAsync();

        // we cannot access Services for IOptions here because that automatically starts the server, and the
        // server will fail to start without topics pre-created
        var topicsOptions = LoadTopicsFromConfiguration();
        await _kafkaContainer.CreateKafkaTopicsAsync(topicsOptions.GetAllTopics());

        KafkaProducer = new KafkaTestProducer(_kafkaContainer.KafkaOptions);

        // Start the MassTransit bus (includes SQL transport for internal saga messages/timeouts).
        // Accessing Services builds + starts the WAF host, which runs the bus's hosted service, so the
        // rider startup happens here. Bound it: a Kafka-rider startup hang would otherwise stall the
        // entire shared collection indefinitely (the class of failure behind #306) — WaitAsync fails
        // this fixture fast with a TimeoutException instead. 90s is generous headroom over a healthy
        // start (seconds), so it never false-trips on slow CI/Docker.
        await Task.Run(async () =>
        {
            _busControl = Services.GetRequiredService<IBusControl>();
            await _busControl.StartAsync();
        }).WaitAsync(TimeSpan.FromSeconds(90));
    }

    /// <summary>
    /// Cleans the saga-domain schema between [Fact]s. The MassTransit SQL transport scheduler
    /// tables (separate schema, set up by <c>UsePostgres</c> in <c>SagaDependencyInjection</c>)
    /// are intentionally NOT included here: armed timeouts that fire after a saga has already
    /// finalised are silently discarded by MassTransit's default missing-instance behaviour, and
    /// every test uses a fresh UUID v7 <c>CorrelationId</c> so no cross-test correlation
    /// is possible. If a future change sets <c>OnMissingInstance(Fault)</c> on any schedule, OR
    /// if a test ever needs to assert on transport-table state, extend
    /// <see cref="RespawnerOptions.SchemasToInclude"/> in the constructor to also clean the
    /// transport schema.
    /// </summary>
    public Task ResetDatabaseAsync() => _dbContainer.CleanDataAsync();

    public new async ValueTask DisposeAsync()
    {
        if (_busControl is not null)
        {
            await _busControl.StopAsync();
        }

        KafkaProducer.Dispose();

        // Defensive catch of OpenFeature SDK's process-global state cleanup race. The saga host
        // wires AddFeatureFlags, so the WebApplicationFactory's host stop sequence
        // calls HostedFeatureLifecycleService.StoppedAsync → Api.Instance.ShutdownAsync(), which
        // closes the static EventExecutor channel. If anything in the dispose chain re-enters the
        // shutdown (or if the channel is already closed by a prior in-process WAF instance), the
        // ChannelWriter.Complete throws ChannelClosedException — surfaces as a Test Collection
        // Cleanup Failure even though every test passed. Tests have already finalised their
        // assertions; cleanup is best-effort.
        try
        {
            await base.DisposeAsync();
        }
        catch (Exception ex) when (ContainsChannelClosedException(ex))
        {
            // Swallow — see comment above. Database + Kafka container cleanup still runs below.
            // Stay loud about it so a future ChannelClosedException wrapping a different bug
            // (i.e. one we did NOT expect) is still discoverable from the test output.
            Console.WriteLine(
                $"[SagaIntegrationTestFixture] Swallowed expected ChannelClosedException during dispose " +
                $"(OpenFeature SDK static-singleton shutdown race from AddFeatureFlags wiring in the saga host): {ex.GetType().FullName}: {ex.Message}");
        }

        await _dbContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }

    private static bool ContainsChannelClosedException(Exception ex) => ex switch
    {
        ChannelClosedException => true,
        AggregateException agg => agg.InnerExceptions.Any(ContainsChannelClosedException),
        _ => ex.InnerException is not null && ContainsChannelClosedException(ex.InnerException)
    };

    private static SagaTopicsOptions LoadTopicsFromConfiguration()
    {
        var sagasProjectPath = Path.Combine(
            SolutionPaths.GetSolutionRootDirectory(), "saga", "SagaOrchestrators");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(sagasProjectPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Testing.json", optional: false)
            .Build();

        return configuration.GetSection(SagaTopicsOptions.Section).Get<SagaTopicsOptions>()
               ?? throw new InvalidOperationException(
                   $"Failed to bind configuration section '{SagaTopicsOptions.Section}' to {nameof(SagaTopicsOptions)}. " +
                   "Verify appsettings.json contains the required Kafka topic values.");
    }
}
