using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using Payments.Application.Abstractions;
using Payments.Infrastructure.ExternalServices.PaymentGateway;
using Payments.Infrastructure.Persistence.Database;
using Payments.IntegrationTests.Common.TestClientInfrastructure;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Database;
using Platform.Test.Framework.Kafka;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;

namespace Payments.IntegrationTests.Common;

internal sealed class IntegrationTestCollection : TestCollection<IntegrationTestFixture>;

/// <summary>
/// The single Payments integration fixture: one real <c>Program.cs</c> host on a Postgres
/// Testcontainer, shared by the whole <see cref="IntegrationTestCollection"/> (one instance, state
/// reset between tests). Both entrances run against it — the HTTP edge via
/// <see cref="HttpClientRegistry"/>, and the saga-command Kafka handlers resolved from the host DI
/// graph and driven with a <see cref="FakeKafkaMessageContext"/>. The KafkaFlow bus is never started
/// (the <c>!IsTesting()</c> guard in <c>Program.cs</c>), so no Kafka container is needed.
/// <para>
/// The host keeps <c>TimeProvider.System</c>: a fixture-level <c>FakeTimeProvider</c> would be shared by
/// every test in the collection and can only move forward. Tests needing fixed time construct one
/// locally (ADR-0015 § Time abstraction).
/// </para>
/// </summary>
// No [DisableWafCache]: FastEndpoints caches one SUT per derived AppFixture type, and this fixture is
// shared through a single collection, so the host boots once with or without the cache. Re-add it
// before a second instance of this type exists — the cache would hand that instance the first one's
// host, while its own container never starts and its signer is not the key the host trusts.
public class IntegrationTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Payments",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Payments/Payments.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [PaymentsDbContext.DefaultSchemaName]
        });

    private readonly FakeTokenSigner _signer = new(audience: "payments-service");

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
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
            webBuilder.UseSetting("ConnectionStrings:Payments", _dbContainer.ConnectionString);
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
                // outbox assertions need no Schema Registry round-trip.
                services.RemoveAll<IOutboxWriter>();
                services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(sp =>
                    new CountingPaymentGateway(new StubPaymentGateway(sp.GetRequiredService<TimeProvider>())));

                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        using var _ = SuppressInstrumentationScope.Begin();

        GetFakeOutbox().Clear();
        GetGateway().Reset();
        await _dbContainer.CleanDataAsync();
    }

    public FakeOutboxWriter GetFakeOutbox() =>
        (FakeOutboxWriter)Services.GetRequiredService<IOutboxWriter>();

    public CountingPaymentGateway GetGateway() =>
        (CountingPaymentGateway)Services.GetRequiredService<IPaymentGateway>();

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _dbContainer.DisposeAsync();
    }
}
