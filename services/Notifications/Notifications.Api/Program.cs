using KafkaFlow;
using Notifications.Api.Common;
using Notifications.Api.SignalRHubs;
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

    // The Hangfire processing server is skipped in the test host: integration tests invoke the
    // channel dispatchers directly (dispatcher-direct seam), so no background job runner is needed.
    var enableBackgroundJobServer = !builder.Environment.IsTesting();

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment, enableBackgroundJobServer);

    // In-app bell transport (#316): JWT bearer auth host + the SignalR hub. Independent of the
    // channel fan-out — no Bell IChannelDispatcher / Keyed-DI entry yet (that is #317). ADR-0032.
    builder.Services
        .AddNotificationsAuthentication(builder.Configuration, builder.Environment)
        .AddNotificationsSignalR();

    var app = builder.Build();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHub<NotificationHub>(NotificationHub.RoutePattern);

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
