using FastEndpoints.Testing;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Inventory.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// Inventory integration-test fixture. Boots the real <c>Inventory.API</c>
/// composition root inside <see cref="AppFixture{TEntryPoint}"/>, spinning a
/// throwaway Postgres container for the EF model and applying the committed
/// <c>V*.sql</c> scripts via Evolve (matches the production migration path).
/// Kafka is wired in DI but its cluster is never started — Program.cs guards
/// <c>kafkaBus.StartAsync()</c> with <c>!IsTesting()</c>, and Inventory's
/// 5 typed Kafka handlers are exercised directly via
/// <see cref="FakeKafkaMessageContext"/>, matching the Ordering M5 precedent.
/// </summary>
/// <remarks>
/// <para>
/// No Kafka container is needed: tests resolve the typed handlers from DI and
/// invoke <c>Handle(IMessageContext, T)</c> directly. The host's KafkaFlow
/// registration still happens at DI time (<c>AddInfrastructure</c> calls
/// <c>AddKafka</c>), but the broker URL points at an unreachable host so a
/// stray production code path would fail loudly instead of leaking onto a
/// real broker. Avro byte-level fidelity is validated in M7 functional tests
/// alongside the Kafka consumer wiring.
/// </para>
/// <para>
/// <see cref="IOutboxWriter"/> is swapped for <see cref="FakeOutboxWriter"/>
/// in <c>ConfigureTestServices</c> so writes don't touch Schema Registry —
/// the fake preserves topic + key + CLR type, which is enough for the
/// "the right message landed in the right topic" assertions.
/// </para>
/// </remarks>
[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Inventory",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Inventory/Inventory.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [InventoryDbContext.DefaultSchemaName]
        });

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Inventory", _dbContainer.ConnectionString)
                // Inventory.API/Program.cs guards the Kafka boot with !IsTesting(), but
                // AddInfrastructure still binds Kafka options at DI time — point them at
                // unreachable hosts so any accidental use blows up loudly instead of
                // silently flowing to a real broker.
                .UseSetting("Kafka:Brokers:0", "kafka-not-used-in-integration-tests:9094")
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
                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter
                // with the fake. Avro byte-fidelity is validated in M7 functional
                // tests; integration tests only need to verify "the right outbox
                // row landed".
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());
            });
    }

    /// <summary>Creates a per-test DI scope; caller disposes.</summary>
    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>
    /// Wipes every table in the Inventory schema (preserving schema + EF
    /// migrations history). Invoked from <see cref="BaseIntegrationTest.DisposeAsync"/>
    /// after each test so per-test isolation no longer relies solely on
    /// <see cref="Guid.NewGuid"/> discipline.
    /// </summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    /// <summary>Connection string for tests that bypass the DbContext (e.g. raw SQL pre-staging).</summary>
    public string ConnectionString => _dbContainer.ConnectionString;

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
    }
}
