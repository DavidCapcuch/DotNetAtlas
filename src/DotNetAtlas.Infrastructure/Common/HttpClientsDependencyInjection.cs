using DotNetAtlas.Application.WeatherForecast.Common.Config;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using DotNetAtlas.Infrastructure.Common.Config;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.OpenMeteo;
using DotNetAtlas.Infrastructure.HttpClients.WeatherProviders.WeatherApiCom;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace DotNetAtlas.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for http clients.
/// </summary>
internal static class HttpClientsDependencyInjection
{
    internal static IServiceCollection AddWeatherHttpClients(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<OpenMeteoOptions>()
            .BindConfiguration(OpenMeteoOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<WeatherApiComOptions>()
            .BindConfiguration(WeatherApiComOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<HttpResilienceOptions>()
            .BindConfiguration(HttpResilienceOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<WeatherHedgingOptions>()
            .BindConfiguration(WeatherHedgingOptions.Section)
            .ValidateDataAnnotations();

        var httpResilienceOptions = configuration
            .GetRequiredSection(HttpResilienceOptions.Section)
            .Get<HttpResilienceOptions>()!;

        services.AddHttpClient(OpenMeteoWeatherProvider.HttpClientName, (sp, config) =>
            {
                var openMeteoOptions =
                    sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
                config.BaseAddress = new Uri(openMeteoOptions.BaseUrl);
            })
            .AddAsKeyed()
            .AddDefaultResilienceHandler(httpResilienceOptions);

        services.AddHttpClient(OpenMeteoGeocodingService.GeoHttpClientName, (sp, config) =>
            {
                var openMeteoOptions =
                    sp.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;
                config.BaseAddress = new Uri(openMeteoOptions.GeoBaseUrl);
            })
            .AddAsKeyed()
            .AddDefaultResilienceHandler(httpResilienceOptions);

        services.AddHttpClient(WeatherApiComProvider.HttpClientName, (sp, config) =>
            {
                var weatherApiComOptions =
                    sp.GetRequiredService<IOptions<WeatherApiComOptions>>().Value;
                config.BaseAddress = new Uri(weatherApiComOptions.BaseUrl);
            })
            .AddAsKeyed()
            .AddDefaultResilienceHandler(httpResilienceOptions);

        services.AddKeyedScoped<IGeocodingService, OpenMeteoGeocodingService>(OpenMeteoGeocodingService.ServiceKey);
        services
            .AddKeyedScoped<IGeocodingService, WeatherApiComGeocodingService>(WeatherApiComGeocodingService.ServiceKey);
        services.AddScoped<IGeocodingService, OpenMeteoGeocodingService>();
        services.AddScoped<IMainWeatherForecastProvider, OpenMeteoWeatherProvider>();
        services.AddScoped<IWeatherForecastProvider, OpenMeteoWeatherProvider>();
        services.AddScoped<IWeatherForecastProvider, WeatherApiComProvider>();

        return services;
    }

    /// <summary>
    /// Cannot use ConfigureHttpClientDefaults because it is applied to health check clients too
    /// which then fail if a degraded service is encountered.
    /// </summary>
    private static IHttpResiliencePipelineBuilder AddDefaultResilienceHandler(
        this IHttpClientBuilder builder,
        HttpResilienceOptions httpResilienceOptions)
    {
        return builder.AddResilienceHandler(
            "DefaultResiliencePipeline",
            resilienceBuilder =>
            {
                resilienceBuilder
                    .AddTimeout(TimeSpan.FromSeconds(httpResilienceOptions.TotalRequestTimeoutSeconds))
                    .AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = httpResilienceOptions.RetryMaxAttempts,
                        UseJitter = true,
                        BackoffType = DelayBackoffType.Exponential,
                        Name = "DefaultRetryPolicy",
                        ShouldHandle = args =>
                            new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                    })
                    .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                    {
                        SamplingDuration = TimeSpan.FromSeconds(httpResilienceOptions.CircuitBreakerSamplingSeconds),
                        FailureRatio = httpResilienceOptions.CircuitBreakerFailureRatio,
                        MinimumThroughput = httpResilienceOptions.CircuitBreakerMinimumThroughput,
                        BreakDuration = TimeSpan.FromSeconds(httpResilienceOptions.CircuitBreakerBreakSeconds),
                        Name = "DefaultCircuitBreakerPolicy",
                        ShouldHandle = args =>
                            new ValueTask<bool>(HttpClientResiliencePredicates.IsTransient(args.Outcome))
                    })
                    .AddTimeout(TimeSpan.FromSeconds(httpResilienceOptions.AttemptTimeoutSeconds));
            });
    }
}
