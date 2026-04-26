using FastEndpoints.Testing;
using Inventory.FunctionalTests.Common.TestClientInfrastructure;
using Inventory.Infrastructure.Persistence.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Platform.ReliableMessaging.Outbox.EFCore;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Inventory.FunctionalTests.Common;

/// <summary>
/// Inventory functional-test fixture. Spins Postgres + Redis Testcontainers,
/// runs EF migrations, and disables JWT signature validation so
/// <see cref="FakeTokenCreator"/> can mint unsigned tokens with a
/// <c>scope</c> claim that drives <see cref="InventoryAuthorizationPolicies"/>.
/// </summary>
/// <remarks>
/// <para>
/// No Kafka container is needed — <c>Inventory.API/Program.cs</c> guards the
/// KafkaFlow cluster boot with <c>!IsTesting()</c>, and Inventory's outbox
/// publishers use the <see cref="FakeOutboxWriter"/> registered below
/// (replaces the production Avro+SchemaRegistry-backed writer). Saga-command
/// Kafka consumer flows are covered by the M5 integration tests; M7
/// functional tests focus on the HTTP surface end-to-end.
/// </para>
/// <para>
/// JWT validation is relaxed (issuer/audience/lifetime/signing-key all off,
/// signature accepted as-is) the same way Basket / Weather do — the
/// authentication scheme still runs, the policy still parses scope claims,
/// only signature verification is bypassed.
/// </para>
/// </remarks>
[DisableWafCache]
public class InventoryApiFixture : AppFixture<Program>
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Inventory")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7.4.6")
        .WithCleanUp(true)
        .Build();

    private ConnectionMultiplexer _redisMultiplexer = null!;

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public IConnectionMultiplexer RedisMultiplexer => _redisMultiplexer;

    protected override async ValueTask PreSetupAsync()
    {
        await _pgContainer.StartAsync();
        await _redisContainer.StartAsync();

        var redisOptions = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
        redisOptions.AllowAdmin = true;
        _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisOptions);
    }

    protected override async ValueTask SetupAsync()
    {
        HttpClientRegistry = new HttpClientRegistry<Program>(this);

        // Apply EF Core migrations after the host starts. Safe because
        // Program.cs guards `AddReservationExpiryWorker` behind
        // `!IsTesting()` — no hosted service queries the projections
        // before this runs. The production DbContext config (snake-case
        // + exception processor) flows through DI from
        // PersistenceDependencyInjection.AddDatabase, so no parallel
        // config is needed here.
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Inventory", _pgContainer.GetConnectionString())
                .UseSetting("ConnectionStrings:Redis:Cache", _redisContainer.GetConnectionString())
                // Inventory.API/Program.cs guards the Kafka boot with !IsTesting(), but
                // AddInfrastructure still binds Kafka options at DI time — point them at
                // unreachable hosts so any accidental use blows up loudly instead of
                // silently flowing to a real broker.
                .UseSetting("Kafka:Brokers:0", "kafka-not-used-in-functional-tests:9094")
                .UseSetting("Kafka:SchemaRegistry:Url", "http://schema-registry-not-used-in-functional-tests:8081")
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
                // with the fake. Avro byte-fidelity is tested in IntegrationTests;
                // functional tests only need to verify "the right outbox row landed".
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());

                // Relax JWT validation for the in-process test host. Use Configure
                // (IConfigureNamedOptions) — not PostConfigure — so RequireHttpsMetadata=false
                // is observed before JwtBearerPostConfigureOptions throws on the http://
                // authority (mirrors Weather + Basket fixtures' rationale).
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.RequireHttpsMetadata = false;
#pragma warning disable CA5404
                        options.TokenValidationParameters.ValidateIssuer = false;
                        options.TokenValidationParameters.ValidateAudience = false;
                        options.TokenValidationParameters.ValidateLifetime = false;
#pragma warning restore CA5404
                        options.TokenValidationParameters.ValidateIssuerSigningKey = false;
                        options.TokenValidationParameters.RequireSignedTokens = false;
                        options.TokenValidationParameters.SignatureValidator = (token, _) =>
                            new JsonWebToken(token);
                    });
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        // Flush Redis so idempotency-cache state cannot leak between tests.
        var endpoints = _redisMultiplexer.GetEndPoints();
        foreach (var endpoint in endpoints)
        {
            var server = _redisMultiplexer.GetServer(endpoint);
            await server.FlushAllDatabasesAsync();
        }
    }

    protected override async ValueTask TearDownAsync()
    {
        await _redisMultiplexer.DisposeAsync();
        await _pgContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
