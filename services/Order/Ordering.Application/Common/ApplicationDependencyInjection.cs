using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Ordering.Application.Common;

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
                .AddCqrsHandlerBehaviors();

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
