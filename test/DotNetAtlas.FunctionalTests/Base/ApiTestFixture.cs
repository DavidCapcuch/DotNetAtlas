using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.ArchitectureTests;
using DotNetAtlas.Infrastructure.Common.Config;
using EvolveDb;
using FastEndpoints.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Respawn;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.XUnit.Injectable;
using Serilog.Sinks.XUnit.Injectable.Abstract;
using Serilog.Sinks.XUnit.Injectable.Extensions;
using Testcontainers.MsSql;

namespace DotNetAtlas.FunctionalTests.Base;

internal sealed class CollectionA : TestCollection<ApiTestFixture>;

public class ApiTestFixture : AppFixture<Program>
{
    private const string Database = "Weather";

    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("pass123*!QWER")
        .WithName($"TestSqlServerFixture-{Guid.NewGuid()}")
        .Build();

    public IDotNetAtlasInstrumentation Instrumentation { get; private set; } = null!;

    private TracerProvider? _testTracerProvider;

    private string _dbContainerConnectionString = null!;
    private Respawner _respawner = null!;

    protected override async ValueTask PreSetupAsync()
    {
        await _dbContainer.StartAsync();
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
        Instrumentation = Services.GetRequiredService<IDotNetAtlasInstrumentation>();
        _testTracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddHttpClientInstrumentation()
            .AddSource("*")
            .Build();
        return ValueTask.CompletedTask;
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
                });
            })
            .ConfigureAppConfiguration((_, configBuilder) =>
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
        _testTracerProvider?.Dispose();
        await _dbContainer.DisposeAsync();
    }
}
