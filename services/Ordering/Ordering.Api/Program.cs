using KafkaFlow;
using Ordering.Api.Common;
using Ordering.Application.Common;
using Ordering.Infrastructure.Common;
using Ordering.Infrastructure.Persistence.Database;
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
        options.ServiceName = "Ordering";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services
        .AddApi(builder.Configuration)
        .AddOrderingAuthentication(builder.Configuration)
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    var app = builder.Build();

    app.UsePlatformExceptionHandling();

    app.UseStatusCodePages();

    // Order matters: OutputCache reads must happen before authn so cached
    // responses can short-circuit (FastEndpoints' .Idempotency() filter
    // sits inside the endpoint pipeline, but AddIdempotencyKeyOutputCache
    // wires the underlying IOutputCacheStore which UseOutputCache attaches).
    app.UseRouting()
        .UseOutputCache()
        .UseAuthentication()
        .UseAuthorization();

    app.UseOrderingFastEndpoints();

    app.MapRazorPages();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    await app.MigrateOnStartupIfDevelopmentAsync<OrderingDbContext>();

    // Skip the Kafka saga-command consumer in the test host. The
    // functional-test slice exercises the HTTP surface only; the consumer
    // is integration-tested against a real broker elsewhere. Booting the
    // consumer in tests would require a Kafka + schema-registry container
    // pair the HTTP-surface tests don't need.
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
/// Partial <c>Program</c> marker so integration / functional tests can use
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
