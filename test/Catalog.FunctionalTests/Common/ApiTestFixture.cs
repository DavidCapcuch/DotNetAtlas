using Catalog.FunctionalTests.Common.TestClientInfrastructure;
using Catalog.Infrastructure.Common.Config;
using Catalog.Infrastructure.Persistence.Database;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ClearExtensions;
using OpenFeature;
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

namespace Catalog.FunctionalTests.Common;

internal sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// Functional-test fixture for the Catalog HTTP surface.
/// Spins Postgres + Redis + Kafka (KRaft) Testcontainers. Schema comes from the same
/// idempotent V*.sql scripts Flyway runs in compose (#269) — Integration and Functional
/// fixtures share one source of truth.
/// Replaces the production <see cref="IOutboxWriter"/> with <see cref="FakeOutboxWriter"/>
/// (skips Schema Registry; byte-fidelity is asserted by the dedicated
/// <c>EndToEnd/AvroByteFidelityTests</c>), pins <see cref="TimeProvider"/> to
/// <see cref="Now"/>, replaces <see cref="IFeatureClient"/> with an NSubstitute mock so
/// tests can flip <c>catalog.show-discontinued-in-search</c> per-scenario, and trusts the
/// <see cref="FakeTokenSigner"/>'s RSA key via <see cref="JwtBearerTestExtensions.ConfigureJwtBearerForTests"/>.
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    /// <summary>Stable test clock — matches <c>Catalog.IntegrationTests.IntegrationTestFixture.Now</c>.</summary>
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

    private readonly FakeTokenSigner _signer = new(audience: "catalog-service-tests");

    /// <summary>
    /// Test-controlled <see cref="IFeatureClient"/>. Defaults all flags to <c>false</c>; tests
    /// override per-scenario by calling <c>fixture.FeatureClient.GetBooleanValueAsync(...).Returns(...)</c>.
    /// </summary>
    public IFeatureClient FeatureClient { get; } = Substitute.For<IFeatureClient>();

    /// <summary>Mutable test clock — call <c>Advance(...)</c> to move time forward in a test.</summary>
    public FakeTimeProvider TimeProvider { get; } = new(Now);

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    public string PostgresConnectionString => _dbContainer.ConnectionString;

    public IDatabase RedisCacheDb => ConnectionMultiplexer
        .Connect(_redisContainer.ConfigurationOptions)
        .GetDatabase();

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();
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
                // assertions match across CI runs (mirrors IntegrationTestFixture).
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider));

                // Replace the OpenFeature client so per-test feature-flag flips don't depend
                // on a JSON file on disk. The closed M5 follow-up
                // (catalog.show-discontinued-in-search) is exercised by SearchProductsTests
                // by stubbing this mock.
                services.Replace(ServiceDescriptor.Singleton(FeatureClient));

                // Relax the OIDC scheme's HTTPS-metadata requirement BEFORE the framework's
                // default IPostConfigureOptions<OpenIdConnectOptions> runs. Using Configure
                // (IConfigureNamedOptions) ensures ordering: all IConfigureOptions run before
                // any IPostConfigureOptions, so the default post-configure sees
                // RequireHttpsMetadata=false and skips the HTTPS-authority throw.
                // PostConfigure would fire too late (after the default already threw).
                services.Configure<OpenIdConnectOptions>(
                    OpenIdConnectDefaults.AuthenticationScheme,
                    options => options.RequireHttpsMetadata = false);

                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps
                // every TokenValidationParameters flag at its production default
                // of TRUE. See Platform.Test.Framework.Auth.JwtBearerTestExtensions.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        FeatureClient.ClearSubstitute(ClearOptions.All);
        TimeProvider.SetUtcNow(Now);

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
        await _kafkaContainer.DisposeAsync();
    }
}
