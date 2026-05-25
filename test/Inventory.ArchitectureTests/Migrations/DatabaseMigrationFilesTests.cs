using Platform.Test.Framework;

namespace Inventory.ArchitectureTests.Migrations;

public class DatabaseMigrationFilesTests
{
    private const string InventoryInfrastructureRelativePath = "services/Inventory/Inventory.Infrastructure";

    [Fact]
    public void EfCoreMigrations_ShouldBeEqualToVersionedSqlScriptMigrations()
    {
        // Arrange
        var migrationsCount = Directory
            .GetFiles(
                SolutionPaths.EfMigrationsDirectoryFor(InventoryInfrastructureRelativePath),
                "2*.cs",
                SearchOption.TopDirectoryOnly)
            .Count(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

        var sqlCount = Directory
            .GetFiles(
                SolutionPaths.SqlScriptMigrationsDirectoryFor(InventoryInfrastructureRelativePath),
                "V*.sql",
                SearchOption.TopDirectoryOnly).Length;

        // Assert
        migrationsCount.Should().Be(sqlCount);
    }
}
