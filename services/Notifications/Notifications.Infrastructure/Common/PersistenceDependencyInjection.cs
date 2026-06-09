using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Common.Data;
using Notifications.Infrastructure.Common.Config;
using Notifications.Infrastructure.Persistence.Database;
using Notifications.Infrastructure.Persistence.Database.Seed;

namespace Notifications.Infrastructure.Common;

/// <summary>
/// Dependency injection extensions for persistence infrastructure.
/// </summary>
internal static class PersistenceDependencyInjection
{
    /// <summary>
    /// Configures Entity Framework Core database context with PostgreSQL.
    /// Sets up connection pooling, interceptors, retry policies, seeding, and outbox pattern.
    /// </summary>
    internal static IServiceCollection AddDatabase(
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

        services.AddDbContext<NotificationsDbContext>((
            sp,
            options) => options
            .UseNpgsql(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Notifications)),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                        NotificationsDbContext.DefaultSchemaName);
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
            // very useful for local debugging / investigating failed tests; off in deployed envs.
            .EnableSensitiveDataLogging(!isDeployedEnvironment)
            // CAT-SEC-009: detailed errors leak EF parameter/column info into exception
            // responses. Honour the config flag in non-deployed envs only; force off in
            // deployed environments regardless of config.
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors && !isDeployedEnvironment)
            // required for the Inbox pattern, see Platform.ReliableMessaging.Inbox.EFCore
            .UseExceptionProcessor()
            // Dev/compose template seeding (seed-if-empty); fires only on MigrateAsync/update-database
            // (Development), never in Testing/deployed — see DatabaseSeedExtensions.
            .UseSeeding()
            .UseAsyncSeeding());

        services.AddScoped<INotificationsDbContext>(sp => sp.GetRequiredService<NotificationsDbContext>());

        return services;
    }
}
