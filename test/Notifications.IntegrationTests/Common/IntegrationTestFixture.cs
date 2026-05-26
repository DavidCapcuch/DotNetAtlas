using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Notifications.Application.Common.Data;
using Notifications.Infrastructure.Persistence.Database;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Notifications.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// xUnit fixture for Notifications integration tests. Boots the real
/// Notifications.Api host via <see cref="AppFixture{TProgram}"/> against a
/// Postgres testcontainer (real schema via the BC's <c>V*.sql</c> migrations).
/// Program.cs's <c>!IsTesting()</c> guard skips the KafkaFlow cluster boot —
/// the typed Kafka handlers are still registered (so tests can resolve them via
/// DI), but no consumer ever opens a broker connection.
/// </summary>
/// <remarks>
/// <para>
/// Booting through <see cref="AppFixture{TProgram}"/> means every
/// <c>AddOptionsWithValidateOnStart</c> chain in the production composition root
/// runs during fixture initialisation — drift between <c>[Required]</c> IOptions
/// properties and appsettings keys fails at test setup instead of at first
/// container start. Mirrors Weather's canonical fixture pattern.
/// </para>
/// <para>
/// The transactional outbox is replaced with an NSubstitute stub via
/// <see cref="TestHostBuilderExtensions.ConfigureTestServices"/> so tests can
/// assert on outbox calls without standing up a Schema Registry container.
/// </para>
/// </remarks>
[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Notifications",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Notifications/Notifications.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [NotificationDbContext.DefaultSchemaName]
        });

    /// <summary>NSubstitute transactional-outbox stub. Tests assert on its <c>Received</c> AddOutboxMessage calls.</summary>
    public ITransactionalOutbox<INotificationDbContext> OutboxSubstitute { get; } =
        Substitute.For<ITransactionalOutbox<INotificationDbContext>>();

    /// <summary>Connection string for tests that bypass the DbContext (e.g. raw SQL pre-staging).</summary>
    public string ConnectionString => _dbContainer.ConnectionString;

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
                .UseSetting("ConnectionStrings:Notifications", _dbContainer.ConnectionString)
                // Kafka cluster boot is guarded by !IsTesting() in Program.cs but
                // AddInfrastructure still binds KafkaOptions at DI time. Point those
                // at unreachable hosts so any accidental use blows up loudly rather
                // than silently producing to a real broker.
                .UseSetting("Kafka:Brokers:0", "kafka-not-used-in-integration-tests:9094")
                .UseSetting("Kafka:SchemaRegistry:Url", "http://schema-registry-not-used-in-integration-tests:8081");
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
                // TimeProvider is intentionally not replaced fixture-side (ADR-0015): a shared
                // FakeTimeProvider singleton leaks between tests because SetUtcNow cannot move
                // backwards. The Generic Host already registers TimeProvider.System; tests that
                // need determinism construct their own FakeTimeProvider locally.

                // Swap the production Avro+SchemaRegistry-backed ITransactionalOutbox
                // for an NSubstitute stub. Tests assert on received AddOutboxMessage
                // calls — production wiring requires a live Schema Registry which we
                // don't stand up in integration tests.
                services.Replace(ServiceDescriptor.Singleton<ITransactionalOutbox<INotificationDbContext>>(OutboxSubstitute));
            });
    }

    /// <summary>Creates a per-test DI scope; caller disposes (supports <c>await using</c>).</summary>
    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    /// <summary>Wipes every table in the Notifications schema between tests.</summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    /// <summary>Resets the NSubstitute call recorder between tests.</summary>
    public void ResetOutboxSubstitute() => OutboxSubstitute.ClearReceivedCalls();

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
    }
}
