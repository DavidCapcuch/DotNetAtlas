using DotNetAtlas.ReliableMessaging.Inbox.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotNetAtlas.ReliableMessaging.Inbox.EFCore.Common;

/// <summary>
/// Extension methods for registering Inbox services.
/// </summary>
public static class InboxDependencyInjection
{
    /// <summary>
    /// Registers inbox services required for the inbox pattern.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements <see cref="IInboxDbContext"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Important:</b> The DbContext must be configured with <c>UseExceptionProcessor()</c>
    /// from EntityFramework.Exceptions to handle concurrent duplicate inserts gracefully.
    /// This converts database-specific constraint violations to a common
    /// <see cref="EntityFramework.Exceptions.Common.UniqueConstraintException"/>.
    /// </para>
    /// <para>
    /// Call this during service registration to enable inbox pattern support.
    /// The DbContext must implement <see cref="IInboxDbContext"/> and include
    /// the <see cref="InboxMessage"/> entity configuration.
    /// </para>
    /// <para>
    /// Registers <see cref="TimeProvider"/> as singleton and <see cref="IInboxDbContext"/> as scoped.
    /// Also adds a hosted service that validates DbContext configuration at startup.
    /// </para>
    /// <example>
    /// <code>
    /// // Configure DbContext with UseExceptionProcessor
    /// services.AddDbContextPool&lt;MyDbContext&gt;(options => options
    ///     .UseSqlServer(connectionString)
    ///     .UseExceptionProcessor());
    ///
    /// // Register inbox services (order doesn't matter)
    /// services.AddInbox&lt;MyDbContext&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown at application startup if the DbContext is not configured with <c>UseExceptionProcessor()</c>.
    /// The validation is performed by <see cref="InboxConfigurationValidatorHostedService{TContext}"/>.
    /// </exception>
    public static IServiceCollection AddInbox<TContext>(this IServiceCollection services)
        where TContext : DbContext, IInboxDbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IInboxDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddHostedService<InboxConfigurationValidatorHostedService<TContext>>();

        return services;
    }
}
