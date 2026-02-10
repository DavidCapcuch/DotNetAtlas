using DotNetAtlas.ServiceDefaults;
using Payments.Common;
using Payments.Common.Observability;
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

    builder.Services.AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    var app = builder.Build();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

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
