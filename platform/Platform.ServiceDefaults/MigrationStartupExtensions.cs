using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Platform.ServiceDefaults;

/// <summary>
/// Applies EF Core schema migrations at API startup, gated to the "Local" host
/// environment. Non-Local environments (Development, Testing, Staging, Production)
/// must apply migrations out-of-band — Flyway with EF-generated SQL scripts in
/// compose; migration bundles or a dedicated runner in deployed clusters. Runtime
/// MigrateAsync is positioned by Microsoft as a dev convenience, not a deployment
/// strategy:
/// https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#apply-migrations-at-runtime.
/// </summary>
public static class MigrationStartupExtensions
{
    /// <summary>
    /// Applies any pending migrations for <typeparamref name="TContext"/> when
    /// <see cref="HostEnvironmentExtensions.IsLocal"/> is true. No-op in every
    /// other environment, with an explicit log line so the skip is visible.
    /// Call after <c>builder.Build()</c> and before <c>app.RunAsync()</c> so
    /// migration completes before Kestrel binds.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> whose migrations should run.</typeparam>
    /// <param name="app">The built <see cref="WebApplication"/>.</param>
    public static async Task MigrateOnStartupIfLocalAsync<TContext>(this WebApplication app)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(MigrationStartupExtensions));

        if (!app.Environment.IsLocal())
        {
            logger.LogInformation(
                "Skipping {Context}.MigrateAsync() — host environment is {Env}, not Local. " +
                "Non-Local environments must apply migrations out-of-band.",
                typeof(TContext).Name,
                app.Environment.EnvironmentName);
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        try
        {
            logger.LogInformation(
                "Applying pending migrations for {Context} (Local environment) ...",
                typeof(TContext).Name);
            await db.Database.MigrateAsync();
            logger.LogInformation("Migrations applied for {Context}.", typeof(TContext).Name);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Migration failed for {Context}; rethrowing so the host aborts.",
                typeof(TContext).Name);
            throw;
        }
    }
}
