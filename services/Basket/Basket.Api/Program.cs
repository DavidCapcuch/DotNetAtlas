using Basket.Api.Common;
using Basket.Application.Common;
using Basket.Infrastructure.Common;
using Basket.Infrastructure.Persistence.Database;
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

    builder.AddPlatformHostConfiguration();
    builder.UsePlatformSerilog(options =>
    {
        options.ServiceName = "Basket";
    });

    var isDeployedEnvironment = builder.Environment.IsDeployedEnvironment();

    builder.Services.AddCorrelationId();

    builder.Services
        .AddPresentation(builder.Configuration, builder.Environment)
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

    app.UseRouting()
        .UseCors(Basket.Api.Common.Config.BasketCorsOptions.DefaultCorsPolicyName)
        .UseOutputCache()
        .UseCorrelationId()
        .UseAuthentication()
        .UseAuthorization();

    app.UseBasketFastEndpoints();

    app.MapPlatformHealthCheckEndpoints();
    app.UsePlatformHealthChecksPrometheusExporter();

    await app.ApplySqlScriptsOnStartupIfLocalAsync<BasketDbContext>("services/Basket/Basket.Infrastructure");

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
/// Public Program class so test hosts (FastEndpoints' <c>AppFixture&lt;Program&gt;</c>) can
/// reference the entry point.
/// </summary>
public partial class Program;
