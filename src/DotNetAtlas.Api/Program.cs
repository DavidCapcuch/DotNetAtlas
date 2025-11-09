using System.Reflection;
using DotNetAtlas.Api.Common;
using DotNetAtlas.Api.Common.Config;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Application.Common;
using DotNetAtlas.Infrastructure.Common;
using DotNetAtlas.Infrastructure.Common.Authorization;
using DotNetAtlas.Infrastructure.Common.Extensions;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using Hangfire;
using Serilog;

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

    builder.UseSerilogInternal(isClusterEnvironment);

    if (builder.Environment.IsLocal())
    {
        builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), true);
    }

    builder.Services
        .AddPresentation(builder.Configuration)
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isClusterEnvironment);

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

    if (isClusterEnvironment)
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

    app.MapHealthChecksInternal()
        .MapSignalRWithDevTools()
        .MapClientGenerationApis()
        .MapHangfireDashboardWithAuthorizationPolicy(AuthPolicies.DevOnly, "/hangfire-dashboard");
    app.UseHealthChecksPrometheusExporterInternal();

    app.MapStaticAssets();
    app.MapRazorPages()
        .WithStaticAssets();

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
