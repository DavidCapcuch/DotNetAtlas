using System.Reflection;
using DotNetAtlas.Api;
using DotNetAtlas.Api.Common;
using DotNetAtlas.Api.Common.Config;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Api.Common.Swagger;
using DotNetAtlas.Api.Endpoints;
using DotNetAtlas.Application.Common;
using DotNetAtlas.Infrastructure.Common;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using FastEndpoints;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Debug()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var isClusterEnvironment = !(builder.Environment.IsLocal() || builder.Environment.IsTesting());
    builder
        .Host
        .UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = !isClusterEnvironment;
            options.ValidateOnBuild = !isClusterEnvironment;
        });

    builder.Configuration
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    builder.UseSerilogConfiguration(isClusterEnvironment);

    if (builder.Environment.IsLocal())
    {
        builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), true);
    }

    builder.AddPresentation();
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructure(builder.Configuration, isClusterEnvironment);

    var app = builder.Build();

    if (!app.Environment.IsProduction())
    {
        app.UseDeveloperExceptionPage();
    }
    else
    {
        app.UseExceptionHandler();
    }

    app.UseStatusCodePages();

    if (isClusterEnvironment)
    {
        app.UseHttpsRedirection();
        app.UseSecurityHeaders();
    }

    app.UseRouting();
    app.UseCors(CorsPolicyOptions.DefaultCorsPolicyName);
    app.UseOutputCache();
    app.UseAuthentication();
    app.UseRequestContextTelemetry();
    app.UseAuthorization();

    app.UseFastEndpoints(config =>
        {
            config.Errors.UseProblemDetails(detailsConfig =>
            {
                detailsConfig.IndicateErrorCode = true;
                detailsConfig.IndicateErrorSeverity = false;
            });
            config.Endpoints.Filter = ep =>
            {
                if (builder.Environment.IsProduction() &&
                    ep.EndpointTags?.Contains(EndpointGroupConstants.Dev) is true)
                {
                    return false;
                }

                return true;
            };

            config.Versioning.Prefix = "v";
            config.Versioning.PrependToRoute = true;
            config.Versioning.DefaultVersion = 1;
            config.Endpoints.RoutePrefix = "api";
            config.Binding.ReflectionCache
                .AddFromDotNetAtlasApi();
        })
        .UseAuthSwaggerGen(app.Configuration);

    app.MapHealthChecksInternal();
    app.MapClientGenerationApis();
    app.MapStaticAssets();
    app.MapRazorPages()
        .WithStaticAssets();

    // In production, flyway should be used, therefore also during
    // integration tests to ensure the SQL scripts are applied correctly
    if (!builder.Environment.IsProduction() && !builder.Environment.IsTesting())
    {
        await app.InitialiseDatabaseAsync();
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
