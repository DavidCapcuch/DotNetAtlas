using Platform.OutboxRelay.WorkerService.Common;
using Platform.OutboxRelay.WorkerService.Observability;
using Platform.ServiceDefaults;
using Serilog;

namespace Platform.OutboxRelay.WorkerService;

/// <summary>
/// Can't use minimal host because Platform.OutboxRelay.Benchmark needs to reference the Program
/// which wouldn't have a namespace.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? ApplicationInfo.AppName;

            builder.AddPlatformHostConfiguration();
            builder.UsePlatformSerilog(options => options.ServiceName = serviceName);

            var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

            builder.Services.AddOpenTelemetryInternal(isDeployedEnvironment, builder.Configuration);
            builder.Services.AddHealthChecksInternal(builder.Configuration);
            builder.Services.AddDatabase(builder.Configuration);
            builder.Services.AddMemoryCache();
            builder.AddOutboxRelayWorker();

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
    }
}
