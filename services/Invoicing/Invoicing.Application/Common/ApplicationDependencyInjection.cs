using FluentValidation;
using Invoicing.Application.Common.Messaging;
using Invoicing.Application.Common.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Invoicing.Application.Common;

/// <summary>
/// Composition root for the Invoicing Application layer. The host project
/// (<c>Invoicing.Api</c>) calls <c>services.AddApplication()</c>; the Infrastructure
/// layer's <c>AddInfrastructure</c> wires the concretions on top.
/// </summary>
/// <remarks>
/// <para>
/// Registers FluentValidation validators, CQRS command/query handlers, the
/// Tracing → Logging → Metrics → Validation behaviour chain (M8 — all three handler
/// shapes are now present so Scrutor's <c>Decorate</c> is satisfied), domain-event
/// handlers + dispatcher (the <c>DispatchDomainEventsInterceptor</c> in Infrastructure
/// picks up the dispatcher from this composition root), and the
/// <see cref="InvoicingTopicsOptions"/> binding. <c>BlobStorageOptions</c> is registered
/// in Infrastructure (it injects the connection string) and consumed by the M7 command
/// handlers + M8 query handlers via DI.
/// </para>
/// </remarks>
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

            services.AddOptionsWithValidateOnStart<InvoicingTopicsOptions>()
                .BindConfiguration(InvoicingTopicsOptions.Section)
                .ValidateDataAnnotations();

            services.AddOptionsWithValidateOnStart<BuyerPortalOptions>()
                .BindConfiguration(BuyerPortalOptions.Section)
                .ValidateDataAnnotations();

            // BlobStorageOptions registration lives in Infrastructure (it injects the
            // ConnectionStrings:AzureStorage value into the same options object); the
            // M7 command handlers + M8 query handlers consume it via IOptions<BlobStorageOptions>.

            return services;
        }

        private IServiceCollection AddCqrsHandlerBehaviors()
        {
            // Decorator order: last registered = first to execute.
            // Tracing (outer) -> Logging -> Metrics -> Validation -> Handler (inner).
            services.AddCqrsValidationBehavior();
            services.AddCqrsMetricsBehavior();
            services.AddCqrsLoggingBehavior();

            // Always keep before metrics so OTel exemplars work.
            services.AddCqrsTracingBehavior();

            return services;
        }
    }
}
