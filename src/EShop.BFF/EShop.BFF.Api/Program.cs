using EShop.BFF.Api.Common;
using EShop.BFF.Api.Composition;
using EShop.BFF.Infrastructure.Common;
using KafkaFlow;
using Platform.ServiceDefaults;
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
        options.ServiceName = "BFF";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services
        .AddApi(builder.Configuration)
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    // Registered after AddInfrastructure (which wires feature flags) so this hosted service starts AFTER
    // OpenFeature has initialized its provider — the warmer must read bff.home-page-eager-cache-warm from
    // the loaded flags.json, not a not-yet-ready provider (ADR-0014 kill-switch correctness).
    builder.Services.AddHostedService<HomePageCacheWarmer>();

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

    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseBffFastEndpoints();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    // Start the cache-invalidation consumer (group bff-group). Skipped in the test host — the Kafka
    // integration fixture boots the bus explicitly against its Testcontainers broker.
    if (!app.Environment.IsTesting())
    {
        var kafkaBus = app.Services.CreateKafkaBus();
        await kafkaBus.StartAsync();
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

/// <summary>
/// Public Program class so test hosts (FastEndpoints' <c>AppFixture&lt;Program&gt;</c>) can
/// reference the entry point.
/// </summary>
public partial class Program;
