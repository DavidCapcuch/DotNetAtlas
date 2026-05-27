using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platform.ServiceDefaults;

/// <summary>
/// Applies EF Core schema migrations at API startup, gated to the <c>Development</c> host
/// environment (laptop <c>dotnet run</c> + <c>docker-compose</c> per the taxonomy on
/// <see cref="HostEnvironmentExtensions"/>). This is the developer fast-iteration path — the EF
/// model is the source of truth and migrations run via <c>DbContext.Database.MigrateAsync()</c>, so
/// a developer can iterate on entity classes without re-emitting <c>V*.sql</c> for every schema
/// tweak. Other environments apply schema out-of-band against the committed <c>V*.sql</c> scripts:
/// the Testing host uses Evolve through <c>PostgreSqlTestContainer</c> (so test runs exercise the
/// exact SQL Flyway will run in prod), and deployed clusters (Dev, Staging, Production) use the
/// unified <c>flyway</c> service. Compose also has Flyway as a dependency, so when this path
/// runs on the compose tier the schema is already applied and <c>MigrateAsync</c> no-ops. Runtime
/// <c>MigrateAsync</c> is positioned by Microsoft as a dev convenience, not a deployment strategy:
/// https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#apply-migrations-at-runtime.
/// </summary>
public static class MigrationStartupExtensions
{
    /// <summary>
    /// Applies any pending migrations for <typeparamref name="TContext"/> when
    /// <see cref="HostEnvironmentEnvExtensions.IsDevelopment(IHostEnvironment)"/> is true. No-op
    /// in every other environment, with an explicit log line so the skip is visible. Call after
    /// <c>builder.Build()</c> and before <c>app.RunAsync()</c> so migration completes before
    /// Kestrel binds.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> whose pending migrations should be applied.</typeparam>
    /// <param name="app">The built <see cref="WebApplication"/>.</param>
    public static async Task MigrateOnStartupIfDevelopmentAsync<TContext>(this WebApplication app)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(MigrationStartupExtensions));

        if (!app.Environment.IsDevelopment())
        {
            logger.LogInformation(
                "Skipping {Context}.MigrateAsync() — host environment is {Env}, not Development. " +
                "Non-Development environments apply schema out-of-band (Evolve in Testing, Flyway in deployed).",
                typeof(TContext).Name,
                app.Environment.EnvironmentName);
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        try
        {
            logger.LogInformation(
                "Applying pending migrations for {Context} (Development environment) ...",
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
