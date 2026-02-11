using FastEndpoints.ClientGen.Kiota;
using Kiota.Builder;
using Ordering.API.Common.Config;
using Ordering.API.Common.Exceptions;

namespace Ordering.API.Common;

public static class ApiDependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPresentation(ConfigurationManager configuration)
        {
            services.AddFastEndpointsInternal(configuration);

            services.AddCorsInternal(configuration);

            services.AddRazorPages();

            services
                .AddProblemDetails()
                .AddExceptionHandler<GlobalExceptionHandler>();

            return services;
        }

        private IServiceCollection AddCorsInternal(ConfigurationManager configuration)
        {
            services.AddOptionsWithValidateOnStart<CorsPolicyOptions>()
                .BindConfiguration(CorsPolicyOptions.Section)
                .ValidateDataAnnotations();

            var corsOptions =
                configuration.GetRequiredSection(CorsPolicyOptions.Section).Get<CorsPolicyOptions>()!;

            services.AddCors(options =>
            {
                options.AddPolicy(CorsPolicyOptions.DefaultCorsPolicyName, policy =>
                {
                    if (corsOptions.AllowedOrigins.Contains("*"))
                    {
                        policy.AllowAnyOrigin();
                    }
                    else
                    {
                        policy.WithOrigins(corsOptions.AllowedOrigins);

                        if (corsOptions.AllowWildcardSubdomains)
                        {
                            policy.SetIsOriginAllowedToAllowWildcardSubdomains();
                        }
                    }

                    if (corsOptions.AllowCredentials)
                    {
                        policy.AllowCredentials();
                    }

                    if (corsOptions.AllowedMethods.Contains("*"))
                    {
                        policy.AllowAnyMethod();
                    }
                    else
                    {
                        policy.WithMethods(corsOptions.AllowedMethods);
                    }

                    if (corsOptions.AllowedHeaders.Contains("*"))
                    {
                        policy.AllowAnyHeader();
                    }
                    else
                    {
                        policy.WithHeaders(corsOptions.AllowedHeaders);
                    }

                    if (corsOptions.ExposedHeaders is { Length: > 0 })
                    {
                        policy.WithExposedHeaders(corsOptions.ExposedHeaders);
                    }
                });
            });

            return services;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>
        /// Maps client generation APIs for each supported <see cref="GenerationLanguage"/>.
        /// </summary>
        public WebApplication MapClientGenerationApisInternal()
        {
            var documentName = app.Configuration[
                $"{SwaggerConfigSections.OpenApiInfoSection}:DocumentName"]!;

            foreach (var generationLanguage in Enum.GetValues<GenerationLanguage>())
            {
                var route = $"/{generationLanguage}";

                app.MapApiClientEndpoint(route, genConfig =>
                    {
                        genConfig.SwaggerDocumentName = documentName;
                        genConfig.Language = generationLanguage;
                        genConfig.ClientNamespaceName = "Ordering";
                        genConfig.ClientClassName = $"{generationLanguage}Client";
                    },
                    options =>
                    {
                        options.CacheOutput(p => p.Expire(TimeSpan.FromDays(1)));
                        options.ExcludeFromDescription();
                    });
            }

            return app;
        }
    }
}
