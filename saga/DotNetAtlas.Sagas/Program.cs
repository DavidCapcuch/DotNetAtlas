using System.Globalization;
using DotNetAtlas.Sagas.Common;
using DotNetAtlas.Sagas.Common.Extensions;
using DotNetAtlas.Sagas.Persistence.Database;
using Serilog;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var isClusterEnvironment = builder.Environment.IsInCluster();

    builder.Configuration.AddEnvironmentVariables();
    builder
        .Host
        .UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = !isClusterEnvironment;
            options.ValidateOnBuild = !isClusterEnvironment;
        });

    builder.AddInfrastructure(isClusterEnvironment);
    builder.Services.AddSagaOrchestration(builder.Configuration, isClusterEnvironment);

    var app = builder.Build();

    app.MapHealthChecksInternal();
    app.UseHealthChecksPrometheusExporterInternal();

    // In production, flyway should be used, therefore also during
    // integration tests to ensure the SQL scripts are applied correctly
    if (!app.Environment.IsProduction() && !app.Environment.IsTesting())
    {
        await app.InitialiseDatabaseAsync();
    }

    await app.RunAsync();
}
catch (HostAbortedException)
{
    Log.Information("Host aborted, shutting down gracefully");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
