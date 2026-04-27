using Catalog.FunctionalTests.Common.TestClientInfrastructure;
using Catalog.Infrastructure.Persistence.Database;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using OpenFeature;
using Platform.ReliableMessaging.Outbox.EFCore;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using StackExchange.Redis;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Catalog.FunctionalTests.Common;

/// <summary>
/// Functional-test fixture for the Catalog HTTP surface.
/// Spins Postgres + Redis + Kafka (KRaft) Testcontainers, materializes the EF schema via
/// <c>EnsureCreatedAsync</c> (per <c>CLAUDE.md</c> migrations are user-generated), replaces
/// the production <see cref="IOutboxWriter"/> with <see cref="FakeOutboxWriter"/> (skips
/// Schema Registry; byte-fidelity is asserted by the dedicated
/// <c>EndToEnd/AvroByteFidelityTests</c>), pins <see cref="TimeProvider"/> to
/// <see cref="Now"/>, replaces <see cref="IFeatureClient"/> with an
/// NSubstitute mock so tests can flip <c>catalog.show-discontinued-in-search</c>
/// per-scenario, and relaxes <see cref="JwtBearerOptions"/> so the
/// <see cref="FakeTokenCreator"/> unsigned tokens parse.
/// </summary>
[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    /// <summary>Stable test clock — matches <c>Catalog.IntegrationTests.IntegrationTestFixture.Now</c>.</summary>
    public static readonly DateTimeOffset Now =
        new(2026, 04, 25, 12, 00, 00, TimeSpan.Zero);

    private const string StockLevelChangedTopic = "inventory.stock-level-changed";

    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("Catalog")
        .WithUsername("postgres")
        .WithPassword("TestingPasswordThatShouldBeInVault123!")
        .WithCleanUp(true)
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7.4.6")
        .WithCleanUp(true)
        .Build();

    private readonly KafkaContainer _kafkaContainer = new KafkaBuilder("confluentinc/cp-kafka:7.5.9")
        .WithKRaft()
        .WithCleanUp(true)
        .Build();

    private ConnectionMultiplexer _redisMultiplexer = null!;

    /// <summary>
    /// Test-controlled <see cref="IFeatureClient"/>. Defaults all flags to <c>false</c>; tests
    /// override per-scenario by calling <c>fixture.FeatureClient.GetBooleanValueAsync(...).Returns(...)</c>.
    /// </summary>
    public IFeatureClient FeatureClient { get; } = Substitute.For<IFeatureClient>();

    /// <summary>Mutable test clock — call <c>Advance(...)</c> to move time forward in a test.</summary>
    public FakeTimeProvider TimeProvider { get; } = new(Now);

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public string PostgresConnectionString => _pgContainer.GetConnectionString();

    public IDatabase RedisCacheDb => _redisMultiplexer.GetDatabase();

    protected override async ValueTask PreSetupAsync()
    {
        await _pgContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();

        var redisOptions = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
        redisOptions.AllowAdmin = true;
        _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisOptions);

        // Pre-create the inbound topic so KafkaFlow's StockLevelChanged consumer can
        // subscribe at host startup. Without it the consumer logs subscription errors
        // every poll cycle and floods the test output.
        await CreateStockLevelChangedTopicAsync();
    }

    protected override async ValueTask SetupAsync()
    {
        HttpClientRegistry = new HttpClientRegistry<Program>(this);

        // Materialize the schema from the EF model — per CLAUDE.md migrations are user-generated.
        // Mirrors the M4.4 Catalog.IntegrationTests.IntegrationTestFixture approach.
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            webBuilder
                .UseSetting("ConnectionStrings:Catalog", _pgContainer.GetConnectionString())
                .UseSetting("ConnectionStrings:Redis:Cache", _redisContainer.GetConnectionString())
                .UseSetting("Kafka:Brokers:0", _kafkaContainer.GetBootstrapAddress())
                // FakeOutboxWriter bypasses Schema Registry; default-tests don't need it. The
                // dedicated AvroByteFidelityTests bring their own real registry container.
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
                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter with a
                // fake. Avro byte fidelity is asserted in AvroByteFidelityTests with its own
                // Schema-Registry container; the rest of the suite stays fast.
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());

                // Pin the wall-clock so deterministic OccurredOnUtc / LastUpdatedAtUtc
                // assertions match across CI runs (mirrors M4.4 IntegrationTestFixture).
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(TimeProvider));

                // Replace the OpenFeature client so per-test feature-flag flips don't depend
                // on a JSON file on disk. The closed M5 follow-up
                // (catalog.show-discontinued-in-search) is exercised by SearchProductsTests
                // by stubbing this mock.
                services.Replace(ServiceDescriptor.Singleton(FeatureClient));

                // Relax JWT for the in-process test host. Configure (NOT PostConfigure) is
                // chosen so subsequent user-code overrides remain effective — the production
                // JwtBearerPostConfigureOptions runs in the post-configure pass and would
                // otherwise win the last-write race against our flips. Mirrors Weather + Basket
                // precedents.
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.RequireHttpsMetadata = false;
#pragma warning disable CA5404
                        options.TokenValidationParameters.ValidateIssuer = false;
                        options.TokenValidationParameters.ValidateAudience = false;
                        options.TokenValidationParameters.ValidateLifetime = false;
                        options.TokenValidationParameters.RequireExpirationTime = false;
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
        FeatureClient.ClearSubstitute(ClearOptions.All);
        TimeProvider.SetUtcNow(Now);

        // Flush Redis so idempotency-cache state cannot leak between tests.
        var endpoints = _redisMultiplexer.GetEndPoints();
        foreach (var endpoint in endpoints)
        {
            var server = _redisMultiplexer.GetServer(endpoint);
            await server.FlushAllDatabasesAsync();
        }

        // Truncate Catalog tables — cheaper than EnsureCreated/Delete cycles.
        // Snake-case conversion applies to entity-driven names (products, categories,
        // product_search_view) but Platform.ReliableMessaging.Outbox sets its table name
        // explicitly via ToTable("OutboxMessages", ...) which bypasses the convention.
        // Same shape for InboxMessages. Quote the mixed-case names so Postgres preserves them.
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync(
            $"TRUNCATE TABLE \"{CatalogDbContext.DefaultSchemaName}\".product_search_view, " +
            $"\"{CatalogDbContext.DefaultSchemaName}\".products, " +
            $"\"{CatalogDbContext.DefaultSchemaName}\".categories, " +
            $"\"{CatalogDbContext.DefaultSchemaName}\".\"OutboxMessages\", " +
            $"\"{CatalogDbContext.DefaultSchemaName}\".\"InboxMessages\" CASCADE;");
    }

    protected override async ValueTask TearDownAsync()
    {
        await _redisMultiplexer.DisposeAsync();
        await _pgContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }

    private async Task CreateStockLevelChangedTopicAsync()
    {
        var adminConfig = new AdminClientConfig
        {
            BootstrapServers = _kafkaContainer.GetBootstrapAddress(),
        };

        using var adminClient = new AdminClientBuilder(adminConfig).Build();
        try
        {
            await adminClient.CreateTopicsAsync(
            [
                new TopicSpecification
                {
                    Name = StockLevelChangedTopic,
                    NumPartitions = 3,
                    ReplicationFactor = 1,
                },
            ]);
        }
        catch (CreateTopicsException ex) when (ex.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            // Topic already exists — re-using container.
        }
    }
}
