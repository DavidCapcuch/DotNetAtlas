using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.ArchitectureTests;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.HttpClients.Weather.OpenMeteoProvider;
using DotNetAtlas.Infrastructure.HttpClients.Weather.WeatherApiComProvider;
using EvolveDb;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Respawn;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using Testcontainers.MsSql;

namespace DotNetAtlas.IntegrationTests.Base;

internal sealed class CollectionA : TestCollection<IntegrationTestFixture>;

public class IntegrationTestFixture : AppFixture<Program>
{
    public IDotNetAtlasInstrumentation Instrumentation { get; private set; } = null!;

    private const string DATABASE = "Weather";

    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("pass123*!QWER")
        .WithName($"TestSqlServerFixture-{Guid.NewGuid()}")
        .Build();

    private string _dbContainerConnectionString = null!;
    private Respawner _respawner = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _dbContainer.StartAsync();
        _dbContainerConnectionString = new SqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            InitialCatalog = DATABASE,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectRetryCount = 10
        }.ToString();

        await _dbContainer.ExecScriptAsync($"CREATE DATABASE [{DATABASE}]");
        await _dbContainer.ExecScriptAsync($"ALTER LOGIN sa WITH DEFAULT DATABASE = [{DATABASE}]");
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
        Instrumentation = Services.GetRequiredService<IDotNetAtlasInstrumentation>();
        return ValueTask.CompletedTask;
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
                services.AddSingleton<IInjectableTestOutputSink>(injectableTestOutputSink);
                services.AddSerilog((_, loggerConfiguration) =>
                {
                    loggerConfiguration.MinimumLevel.Debug()
                        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);

                    loggerConfiguration.WriteTo.InjectableTestOutput(injectableTestOutputSink);
                    loggerConfiguration.Enrich.FromLogContext();
                });
            }).ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"ConnectionStrings:{ConnectionStrings.Weather}"] = _dbContainerConnectionString
                });
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
        await _respawner.ResetAsync(_dbContainerConnectionString);
    }

    protected override async ValueTask TearDownAsync()
    {
        await _dbContainer.DisposeAsync();
    }
}
