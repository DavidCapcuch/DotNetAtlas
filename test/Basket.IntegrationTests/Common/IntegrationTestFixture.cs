using Basket.Application.Abstractions;
using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Persistence.Database;
using Basket.IntegrationTests.Common.TestClientInfrastructure;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NSubstitute.ClearExtensions;
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

namespace Basket.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// The single Basket integration fixture: one real <c>Program.cs</c> host on Postgres + Redis
/// Testcontainers, shared by the whole <see cref="IntegrationTestCollection"/> (one instance,
/// state reset between tests). Both entrances run against it — the HTTP edge via
/// <see cref="HttpClientRegistry"/> and, for behaviour with no outer entrance, a DI scope off
/// <see cref="AppFixture{TEntryPoint}.Services"/>.
/// <para>
/// Schema is provisioned by the same idempotent <c>V*.sql</c> scripts Flyway runs in compose —
/// never a test-only <c>MigrateAsync</c>/<c>EnsureCreated</c>, so the tested schema matches the
/// deployed one. The production Avro+SchemaRegistry <see cref="IOutboxWriter"/> is replaced with
/// <see cref="FakeOutboxWriter"/> so outbox assertions need no Schema Registry round-trip; the
/// fake leaves the <c>AvroPayload</c> empty and captures topic + CLR type.
/// </para>
/// <para>
/// Basket's one unmanaged upstream — the Catalog ACL port <see cref="IProductCatalogQueryPort"/>
/// (<c>basket.md</c> § 12.2) — is an NSubstitute mock, cleared between tests, so tests stub
/// product snapshots without a Catalog service; which upstream HTTP shapes map to which error is
/// settled in the unit tier (<c>ProductCatalogHttpAdapterTests</c>). The
/// <see cref="FakeTokenSigner"/>'s RSA key is trusted via
/// <see cref="JwtBearerTestExtensions.ConfigureJwtBearerForTests"/>.
/// </para>
/// <para>
/// The host keeps the real <c>RedisBasketRepository</c> (so the basket store's cache behaviour and
/// keyed-multiplexer tracing stay observable) and <c>TimeProvider.System</c> (ADR-0015). A test
/// needing a deterministic CAS outcome or clock substitutes it into a directly-constructed SUT.
/// </para>
/// </summary>
// No [DisableWafCache]: FastEndpoints caches the host per fixture type, and IntegrationTestCollection
// builds this type once. Add it if the type is ever instantiated twice (a second collection, an
// IClassFixture or TestBase<IntegrationTestFixture>) — the second instance would reuse the first's
// host and never run PreSetupAsync.
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Basket",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Basket/Basket.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [BasketDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "basket-service");

    public IProductCatalogQueryPort Catalog { get; } = Substitute.For<IProductCatalogQueryPort>();

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

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
        HttpClientRegistry = new HttpClientRegistry<Program>(this, new FakeTokenCreator(_signer));
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            var redisConnectionString = _redisContainer.ConfigurationOptions.ToString();
            webBuilder
                .UseSetting($"ConnectionStrings:{nameof(ConnectionStringsOptions.Basket)}", _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Basket", redisConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", redisConnectionString)
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
                services.Replace(ServiceDescriptor.Singleton<IProductCatalogQueryPort>(Catalog));
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    /// <summary>
    /// Resolves the singleton <see cref="FakeOutboxWriter"/> so a test can read the integration
    /// events it captured — the committed outbox row carries an empty Avro payload.
    /// </summary>
    public FakeOutboxWriter GetFakeOutbox() =>
        (FakeOutboxWriter)Services.GetRequiredService<IOutboxWriter>();

    /// <summary>
    /// Wipes every table in the Basket schema between tests, flushes Redis, and clears the
    /// captured outbox messages.
    /// </summary>
    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        Catalog.ClearSubstitute(ClearOptions.All);
        GetFakeOutbox().Clear();

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
