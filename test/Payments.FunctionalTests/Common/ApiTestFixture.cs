using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Payments.FunctionalTests.Common.TestClientInfrastructure;
using Payments.Infrastructure.Persistence.Database;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Kafka;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using Testcontainers.PostgreSql;

namespace Payments.FunctionalTests.Common;

[CollectionDefinition(nameof(FunctionalTestCollection))]
public sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// FastEndpoints <see cref="AppFixture{TEntryPoint}"/> for the Payments API.
/// Spins up a Postgres Testcontainer, applies EF Core migrations
/// programmatically, forces <c>ASPNETCORE_ENVIRONMENT=Testing</c> so the host
/// skips the saga-command Kafka consumer (booting it would require a Kafka +
/// Schema Registry container pair that is out of scope for the M6 functional
/// slice), and pins a test-side RSA signing key into the JwtBearer pipeline so
/// <see cref="FakeTokenCreator"/>'s tokens validate end-to-end without the
/// pipeline disabling signature / issuer / audience / lifetime checks.
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Payments")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private readonly FakeTokenSigner _signer = new(audience: "payments-service-tests");

    /// <summary>
    /// Pinned to 2026-04-27 10:00 UTC so timestamp assertions in admin GETs
    /// stay deterministic. Same instant the M5 integration fixture uses.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(
        new DateTimeOffset(2026, 4, 27, 10, 0, 0, TimeSpan.Zero));

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _pgContainer.StartAsync();
    }

    protected override async ValueTask SetupAsync()
    {
        HttpClientRegistry = new HttpClientRegistry<Program>(this, new FakeTokenCreator(_signer));

        // Apply EF Core migrations once per fixture lifetime against the
        // freshly-started container.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseSetting("ConnectionStrings:Payments", _pgContainer.GetConnectionString());
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
                // Pin time so timestamp assertions are stable.
                services.AddSingleton<TimeProvider>(FakeTime);

                // Replace the real Avro/SchemaRegistry-backed outbox writer
                // with an in-memory fake. Without this, any seed helper that
                // touches the outbox would attempt to talk to a non-existent
                // schema registry. The M6 functional slice exercises only
                // the HTTP read surface, so capturing emitted messages is not
                // asserted here — Kafka emission fidelity lives in M5.
                services.RemoveAll<IOutboxWriter>();
                services.AddSingleton<IOutboxWriter, FakeOutboxWriter>();

                // Wire the JwtBearer scheme to trust _signer's RSA key — keeps
                // every TokenValidationParameters flag at its production default
                // of TRUE. See Platform.Test.Framework.Auth.JwtBearerTestExtensions.
                services.ConfigureJwtBearerForTests(_signer);
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        // Truncate the Payments schema's user tables. Inbox + Outbox table
        // names are PascalCase (configured by the Platform.ReliableMessaging
        // helpers — quoted so Postgres preserves the case).
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            $"""
             TRUNCATE TABLE
                 "{PaymentsDbContext.DefaultSchemaName}"."payment_transactions",
                 "{PaymentsDbContext.DefaultSchemaName}"."OutboxMessages",
                 "{PaymentsDbContext.DefaultSchemaName}"."InboxMessages"
             RESTART IDENTITY CASCADE;
             """);
    }

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _pgContainer.DisposeAsync();
    }
}
