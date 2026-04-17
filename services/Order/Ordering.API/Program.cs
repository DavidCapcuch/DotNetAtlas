using KafkaFlow;
using Ordering.API.Common;
using Ordering.API.Common.Config;
using Ordering.API.Common.Extensions;
using Ordering.Application.Common;
using Ordering.Application.Common.Observability;
using Ordering.Infrastructure.Common;
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

    builder.Services
        .AddPresentation(builder.Configuration)
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    var app = builder.Build();

    if (app.Environment.IsProduction())
    {
        app.UseExceptionHandler();
    }
    else
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseStatusCodePages();

    if (isDeployedEnvironment)
    {
        app.UseHttpsRedirection()
            .UseSecurityHeaders();
    }

    app.UseRouting()
        .UseCors(CorsPolicyOptions.DefaultCorsPolicyName)
        .UseRequestContextTelemetry();

    app.UseFastEndpointsInternal();

    app.MapStaticAssets();
    app.MapRazorPages()
        .WithStaticAssets();

    app.MapPlatformHealthCheckEndpoints();
    app.MapClientGenerationApisInternal();
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
