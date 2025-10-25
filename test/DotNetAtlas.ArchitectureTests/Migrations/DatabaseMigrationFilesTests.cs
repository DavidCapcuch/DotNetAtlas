using DotNetAtlas.Test.Shared;

namespace DotNetAtlas.ArchitectureTests.Migrations;

public class DatabaseMigrationFilesTests
{
    [Fact]
    public void EfCoreMigrations_ShouldBeEqualToVersionedFlywayMigrations()
    {
        // Arrange
        var migrationsCount = Directory
            .GetFiles(SolutionPaths.EfMigrationsDirectory, "2*.cs", SearchOption.TopDirectoryOnly)
            .Count(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

        var sqlCount = Directory
            .GetFiles(
                SolutionPaths.FlywayMigrationsDirectory,
                "V*.sql",
                SearchOption.TopDirectoryOnly).Length;

        // Assert
        migrationsCount.Should().Be(sqlCount);
    }
}
