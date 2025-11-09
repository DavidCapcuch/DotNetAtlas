using DotNetAtlas.OutboxRelay.WorkerService.Common;
using DotNetAtlas.OutboxRelay.WorkerService.Common.Extensions;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay.Config;
using Serilog;

namespace DotNetAtlas.OutboxRelay.WorkerService;

#pragma warning disable CA1052
public class Program
#pragma warning restore CA1052
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            var isClusterEnvironment = builder.Environment.IsInCluster();

            builder.Configuration.AddEnvironmentVariables();

            builder.UseSerilogInternal(isClusterEnvironment);
            builder.Services.AddOpenTelemetryInternal(isClusterEnvironment, builder.Configuration);
            builder.Services.AddHealthChecksInternal(builder.Configuration);
            builder.Services.AddDatabase(builder.Configuration);
            builder.Services.AddMemoryCache();
            builder.AddOutboxRelayWorker();

            var app = builder.Build();

            app.MapHealthChecksInternal();
            app.UseHealthChecksPrometheusExporterInternal();

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
