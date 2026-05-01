using KafkaFlow;
using Microsoft.Extensions.Hosting;
using Payments.Application.Common;
using Payments.Infrastructure.Common;
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
        options.ServiceName = "Payments.Api";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services
        .AddPaymentsApplication()
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

    app.MapPlatformHealthCheckEndpoints();

    app.MapGet("/", () => "Payments.Api - M5 infrastructure online; admin HTTP endpoints land in M6.");

    // Skip the Kafka saga-command consumer in the test host. Integration tests stand up their
    // own Kafka container and start the bus explicitly via DI; booting it twice would produce
    // duplicate consumer registrations.
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
