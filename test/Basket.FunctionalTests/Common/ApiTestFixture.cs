using Basket.Application.Abstractions;
using Basket.FunctionalTests.Common.TestClientInfrastructure;
using Basket.Infrastructure.Persistence.Database;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Platform.ReliableMessaging.Outbox.EFCore;
using Platform.Test.Framework;
using Platform.Test.Framework.Database;
using Respawn;
using Serilog;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using StackExchange.Redis;
using Testcontainers.Kafka;
using Testcontainers.Redis;

namespace Basket.FunctionalTests.Common;

[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private const string BasketTopic = "basket.sessions";

    private readonly PostgreSqlTestContainer _dbContainer = new(
        databaseName: "Basket",
        sqlScriptsMigrationsPath: SolutionPaths.SqlScriptMigrationsDirectoryFor("services/Basket/Basket.Infrastructure"),
        new RespawnerOptions
        {
            SchemasToInclude = [BasketDbContext.DefaultSchemaName]
        });

    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7.4.6")
        .WithCleanUp(true)
        .Build();

    private readonly KafkaContainer _kafkaContainer = new KafkaBuilder("confluentinc/cp-kafka:7.5.9")
        .WithKRaft()
        .WithCleanUp(true)
        .Build();

    private ConnectionMultiplexer _redisMultiplexer = null!;

    /// <summary>
    /// Test-controlled <see cref="IProductCatalogQueryPort"/>. Tests call
    /// <c>fixture.Catalog</c> to stub Catalog responses without spinning a real Catalog
    /// service. WireMock'd HTTP would exercise the adapter end-to-end — that's a
    /// proposed M9 follow-up.
    /// </summary>
    public IProductCatalogQueryPort Catalog { get; } = Substitute.For<IProductCatalogQueryPort>();

    public HttpClientRegistry<Program> HttpClientRegistry { get; private set; } = null!;

    public IConnectionMultiplexer RedisMultiplexer => _redisMultiplexer;

    public IDatabase RedisBasketDb => _redisMultiplexer.GetDatabase();

    protected override async ValueTask PreSetupAsync()
    {
        await _dbContainer.StartAsync();
        await _redisContainer.StartAsync();
        await _kafkaContainer.StartAsync();

        var redisOptions = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
        redisOptions.AllowAdmin = true;
        _redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisOptions);

        await CreateBasketTopicAsync();
    }

    protected override ValueTask SetupAsync()
    {
        HttpClientRegistry = new HttpClientRegistry<Program>(this);
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        a.ConfigureWebHost(webBuilder =>
        {
            var redisConnectionString = _redisContainer.GetConnectionString();
            webBuilder
                .UseSetting("ConnectionStrings:Basket", _dbContainer.ConnectionString)
                .UseSetting("ConnectionStrings:Redis:Basket", redisConnectionString)
                .UseSetting("ConnectionStrings:Redis:Cache", redisConnectionString)
                .UseSetting("Kafka:Brokers:0", _kafkaContainer.GetBootstrapAddress())
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
                // Substitute the ACL port so tests can stub Catalog responses without
                // a Catalog service running. Use Replace so the production HTTP-adapter
                // registration in Basket.Infrastructure is removed — AddSingleton(instance)
                // would only add a second descriptor with the NSubstitute proxy's runtime
                // type, leaving the real adapter live for resolution. Real adapter exercise
                // stays in M9 follow-up.
                services.Replace(ServiceDescriptor.Singleton<IProductCatalogQueryPort>(Catalog));

                // Replace the production Avro+SchemaRegistry-backed IOutboxWriter with a
                // fake that writes a stub OutboxMessage row directly. Avro byte-level
                // fidelity is asserted in Basket.IntegrationTests; functional tests only
                // need to verify "the right outbox row landed". Avoids spinning a
                // Confluent Schema Registry Testcontainer.
                services.Replace(ServiceDescriptor.Singleton<IOutboxWriter, FakeOutboxWriter>());

                // Relax JWT validation for the in-process test host. Split across two
                // option-pipeline phases because each half has a different ordering constraint:
                //
                //   * Configure phase  — RequireHttpsMetadata = false must land here so the
                //     built-in JwtBearerPostConfigureOptions (which runs in PostConfigure phase)
                //     skips its HTTPS-authority guard. SignatureValidator is also fine here:
                //     it's a per-options field that any later callback could overwrite, but
                //     nothing downstream re-sets it.
                //
                //   * PostConfigure phase — the five TokenValidationParameters flags must land
                //     here, NOT in Configure, because AddPlatformJwtBearer (#223,
                //     platform/Platform.ServiceDefaults/Auth/JwtBearerConfigurator.cs) installs
                //     its own PostConfigure that re-pins these five flags to true. PostConfigure
                //     callbacks run in registration order; ConfigureTestServices runs after
                //     Program.cs, so the test's PostConfigure registers later and gets the
                //     actual last word.
                services.Configure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
                        options.RequireHttpsMetadata = false;
                        options.TokenValidationParameters.SignatureValidator = (token, _) =>
                            new JsonWebToken(token);
                    });

                services.PostConfigure<JwtBearerOptions>(
                    JwtBearerDefaults.AuthenticationScheme,
                    options =>
                    {
#pragma warning disable CA5404
                        options.TokenValidationParameters.ValidateIssuer = false;
                        options.TokenValidationParameters.ValidateAudience = false;
                        options.TokenValidationParameters.ValidateLifetime = false;
#pragma warning restore CA5404
                        options.TokenValidationParameters.ValidateIssuerSigningKey = false;
                        options.TokenValidationParameters.RequireSignedTokens = false;
                    });
            });
    }

    public async Task ResetFixtureStateAsync()
    {
        Catalog.ClearSubstitute(ClearOptions.All);

        // Flush Redis between tests so basket-key + idempotency-cache state cannot leak.
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
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }

    private async Task CreateBasketTopicAsync()
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
                    Name = BasketTopic,
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
