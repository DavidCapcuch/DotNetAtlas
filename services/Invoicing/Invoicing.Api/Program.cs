using Invoicing.Api.Common;
using Invoicing.Application.Common;
using Invoicing.Infrastructure.Common;
using Invoicing.Infrastructure.Persistence.Database;
using KafkaFlow;
using Microsoft.Extensions.Hosting;
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
        options.ServiceName = "Invoicing";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services
        .AddApi(builder.Configuration)
        .AddInvoicingAuthentication(builder.Configuration, builder.Environment)
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

    // Order matters: OutputCache reads must happen before authn so cached
    // responses can short-circuit (FastEndpoints' .Idempotency() filter sits
    // inside the endpoint pipeline, but AddIdempotencyKeyOutputCache wires the
    // underlying IOutputCacheStore which UseOutputCache attaches).
    app.UseRouting()
        .UseOutputCache()
        .UseAuthentication()
        .UseAuthorization();

    app.UseInvoicingFastEndpoints();

    app.MapRazorPages();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    await app.MigrateOnStartupIfDevelopmentAsync<InvoicingDbContext>();

    // Skip the Kafka enrichment-projection consumers in the test host. The
    // integration tests exercise the consumer slice against a real broker; the
    // functional tests exercise the HTTP surface only and do not need Kafka up.
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
