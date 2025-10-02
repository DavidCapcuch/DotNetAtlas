using DotNetAtlas.ArchitectureTests;
using DotNetAtlas.FunctionalTests.Common;
using DotNetAtlas.Infrastructure.Common.Config;
using EvolveDb;
using FastEndpoints.Testing;
using Hangfire;
using HealthChecks.UI.Core.HostedService;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OpenTelemetry;
using Respawn;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(ApiTestFixture))]

namespace DotNetAtlas.FunctionalTests.Common;

internal sealed class FeedbackTestCollection : TestCollection<ApiTestFixture>;

internal sealed class ForecastTestCollection : TestCollection<ApiTestFixture>;

internal sealed class SignalRTestCollection : TestCollection<ApiTestFixture>;

[DisableWafCache]
public class ApiTestFixture : AppFixture<Program>
{
    private const string Database = "Weather";

    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithPassword("pass123*!QWER")
        .WithName($"TestSqlServerFixture-{Guid.NewGuid()}")
        .Build();

    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7.4.2")
        .WithCleanUp(true)
        .WithName($"TestRedisFixture-{Guid.NewGuid()}")
        .Build();

    private string _dbContainerConnectionString = null!;
    private Respawner _respawner = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await Task.WhenAll(
            _dbContainer.StartAsync(),
            _redisContainer.StartAsync());
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
                "weather"
            ]
        });
    }

    protected override ValueTask SetupAsync()
    {
        return ValueTask.CompletedTask;
    }

    protected override IHost ConfigureAppHost(IHostBuilder a)
    {
        var redisOptions = ConfigurationOptions.Parse(_redisContainer.GetConnectionString());
        redisOptions.AllowAdmin = true;
        redisOptions.DefaultDatabase = 0;
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = 5;
        redisOptions.ConnectTimeout = 15000;
        redisOptions.SyncTimeout = 10000;
        redisOptions.KeepAlive = 60;

        a.ConfigureWebHost(builder =>
        {
            builder.UseSetting($"ConnectionStrings:{ConnectionStrings.Weather}", _dbContainerConnectionString);
            builder.UseSetting($"ConnectionStrings:{ConnectionStrings.Redis}", redisOptions.ToString());
        });

        return base.ConfigureAppHost(a);
    }

    protected override void ConfigureApp(IWebHostBuilder builder)
    {
        var injectableTestOutputSink = new InjectableTestOutputSink();
        builder
            .UseEnvironment("Testing")
            .ConfigureTestServices(services =>
            {
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
                // Disable background jobs
                services.AddSingleton<IRecurringJobManager>(Substitute.For<IRecurringJobManager>());
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
        using var _ = SuppressInstrumentationScope.Begin();

        var multiplexer = Services.GetRequiredService<IConnectionMultiplexer>();
        var resetDbTasks = multiplexer.GetServers()
            .Select(server => server.FlushAllDatabasesAsync())
            .ToList();
        resetDbTasks.Add(_respawner.ResetAsync(_dbContainerConnectionString));

        await Task.WhenAll(resetDbTasks);
    }

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }
}
