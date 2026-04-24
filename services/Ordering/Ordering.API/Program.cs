using KafkaFlow;
using Ordering.Application.Common;
using Ordering.Infrastructure.Common;
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
        .AddApplication()
        .AddInfrastructure(builder.Configuration, isDeployedEnvironment);

    var app = builder.Build();

    app.UseCorrelationId();

    app.MapPlatformHealthCheckEndpoints();

    app.MapGet("/", () => "Ordering.Api — M4 Infrastructure wired; FastEndpoints land in M5.");

    var kafkaBus = app.Services.CreateKafkaBus();
    await kafkaBus.StartAsync();

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
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
