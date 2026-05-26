using EntityFramework.Exceptions.PostgreSQL;
using Inventory.Application.Common.Data;
using Inventory.Infrastructure.Common.Config;
using Inventory.Infrastructure.Persistence.Database;
using Inventory.Infrastructure.Persistence.EventStore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Infrastructure.Common;

/// <summary>
/// DI wiring for the Inventory persistence slice: Npgsql, EF Core,
/// <see cref="InventoryDbContext"/>, the event-store repository, and the
/// <see cref="IInventoryDbContext"/> + <see cref="IEventStore"/> port
/// bindings consumed by the Application layer.
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

        services.AddDbContext<InventoryDbContext>((_, options) => options
            .UseNpgsql(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Inventory)),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        InventoryDbContext.DefaultSchemaName);
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

        services.AddScoped<EventStoreRepository>();
        services.AddScoped<IEventStore>(sp => sp.GetRequiredService<EventStoreRepository>());
        services.AddScoped<IInventoryDbContext>(sp => sp.GetRequiredService<InventoryDbContext>());

        return services;
    }
}
