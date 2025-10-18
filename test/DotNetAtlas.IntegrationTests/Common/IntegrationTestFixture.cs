using System.Collections.Frozen;
using DotNetAtlas.ArchitectureTests;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.Communication.Kafka.Config;
using DotNetAtlas.Infrastructure.HttpClients.Weather.OpenMeteo;
using DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiCom;
using DotNetAtlas.Infrastructure.SignalR;
using DotNetAtlas.IntegrationTests.Common;
using EvolveDb;
using FastEndpoints.Testing;
using Hangfire;
using Hangfire.Storage;
using HealthChecks.UI.Core.HostedService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Respawn;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using Weather.Contracts;

[assembly: AssemblyFixture(typeof(IntegrationTestFixture))]

namespace DotNetAtlas.IntegrationTests.Common;

internal sealed class ForecastTestCollection : TestCollection<IntegrationTestFixture>;

internal sealed class SignalRTestCollection : TestCollection<IntegrationTestFixture>;

[DisableWafCache]
public class IntegrationTestFixture : AppFixture<Program>
{
    private const string Database = "Weather";

    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithPassword("pass123*!QWER")
        .WithName($"TestSqlServerFixture-{Guid.NewGuid()}")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7.4.2")
        .WithName($"TestRedisFixture-{Guid.NewGuid()}")
        .WithCleanUp(true)
        .Build();

    private readonly KafkaTestContainer _kafkaContainer = new KafkaTestContainer();

    private string _dbContainerConnectionString = null!;
    private Respawner _respawner = null!;
    private FrozenDictionary<string, object> _kafkaTestConsumers = null!;

    /// <summary>
    /// Shared Kafka test consumers reused across all tests for better performance.
    /// </summary>
    public FrozenDictionary<string, object> KafkaTestConsumers => _kafkaTestConsumers;

    protected override async ValueTask PreSetupAsync()
    {
        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _redisContainer.StartAsync(),
            _kafkaContainer.StartAsync());

        _dbContainerConnectionString = new SqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            InitialCatalog = Database,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectRetryCount = 10
        }.ToString();

        await _dbContainer.ExecScriptAsync($"CREATE DATABASE [{Database}]");
        await _dbContainer.ExecScriptAsync($"ALTER LOGIN sa WITH DEFAULT DATABASE = [{Database}]");
        await ExecuteFlywayScriptsAsync();
        _respawner = await Respawner.CreateAsync(_dbContainerConnectionString, new RespawnerOptions
        {
            SchemasToInclude =
            [
                "weather",
                "HangFire"
            ],
            WithReseed = true
        });
    }

    protected override ValueTask SetupAsync()
    {
        // Initialize shared Kafka consumers after the app is fully configured
        _kafkaTestConsumers = SetupKafkaTestConsumers();
        return ValueTask.CompletedTask;
    }

    private FrozenDictionary<string, object> SetupKafkaTestConsumers()
    {
        var kafkaOptions = Services.GetRequiredService<IOptions<KafkaOptions>>().Value;
        var topicsOptions = Services.GetRequiredService<IOptions<TopicsOptions>>().Value;

        return new Dictionary<string, object>
        {
            [topicsOptions.ForecastRequested] = new KafkaTestConsumer<ForecastRequestedEvent>(
                kafkaOptions.BrokersFlat,
                kafkaOptions.SchemaRegistry.Url,
                topicsOptions.ForecastRequested)
        }.ToFrozenDictionary();
    }

    protected override IHost ConfigureAppHost(IHostBuilder builder)
    {
        var redisOptions = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
        redisOptions.AllowAdmin = true;
        redisOptions.DefaultDatabase = 0;
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = 5;
        redisOptions.ConnectTimeout = 15000;
        redisOptions.SyncTimeout = 10000;
        redisOptions.KeepAlive = 60;

        var kafkaOptions = _kafkaContainer.KafkaOptions;
        builder.ConfigureWebHost(builder =>
        {
            builder.UseSetting($"ConnectionStrings:{ConnectionStrings.Weather}", _dbContainerConnectionString);
            builder.UseSetting($"ConnectionStrings:{ConnectionStrings.Redis}", redisOptions.ToString());

            for (var i = 0; i < kafkaOptions.Brokers.Length; i++)
            {
                builder.UseSetting($"{KafkaOptions.Section}:Brokers:{i}", kafkaOptions.Brokers[i]);
            }

            builder.UseSetting($"{SchemaRegistryOptions.Section}:Url", kafkaOptions.SchemaRegistry.Url);
            builder.UseSetting($"{AvroSerializerOptions.Section}:AutoRegisterSchemas",
                kafkaOptions.AvroSerializer.AutoRegisterSchemas.ToString());
            builder.UseSetting($"{AvroSerializerOptions.Section}:SubjectNameStrategy",
                kafkaOptions.AvroSerializer.SubjectNameStrategy.ToString());
        });

        return base.ConfigureAppHost(builder);
    }

    protected override void ConfigureApp(IWebHostBuilder builder)
    {
        var injectableTestOutputSink = new InjectableTestOutputSink();
        builder
            .UseEnvironment("Testing")
            .ConfigureTestServices(services =>
            {
                services.AddScoped<OpenMeteoWeatherProvider>();
                services.AddScoped<WeatherApiComProvider>();
                services.AddSingleton<RedisSignalRGroupManager>();
                services.AddSingleton<IInjectableTestOutputSink>(injectableTestOutputSink);
                services.AddSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration.MinimumLevel.Debug()
                        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);

                    loggerConfiguration.WriteTo.InjectableTestOutput(injectableTestOutputSink);
                    loggerConfiguration.Enrich.FromLogContext();
                }, true, true);
                services.AddSingleton<IHealthCheckReportCollector>(Substitute.For<IHealthCheckReportCollector>());
            });
    }

    private async Task ExecuteFlywayScriptsAsync()
    {
        await using var dbContainerConnection = new SqlConnection(_dbContainerConnectionString);
        await dbContainerConnection.OpenAsync();
        var evolve = new Evolve(dbContainerConnection)
        {
            Locations =
            [
                DatabasePaths.FlywayMigrationsDirectory
            ]
        };

        evolve.Migrate();
    }

    public async Task ResetDatabasesAsync()
    {
        // Clear all recurring Hangfire jobs explicitly, SQL clean up is not enough
        // internal states of hangfire etc. need to be cleaned up
        var recurringJobManager = Services.GetRequiredService<IRecurringJobManager>();
        var recurringJobs = Services.GetRequiredService<IBackgroundJobClientV2>().Storage.GetConnection()
            .GetRecurringJobs();
        foreach (var job in recurringJobs)
        {
            recurringJobManager.RemoveIfExists(job.Id);
        }

        var multiplexer = Services.GetRequiredService<IConnectionMultiplexer>();
        var resetDbTasks = multiplexer.GetServers()
            .Select(server => server.FlushAllDatabasesAsync())
            .ToList();
        resetDbTasks.Add(_respawner.ResetAsync(_dbContainerConnectionString));

        await Task.WhenAll(resetDbTasks);
    }

    protected override async ValueTask TearDownAsync()
    {
        // Dispose all Kafka test consumers
        if (_kafkaTestConsumers != null)
        {
            foreach (var consumer in _kafkaTestConsumers.Values.OfType<IKafkaTestConsumer>())
            {
                consumer.Dispose();
            }
        }

        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
        await _kafkaContainer.DisposeAsync();
    }
}
