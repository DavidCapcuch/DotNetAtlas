using FastEndpoints.Testing;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.IntegrationTests.Common.TestClientInfrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Redis;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using StackExchange.Redis;

namespace Inventory.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// The single Inventory integration fixture: one real <c>Program.cs</c> host on Postgres + Redis
/// Testcontainers, shared by the whole <see cref="IntegrationTestCollection"/> (one instance, state
/// reset between tests). Every entrance runs against it — the HTTP edge via
/// <see cref="HttpClientRegistry"/>, the Kafka message via the typed handlers resolved from a DI
/// scope, and, for behaviour with no outer entrance at all (the reservation-expiry worker,
/// event-store internals), a scope off <see cref="AppFixture{TEntryPoint}.Services"/>.
/// <para>
/// Schema is provisioned by the same committed <c>V*.sql</c> scripts Flyway runs in compose — never
/// a test-only <c>MigrateAsync</c>/<c>EnsureCreated</c>, so the tested schema matches the deployed
/// one.
/// </para>
/// <para>
/// The stock-level cache is deliberately <b>not</b> faked: ADR-0034's read-through cache runs for
/// real against the <c>redis-cache</c> container, because Redis is state only Inventory observes and
/// the display-cache behaviour is part of what the slice tests assert.
/// </para>
/// <para>
/// No Kafka container: <c>Inventory.Api/Program.cs</c> guards the KafkaFlow cluster boot with
/// <c>!IsTesting()</c>, and the typed handlers are driven directly with the real Avro contract type
/// via <c>FakeKafkaMessageContext</c>. The broker address points at an unreachable host so a stray
/// production code path fails instead of leaking onto a real broker.
/// </para>
/// <para>
/// Per ADR-0015 the host's <c>TimeProvider.System</c> singleton is left in place — tests that need
/// deterministic time construct <c>FakeTimeProvider</c> locally and inject it into a
/// directly-constructed SUT, as <c>BackgroundJobs/ReservationExpiryWorkerTests</c> does.
/// </para>
/// </summary>
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Inventory",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Inventory/Inventory.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [InventoryDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "inventory-service");

    private ConnectionMultiplexer _redisMultiplexer = null!;

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public IConnectionMultiplexer RedisMultiplexer => _redisMultiplexer;

    /// <summary>Creates a per-test DI scope; caller disposes.</summary>
    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>Connection string for tests that bypass the DbContext (e.g. raw SQL pre-staging).</summary>
    public string ConnectionString => _dbContainer.ConnectionString;

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();

        _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(_redisContainer.ConfigurationOptions);
    }

    protected override ValueTask SetupAsync()
    {
        HttpClientRegistry = new HttpClientRegistry<Program>(this, new FakeTokenCreator(_signer));
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            var redisConfig = _redisContainer.ConfigurationOptions;
            webBuilder
                .UseSetting("ConnectionStrings:Inventory", _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", redisConfig.ToString())
                .UseUnreachableKafkaSettings();
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
                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter with the fake so
                // outbox assertions need no Schema Registry round-trip; the fake preserves topic,
                // key and CLR type, which is what those assertions read.
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());

                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps
                // every TokenValidationParameters flag at its production default
                // of TRUE. See Platform.Test.Framework.Auth.JwtBearerTestExtensions.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    /// <summary>Wipes every table in the Inventory schema between tests and flushes redis-cache.</summary>
    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        await Task.WhenAll(
            _dbContainer.CleanDataAsync(),
            _redisContainer.CleanDataAsync()
        );
    }

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _redisMultiplexer.DisposeAsync();
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
