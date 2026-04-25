using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Basket.Infrastructure.Persistence.Database;

/// <summary>
/// Used by <c>dotnet ef</c> to instantiate <see cref="BasketDbContext"/> at
/// design time (e.g. <c>dotnet ef migrations add ...</c>). This keeps
/// migration generation decoupled from the API host — the host wires its own
/// <see cref="BasketDbContext"/> via
/// <see cref="Common.PersistenceDependencyInjection"/> at runtime.
/// </summary>
/// <remarks>
/// The connection string is read from the <c>BASKET_CONNECTION_STRING</c>
/// environment variable when set, otherwise falls back to the local-dev
/// value mirrored from <c>Basket.Api/appsettings.json</c>. It is only used
/// by the EF tooling to know which provider to target; no queries run during
/// migration authoring.
/// </remarks>
public sealed class BasketDbContextDesignTimeFactory : IDesignTimeDbContextFactory<BasketDbContext>
{
    private const string DefaultLocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=Basket;Username=postgres;Password=PasswordThatShouldBeInVaultAndNotExposed;Include Error Detail=true";

    public BasketDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("BASKET_CONNECTION_STRING")
            ?? DefaultLocalConnectionString;

        var options = new DbContextOptionsBuilder<BasketDbContext>()
            .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable(
                HistoryRepository.DefaultTableName,
                BasketDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new BasketDbContext(options);
    }
}
