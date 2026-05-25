using Platform.Test.Framework;

namespace Notifications.ArchitectureTests.Migrations;

public class DatabaseMigrationFilesTests
{
    private const string NotificationsInfrastructureRelativePath = "services/Notifications/Notifications.Infrastructure";

    [Fact]
    public void EfCoreMigrations_ShouldBeEqualToVersionedSqlScriptMigrations()
    {
        var migrationsCount = Directory
            .GetFiles(
                SolutionPaths.EfMigrationsDirectoryFor(NotificationsInfrastructureRelativePath),
                "2*.cs",
                SearchOption.TopDirectoryOnly)
            .Count(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));

        var sqlCount = Directory
            .GetFiles(
                SolutionPaths.SqlScriptMigrationsDirectoryFor(NotificationsInfrastructureRelativePath),
                "V*.sql",
                SearchOption.TopDirectoryOnly).Length;

        migrationsCount.Should().Be(sqlCount);
    }
}
