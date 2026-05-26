using Basket.Application.Abstractions;
using Basket.FunctionalTests.Common.TestClientInfrastructure;
using Basket.Infrastructure.Persistence.Database;
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
using StackExchange.Redis;

namespace Basket.FunctionalTests.Common;

internal sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Basket",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Basket/Basket.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [BasketDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();
    private readonly KafkaTestContainer _kafkaContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "basket-service-tests");

    private ConnectionMultiplexer _redisMultiplexer = null!;

    /// <summary>
    /// Test-controlled <see cref="IProductCatalogQueryPort"/>. Tests call
    /// <c>fixture.Catalog</c> to stub Catalog responses without spinning a real Catalog
    /// service. WireMock'd HTTP would exercise the adapter end-to-end — that's a
    /// proposed M9 follow-up.
    /// </summary>
    public IProductCatalogQueryPort Catalog { get; } = Substitute.For<IProductCatalogQueryPort>();

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    public IConnectionMultiplexer RedisMultiplexer => _redisMultiplexer;

    public IDatabase RedisBasketDb => _redisMultiplexer.GetDatabase();

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();

        // Dedicated multiplexer for test-side Redis assertions (e.g. KeyExistsAsync on
        // CheckoutBasketTests). The host gets its own multiplexer from
        // AddBasketRedisPersistence using the same connection string.
        var redisOptions = _redisContainer.ConfigurationOptions;
        _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisOptions);
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
            var redisConnectionString = _redisContainer.ConfigurationOptions.ToString();
            webBuilder
                .UseSetting("ConnectionStrings:Basket", _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Basket", redisConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", redisConnectionString)
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
                // Substitute the ACL port so tests can stub Catalog responses without
                // a Catalog service running. Use Replace so the production HTTP-adapter
                // registration in Basket.Infrastructure is removed — AddSingleton(instance)
                // would only add a second descriptor with the NSubstitute proxy's runtime
                // type, leaving the real adapter live for resolution. Real adapter exercise
                // stays in M9 follow-up.
                services.Replace(ServiceDescriptor.Singleton<IProductCatalogQueryPort>(Catalog));

                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter with a
                // fake that writes a stub OutboxMessage row directly. Avro byte-level
                // fidelity is asserted in Basket.IntegrationTests; functional tests only
                // need to verify "the right outbox row landed". Avoids exercising the
                // KafkaTestContainer's Schema Registry just for outbox shape assertions.
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());

                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps
                // every TokenValidationParameters flag at its production default
                // of TRUE. See Platform.Test.Framework.Auth.JwtBearerTestExtensions.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        Catalog.ClearSubstitute(ClearOptions.All);

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
        await _kafkaContainer.DisposeAsync();
    }
}
