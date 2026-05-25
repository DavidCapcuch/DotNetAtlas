using Platform.Test.Framework;

namespace Ordering.ArchitectureTests.Migrations;

public class DatabaseMigrationFilesTests
{
    private const string OrderingInfrastructureRelativePath = "services/Ordering/Ordering.Infrastructure";

    [Fact]
    public void EfCoreMigrations_ShouldBeEqualToVersionedSqlScriptMigrations()
    {
        // Arrange
        var migrationsCount = Directory
            .GetFiles(
                SolutionPaths.EfMigrationsDirectoryFor(OrderingInfrastructureRelativePath),
                "2*.cs",
                SearchOption.TopDirectoryOnly)
            .Count(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

        var sqlCount = Directory
            .GetFiles(
                SolutionPaths.SqlScriptMigrationsDirectoryFor(OrderingInfrastructureRelativePath),
                "V*.sql",
                SearchOption.TopDirectoryOnly).Length;

        // Assert
        migrationsCount.Should().Be(sqlCount);
    }
}
