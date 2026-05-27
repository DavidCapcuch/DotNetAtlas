using KafkaFlow;
using Notifications.Application.Common;
using Notifications.Infrastructure.Common;
using Notifications.Infrastructure.Common.Observability;
using Notifications.Infrastructure.Persistence.Database;
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

    builder.AddServiceDefaults(options => options.ServiceName = serviceName);

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    var app = builder.Build();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    await app.MigrateOnStartupIfDevelopmentAsync<NotificationsDbContext>();

    // Skip the Kafka cluster boot in the test host. Integration tests register the
    // typed Kafka handlers directly and invoke them with synthetic message contexts
    // (matches the Inventory precedent at services/Inventory/Inventory.Api/Program.cs:79-83).
    // Booting the consumers in-test would require Kafka + Schema Registry test
    // containers — out of scope for the handler-level coverage these tests provide.
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
/// Partial <c>Program</c> marker so integration tests can use
/// <c>Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory&lt;Program&gt;</c>.
/// Matches the convention in every other BC API (Catalog/Ordering/Invoicing/etc.).
/// </summary>
public partial class Program;
