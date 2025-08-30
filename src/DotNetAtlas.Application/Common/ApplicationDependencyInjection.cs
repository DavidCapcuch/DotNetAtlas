using DotNetAtlas.Application.Common.Behaviors;
using DotNetAtlas.Application.Common.CQS;
using DotNetAtlas.Application.Feedback.ChangeFeedback;
using DotNetAtlas.Application.Feedback.GetFeedback;
using DotNetAtlas.Application.Feedback.SendFeedback;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetAtlas.Application.Common
{
    public static class ApplicationDependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddValidatorsFromAssembly(typeof(ApplicationDependencyInjection).Assembly, includeInternalTypes: true);

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
}