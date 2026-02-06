using DotNetAtlas.Api.Common;
using DotNetAtlas.Api.Common.Config;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Application.Common;
using DotNetAtlas.Application.Common.Observability;
using DotNetAtlas.Infrastructure.Common;
using DotNetAtlas.Infrastructure.Common.Authorization;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using DotNetAtlas.ServiceDefaults;
using Hangfire;
using KafkaFlow;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddPlatformHostConfiguration();
    builder.UsePlatformSerilog(options =>
    {
        options.ServiceName = ApplicationInfo.AppName;
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

    app.MapPlatformHealthCheckEndpoints()
        .MapHealthChecksUI()
        .RequireAuthorization(AuthPolicies.DevOnly);

    app.MapSignalRWithDevTools()
        .MapClientGenerationApis()
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
