using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Catalog.UnitTests.Common;

/// <summary>
/// Test seam for CAT-RV-H04: a <see cref="FakeCatalogDbContext"/> that throws a configured
/// exception on the next <c>SaveChangesAsync</c> call AFTER the test has finished seeding.
/// Mirrors what the production EntityFramework.Exceptions interceptor does when a unique
/// constraint races a concurrent commit.
/// </summary>
public sealed class ThrowOnSaveCatalogDbContext : FakeCatalogDbContext
{
    private readonly Exception _exception;
    private bool _armed;

    private ThrowOnSaveCatalogDbContext(Exception exception, DbContextOptions<FakeCatalogDbContext> options)
        : base(options)
    {
        _exception = exception;
    }

    public static ThrowOnSaveCatalogDbContext CreateThrowing(Exception exception)
    {
        var options = new DbContextOptionsBuilder<FakeCatalogDbContext>()
            .UseInMemoryDatabase(Guid.CreateVersion7().ToString())
            .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ThrowOnSaveCatalogDbContext(exception, options);
    }

    /// <summary>Bypasses the throwing override during arrange-phase seeding.</summary>
    public Task<int> SaveChangesViaBaseAsync(CancellationToken ct)
    {
        var result = base.SaveChangesAsync(ct);
        _armed = true;
        return result;
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (!_armed)
        {
            return base.SaveChangesAsync(ct);
        }

        throw _exception;
    }
}
