using Catalog.Application.Common.Data;
using Catalog.Infrastructure.Common.Config;
using Catalog.Infrastructure.Persistence.Database;
using Catalog.Infrastructure.Persistence.Database.Interceptors;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Infrastructure.Common;

/// <summary>
/// DI wiring for EF Core + Postgres. Catalog has no Redis primary store in v1
/// (per ADR-0016 — the projection IS the cache for product reads); a Redis cache
/// for <c>GetCategoryTreeQuery</c> can be added later if read amplification warrants it.
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

        // Scoped: depends on the scoped IDomainEventDispatcher and runs domain-event handlers
        // (projection + outbox publishers) in the same scope as the DbContext write — atomic UoW.
        services.AddScoped<DispatchDomainEventsInterceptor>();

        // Singleton: stateless apart from TimeProvider.
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

        services.AddDbContext<CatalogDbContext>((sp, options) => options
            .UseNpgsql(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Catalog)),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        CatalogDbContext.DefaultSchemaName);
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
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors)
            .UseExceptionProcessor()
            .AddInterceptors(
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<ICatalogDbContext>(sp => sp.GetRequiredService<CatalogDbContext>());

        return services;
    }
}
