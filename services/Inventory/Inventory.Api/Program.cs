using Inventory.Api.Common;
using Inventory.Api.Common.Config;
using Inventory.Application.Common;
using Inventory.Infrastructure.Common;
using Inventory.Infrastructure.Persistence.Database;
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
        options.ServiceName = "Inventory";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services
        .AddApi(builder.Configuration)
        .AddInventoryAuthentication(builder.Configuration)
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    // Tests seed reservations at fixed past timestamps, so an expiry worker ticking on the real clock
    // in the shared test host would release them under other tests.
    if (!builder.Environment.IsTesting())
    {
        builder.Services.AddReservationExpiryWorker();
    }

    var app = builder.Build();

    app.UsePlatformExceptionHandling();

    app.UseStatusCodePages();

    // Output cache after auth: the idempotency cache answers a replay (or rejects a missing key)
    // before the endpoint runs, so ahead of auth it would do so for a caller auth would reject
    // (401/403).
    app.UseRouting()
        .UseCors(InventoryCorsOptions.DefaultCorsPolicyName)
        .UseAuthentication()
        .UseAuthorization()
        .UseOutputCache();

    app.UseInventoryFastEndpoints();

    app.MapRazorPages();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    await app.MigrateOnStartupIfDevelopmentAsync<InventoryDbContext>();

    // Integration tests invoke the typed Kafka handlers directly with synthetic message contexts;
    // booting the consumers in-test would require Kafka + Schema Registry containers.
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
/// Partial <c>Program</c> marker so integration tests can host the service through FastEndpoints'
/// <c>AppFixture&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
