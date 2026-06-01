using Microsoft.EntityFrameworkCore;
using Platform.OutboxRelay.WorkerService.Common.Config;
using Platform.OutboxRelay.WorkerService.OutboxRelay;
using Platform.ReliableMessaging.Outbox.EFCore;

namespace Platform.OutboxRelay.WorkerService.Common;

/// <summary>
/// Dependency injection extensions for persistence infrastructure (EF Core / PostgreSQL).
/// </summary>
public static class PersistenceDependencyInjection
{
    /// <summary>
    /// Configures the Entity Framework Core outbox context with PostgreSQL (Npgsql).
    /// Sets up the pooled DbContext factory, connection retry policy, and snake_case naming convention.
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
                options.UseNpgsql(
                        configuration.GetConnectionString(nameof(ConnectionStringsOptions.Outbox)),
                        npgsqlOptions =>
                        {
                            npgsqlOptions.EnableRetryOnFailure(
                                maxRetryCount: efCoreOptions.RetryMaxCount,
                                maxRetryDelay: TimeSpan.FromSeconds(efCoreOptions.RetryMaxDelaySeconds),
                                errorCodesToAdd: null);
                        })
                    .UseSnakeCaseNamingConvention()
                    .EnableDetailedErrors(efCoreOptions.EnableDetailedErrors),
            poolSize: efCoreOptions.DbContextPoolSize);

        services.AddScoped<IOutboxDbContext, OutboxDbContext>();

        return services;
    }
}
