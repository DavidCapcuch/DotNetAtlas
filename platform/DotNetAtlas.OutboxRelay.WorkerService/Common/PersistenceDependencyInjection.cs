using DotNetAtlas.Outbox.EntityFrameworkCore.EntityFramework;
using DotNetAtlas.OutboxRelay.WorkerService.Common.Config;
using DotNetAtlas.OutboxRelay.WorkerService.OutboxRelay;
using Microsoft.EntityFrameworkCore;

namespace DotNetAtlas.OutboxRelay.WorkerService.Common;

/// <summary>
/// Dependency injection extensions for persistence infrastructure.
/// Configures database (EF Core) and distributed caching (Redis + FusionCache).
/// </summary>
public static class PersistenceDependencyInjection
{
    /// <summary>
    /// Configures Entity Framework Core database context with SQL Server.
    /// Sets up connection pooling, interceptors, retry policies, seeding, and outbox pattern.
    /// </summary>
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        ConfigurationManager configuration)
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

        services.AddPooledDbContextFactory<OutboxDbContext>(options =>
                options.UseSqlServer(
                        configuration.GetConnectionString(nameof(ConnectionStringsOptions.Outbox)),
                        sqlServerOptions =>
                        {
                            sqlServerOptions.EnableRetryOnFailure(
                                maxRetryCount: efCoreOptions.RetryMaxCount,
                                maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                                errorNumbersToAdd: null);
                        })
                    .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors),
            poolSize: efCoreOptions.DbContextPoolSize);

        services.AddScoped<IOutboxDbContext, OutboxDbContext>();

        return services;
    }
}
