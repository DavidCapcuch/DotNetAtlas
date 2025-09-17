using DotNetAtlas.Application.Common.Behaviors;
using DotNetAtlas.Application.Common.Config;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Feedback.ChangeFeedback;
using DotNetAtlas.Application.Feedback.GetFeedback;
using DotNetAtlas.Application.Feedback.SendFeedback;
using DotNetAtlas.Application.Forecast.GetForecasts;
using DotNetAtlas.Application.Forecast.Services;
using DotNetAtlas.Application.Forecast.Services.Abstractions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.Application.Common;

public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, ConfigurationManager configuration)
    {
        services.AddValidatorsFromAssembly(typeof(ApplicationDependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<IQueryHandler<GetFeedbackByIdQuery, GetFeedbackByIdResponse>, GetFeedbackByIdQueryHandler>();
        services.AddScoped<ICommandHandler<SendFeedbackCommand, Guid>, SendFeedbackCommandHandler>();
        services.AddScoped<ICommandHandler<ChangeFeedbackCommand>, ChangeFeedbackCommandHandler>();

        services.AddOptionsWithValidateOnStart<WeatherHedgingOptions>()
            .Bind(configuration.GetSection(WeatherHedgingOptions.Section));
        services.AddScoped<IWeatherForecastService, HedgingWeatherForecastService>();
        services.AddScoped<IQueryHandler<GetForecastQuery, GetForecastResponse>, GetForecastQueryHandler>();

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
