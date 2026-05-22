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
/// DI wiring for the Basket SQL side-car: binds <see cref="EfCoreOptions"/>,
/// reads the Postgres connection string under
/// <see cref="ConnectionStringNames.Basket"/>, registers
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

        var efCoreOptions = configuration
            .GetRequiredSection(EfCoreOptions.Section)
            .Get<EfCoreOptions>()!;

        var connectionString = configuration.GetConnectionString(ConnectionStringNames.Basket);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringNames.Basket}' is not configured. " +
                $"Basket.Infrastructure requires the {ConnectionStringNames.Basket} entry " +
                $"(Postgres SQL side-car for outbox + inbox per ADR-0003).");
        }

        services.AddDbContext<BasketDbContext>((_, options) => options
            .UseNpgsql(
                connectionString,
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
            .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors)
            .UseExceptionProcessor());

        services.AddScoped<IBasketDbContext>(sp => sp.GetRequiredService<BasketDbContext>());

        return services;
    }
}
