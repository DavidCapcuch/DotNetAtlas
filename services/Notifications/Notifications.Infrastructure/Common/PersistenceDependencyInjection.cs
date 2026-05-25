using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Common.Data;
using Notifications.Infrastructure.Common.Config;
using Notifications.Infrastructure.Persistence.Database;

namespace Notifications.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for persistence infrastructure.
/// </summary>
public static class PersistenceDependencyInjection
{
    /// <summary>
    /// Configures Entity Framework Core database context with PostgreSQL.
    /// Sets up connection pooling, interceptors, retry policies, seeding, and outbox pattern.
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

        services.AddDbContext<NotificationDbContext>((
            sp,
            options) => options
            .UseNpgsql(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Notifications)),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                        NotificationDbContext.DefaultSchemaName);
                    npgsqlOptions.UseQuerySplittingBehavior(
                        efCoreOptions.UseQuerySplitting
                            ? QuerySplittingBehavior.SplitQuery
                            : QuerySplittingBehavior.SingleQuery);
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: efCoreOptions.RetryMaxCount,
                        maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                        errorCodesToAdd: null);
                })
            .UseSnakeCaseNamingConvention()
            .EnableSensitiveDataLogging(
                !isDeployedEnvironment) // this is very useful for local debugging/investigating failed tests
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors)
            .UseExceptionProcessor()); // required for the Inbox pattern, see Platform.ReliableMessaging.Inbox.EFCore

        services.AddScoped<INotificationDbContext>(sp => sp.GetRequiredService<NotificationDbContext>());

        return services;
    }
}
