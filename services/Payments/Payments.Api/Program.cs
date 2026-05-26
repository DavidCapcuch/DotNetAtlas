using KafkaFlow;
using Microsoft.Extensions.Hosting;
using Payments.Api.Common;
using Payments.Application.Common;
using Payments.Infrastructure.Common;
using Payments.Infrastructure.Persistence.Database;
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
        .AddPresentation()
        .AddPaymentsAuthentication(builder.Configuration, builder.Environment)
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

    app.UseAuthentication();
    app.UseAuthorization();

    app.UsePaymentsFastEndpoints();

    app.MapPlatformHealthCheckEndpoints();

    await app.MigrateOnStartupIfLocalAsync<PaymentsDbContext>();

    // Skip the Kafka saga-command consumer in the test host. The consumer is
    // integration-tested in M5 against a real broker; functional tests
    // exercise the HTTP surface only and do not stand up a Kafka container.
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
