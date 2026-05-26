using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ordering.Infrastructure.Persistence.Database;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Ordering.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// FastEndpoints <see cref="AppFixture{TEntryPoint}"/> for Ordering integration tests.
/// Boots the full <c>Ordering.API</c> host against a Postgres Testcontainer so the
/// composition root under test is the same one production uses (validators, CQRS,
/// domain-event dispatcher + outbox-publisher handlers, the four saga-command Kafka
/// typed handlers registered by KafkaFlow as Scoped). The KafkaFlow consumer cluster
/// itself is NOT started — <c>Ordering.API/Program.cs</c> guards <c>StartAsync()</c>
/// with <c>!IsTesting()</c> — so no Kafka or Schema-Registry container is needed.
/// Tests resolve the typed handlers from <see cref="CreateScope"/> and invoke
/// <c>Handle(IMessageContext, T)</c> directly with a synthetic
/// <see cref="FakeKafkaMessageContext"/>; outbox-emission fidelity is asserted by
/// reading the <see cref="FakeOutboxWriter"/>'s captured messages.
/// </summary>
[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Ordering",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Ordering/Ordering.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [OrderingDbContext.DefaultSchemaName]
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
                .UseSetting("ConnectionStrings:Ordering", _dbContainer.ConnectionString)
                // Ordering.API/Program.cs guards the Kafka boot with !IsTesting(), but
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
                // Replace the production Avro/SchemaRegistry-backed IOutboxWriter with
                // an in-memory fake. The outbox publisher domain-event handlers fire on
                // every SaveChanges; without this they would attempt to talk to a
                // non-existent schema registry. Byte-level Avro fidelity is covered by
                // the docker-compose smoke (M8); integration tests assert on the topic
                // + key + CLR instance captured by the fake.
                services.RemoveAll<IOutboxWriter>();
                services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();
            });
    }

    /// <summary>
    /// Creates a per-test DI scope. Caller disposes.
    /// </summary>
    public IServiceScope CreateScope() => Services.CreateScope();

    /// <summary>Wipes every table in the Ordering schema between tests.</summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    /// <summary>
    /// Resolves the singleton <see cref="FakeOutboxWriter"/> so individual
    /// tests can <c>Clear()</c> captured messages or assert on them after
    /// driving a handler.
    /// </summary>
    public FakeOutboxWriter GetFakeOutbox() =>
        (FakeOutboxWriter)Services.GetRequiredService<IOutboxWriter>();

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
    }
}
