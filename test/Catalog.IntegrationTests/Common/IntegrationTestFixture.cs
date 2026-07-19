using Catalog.Infrastructure.Common.Config;
using Catalog.Infrastructure.Persistence.Database;
using Catalog.IntegrationTests.Common.TestClientInfrastructure;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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

namespace Catalog.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// The single Catalog integration fixture: one real <c>Program.cs</c> host on Postgres + Redis +
/// Kafka Testcontainers, shared by the whole <see cref="IntegrationTestCollection"/> (one instance,
/// state reset between tests). Both entrances run against it — the HTTP edge via
/// <see cref="HttpClientRegistry"/> and, for cross-cutting machinery with no outer entrance, a DI
/// scope off <see cref="AppFixture{TEntryPoint}.Services"/>.
/// <para>
/// Schema is provisioned by the same idempotent <c>V*.sql</c> scripts Flyway runs in compose
/// (#269) — never a test-only <c>MigrateAsync</c>/<c>EnsureCreated</c>, so the tested schema
/// matches the deployed one. The production Avro+SchemaRegistry <see cref="IOutboxWriter"/> is
/// replaced with <see cref="FakeOutboxWriter"/> so outbox assertions don't need a Schema Registry
/// round-trip; the fake leaves the <c>AvroPayload</c> empty and captures topic + CLR type.
/// <see cref="IFeatureClient"/> is an NSubstitute mock so
/// tests flip <c>catalog.show-discontinued-in-search</c> per scenario, and the
/// <see cref="FakeTokenSigner"/>'s RSA key is trusted via
/// <see cref="JwtBearerTestExtensions.ConfigureJwtBearerForTests"/>.
/// </para>
/// <para>
/// Per ADR-0015 the host's <c>TimeProvider.System</c> singleton is left in place — tests that need
/// deterministic time construct <c>FakeTimeProvider</c> locally and inject it into a
/// directly-constructed SUT (ADR-0015 line 104).
/// </para>
/// </summary>
// No [DisableWafCache]: FastEndpoints caches the WebApplicationFactory by entry point, and this
// is the only AppFixture<Program> in the project, so nothing can cross-wire onto its cached host.
// Re-add it only if a second AppFixture<Program> is introduced (e.g. a real-SchemaRegistry
// fixture for Contracts/) — two subtypes sharing this entry point would otherwise
// reuse the first-built host and its containers.
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Catalog",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Catalog/Catalog.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [CatalogDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "catalog-service");

    /// <summary>
    /// Test-controlled <see cref="IFeatureClient"/>. Defaults all flags to <c>false</c>; tests
    /// override per-scenario by calling <c>Fixture.FeatureClient.GetBooleanValueAsync(...).Returns(...)</c>.
    /// </summary>
    public IFeatureClient FeatureClient { get; } = Substitute.For<IFeatureClient>();

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

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
        HttpClientRegistry = new HttpClientRegistry<Program>(this, new FakeTokenCreator(_signer));
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
                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter with a fake so
                // command-handler outbox assertions stay fast (no Schema Registry round-trip).
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());

                // Replace the OpenFeature client so per-test feature-flag flips don't depend on a
                // JSON file on disk (e.g. catalog.show-discontinued-in-search, ADR-0014).
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

                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps every
                // TokenValidationParameters flag at its production default of TRUE. See
                // Platform.Test.Framework.Auth.JwtBearerTestExtensions.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    /// <summary>Wipes every table in the Catalog schema between tests and flushes Redis.</summary>
    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        FeatureClient.ClearSubstitute(ClearOptions.All);

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
