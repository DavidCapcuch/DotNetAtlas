using System.Reflection;
using DotNetAtlas.Api.Common;
using DotNetAtlas.Api.Common.Exceptions;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Api.Common.Swagger;
using DotNetAtlas.Contracts;
using DotNetAtlas.Infrastructure.Common;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.HttpLogging;
using Serilog;

namespace DotNetAtlas.Api
{
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
                
                builder.Services.AddFastEndpoints(options =>
                    {
                        options.SourceGeneratorDiscoveredTypes.AddRange(DiscoveredTypes.All);
                    })
                    .AddSwaggerDoc(builder);
                builder.Services.AddOutputCache();
                
                builder.Services.AddHttpContextAccessor();
                builder.Services.AddHttpLogging(httpOptions =>
                {
                    httpOptions.LoggingFields = HttpLoggingFields.RequestPath
                                                | HttpLoggingFields.RequestProperties
                                                | HttpLoggingFields.ResponsePropertiesAndHeaders
                                                | HttpLoggingFields.ResponseStatusCode;
                });
                builder.Services.AddRazorPages();
                builder.Services.AddInfrastructure(builder.Configuration, isClusterEnvironment);

                builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

                if (builder.Environment.IsProduction())
                {
                    builder.Services.AddProblemDetails();
                }
                else
                {
                    builder.Services.AddProblemDetailsWithExceptions();
                }

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
                    app.UseHsts();
                }

                app.UseSerilogRequestLogging();
                app.UseRouting();
                app.UseOutputCache();
                app.UseAuthorization();
                app.UseFastEndpoints(config =>
                    {
                        config.Endpoints.RoutePrefix = "api";
                        config.Errors.UseProblemDetails();
                        config.Binding.ReflectionCache
                            .AddFromDotNetAtlasApi()
                            .AddFromDotNetAtlasContracts();
                    })
                    .UseSwaggerGen(null, uiSettings =>
                    {
                        uiSettings.ConfigureDefaults();
                        uiSettings.DocExpansion = "list";
                    });

                app.MapClientGenerationApis();
                app.MapStaticAssets();
                app.MapRazorPages()
                    .WithStaticAssets();

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
}