using KafkaFlow;
using Microsoft.Extensions.Hosting;
using Ordering.Api.Common;
using Ordering.Application.Common;
using Ordering.Infrastructure.Common;
using Ordering.Infrastructure.Persistence.Database;
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
        options.ServiceName = "Ordering.Api";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services
        .AddPresentation(builder.Configuration)
        .AddOrderingAuthentication(builder.Configuration, builder.Environment)
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

    app.UseCorrelationId();

    app.UseRouting();

    // Order matters: OutputCache reads must happen before authn so cached
    // responses can short-circuit (FastEndpoints' .Idempotency() filter
    // sits inside the endpoint pipeline, but AddIdempotencyKeyOutputCache
    // wires the underlying IOutputCacheStore which UseOutputCache attaches).
    app.UseOutputCache();
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseOrderingFastEndpoints();

    app.MapPlatformHealthCheckEndpoints();

    await app.MigrateOnStartupIfDevelopmentAsync<OrderingDbContext>();

    // Skip the Kafka saga-command consumer in the test host. M5's
    // functional-test slice exercises the HTTP surface only; the consumer
    // is integration-tested in M4 / M7 against a real broker. Booting the
    // consumer in tests would require a Kafka + schema-registry container
    // pair that isn't part of M5's scope.
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
