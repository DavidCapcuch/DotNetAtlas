using Invoicing.API.Common;
using Invoicing.Application.Common;
using Invoicing.Infrastructure.Common;
using KafkaFlow;
using Microsoft.Extensions.Hosting;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.CorrelationId;
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
        options.ServiceName = "Invoicing.Api";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    // EF Core sensitive-data logging exposes parameter values (PII-bearing _enc
    // columns per ADR-0011) in the query log. Gate strictly to Development —
    // not all non-deployed environments (Test, Staging, the Testing test-host)
    // should leak PII into logs (closeout1 M7).
    var enableSensitiveDataLogging = builder.Environment.IsDevelopment();

    builder.Services
        .AddInvoicingAuth(builder.Configuration, isDeployedEnvironment)
        .AddPresentation(builder.Configuration)
        .AddInvoicingApplication()
        .AddInvoicingInfrastructure(builder.Configuration, enableSensitiveDataLogging);

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

    app.UseRouting();

    // Order matters: OutputCache reads must happen before authn so cached
    // responses can short-circuit (FastEndpoints' .Idempotency() filter sits
    // inside the endpoint pipeline, but AddIdempotencyKeyOutputCache wires the
    // underlying IOutputCacheStore which UseOutputCache attaches).
    app.UseOutputCache();
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseInvoicingFastEndpoints();

    app.MapPlatformHealthCheckEndpoints();

    // Skip the Kafka enrichment-projection consumers in the test host. M6's
    // integration tests exercise the consumer slice against a real broker; M8's
    // functional tests exercise the HTTP surface only and do not need Kafka up.
    if (!app.Environment.IsEnvironment("Testing"))
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
