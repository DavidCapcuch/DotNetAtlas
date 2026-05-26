using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Common.Messaging;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Ordering.Application.Common;

/// <summary>
/// Composition root for the Ordering Application layer. The host project
/// (Ordering.Api / M5) calls <c>services.AddApplication()</c>; M4 wires the
/// Infrastructure concretions (<c>OrderingDbContext</c>, Kafka consumers) on
/// top.
/// </summary>
public static class ApplicationDependencyInjection
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers validators, CQRS handlers + behaviour chain, domain-event
        /// handlers + dispatcher, and the <see cref="TopicsOptions"/> binding.
        /// Call AFTER <c>AddServiceDefaults</c> and BEFORE the Infrastructure
        /// registrations.
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

            services.AddOptionsWithValidateOnStart<TopicsOptions>()
                .BindConfiguration(TopicsOptions.Section)
                .ValidateDataAnnotations();

            return services;
        }

        private IServiceCollection AddCqrsHandlerBehaviors()
        {
            // Decorator order: last registered = first to execute.
            // Tracing (outer) -> Logging -> Metrics -> Validation -> Handler (inner).
            services.AddCqrsValidationBehavior();
            services.AddCqrsMetricsBehavior();
            services.AddCqrsLoggingBehavior();

            // Always keep before metrics so that OTEL exemplars work.
            services.AddCqrsTracingBehavior();

            return services;
        }
    }
}
