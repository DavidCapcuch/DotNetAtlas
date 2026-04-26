using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Common.Messaging;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Payments.Application.Common;

/// <summary>
/// Composition root for the Payments Application layer. Mirrors the Catalog/Basket convention —
/// scans this assembly for FluentValidation validators, CQRS handlers, and domain-event handlers,
/// installs the behaviour-decorator chain, and registers the strongly-typed
/// <see cref="PaymentsTopicsOptions"/>. Concrete persistence (<c>PaymentsDbContext</c>) and the
/// Kafka command consumers are wired separately by <c>Payments.Infrastructure</c> in M5/M6.
/// </summary>
/// <remarks>
/// <see cref="PaymentsTopicsOptions"/> is registered with <c>AddOptions&lt;T&gt;()</c> only; the API
/// host (M6) is responsible for binding the section and calling <c>ValidateOnStart()</c>.
/// Keeping <c>IConfiguration</c> out of the Application layer avoids a transitive dependency on
/// <c>Microsoft.Extensions.Configuration</c>.
/// </remarks>
public static class ApplicationDependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPaymentsApplication()
        {
            var assembly = typeof(ApplicationDependencyInjection).Assembly;

            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

            services.AddCqrsHandlersFromAssembly(assembly);
            services
                .AddDomainEventHandlersFromAssembly(assembly)
                .AddDomainEventDispatcher();

            services.AddOptions<PaymentsTopicsOptions>();

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
