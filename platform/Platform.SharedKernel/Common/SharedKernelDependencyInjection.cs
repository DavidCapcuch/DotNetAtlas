using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.SharedKernel.Time;

namespace Platform.SharedKernel.Common;

/// <summary>
/// Registers shared-kernel services consumed by every BC (ADR-0015).
/// </summary>
public static class SharedKernelDependencyInjection
{
    /// <summary>
    /// Adds the ambient <see cref="IClock"/> (<see cref="SystemClock"/> singleton) and any
    /// future shared-kernel singletons.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSharedKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        return services;
    }
}
