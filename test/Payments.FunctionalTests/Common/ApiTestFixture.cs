using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Payments.FunctionalTests.Common.TestClientInfrastructure;
using Payments.Infrastructure.Persistence.Database;
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

namespace Payments.FunctionalTests.Common;

internal sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// FastEndpoints <see cref="AppFixture{TEntryPoint}"/> for the Payments API.
/// Spins up a Postgres Testcontainer, applies EF Core migrations
/// programmatically, forces <c>ASPNETCORE_ENVIRONMENT=Testing</c> so the host
/// skips the saga-command Kafka consumer (booting it would require a Kafka +
/// Schema Registry container pair that is out of scope for the functional
/// slice), and pins a test-side RSA signing key into the JwtBearer pipeline so
/// <see cref="FakeTokenCreator"/>'s tokens validate end-to-end without the
/// pipeline disabling signature / issuer / audience / lifetime checks.
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
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

    public FakeTokenCreator TokenCreator { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync calls over the
        // Windows named pipe interleave on the shared ChunkedReadStream and intermittently
        // raise "Invalid chunk header encountered".
        await _dbContainer.StartAsync();
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
                // TimeProvider.System is auto-registered by the Generic Host (ADR-0015).
                // Tests that need determinism construct FakeTimeProvider locally per
                // ADR-0015 line 104; the previous fixture-level FakeTimeProvider leaked
                // state across tests in the shared collection (SetUtcNow can only move
                // forward) and is removed.

                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter with the
                // fake so seed helpers that touch the outbox need no Schema Registry; this
                // suite asserts the HTTP surface, not the emitted outbox messages.
                services.RemoveAll<IOutboxWriter>();
                services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps
                // every TokenValidationParameters flag at its production default
                // of TRUE. See Platform.Test.Framework.Auth.JwtBearerTestExtensions.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    public Task ResetFixtureStateAsync() => _dbContainer.CleanDataAsync();

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _dbContainer.DisposeAsync();
    }
}
