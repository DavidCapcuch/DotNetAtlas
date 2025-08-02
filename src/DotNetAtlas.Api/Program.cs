using System.Reflection;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Infrastructure.Common;
using Microsoft.AspNetCore.HttpLogging;
using Serilog;

namespace DotNetAtlas.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            var isClusterEnvironment = !(builder.Environment.IsLocal() || builder.Environment.IsTest());
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
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true,
                    reloadOnChange: true)
                .AddEnvironmentVariables();

            builder.UsePlatformSerilog(isClusterEnvironment);

            if (builder.Environment.IsLocal())
            {
                builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), true);
            }

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddHttpLogging(httpOptions =>
            {
                httpOptions.LoggingFields = HttpLoggingFields.RequestPath
                                            | HttpLoggingFields.RequestProperties
                                            | HttpLoggingFields.ResponsePropertiesAndHeaders
                                            | HttpLoggingFields.ResponseStatusCode;
            });
            builder.Services.AddRazorPages();
            builder.Services.AddOpenApi();
            builder.Services.AddInfrastructure(builder.Configuration, isClusterEnvironment);

            var app = builder.Build();

            if (app.Environment.IsLocal())
            {
                app.MapOpenApi();
            }

            if (isClusterEnvironment)
            {
                app.UseHttpsRedirection();
                app.UseHsts();
            }

            app.UseSerilogRequestLogging();
            app.UseRouting();
            app.UseAuthorization();
            app.MapStaticAssets();
            app.MapRazorPages()
                .WithStaticAssets();

            var summaries = new[]
            {
                "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
            };

            app.MapGet("/weatherforecast", (HttpContext httpContext) =>
                {
                    var forecast = Enumerable.Range(1, 5).Select(index =>
                            new WeatherForecast
                            {
                                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                                TemperatureC = Random.Shared.Next(-20, 55),
                                Summary = summaries[Random.Shared.Next(summaries.Length)]
                            })
                        .ToArray();
                    return forecast;
                })
                .WithName("GetWeatherForecast");

            await app.RunAsync();
        }
        catch (HostAbortedException)
        {
            Log.Information("Host aborted, shutting down gracefully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}