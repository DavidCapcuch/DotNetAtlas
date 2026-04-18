using Hangfire;
using KafkaFlow;
using Platform.ServiceDefaults;
using Serilog;
using Weather.Api.Common;
using Weather.Api.Common.Config;
using Weather.Api.Common.Extensions;
using Weather.Application.Common;
using Weather.Application.Common.Observability;
using Weather.Infrastructure.Common;
using Weather.Infrastructure.Common.Authorization;
using Weather.Infrastructure.Persistence.Database.Seed;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? ApplicationInfo.AppName;

    builder.AddPlatformHostConfiguration();
    builder.UsePlatformSerilog(options =>
    {
        options.ServiceName = serviceName;
    });

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
        .UseOutputCache()
        .UseAuthentication()
        .UseRequestContextTelemetry()
        .UseAuthorization();

    app.UseFastEndpointsInternal();

    app.MapPlatformHealthCheckEndpoints();

    if (!isDeployedEnvironment)
    {
        app.MapHealthChecksUI()
            .RequireAuthorization(AuthPolicies.DevOnly);
    }

    app.MapSignalRWithDevTools()
        .MapClientGenerationApisInternal()
        .MapHangfireDashboardWithAuthorizationPolicy(AuthPolicies.DevOnly, "/hangfire-dashboard");
    app.UsePlatformHealthChecksPrometheusExporter();

    app.MapStaticAssets();
    app.MapRazorPages()
        .WithStaticAssets();

    // In production, SQL scripts generated from EF core migrations should be used,
    // therefore also during integration tests to ensure the SQL scripts are applied correctly,
    // see https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying?tabs=dotnet-core-cli
    if (app.Environment.IsLocal())
    {
        await app.InitialiseDatabaseAsync();
    }

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
