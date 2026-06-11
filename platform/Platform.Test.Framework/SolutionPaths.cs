using System.Reflection;

namespace Platform.Test.Framework;

public static class SolutionPaths
{
    private const string SolutionFileName = "DotNetAtlas.slnx";

    /// <summary>
    /// Resolves <c>&lt;solutionRoot&gt;/&lt;infrastructureProjectRelativePath&gt;/Persistence/Database</c>
    /// for the given bounded context's Infrastructure project.
    /// </summary>
    /// <param name="infrastructureProjectRelativePath">Repo-relative path to the Infrastructure project (forward slashes), e.g. <c>"services/Basket/Basket.Infrastructure"</c>.</param>
    public static string DatabaseRootDirectoryFor(string infrastructureProjectRelativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(infrastructureProjectRelativePath);

        var segments = infrastructureProjectRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var combined = new List<string> { GetSolutionRootDirectory() };
        combined.AddRange(segments);
        combined.Add("Persistence");
        combined.Add("Database");
        return Path.Combine([.. combined]);
    }

    /// <summary>
    /// Resolves the EF Core migrations directory (<c>&lt;DatabaseRoot&gt;/Migrations</c>) for the given BC.
    /// </summary>
    public static string EfMigrationsDirectoryFor(string infrastructureProjectRelativePath) =>
        Path.Combine(DatabaseRootDirectoryFor(infrastructureProjectRelativePath), "Migrations");

    /// <summary>
    /// Resolves the SQL script migrations directory (<c>&lt;DatabaseRoot&gt;/Migrations/SqlScripts</c>) for the given BC.
    /// Use this as the <c>sqlScriptsMigrationsPath</c> argument to <see cref="Database.PostgreSqlTestContainer"/>.
    /// </summary>
    public static string SqlScriptMigrationsDirectoryFor(string infrastructureProjectRelativePath) =>
        Path.Combine(EfMigrationsDirectoryFor(infrastructureProjectRelativePath), "SqlScripts");

    public static string GetSolutionRootDirectory()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        var current = new DirectoryInfo(assemblyLocation);

        while (current != null)
        {
            var slnPath = Path.Combine(current.FullName, SolutionFileName);
            if (File.Exists(slnPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate solution root ({SolutionFileName}) starting from assembly location: {assemblyLocation}");
    }
}
