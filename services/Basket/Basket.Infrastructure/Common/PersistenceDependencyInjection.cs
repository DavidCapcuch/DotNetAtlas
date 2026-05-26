using Basket.Application.Common.Data;
using Basket.Infrastructure.Common.Config;
using Basket.Infrastructure.Persistence.Database;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Basket.Infrastructure.Common;

/// <summary>
/// DI wiring for the Basket SQL side-car: binds <see cref="EfCoreOptions"/>
/// and <see cref="ConnectionStringsOptions"/>, registers
/// <see cref="BasketDbContext"/> with snake_case + exception-processor
/// conventions, and exposes the <see cref="IBasketDbContext"/> application
/// port. Redis wiring lives separately in
/// <see cref="Basket.Infrastructure.Persistence.PersistenceDependencyInjection"/>
/// — the two are composed by
/// <see cref="InfrastructureDependencyInjection"/>.
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

        services.AddDbContext<BasketDbContext>((_, options) => options
            .UseNpgsql(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Basket)),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        BasketDbContext.DefaultSchemaName);
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
            .UseExceptionProcessor());

        services.AddScoped<IBasketDbContext>(sp => sp.GetRequiredService<BasketDbContext>());

        return services;
    }
}
