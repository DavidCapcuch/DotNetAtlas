using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Ordering.FunctionalTests.Common.TestClientInfrastructure;
using Ordering.Infrastructure.Persistence.Database;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework.Auth;
using Platform.Test.Framework.Kafka;
using Platform.Test.Framework.Redis;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using Testcontainers.PostgreSql;

namespace Ordering.FunctionalTests.Common;

[CollectionDefinition(nameof(FunctionalTestCollection))]
public sealed class FunctionalTestCollection : TestCollection<ApiTestFixture>;

/// <summary>
/// FastEndpoints <see cref="AppFixture{TEntryPoint}"/> for the Ordering API.
/// Spins up Postgres + Redis Testcontainers, applies EF Core migrations
/// programmatically (no Evolve — Ordering owns its EF migrations per
/// <c>CLAUDE.md</c>), forces <c>ASPNETCORE_ENVIRONMENT=Testing</c> so the
/// host skips the saga-command Kafka consumer, and relaxes JWT validation
/// so <see cref="FakeTokenCreator"/>'s unsigned tokens authenticate.
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Ordering")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private readonly RedisTestContainer _redisContainer = new();

    private readonly FakeTokenSigner _signer = new(audience: "ordering-service-tests");

    /// <summary>
    /// Exposes the fixture's RSA signer so tests can mint tokens with a
    /// custom claim shape (e.g. pinning the production "roles" array claim
    /// instead of the FakeTokenCreator default).
    /// </summary>
    public FakeTokenSigner Signer => _signer;

    /// <summary>
    /// Pinned to 2026-04-23 10:00 UTC so cancellation/ship/deliver
    /// timestamps in functional-test assertions stay deterministic.
    /// </summary>
    public FakeTimeProvider FakeTime { get; } = new(
        new DateTimeOffset(2026, 4, 23, 10, 0, 0, TimeSpan.Zero));

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    protected override async ValueTask PreSetupAsync()
    {
        // Start sequentially: concurrent Docker.DotNet InspectContainerAsync
        // calls over the Windows named pipe interleave on the shared
        // ChunkedReadStream and intermittently raise "Invalid chunk header
        // encountered". Mirrors the Weather fixture's reasoning.
        await _pgContainer.StartAsync();
        await _redisContainer.StartAsync();
    }

    protected override async ValueTask SetupAsync()
    {
        HttpClientRegistry = new HttpClientRegistry<Program>(this, new FakeTokenCreator(_signer));

        // Apply EF Core migrations once per fixture lifetime against the
        // freshly-started container.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        await db.Database.MigrateAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Ordering", _pgContainer.GetConnectionString())
                .UseSetting("ConnectionStrings:Redis:Cache", _redisContainer.ConnectionString);
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
                // with an in-memory fake. The outbox publisher domain-event
                // handlers fire on every SaveChanges; without this the seed
                // helpers attempt to talk to a non-existent schema registry.
                // Asserting on Kafka messages is M7's job; M5 only needs the
                // HTTP surface to round-trip.
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
        // Wipe Redis so the idempotency cache from a prior test does not
        // poison the next one. Postgres state is wiped by truncating the
        // ordering schema's user tables.
        await _redisContainer.CleanDataAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        // Table names mirror the EF migration:
        //   - "orders" + "order_items" (snake_case from EFCore.NamingConventions)
        //   - "InboxMessages" + "OutboxMessages" (PascalCase, configured by
        //     the Platform.ReliableMessaging.* helpers — quoted so Postgres
        //     preserves the case).
        await db.Database.ExecuteSqlRawAsync(
            $"""
             TRUNCATE TABLE
                 "{OrderingDbContext.DefaultSchemaName}"."order_items",
                 "{OrderingDbContext.DefaultSchemaName}"."orders",
                 "{OrderingDbContext.DefaultSchemaName}"."OutboxMessages",
                 "{OrderingDbContext.DefaultSchemaName}"."InboxMessages"
             RESTART IDENTITY CASCADE;
             """);
    }

    protected override async ValueTask TearDownAsync()
    {
        _signer.Dispose();
        await _pgContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
