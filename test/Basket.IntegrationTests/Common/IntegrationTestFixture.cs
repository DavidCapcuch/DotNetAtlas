using Basket.Application.Abstractions;
using Basket.Infrastructure.Persistence.Database;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using OpenTelemetry.Trace;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Redis;
using Platform.Test.Framework.Tracing;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Basket.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    /// <summary>
    /// Stable test clock — shared with tests that want to assert on
    /// deterministic timestamps without re-importing FakeTimeProvider.
    /// </summary>
    public static readonly DateTimeOffset Now =
        new(2026, 04, 25, 12, 00, 00, TimeSpan.Zero);

    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Basket",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Basket/Basket.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [BasketDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();

    /// <summary>
    /// Test-controlled <see cref="IBasketRepository"/>. Tests configure return
    /// values per-scenario; the fixture exposes the substitute so they don't
    /// have to re-resolve from DI. The production
    /// <c>RedisBasketRepository</c> registration in
    /// <c>Basket.Infrastructure.Persistence.PersistenceDependencyInjection</c>
    /// is swapped via <c>Replace</c> in <see cref="ConfigureApp"/>.
    /// </summary>
    public IBasketRepository Repository { get; } = Substitute.For<IBasketRepository>();

    /// <summary>
    /// Test-controlled <see cref="IProductCatalogQueryPort"/>. Registered
    /// because Application DI requires it; not exercised by Checkout
    /// (snapshot-validation runs at AddItem time, not Checkout). The
    /// production HTTP adapter is swapped via <c>Replace</c> in
    /// <see cref="ConfigureApp"/>.
    /// </summary>
    public IProductCatalogQueryPort Catalog { get; } = Substitute.For<IProductCatalogQueryPort>();

    /// <summary>
    /// Stable <see cref="FakeTimeProvider"/> pinned at <see cref="Now"/>.
    /// Exposed for tests that want to advance time deterministically.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(Now);

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            var redisConnectionString = _redisContainer.ConfigurationOptions.ToString();
            webBuilder
                .UseSetting("ConnectionStrings:Basket", _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Basket", redisConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", redisConnectionString)
                // KafkaOptions.ValidateOnStart requires Brokers + SchemaRegistry + AvroSerializer
                // even though no Kafka container runs in IT — FakeOutboxWriter handles the publish
                // path so a real cluster isn't needed. Mirrors Basket.FunctionalTests' approach for
                // the SchemaRegistry URL placeholder.
                .UseSetting("Kafka:Brokers:0", "kafka-not-used-in-integration-tests:9092")
                .UseSetting("Kafka:SchemaRegistry:Url", "http://schema-registry-not-used-in-integration-tests:8081")
                .UseSetting("Kafka:AvroSerializer:AutoRegisterSchemas", "false")
                .UseSetting("Kafka:AvroSerializer:SubjectNameStrategy", "Record")
                .UseSetting("Kafka:AvroSerializer:NormalizeSchemas", "true");
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
                // Basket.Infrastructure does not call AddOpenTelemetry() (unlike Notifications/
                // Catalog/etc.) so TracerProvider is absent from the production composition.
                // Register a minimal tracer that listens on TestActivitySource so
                // TestCaseTracer (Platform.Test.Framework.Tracing) can resolve TracerProvider
                // from DI. Same pattern test fixtures use to plug observability gaps without
                // editing Basket.Infrastructure (outside this agent's file-ownership island).
                services.AddOpenTelemetry()
                    .WithTracing(tracing => tracing.AddSource(TestActivitySource.ActivitySourceName));

                // Pin the clock so deterministic timestamp assertions work without
                // each test re-creating a FakeTimeProvider.
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(FakeTime));

                // Swap the production RedisBasketRepository with the NSubstitute so tests
                // can stub repository responses without standing up basket state in Redis.
                // Use Replace so the production scoped registration is removed — AddSingleton
                // would only add a second descriptor with the proxy's runtime type, leaving
                // the real adapter live for resolution.
                services.Replace(ServiceDescriptor.Singleton<IBasketRepository>(Repository));

                // Swap the Catalog HTTP adapter for the substitute — application DI requires
                // the port, but DB-backed integration tests don't exercise the HTTP roundtrip.
                services.Replace(ServiceDescriptor.Singleton<IProductCatalogQueryPort>(Catalog));

                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter with a
                // fake that writes a stub OutboxMessage row directly. M6 owns "the right
                // outbox row hits Postgres" — Avro byte-level fidelity is decoupled (matches
                // Inventory + Ordering precedent of not standing up Schema Registry just
                // for outbox shape assertions).
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());
            });
    }

    /// <summary>Creates a per-test DI scope; caller disposes.</summary>
    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>Connection string for tests that bypass the DbContext.</summary>
    public string ConnectionString => _dbContainer.ConnectionString;

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
    }
}
