using DotNetAtlas.Application.Common.Behaviors;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Feedback.ChangeFeedback;
using DotNetAtlas.Application.Feedback.GetFeedback;
using DotNetAtlas.Application.Feedback.SendFeedback;
using DotNetAtlas.Application.Forecast.Common.Config;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using DotNetAtlas.Application.WeatherAlerts.DisconnectCleanup;
using DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;
using DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;
using DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromCityAlerts;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.Application.Common;

public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services,
        ConfigurationManager configuration)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationDependencyInjection).Assembly, includeInternalTypes: true);
        services
            .AddFeedback()
            .AddForecast(configuration)
            .AddWeatherAlert()
            .AddHandlerBehaviors();

        return services;
    }

    private static IServiceCollection AddForecast(this IServiceCollection services, ConfigurationManager configuration)
    {
        services.AddOptionsWithValidateOnStart<WeatherHedgingOptions>()
            .Bind(configuration.GetSection(WeatherHedgingOptions.Section))
            .ValidateDataAnnotations();
        services.AddOptionsWithValidateOnStart<ForecastCacheOptions>()
            .Bind(configuration.GetSection(ForecastCacheOptions.Section))
            .ValidateDataAnnotations();

        services.AddScoped<IWeatherForecastService, HedgingWeatherForecastService>();
        services.Decorate<IWeatherForecastService, CachedWeatherForecastService>();
        services.AddScoped<IQueryHandler<GetForecastQuery, GetForecastResponse>, GetForecastQueryHandler>();

        return services;
    }

    private static IServiceCollection AddWeatherAlert(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<SendWeatherAlertCommand>, SendWeatherAlertCommandHandler>();
        services.AddScoped<ICommandHandler<SubscribeForCityAlertsCommand>, SubscribeForCityAlertsCommandHandler>();
        services
            .AddScoped<ICommandHandler<UnsubscribeFromCityAlertsCommand>, UnsubscribeFromCityAlertsCommandHandler>();
        services.AddScoped<ICommandHandler<ConnectionDisconnectCleanupCommand>, ConnectionDisconnectCleanupHandler>();

        return services;
    }

    private static IServiceCollection AddFeedback(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse>, GetFeedbackByIdQueryHandler>();
        services.AddScoped<ICommandHandler<SendFeedbackCommand, Guid>, SendFeedbackCommandHandler>();
        services.AddScoped<ICommandHandler<ChangeFeedbackCommand>, ChangeFeedbackCommandHandler>();

        return services;
    }

    private static IServiceCollection AddHandlerBehaviors(this IServiceCollection services)
    {
        services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationHandlerBehavior.CommandHandler<,>));
        services.Decorate(typeof(ICommandHandler<>), typeof(ValidationHandlerBehavior.CommandBaseHandler<>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(ValidationHandlerBehavior.QueryHandler<,>));

        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingHandlerBehavior.QueryHandler<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingHandlerBehavior.CommandHandler<,>));
        services.Decorate(typeof(ICommandHandler<>), typeof(LoggingHandlerBehavior.CommandBaseHandler<>));

        services.Decorate(typeof(IQueryHandler<,>), typeof(TracingHandlerBehavior.QueryHandler<,>));
        services.Decorate(typeof(ICommandHandler<,>), typeof(TracingHandlerBehavior.CommandHandler<,>));
        services.Decorate(typeof(ICommandHandler<>), typeof(TracingHandlerBehavior.CommandBaseHandler<>));

        return services;
    }
}
