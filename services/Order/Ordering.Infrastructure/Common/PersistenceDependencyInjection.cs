using EntityFramework.Exceptions.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Common.Data;
using Ordering.Infrastructure.Common.Config;
using Ordering.Infrastructure.Common.Persistence.Database;
using Ordering.Infrastructure.Common.Persistence.Database.Interceptors;
using Ordering.Infrastructure.Common.Persistence.Database.Seed;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for persistence infrastructure.
/// </summary>
public static class PersistenceDependencyInjection
{
    /// <summary>
    /// Configures Entity Framework Core database context with SQL Server.
    /// </summary>
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services.AddOptionsWithValidateOnStart<EfCoreOptions>()
            .BindConfiguration(EfCoreOptions.Section)
            .ValidateDataAnnotations();

        var efCoreOptions = configuration
            .GetRequiredSection(EfCoreOptions.Section)
            .Get<EfCoreOptions>()!;

        services
            .AddOptionsWithValidateOnStart<ConnectionStringsOptions>()
            .BindConfiguration(ConnectionStringsOptions.Section)
            .ValidateDataAnnotations();

        // See: https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors#registering-interceptors
        // DispatchDomainEventsInterceptor is registered as Scoped because it depends on scoped IDomainEventDispatcher,
        // which resolves Domain Event handlers from the same scope as the DbContext.
        // This ensures that the same DbContext is used for both SaveChanges and Domain Event Dispatching
        // and the same DbContext is used in the Domain Event Handlers (e.g., for Outbox dispatching within the same transaction)
        services.AddScoped<DispatchDomainEventsInterceptor>();

        // UpdateAuditableEntitiesInterceptor is registered as Singleton for performance optimization.
        // This is safe because the interceptor is stateless - it doesn't capture or store any per-request data
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();
        services.AddDbContext<OrderingDbContext>((
            sp,
            options) => options
            .UseSqlServer(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Ordering)),
                sqlServerOptions =>
                {
                    sqlServerOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                        OrderingDbContext.DefaultSchemaName);
                    sqlServerOptions.UseQuerySplittingBehavior(
                        efCoreOptions.UseQuerySplitting
                            ? QuerySplittingBehavior.SplitQuery
                            : QuerySplittingBehavior.SingleQuery);
                    sqlServerOptions.EnableRetryOnFailure(
                        maxRetryCount: efCoreOptions.RetryMaxCount,
                        maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                        errorNumbersToAdd: null);
                })
            .EnableSensitiveDataLogging(
                !isDeployedEnvironment) // this is very useful for local debugging/investigating failed tests
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors)
            .UseExceptionProcessor() // required for the Inbox pattern, see Platform.ReliableMessaging.Inbox.EFCore
            .UseSeeding() // see https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding
            .UseAsyncSeeding()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IOrderingDbContext>(sp => sp.GetRequiredService<OrderingDbContext>());

        return services;
    }
}
