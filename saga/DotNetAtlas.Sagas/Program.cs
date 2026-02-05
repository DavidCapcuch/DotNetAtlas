using DotNetAtlas.Sagas.Common;
using DotNetAtlas.Sagas.Common.Observability;
using DotNetAtlas.Sagas.Persistence.Database;
using DotNetAtlas.ServiceDefaults;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddPlatformHostConfiguration();
    builder.UsePlatformSerilog(options => options.ServiceName = ApplicationInfo.AppName);

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.AddInfrastructure();
    builder.Services.AddSagaOrchestration(builder.Configuration, isDeployedEnvironment);

    var app = builder.Build();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

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
