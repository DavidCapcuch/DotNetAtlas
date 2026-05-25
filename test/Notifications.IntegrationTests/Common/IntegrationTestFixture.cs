using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Notifications.Application.Common.Data;
using Notifications.Infrastructure.Persistence.Database;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Respawn;

namespace Notifications.IntegrationTests.Common;

/// <summary>
/// xUnit fixture for Notifications integration tests. Boots the real
/// Notifications.Api host via <see cref="WebApplicationFactory{TEntryPoint}"/>
/// against a Postgres testcontainer (real schema via the BC's <c>V*.sql</c>
/// migrations). Program.cs's <c>!IsTesting()</c> guard skips the KafkaFlow
/// cluster boot — the typed Kafka handlers are still registered (so tests can
/// resolve them via DI), but no consumer ever opens a broker connection.
/// </summary>
/// <remarks>
/// <para>
/// Booting through <see cref="WebApplicationFactory{TEntryPoint}"/> means every
/// <c>AddOptionsWithValidateOnStart</c> chain in the production composition root
/// runs during fixture initialisation — drift between <c>[Required]</c> IOptions
/// properties and appsettings keys fails at test setup instead of at first
/// container start. Mirrors the saga + Inventory fixture patterns.
/// </para>
/// <para>
/// The transactional outbox is replaced with an NSubstitute stub via
/// <see cref="ITestServiceProviderFactoryExtensions.ConfigureTestServices"/> so
/// tests can assert on outbox calls without standing up a Schema Registry
/// container. Mirrors the Inventory functional-test pattern of swapping
/// <c>IOutboxWriter</c>.
/// </para>
/// </remarks>
public sealed class IntegrationTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Pinned to 2026-05-22 09:00 UTC so business-timestamp assertions stay deterministic.</summary>
    public static readonly DateTimeOffset FixedFakeNow =
        new(2026, 05, 22, 09, 00, 00, TimeSpan.Zero);

    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Notifications",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Notifications/Notifications.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [NotificationDbContext.DefaultSchemaName]
        });

    /// <summary>Test-controlled clock pinned to <see cref="FixedFakeNow"/>; resolvable as <see cref="TimeProvider"/>.</summary>
    public FakeTimeProvider FakeTime { get; } = new(FixedFakeNow);

    /// <summary>NSubstitute transactional-outbox stub. Tests assert on its <c>Received</c> AddOutboxMessage calls.</summary>
    public ITransactionalOutbox<INotificationDbContext> OutboxSubstitute { get; } =
        Substitute.For<ITransactionalOutbox<INotificationDbContext>>();

    /// <summary>Connection string for tests that bypass the DbContext (e.g. raw SQL pre-staging).</summary>
    public string ConnectionString => _dbContainer.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment("Testing")
            .UseSetting("ConnectionStrings:Notifications", _dbContainer.ConnectionString)
            // Kafka cluster boot is guarded by !IsTesting() in Program.cs but
            // AddInfrastructure still binds KafkaOptions at DI time. Point those
            // at unreachable hosts so any accidental use blows up loudly rather
            // than silently producing to a real broker. Mirrors InventoryApiFixture.
            .UseSetting("Kafka:Brokers:0", "kafka-not-used-in-integration-tests:9094")
            .UseSetting("Kafka:SchemaRegistry:Url", "http://schema-registry-not-used-in-integration-tests:8081")
            .ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(FakeTime);

                // Swap the production Avro+SchemaRegistry-backed ITransactionalOutbox
                // for an NSubstitute stub. Tests assert on received AddOutboxMessage
                // calls — production wiring requires a live Schema Registry which we
                // don't stand up in integration tests.
                services.Replace(ServiceDescriptor.Singleton<ITransactionalOutbox<INotificationDbContext>>(OutboxSubstitute));
            });
    }

    public async ValueTask InitializeAsync()
    {
        await _dbContainer.StartAsync(TestContext.Current.CancellationToken);

        // Force eager host construction + StartupValidator pass. WebApplicationFactory
        // builds the host lazily on first Server / CreateClient access; touching Server
        // here triggers the host startup (including AddOptionsWithValidateOnStart) so
        // any IOptions binding mismatch surfaces in fixture setup, not mid-test.
        _ = Server;
    }

    /// <summary>Creates a per-test DI scope; caller disposes (supports <c>await using</c>).</summary>
    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    /// <summary>Wipes every table in the Notifications schema between tests.</summary>
    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    /// <summary>Resets the NSubstitute call recorder between tests.</summary>
    public void ResetOutboxSubstitute() => OutboxSubstitute.ClearReceivedCalls();

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }
}
