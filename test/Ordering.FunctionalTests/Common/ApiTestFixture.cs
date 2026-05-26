using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;
using Ordering.Infrastructure.Persistence.Database;
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

namespace Ordering.FunctionalTests.Common;

internal sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// FastEndpoints <see cref="AppFixture{TEntryPoint}"/> for the Ordering API.
/// Spins up Postgres + Redis Testcontainers via the platform helpers (Evolve
/// applies the same SQL scripts production runs), forces
/// <c>ASPNETCORE_ENVIRONMENT=Testing</c> so the host skips the saga-command
/// Kafka consumer (per <c>Ordering.API/Program.cs</c> <c>!IsTesting()</c>
/// guard), and wires the JwtBearer scheme to trust the in-fixture RSA signer
/// so <see cref="FakeTokenCreator"/>'s signed tokens authenticate without
/// relaxing any production <see cref="Microsoft.IdentityModel.Tokens.TokenValidationParameters"/>
/// flags.
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Ordering",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Ordering/Ordering.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [OrderingDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "ordering-service-tests");

    /// <summary>
    /// Exposes the fixture's RSA signer so tests can mint tokens with a
    /// custom claim shape (e.g. pinning the production "roles" array claim
    /// instead of the FakeTokenCreator default).
    /// </summary>
    public FakeTokenSigner Signer => _signer;

    /// <summary>
    /// Pinned to 2026-04-23 10:00 UTC so cancellation/ship/deliver
    /// timestamps in functional-test assertions stay deterministic.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(
        new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero));

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    protected override ValueTask SetupAsync()
    {
        TokenCreator = new FakeTokenCreator(_signer);
        HttpClientRegistry = new HttpClientRegistry<Program>(this, TokenCreator);
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            var redisConfig = _redisContainer.ConfigurationOptions;
            webBuilder
                .UseSetting("ConnectionStrings:Ordering", _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", redisConfig.ToString())
                // Ordering.API/Program.cs guards the Kafka boot with !IsTesting(), but
                // AddInfrastructure still binds Kafka options at DI time — point them at
                // unreachable hosts so any accidental use blows up loudly instead of
                // silently flowing to a real broker.
                .UseSetting("Kafka:Brokers:0", "kafka-not-used-in-functional-tests:9094")
                .UseSetting("Kafka:SchemaRegistry:Url", "http://schema-registry-not-used-in-functional-tests:8081")
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
                // Pin time so timestamp assertions are stable.
                services.AddSingleton<TimeProvider>(FakeTime);

                // Replace the real Avro/SchemaRegistry-backed outbox writer
                // with an in-memory fake. The outbox publisher domain-event
                // handlers fire on every SaveChanges; without this the seed
                // helpers attempt to talk to a non-existent schema registry.
                // Asserting on Kafka messages is the integration-tests' job;
                // functional tests only need the HTTP surface to round-trip.
                services.RemoveAll<IOutboxWriter>();
                services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps
                // every TokenValidationParameters flag at its production default
                // of TRUE. See Platform.Test.Framework.Auth.JwtBearerTestExtensions.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

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
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
