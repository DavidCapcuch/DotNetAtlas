using FastEndpoints.Testing;
using Inventory.FunctionalTests.Common.TestClientInfrastructure;
using Inventory.Infrastructure.Persistence.Database;
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

namespace Inventory.FunctionalTests.Common;

internal sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// Inventory functional-test fixture. Spins Postgres + Redis Testcontainers,
/// applies the committed <c>V*.sql</c> migrations via Evolve, and disables
/// JWT signature validation so <see cref="FakeTokenCreator"/> can mint
/// unsigned tokens with a <c>scope</c> claim that drives
/// <see cref="Inventory.Api.Common.Authorization.InventoryAuthorizationPolicies"/>.
/// </summary>
/// <remarks>
/// <para>
/// No Kafka container is needed — <c>Inventory.Api/Program.cs</c> guards the
/// KafkaFlow cluster boot with <c>!IsTesting()</c>, and Inventory's outbox
/// publishers use the <see cref="FakeOutboxWriter"/> registered below
/// (replaces the production Avro+SchemaRegistry-backed writer). Saga-command
/// Kafka consumer flows are covered by the integration tests; functional
/// tests focus on the HTTP surface end-to-end.
/// </para>
/// <para>
/// JwtBearer validation keeps every <c>TokenValidationParameters</c> flag at
/// its production default; the test host trusts only the
/// <see cref="FakeTokenSigner"/>'s RSA key via
/// <see cref="JwtBearerTestExtensions.ConfigureJwtBearerForTests"/>.
/// </para>
/// </remarks>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
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

    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    public IConnectionMultiplexer RedisMultiplexer => _redisMultiplexer;

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
                .UseSetting("ConnectionStrings:Inventory", _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", redisConfig.ToString())
                // Inventory.Api/Program.cs guards the Kafka boot with !IsTesting(), but
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
                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter
                // with the fake. Avro byte-fidelity is tested in IntegrationTests;
                // functional tests only need to verify "the right outbox row landed".
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
