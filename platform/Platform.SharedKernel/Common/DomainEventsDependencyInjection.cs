using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Platform.SharedKernel.Base.DomainEvents;

namespace Platform.SharedKernel.Common;

public static class DomainEventsDependencyInjection
{
    /// <summary>
    /// Registers all <see cref="IDomainEventHandler{T}"/> implementations from the specified assembly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The assembly to scan for domain event handler implementations.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Internal handlers are included in the scan.
    /// </remarks>
    public static IServiceCollection AddDomainEventHandlersFromAssembly(this IServiceCollection services,
        Assembly assembly)
    {
        services.Scan(scan => scan.FromAssemblies(assembly)
            .AddClasses(classes => classes.AssignableTo(typeof(IDomainEventHandler<>)), publicOnly: false)
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        return services;
    }

    /// <summary>
    /// Registers the <see cref="DomainEventDispatcher"/> as the default implementation of <see cref="IDomainEventDispatcher"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDomainEventDispatcher(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }
}
