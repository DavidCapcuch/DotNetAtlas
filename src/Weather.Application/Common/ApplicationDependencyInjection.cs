using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;
using Weather.Application.WeatherForecast.Services;
using Weather.Application.WeatherForecast.Services.Abstractions;
using Weather.Application.WeatherForecast.Services.Config;

namespace Weather.Application.Common;

public static class ApplicationDependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApplication()
        {
            var assembly = typeof(ApplicationDependencyInjection).Assembly;

            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

            services.AddCqrsHandlersFromAssembly(assembly);
            services
                .AddDomainEventHandlersFromAssembly(assembly)
                .AddDomainEventDispatcher();

            services
                .AddWeatherForecast()
                .AddCqrsHandlerBehaviors();

            return services;
        }

        private IServiceCollection AddWeatherForecast()
        {
            services.AddOptionsWithValidateOnStart<WeatherHedgingOptions>()
                .BindConfiguration(WeatherHedgingOptions.Section)
                .ValidateDataAnnotations();
            services.AddOptionsWithValidateOnStart<ForecastCacheOptions>()
                .BindConfiguration(ForecastCacheOptions.Section)
                .ValidateDataAnnotations();

            services.AddScoped<IWeatherForecastService, HedgingWeatherForecastService>();
            services.Decorate<IWeatherForecastService, CachedWeatherForecastService>();

            return services;
        }

        private IServiceCollection AddCqrsHandlerBehaviors()
        {
            // Decorator order: last registered = first to execute
            // Tracing (outer) -> Logging -> Metrics -> Validation -> Handler (inner)
            services.AddCqrsValidationBehavior();
            services.AddCqrsMetricsBehavior();
            services.AddCqrsLoggingBehavior();
            // Always keep before metrics so that OTEL exemplars work
            services.AddCqrsTracingBehavior();

            return services;
        }
    }
}
