using Platform.ServiceDefaults;
using Platform.ServiceDefaults.FeatureFlags;
using SagaOrchestrators.Common;
using SagaOrchestrators.Common.Observability;
using SagaOrchestrators.Common.Persistence.Database;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? ApplicationInfo.AppName;

    builder.AddPlatformHostConfiguration();
    builder.UsePlatformSerilog(options => options.ServiceName = serviceName);

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.AddInfrastructure();
    builder.Services.AddFeatureFlags(builder.Configuration);
    builder.Services.AddSagaOrchestration(builder.Configuration, isDeployedEnvironment);

    var app = builder.Build();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    await app.MigrateOnStartupIfLocalAsync<SagaDbContext>();

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
