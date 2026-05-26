using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering.Application.Common.Data;
using Ordering.Infrastructure.Common.Config;
using Ordering.Infrastructure.Persistence.Database;
using Ordering.Infrastructure.Persistence.Database.Interceptors;

namespace Ordering.Infrastructure.Common;

/// <summary>
/// DI wiring for EF Core + Postgres. Ordering has no cache in v1 (per
/// <c>ordering.md § 4.8</c>) — a dedicated cache tier can be added later
/// if read-amplification warrants it.
/// </summary>
internal static class PersistenceDependencyInjection
{
    internal static IServiceCollection AddDatabase(
        this IServiceCollection services,
        ConfigurationManager configuration,
        bool isDeployedEnvironment)
    {
        services.AddOptionsWithValidateOnStart<EfCoreOptions>()
            .BindConfiguration(EfCoreOptions.Section)
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<ConnectionStringsOptions>()
            .BindConfiguration(ConnectionStringsOptions.Section)
            .ValidateDataAnnotations();

        var efCoreOptions = configuration
            .GetRequiredSection(EfCoreOptions.Section)
            .Get<EfCoreOptions>()!;

        // Scoped: handlers resolved in the same scope as the DbContext write
        // domain-event outbox messages to the same scoped UoW — transactional
        // atomicity with the aggregate save.
        services.AddScoped<DispatchDomainEventsInterceptor>();

        // Singleton: pure function of TimeProvider + the entry's entity state.
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

        services.AddDbContext<OrderingDbContext>((sp, options) => options
            .UseNpgsql(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Ordering)),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        OrderingDbContext.DefaultSchemaName);
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
            .EnableSensitiveDataLogging(!isDeployedEnvironment)
            // CAT-SEC-009: detailed errors leak EF parameter/column info into exception
            // responses. Honour the config flag in non-deployed envs only; force off in
            // deployed environments regardless of config.
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors && !isDeployedEnvironment)
            .UseExceptionProcessor()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IOrderingDbContext>(sp => sp.GetRequiredService<OrderingDbContext>());

        return services;
    }
}
