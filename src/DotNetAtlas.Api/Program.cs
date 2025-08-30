using System.Reflection;
using DotNetAtlas.Api.Common;
using DotNetAtlas.Api.Common.Authentication;
using DotNetAtlas.Api.Common.Exceptions;
using DotNetAtlas.Api.Common.Extensions;
using DotNetAtlas.Api.Common.Swagger;
using DotNetAtlas.Api.Endpoints;
using DotNetAtlas.Application;
using DotNetAtlas.Application.Common;
using DotNetAtlas.Infrastructure.Common;
using DotNetAtlas.Infrastructure.Persistence.Database.Seed;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
                    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true,
                        reloadOnChange: true)
                    .AddEnvironmentVariables();

                builder.UseSerilogConfiguration(isClusterEnvironment);

                if (builder.Environment.IsLocal())
                {
                    builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), true);
                }

                builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(
                        JwtBearerDefaults.AuthenticationScheme,
                        options => { builder.Configuration.Bind(AuthConfigSections.Full.JWT_BEARER, options); })
                    .AddOpenIdConnect(SecuritySchemes.OIDC, options =>
                    {
                        builder.Configuration.Bind(AuthConfigSections.Full.O_AUTH_CONFIG, options);
                        foreach (var scope in Scopes.List)
                        {
                            options.Scope.Add(scope.Name);
                        }
                    });

                builder.Services.AddAuthorization(options =>
                {
                    options.AddPolicy(AuthPolicies.DEV_ONLY, policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.RequireRole(Roles.DEVELOPER);
                    });
                });

                builder.Services.AddCors(options =>
                {
                    options.AddPolicy(
                        "AllowAnyOrigin",
                        policy =>
                        {
                            policy
                                .AllowAnyOrigin()
                                .AllowAnyMethod()
                                .AllowAnyHeader();
                        });
                });

                builder.Services.AddFastEndpoints(options =>
                    {
                        options.SourceGeneratorDiscoveredTypes.AddRange(DiscoveredTypes.All);
                    })
                    .AddAuthSwaggerDocument(builder);
                builder.Services.AddOutputCache();

                builder.Services.AddHttpContextAccessor();
                builder.Services.AddRazorPages();
                builder.Services.AddApplication();
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

                app.UseRouting();
                app.UseCors("AllowAnyOrigin");
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
                                ep.EndpointTags?.Contains(EndpointGroupConstants.DEV) is true)
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
                            .AddFromDotNetAtlasApi()
                            .AddFromDotNetAtlasApplication();
                    })
                    .UseAuthSwaggerGen(app.Configuration);

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
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }
    }
}