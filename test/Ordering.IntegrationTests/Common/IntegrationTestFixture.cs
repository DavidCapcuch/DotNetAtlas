using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.IntegrationTests.Common.TestClientInfrastructure;
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

namespace Ordering.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// The single Ordering integration fixture: one real <c>Ordering.Api</c> host on Postgres + Redis
/// Testcontainers, shared by <see cref="IntegrationTestCollection"/>. The KafkaFlow consumer is not
/// started (<c>Program.cs</c> guards <c>StartAsync()</c> with <c>!IsTesting()</c>), so Kafka-entrance
/// tests resolve the typed handler from <see cref="CreateScope"/> and call
/// <c>Handle(IMessageContext, T)</c> with a <see cref="FakeKafkaMessageContext"/>. Redis is real
/// because the CancelOrder replay tests assert the <c>Idempotency-Key</c> output cache (ADR-0013). The
/// host keeps <c>TimeProvider.System</c> (ADR-0015); deterministic time comes from a
/// <c>FakeTimeProvider</c> handed to the seed.
/// </summary>
// No [DisableWafCache]: FastEndpoints caches one host per derived fixture type, and xUnit builds
// this collection fixture once. Add it if the fixture is ever consumed outside
// IntegrationTestCollection — a second instance would receive the cached host without starting its
// own containers (PreSetupAsync runs only on host build) and would sign tokens the host does not trust.
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Ordering",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Ordering/Ordering.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [OrderingDbContext.DefaultSchemaName]
        });

    private readonly RedisTestContainer _redisContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "ordering-service");

    /// <summary>
    /// Exposes the fixture's RSA signer so tests can mint tokens with a custom claim shape (e.g.
    /// pinning the production "roles" array claim instead of the <see cref="FakeTokenCreator"/>
    /// default).
    /// </summary>
    public FakeTokenSigner Signer => _signer;

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
            var redisConfig = _redisContainer.ConfigurationOptions;
            webBuilder
                .UseSetting("ConnectionStrings:Ordering", _dbContainer.ConnectionString)
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
                // Replace the production Avro/SchemaRegistry-backed IOutboxWriter with an in-memory
                // fake. The outbox publisher domain-event handlers fire on every SaveChanges;
                // without this even the seed helpers would attempt to talk to a non-existent schema
                // registry. Assertions read the captured CLR event (type + payload fields).
                services.RemoveAll<IOutboxWriter>();
                services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    /// <summary>
    /// Creates a per-test DI scope. Caller disposes.
    /// </summary>
    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>
    /// Returns everything a test can mutate through this fixture to its baseline — a fake or
    /// substitute added to the fixture gets its reset here.
    /// </summary>
    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        GetFakeOutbox().Clear();

        await Task.WhenAll(
            _dbContainer.CleanDataAsync(),
            _redisContainer.CleanDataAsync()
        );
    }

    /// <summary>
    /// Resolves the singleton <see cref="FakeOutboxWriter"/>. Seeding emits events too, so a test
    /// asserting on captures clears them after arranging.
    /// </summary>
    public FakeOutboxWriter GetFakeOutbox() =>
        (FakeOutboxWriter)Services.GetRequiredService<IOutboxWriter>();

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
