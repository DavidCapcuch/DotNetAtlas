using Catalog.Application.Categories.Common.Services;
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
/// <see cref="Messaging.CatalogTopicsOptions"/> is registered via
/// <see cref="OptionsServiceCollectionExtensions.AddOptions{TOptions}(IServiceCollection)"/> only;
/// the API host (M6) is responsible for binding it to configuration (e.g.
/// <c>services.Configure&lt;CatalogTopicsOptions&gt;(config.GetSection(CatalogTopicsOptions.Section))</c>)
/// and for calling <c>ValidateOnStart()</c>. Keeping <c>IConfiguration</c> out of the Application
/// layer avoids a transitive dependency on <c>Microsoft.Extensions.Configuration</c>.
/// </para>
/// </remarks>
public static class ApplicationDependencyInjection
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCatalogApplication()
        {
            var assembly = typeof(ApplicationDependencyInjection).Assembly;

            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

            services.AddCqrsHandlersFromAssembly(assembly);
            services
                .AddDomainEventHandlersFromAssembly(assembly)
                .AddDomainEventDispatcher();

            services.AddOptions<Messaging.CatalogTopicsOptions>();

            services.AddScoped<ICategoryAncestryService, CategoryAncestryService>();
            services.AddScoped<ICategoryPathService, CategoryPathService>();

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
