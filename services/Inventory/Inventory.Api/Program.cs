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
        .AddInventoryAuthentication(builder.Configuration, builder.Environment)
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    // The reservation-expiry worker boots WITH the host. Skip it in the
    // Testing environment so functional tests can run EF migrations after
    // the host starts (the worker's eager startup tick would otherwise
    // crash querying reservation_audit before the table exists). M6
    // integration tests resolve the worker directly from DI without the
    // hosted-service loop.
    if (!builder.Environment.IsTesting())
    {
        builder.Services.AddReservationExpiryWorker();
    }

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

    app.UseRouting()
        .UseCors(InventoryCorsOptions.DefaultCorsPolicyName)
        .UseOutputCache()
        .UseAuthentication()
        .UseAuthorization();

    app.UseInventoryFastEndpoints();

    app.MapRazorPages();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    await app.MigrateOnStartupIfDevelopmentAsync<InventoryDbContext>();

    // Skip the Kafka cluster boot in the test host. Functional / integration
    // tests register the typed Kafka handlers directly and invoke them with
    // synthetic message contexts (matches the Ordering M5 precedent at
    // test/Ordering.IntegrationTests/Common/IntegrationTestFixture.cs:19-20).
    // Booting the consumers in-test would require Kafka + Schema Registry
    // containers — deferred to M10's end-to-end smoke.
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
/// Partial <c>Program</c> marker so functional + integration tests can use
/// <c>WebApplicationFactory&lt;Program&gt;</c> and FastEndpoints'
/// <c>AppFixture&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
