using EntityFramework.Exceptions.SqlServer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Payments.Common.Config;
using Payments.Persistence.Database;

namespace Payments.Common;

/// <summary>
/// Dependency injection extensions for persistence infrastructure.
/// </summary>
public static class PersistenceDependencyInjection
{
    /// <summary>
    /// Configures Entity Framework Core database context with SQL Server.
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

        services.AddDbContext<PaymentDbContext>((
            sp,
            options) => options
            .UseSqlServer(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Payment)),
                sqlServerOptions =>
                {
                    sqlServerOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName,
                        PaymentDbContext.DefaultSchemaName);
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
            .UseExceptionProcessor()); // required for the Inbox pattern, see DotNetAtlas.ReliableMessaging.Inbox.EFCore

        services.AddScoped<IPaymentDbContext>(sp => sp.GetRequiredService<PaymentDbContext>());

        return services;
    }
}
