using Platform.Test.Framework;

namespace Invoicing.ArchitectureTests.Migrations;

public class DatabaseMigrationFilesTests
{
    private const string InvoicingInfrastructureRelativePath = "services/Invoicing/Invoicing.Infrastructure";

    [Fact]
    public void EfCoreMigrations_ShouldBeEqualToVersionedSqlScriptMigrations()
    {
        // Arrange
        var migrationsCount = Directory
            .GetFiles(
                SolutionPaths.EfMigrationsDirectoryFor(InvoicingInfrastructureRelativePath),
                "2*.cs",
                SearchOption.TopDirectoryOnly)
            .Count(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

        var sqlCount = Directory
            .GetFiles(
                SolutionPaths.SqlScriptMigrationsDirectoryFor(InvoicingInfrastructureRelativePath),
                "V*.sql",
                SearchOption.TopDirectoryOnly).Length;

        // Assert
        migrationsCount.Should().Be(sqlCount);
    }
}
