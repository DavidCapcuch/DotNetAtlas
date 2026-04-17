using System.Reflection;

namespace Platform.Test.Framework;

public static class SolutionPaths
{
    private const string SolutionFileName = "DotNetAtlas.slnx";

    public static string DatabaseRootDirectory =>
        Path.Combine(GetSolutionRootDirectory(), "src", "Weather.Infrastructure", "Persistence", "Database");

    public static string EfMigrationsDirectory => Path.Combine(DatabaseRootDirectory, "Migrations");

    public static string SqlScriptMigrationsDirectory => Path.Combine(EfMigrationsDirectory, "SqlScripts");

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
