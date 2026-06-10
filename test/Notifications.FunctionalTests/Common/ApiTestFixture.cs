using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Notifications.Application.Common.Data;
using Notifications.FunctionalTests.Common.TestClientInfrastructure;
using Notifications.Infrastructure.Persistence.Database;
using NSubstitute;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Database;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Notifications.FunctionalTests.Common;

internal sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// Functional-test fixture for the in-app bell hub. Boots the real Notifications.Api host
/// (<see cref="AppFixture{TProgram}"/>) against a Postgres testcontainer — the same boot profile as
/// the integration fixture (Kafka cluster boot skipped via <c>!IsTesting()</c>, transactional
/// outbox stubbed) — and wires the in-process JWT bearer trust so authenticated SignalR clients
/// connect for real. No Redis backplane (the hub is in-memory, ADR-0016); no Mailpit/Kafka
/// containers (the bell hub exercises neither).
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Notifications",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Notifications/Notifications.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [NotificationsDbContext.DefaultSchemaName]
        });

    // Matches Notifications.Api appsettings.json Authentication:JwtBearer:...:ValidAudience.
    // JwtBearerTestExtensions asserts these match (loud on drift).
    private readonly FakeTokenSigner _signer = new(audience: "notifications-service");

    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _dbContainer.StartAsync();
    }

    protected override ValueTask SetupAsync()
    {
        TokenCreator = new FakeTokenCreator(_signer);
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Notifications", _dbContainer.ConnectionString)
                // Kafka cluster boot is guarded by !IsTesting() in Program.cs, but AddInfrastructure
                // still binds KafkaOptions at DI time. Point those at unreachable hosts so any
                // accidental use blows up loudly rather than silently producing to a real broker.
                .UseSetting("Kafka:Brokers:0", "kafka-not-used-in-functional-tests:9094")
                .UseSetting("Kafka:SchemaRegistry:Url", "http://schema-registry-not-used-in-functional-tests:8081");
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
                // The production Avro+SchemaRegistry-backed outbox needs a live Schema Registry;
                // the bell hub never writes to it, so a stub keeps the host bootable without one.
                services.Replace(ServiceDescriptor.Singleton<ITransactionalOutbox<INotificationsDbContext>>(
                    Substitute.For<ITransactionalOutbox<INotificationsDbContext>>()));

                // Trust the test signer's RSA key while keeping every TokenValidationParameters flag
                // at its production default of TRUE; asserts the BC's ValidAudience == signer audience.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        await _dbContainer.CleanDataAsync();
    }

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _dbContainer.DisposeAsync();
    }
}
