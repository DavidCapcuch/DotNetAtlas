using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Basket.Application.Common;

/// <summary>
/// Composition root for the Basket Application layer.
/// </summary>
/// <remarks>
/// Follows the codebase-wide Application-layer composition convention
/// (assembly-scan + behaviour chain). Concrete
/// persistence (<c>IBasketRepository</c>, <c>IBasketDbContext</c>) and the ACL adapter
/// (<c>IProductCatalogQueryPort</c>) are wired separately by <c>Basket.Infrastructure</c>
/// in the Application and Infrastructure layers.
/// </remarks>
public static class ApplicationDependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers validators, CQRS handlers, domain-event handlers + dispatcher,
        /// and the behavior chain.
        /// </summary>
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
            // Decorator order: last registered = first to execute.
            // Tracing (outer) -> Logging -> Metrics -> Validation -> Handler (inner).
            services.AddCqrsValidationBehavior();
            services.AddCqrsMetricsBehavior();
            services.AddCqrsLoggingBehavior();
            // Always keep before metrics so OTEL exemplars work.
            services.AddCqrsTracingBehavior();

            return services;
        }
    }
}
