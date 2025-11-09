using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Common.CQS.Behaviors;
using DotNetAtlas.Application.WeatherAlerts.DisconnectCleanup;
using DotNetAtlas.Application.WeatherAlerts.SendWeatherAlert;
using DotNetAtlas.Application.WeatherAlerts.SubscribeForCityAlerts;
using DotNetAtlas.Application.WeatherAlerts.UnsubscribeFromCityAlerts;
using DotNetAtlas.Application.WeatherFeedback.ChangeFeedback;
using DotNetAtlas.Application.WeatherFeedback.GetFeedback;
using DotNetAtlas.Application.WeatherFeedback.SendFeedback;
using DotNetAtlas.Application.WeatherForecast.Common.Config;
using DotNetAtlas.Application.WeatherForecast.GetForecasts;
using DotNetAtlas.Application.WeatherForecast.Services;
using DotNetAtlas.Application.WeatherForecast.Services.Abstractions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.Application.Common;

public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationDependencyInjection).Assembly, includeInternalTypes: true);
        services
            .AddFeedback()
            .AddForecast()
            .AddWeatherAlert()
            .AddHandlerBehaviors();

        return services;
    }

    private static IServiceCollection AddForecast(this IServiceCollection services)
    {
        services.AddOptionsWithValidateOnStart<WeatherHedgingOptions>()
            .BindConfiguration(WeatherHedgingOptions.Section)
            .ValidateDataAnnotations();
        services.AddOptionsWithValidateOnStart<ForecastCacheOptions>()
            .BindConfiguration(ForecastCacheOptions.Section)
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
