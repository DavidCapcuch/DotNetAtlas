using Catalog.Api.Common;
using Catalog.Api.Common.Config;
using Catalog.Application.Common;
using Catalog.Infrastructure.Common;
using Catalog.Infrastructure.Persistence.Database;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.CorrelationId;
using Platform.ServiceDefaults.FeatureFlags;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddServiceDefaults(options =>
    {
        options.ServiceName = "Catalog";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services.AddFeatureFlags(builder.Configuration);

    builder.Services
        .AddApi(builder.Configuration)
        .AddCatalogAuthentication(builder.Configuration, builder.Environment)
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    // Readiness probes (Self + Postgres + Kafka + redis-cache + Schema Registry) are
    // wired by AddInfrastructure → AddCatalogHealthChecks (Catalog.Infrastructure.Common).
    // MapPlatformHealthCheckEndpoints publishes /api/healthz (live) + /api/readiness (ready).

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

    app.UseCorrelationId();

    app.UseRouting()
        .UseCors(CatalogCorsOptions.DefaultCorsPolicyName)
        .UseOutputCache()
        .UseAuthentication()
        .UseAuthorization();

    app.UseCatalogFastEndpoints();

    app.MapRazorPages();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    await app.MigrateOnStartupIfDevelopmentAsync<CatalogDbContext>();

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

/// <summary>
/// Public Program class so test hosts (FastEndpoints' <c>AppFixture&lt;Program&gt;</c>) can
/// reference the entry point.
/// </summary>
public partial class Program;
