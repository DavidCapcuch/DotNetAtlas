using KafkaFlow;
using Notifications.Common;
using Notifications.Common.Observability;
using Platform.ServiceDefaults;
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

    builder.Services.AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    var app = builder.Build();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    var kafkaBus = app.Services.CreateKafkaBus();
    await kafkaBus.StartAsync();

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
