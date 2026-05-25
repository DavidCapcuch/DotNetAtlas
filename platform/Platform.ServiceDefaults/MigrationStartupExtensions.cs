using EvolveDb;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Platform.ServiceDefaults;

/// <summary>
/// Applies SQL-script migrations at API startup, gated to the "Local" host environment.
/// Non-Local environments (Development, Testing, Staging, Production) must apply migrations
/// out-of-band — the unified <c>flyway</c> service in compose runs the same idempotent
/// <c>V*.sql</c> files in dev/staging; a dedicated runner does it in deployed clusters.
/// Local startup uses Evolve against the very same files (#269), so dev + tests + prod
/// converge on one set of artefacts.
/// Runtime schema application is positioned by Microsoft as a dev convenience, not a
/// deployment strategy:
/// https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#apply-migrations-at-runtime.
/// </summary>
public static class MigrationStartupExtensions
{
    private const string SolutionFileName = "DotNetAtlas.slnx";

    /// <summary>
    /// Applies the BC's committed <c>V*.sql</c> scripts via Evolve when
    /// <see cref="HostEnvironmentExtensions.IsLocal"/> is true. No-op in every other
    /// environment, with an explicit log line so the skip is visible. Call after
    /// <c>builder.Build()</c> and before <c>app.RunAsync()</c> so migration completes
    /// before Kestrel binds.
    /// </summary>
    /// <typeparam name="TContext">The <see cref="DbContext"/> whose connection backs the migration. Only used to obtain the live <see cref="System.Data.Common.DbConnection"/> and as the logger context name; the EF model itself is not consulted.</typeparam>
    /// <param name="app">The built <see cref="WebApplication"/>.</param>
    /// <param name="infrastructureProjectRelativePath">Repo-relative path to the BC's Infrastructure project (forward slashes), e.g. <c>"services/Catalog/Catalog.Infrastructure"</c>. The <c>SqlScripts</c> dir is resolved as <c>&lt;solutionRoot&gt;/&lt;path&gt;/Persistence/Database/Migrations/SqlScripts</c>.</param>
    public static async Task ApplySqlScriptsOnStartupIfLocalAsync<TContext>(
        this WebApplication app,
        string infrastructureProjectRelativePath)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(infrastructureProjectRelativePath);

        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(MigrationStartupExtensions));

        if (!app.Environment.IsLocal())
        {
            logger.LogInformation(
                "Skipping {Context} SQL-script application — host environment is {Env}, not Local. " +
                "Non-Local environments must apply migrations out-of-band (the unified `flyway` service in compose).",
                typeof(TContext).Name,
                app.Environment.EnvironmentName);
            return;
        }

        var sqlScriptsPath = ResolveSolutionPath(
            infrastructureProjectRelativePath, "Persistence", "Database", "Migrations", "SqlScripts");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var connection = db.Database.GetDbConnection();

        try
        {
            logger.LogInformation(
                "Applying SQL-script migrations for {Context} from {Path} (Local environment) ...",
                typeof(TContext).Name,
                sqlScriptsPath);

            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var evolve = new Evolve(connection)
            {
                Locations = [sqlScriptsPath],
            };
            evolve.Migrate();

            logger.LogInformation("SQL-script migrations applied for {Context}.", typeof(TContext).Name);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "SQL-script migration failed for {Context}; rethrowing so the host aborts.",
                typeof(TContext).Name);
            throw;
        }
    }

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> looking for the solution
    /// marker (<c>DotNetAtlas.slnx</c>), then joins the repo-relative BC path + tail
    /// segments to produce an absolute filesystem path. Local-only — non-Local
    /// deployments do not have the solution file in their working tree.
    /// </summary>
    private static string ResolveSolutionPath(string repoRelativePath, params string[] tail)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, SolutionFileName)))
            {
                var segments = new List<string> { current.FullName };
                segments.AddRange(repoRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
                segments.AddRange(tail);
                return Path.Combine([.. segments]);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate solution root ({SolutionFileName}) starting from {AppContext.BaseDirectory}. " +
            "ApplySqlScriptsOnStartupIfLocalAsync is intended only for the Local environment, where the API binary lives under a dev checkout.");
    }
}
