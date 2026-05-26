using Catalog.Application.Categories.Common.Services;
using Catalog.Application.Products.UpdateProductSellability;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS.Common;
using Platform.SharedKernel.Common;

namespace Catalog.Application.Common;

/// <summary>
/// Root DI entry-point for the Catalog Application layer. Scans this assembly for FluentValidation
/// validators, CQRS handlers, and domain-event handlers, and installs the behaviour decorator
/// chain in the same order Weather uses.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Messaging.TopicsOptions"/> is bound to configuration by the API host
/// (Catalog.Infrastructure's <c>MessagingDependencyInjection</c> in production; the
/// <c>IntegrationTestFixture</c> in tests). Keeping <c>IConfiguration</c> out of the Application
/// layer avoids a transitive dependency on <c>Microsoft.Extensions.Configuration</c>; the
/// stand-alone <c>AddOptions&lt;TopicsOptions&gt;()</c> previously here was redundant
/// because <c>Configure&lt;T&gt;</c> internally calls <c>AddOptions&lt;T&gt;</c> as well
/// (CAT-ARCH-C06, Wave-1 closeout).
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

            services.AddScoped<ICategoryAncestryService, CategoryAncestryService>();
            services.AddScoped<ICategoryPathService, CategoryPathService>();
            services.AddScoped<IStockLevelChangedProjector, StockLevelChangedProjectionHandler>();

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
            // Always keep before metrics so that OTEL exemplars work
            services.AddCqrsTracingBehavior();

            return services;
        }
    }
}
