using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotNetAtlas.ReliableMessaging.Inbox.EFCore.Common;

/// <summary>
/// Hosted service that validates UseExceptionProcessor() configuration at application startup.
/// </summary>
/// <typeparam name="TContext">The DbContext type to validate.</typeparam>
/// <remarks>
/// <para>
/// This validation ensures that the inbox middleware can properly handle concurrent duplicate
/// message inserts by catching <see cref="UniqueConstraintException"/>. Without <c>UseExceptionProcessor()</c>,
/// database-specific exceptions would not be caught, leading to message processing failures.
/// </para>
/// <para>
/// The validation runs during application startup and throws <see cref="InvalidOperationException"/>
/// if the configuration is missing, failing fast rather than causing runtime errors during message processing.
/// </para>
/// <para>
/// <example>
/// <code>
/// services.AddDbContext&lt;MyDbContext&gt;(options =&gt;
///     options.UseSqlServer(connectionString)
///            .UseExceptionProcessor());
/// </code>
/// </example>
/// </para>
/// </remarks>
/// <seealso cref="ExceptionProcessorInterceptor{TContext}"/>
internal sealed class InboxConfigurationValidatorHostedService<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly string _interceptorTypeFullName = typeof(ExceptionProcessorInterceptor<>).FullName!;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InboxConfigurationValidatorHostedService<TContext>> _logger;

    public InboxConfigurationValidatorHostedService(
        IServiceProvider serviceProvider,
        ILogger<InboxConfigurationValidatorHostedService<TContext>> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        ValidateExceptionProcessorConfiguration(context);

        _logger.LogDebug(
            "Inbox middleware configuration for {DbContextType} validated", typeof(TContext).Name);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Validates that the DbContext is configured with the EntityFramework.Exceptions processor.
    /// </summary>
    /// <param name="context">The DbContext instance to validate.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// <list type="bullet">
    /// <item><description>The DbContext options are missing or not properly configured</description></item>
    /// <item><description>The <c>UseExceptionProcessor()</c> method was not called on the DbContext options</description></item>
    /// </list>
    /// The exception message includes detailed instructions for fixing the configuration.
    /// </exception>
    /// <remarks>
    /// This method inspects the DbContext's interceptor configuration to ensure that the
    /// <see cref="ExceptionProcessorInterceptor{TContext}"/> is registered, which is required
    /// for the inbox middleware to handle database constraint exceptions properly.
    /// </remarks>
    private void ValidateExceptionProcessorConfiguration(DbContext context)
    {
        var coreOptionsExtension = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>() ?? throw new InvalidOperationException(
            $"The DbContext '{context.GetType().Name}' is not properly configured. " +
            "Ensure the DbContext options are configured correctly.");

        var hasExceptionProcessor = coreOptionsExtension.Interceptors?
                                        .Any(i => i.GetType().BaseType?.FullName?
                                            .StartsWith(_interceptorTypeFullName, StringComparison.Ordinal) == true)
                                    ?? false;

        if (!hasExceptionProcessor)
        {
            throw new InvalidOperationException(
                $"The DbContext '{context.GetType().Name}' must be configured with UseExceptionProcessor() " +
                "from the EntityFramework.Exceptions library. This is required for the inbox middleware to " +
                "handle concurrent duplicate message inserts gracefully. " +
                "Install the appropriate package for your database provider (e.g., EntityFrameworkCore.Exceptions.SqlServer " +
                "for SQL Server, EntityFrameworkCore.Exceptions.PostgreSQL for PostgreSQL). " +
                "Then add '.UseExceptionProcessor()' to your DbContext configuration: " +
                "services.AddDbContext<MyDbContext>(options => options.UseSqlServer(connectionString).UseExceptionProcessor());");
        }
    }
}
