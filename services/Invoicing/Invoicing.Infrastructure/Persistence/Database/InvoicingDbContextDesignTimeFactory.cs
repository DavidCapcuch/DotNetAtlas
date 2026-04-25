using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Invoicing.Infrastructure.Persistence.Database;

/// <summary>
/// Used by <c>dotnet ef</c> to instantiate <see cref="InvoicingDbContext"/>
/// at design time (e.g. <c>dotnet ef migrations add ...</c>). This keeps
/// migration generation decoupled from the API host — the host wires its
/// own <see cref="InvoicingDbContext"/> via
/// <see cref="Common.PersistenceDependencyInjection"/> at runtime.
/// </summary>
/// <remarks>
/// The connection string is read from the <c>INVOICING_CONNECTION_STRING</c>
/// environment variable when set, otherwise falls back to the local-dev
/// value mirrored from <c>Invoicing.API/appsettings.json</c>. It is only
/// used by the EF tooling to know which provider to target; no queries run
/// during migration authoring.
/// </remarks>
public sealed class InvoicingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<InvoicingDbContext>
{
    private const string DefaultLocalConnectionString =
        "Host=127.0.0.1;Port=5433;Database=Invoicing;Username=postgres;Password=PasswordThatShouldBeInVaultAndNotExposed;Include Error Detail=true";

    public InvoicingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("INVOICING_CONNECTION_STRING")
            ?? DefaultLocalConnectionString;

        var options = new DbContextOptionsBuilder<InvoicingDbContext>()
            .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable(
                HistoryRepository.DefaultTableName,
                InvoicingDbContext.DefaultSchemaName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new InvoicingDbContext(options);
    }
}
