using System.Reflection;

namespace DotNetAtlas.ArchitectureTests
{
    public static class DatabasePaths
    {
        private const string SOLUTION_FILE_NAME = "DotNetAtlas.sln";

        public static string DatabaseRootDirectory =>
            Path.Combine(GetSolutionRootDirectory(), "src", "DotNetAtlas.Infrastructure", "Persistence", "Database");

        public static string EfMigrationsDirectory => Path.Combine(DatabaseRootDirectory, "Migrations");

        public static string FlywayMigrationsDirectory => Path.Combine(EfMigrationsDirectory, "Flyway");

        private static string GetSolutionRootDirectory()
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var current = new DirectoryInfo(assemblyLocation);

            while (current != null)
            {
                var slnPath = Path.Combine(current.FullName, SOLUTION_FILE_NAME);
                if (File.Exists(slnPath))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException(
                $"Could not locate solution root ({SOLUTION_FILE_NAME}) starting from assembly location: ${assemblyLocation}");
        }
    }
}