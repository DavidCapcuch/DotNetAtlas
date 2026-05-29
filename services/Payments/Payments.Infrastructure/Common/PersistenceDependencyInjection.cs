using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Common.Data;
using Payments.Infrastructure.Common.Config;
using Payments.Infrastructure.Persistence.Database;
using Payments.Infrastructure.Persistence.Database.Interceptors;

namespace Payments.Infrastructure.Common;

/// <summary>
/// DI wiring for EF Core + Postgres. Payments has no cache in v1 (no read-amplification justifies
/// it). <c>UseExceptionProcessor()</c> is mandatory for the inbox pattern: it converts
/// PostgreSQL unique-constraint violations into <c>UniqueConstraintException</c> so the
/// inbox-dedup middleware can treat duplicate-message replays as no-ops.
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

        // Scoped: handlers resolved in the same scope as the DbContext write domain-event
        // outbox messages to the same scoped UoW — transactional atomicity with the
        // aggregate save.
        services.AddScoped<DispatchDomainEventsInterceptor>();

        // Singleton: pure function of TimeProvider + entity entry state. No-op for the
        // current Payments aggregate (PaymentTransaction does not implement IAuditableEntity);
        // registered for forward-compat + cross-BC convention parity.
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

        services.AddDbContext<PaymentsDbContext>((sp, options) => options
            .UseNpgsql(
                configuration.GetConnectionString(nameof(ConnectionStringsOptions.Payments)),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        PaymentsDbContext.DefaultSchemaName);
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

        services.AddScoped<IPaymentsDbContext>(sp => sp.GetRequiredService<PaymentsDbContext>());

        return services;
    }
}
