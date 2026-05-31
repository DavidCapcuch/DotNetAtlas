using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Payments.Application.Common;

/// <summary>
/// Composition root for the Payments Application layer. Scans this assembly for
/// FluentValidation validators, CQRS handlers, and domain-event handlers, and installs the
/// behaviour-decorator chain. Concrete persistence (<c>PaymentsDbContext</c>), the
/// strongly-typed <c>TopicsOptions</c> + Kafka sub-options, and the Kafka command consumers
/// are wired separately by <c>Payments.Infrastructure</c>.
/// </summary>
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

            services.AddCqrsHandlerBehaviors();

            return services;
        }

        private IServiceCollection AddCqrsHandlerBehaviors()
        {
            // Decorator order: last registered = first to execute
            // Tracing (outer) -> Logging -> Metrics -> Validation -> Handler (inner)
            services.AddCqrsValidationBehavior();
            services.AddCqrsMetricsBehavior();
            services.AddCqrsLoggingBehavior();
            // Always keep before metrics so OTEL exemplars work
            services.AddCqrsTracingBehavior();

            return services;
        }
    }
}
