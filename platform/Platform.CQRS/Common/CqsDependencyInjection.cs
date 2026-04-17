using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Platform.CQRS.Behaviors;

namespace Platform.CQRS.Common;

public static class CqrsDependencyInjection
{
    /// <param name="services">The service collection.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers all <see cref="ICommandHandler{TCommand}"/>, <see cref="ICommandHandler{TCommand, TResponse}"/>,
        /// and <see cref="IQueryHandler{TQuery, TResponse}"/> implementations from the specified assembly.
        /// </summary>
        /// <param name="assembly">The assembly to scan for handler implementations.</param>
        /// <returns>The service collection for chaining.</returns>
        /// <remarks>
        /// <para>
        /// This method uses convention-based scanning to register handlers with scoped lifetime.
        /// Internal handlers are included in the scan.
        /// </para>
        /// <para>
        /// <b>Note:</b> Call this method BEFORE registering behaviors (decorators), as behaviors
        /// wrap existing handler registrations.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// // Register handlers first
        /// services.AddCqrsHandlers(typeof(ApplicationDependencyInjection).Assembly);
        ///
        /// // Then add behaviors (decorators)
        /// services.AddCqrsValidationBehavior();
        /// services.AddCqrsLoggingBehavior();
        /// </code>
        /// </example>
        public IServiceCollection AddCqrsHandlersFromAssembly(Assembly assembly)
        {
            services.Scan(scan => scan.FromAssemblies(assembly)
                .AddClasses(classes => classes.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
                .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime()
                .AddClasses(classes => classes.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces()
                .WithScopedLifetime());

            return services;
        }

        /// <summary>
        /// Adds logging behavior that logs command/query processing, completion, and errors.
        /// </summary>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddCqrsLoggingBehavior()
        {
            services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingBehavior.QueryHandler<,>));
            services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingBehavior.CommandHandler<,>));
            services.Decorate(typeof(ICommandHandler<>), typeof(LoggingBehavior.CommandBaseHandler<>));

            return services;
        }

        /// <summary>
        /// Adds metrics behavior that tracks command/query success, failures, exceptions, and duration.
        /// </summary>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddCqrsMetricsBehavior()
        {
            services.Decorate(typeof(IQueryHandler<,>), typeof(MetricsBehavior.QueryHandler<,>));
            services.Decorate(typeof(ICommandHandler<,>), typeof(MetricsBehavior.CommandHandler<,>));
            services.Decorate(typeof(ICommandHandler<>), typeof(MetricsBehavior.CommandBaseHandler<>));

            return services;
        }

        /// <summary>
        /// Adds tracing behavior that creates activity spans for command/query processing.
        /// </summary>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddCqrsTracingBehavior()
        {
            services.Decorate(typeof(IQueryHandler<,>), typeof(TracingBehavior.QueryHandler<,>));
            services.Decorate(typeof(ICommandHandler<,>), typeof(TracingBehavior.CommandHandler<,>));
            services.Decorate(typeof(ICommandHandler<>), typeof(TracingBehavior.CommandBaseHandler<>));

            return services;
        }

        /// <summary>
        /// Adds validation behavior that validates commands and queries using FluentValidation before processing.
        /// </summary>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddCqrsValidationBehavior()
        {
            services.Decorate(typeof(ICommandHandler<,>), typeof(ValidationBehavior.CommandHandler<,>));
            services.Decorate(typeof(ICommandHandler<>), typeof(ValidationBehavior.CommandBaseHandler<>));
            services.Decorate(typeof(IQueryHandler<,>), typeof(ValidationBehavior.QueryHandler<,>));

            return services;
        }
    }
}
