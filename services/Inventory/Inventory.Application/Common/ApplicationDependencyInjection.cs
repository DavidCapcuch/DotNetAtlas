using FluentValidation;
using Inventory.Application.Common.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Inventory.Application.Common;

/// <summary>
/// Composition root for the Inventory Application layer. The host project
/// (<c>Inventory.API</c>) calls <c>services.AddApplication()</c>; Infrastructure
/// DI wires the concretions (<c>InventoryDbContext</c>, event-store repo,
/// outbox + inbox, and — in M5 — Kafka consumers) on top.
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
        /// registrations so the projection handlers and outbox publishers pick
        /// up the concrete <c>IInventoryDbContext</c> / outbox services.
        /// </summary>
        public IServiceCollection AddApplication()
        {
            var assembly = typeof(ApplicationDependencyInjection).Assembly;

            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

            services.AddCqrsHandlersFromAssembly(assembly);
            services
                .AddDomainEventHandlersFromAssembly(assembly)
                .AddDomainEventDispatcher();

            // CQRS behavior chain. Decorator order: last registered = first to
            // execute. Tracing (outer) > Logging > Metrics > Validation > Handler
            // (inner). Each AddCqrs*Behavior decorates all three handler kinds
            // (ICommandHandler<>, ICommandHandler<,>, IQueryHandler<,>); M7
            // satisfies every kind via the admin Receive/Adjust commands plus
            // the GetStockLevel / GetReservation queries.
            services.AddCqrsValidationBehavior();
            services.AddCqrsMetricsBehavior();
            services.AddCqrsLoggingBehavior();
            services.AddCqrsTracingBehavior();

            services.AddOptionsWithValidateOnStart<TopicsOptions>()
                .BindConfiguration(TopicsOptions.Section)
                .ValidateDataAnnotations();

            return services;
        }
    }
}
