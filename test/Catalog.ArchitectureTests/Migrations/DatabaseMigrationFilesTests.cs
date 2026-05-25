using Platform.Test.Framework;

namespace Catalog.ArchitectureTests.Migrations;

public class DatabaseMigrationFilesTests
{
    private const string CatalogInfrastructureRelativePath = "services/Catalog/Catalog.Infrastructure";

    [Fact]
    public void EfCoreMigrations_ShouldBeEqualToVersionedSqlScriptMigrations()
    {
        // Arrange
        var migrationsCount = Directory
            .GetFiles(
                SolutionPaths.EfMigrationsDirectoryFor(CatalogInfrastructureRelativePath),
                "2*.cs",
                SearchOption.TopDirectoryOnly)
            .Count(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

        var sqlCount = Directory
            .GetFiles(
                SolutionPaths.SqlScriptMigrationsDirectoryFor(CatalogInfrastructureRelativePath),
                "V*.sql",
                SearchOption.TopDirectoryOnly).Length;

        // Assert
        migrationsCount.Should().Be(sqlCount);
    }
}
