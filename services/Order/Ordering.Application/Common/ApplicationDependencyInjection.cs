using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQS.Common;
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

            services.AddCqsHandlersFromAssembly(assembly);
            services
                .AddDomainEventHandlersFromAssembly(assembly)
                .AddDomainEventDispatcher();

            services
                .AddCqsHandlerBehaviors();

            return services;
        }

        private IServiceCollection AddCqsHandlerBehaviors()
        {
            // Decorator order: last registered = first to execute
            // Tracing (outer) -> Logging -> Metrics -> Validation -> Handler (inner)
            services.AddCqsValidationBehavior();
            services.AddCqsMetricsBehavior();
            services.AddCqsLoggingBehavior();
            // Always keep before metrics so that OTEL exemplars work
            services.AddCqsTracingBehavior();

            return services;
        }
    }
}
