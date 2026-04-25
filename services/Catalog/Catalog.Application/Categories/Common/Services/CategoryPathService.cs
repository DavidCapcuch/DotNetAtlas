using Catalog.Application.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Categories.Common.Services;

/// <summary>
/// EF Core implementation of <see cref="ICategoryPathService"/> using bulk
/// <c>ExecuteUpdateAsync</c> against descendants of the reparented category.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's <c>ExecuteUpdateAsync</c> bypasses the change tracker and emits a single
/// <c>UPDATE</c> per call. Two calls fire here — one rewrites <c>catalog.categories.path</c>
/// for descendants, the other rewrites <c>catalog.product_search_view.category_path</c> for
/// rows whose category sits beneath the reparented one. Both run within the ambient
/// EF transaction opened by the surrounding command handler / outbox UoW.
/// </para>
/// <para>
/// Translation expectations on Postgres: <c>string.Substring(int)</c> →
/// <c>SUBSTRING(col FROM N + 1)</c>; <c>+</c> on string operands → <c>||</c>.
/// Verified against the Postgres Testcontainer in <c>Catalog.IntegrationTests</c>.
/// </para>
/// <para>
/// Not unit-testable against the EF Core <c>InMemory</c> provider — that provider does not
/// implement <c>ExecuteUpdateAsync</c>. Unit tests for the calling handler substitute
/// <see cref="ICategoryPathService"/> via NSubstitute and assert the call shape; the actual
/// SQL behaviour is covered by <c>ReparentCategoryCommandHandlerTests</c> in
/// <c>Catalog.IntegrationTests</c>.
/// </para>
/// </remarks>
public sealed class CategoryPathService : ICategoryPathService
{
    private readonly ICatalogDbContext _db;

    public CategoryPathService(ICatalogDbContext db)
    {
        _db = db;
    }

    public async Task RewriteDescendantPathsAsync(
        string oldPath,
        string newPath,
        Guid excludedCategoryId,
        CancellationToken cancellationToken)
    {
        if (oldPath == newPath)
        {
            return;
        }

        var oldPathLength = oldPath.Length;
        var descendantPrefix = oldPath + "/";

        // CA1845 (use AsSpan + string.Concat) doesn't apply inside EF Core expression trees —
        // Span<char> can't appear in an expression tree, and EF translates string.Substring(int)
        // to SUBSTRING(col FROM N+1) on Postgres. Keep the LINQ form.
#pragma warning disable CA1845
        await _db.Categories
            .Where(c => c.Id != excludedCategoryId
                && (c.Path.Value == oldPath || c.Path.Value.StartsWith(descendantPrefix)))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    c => c.Path.Value,
                    c => newPath + c.Path.Value.Substring(oldPathLength)),
                cancellationToken);

        await _db.ProductSearchView
            .Where(r => r.CategoryPath == oldPath || r.CategoryPath.StartsWith(descendantPrefix))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    r => r.CategoryPath,
                    r => newPath + r.CategoryPath.Substring(oldPathLength)),
                cancellationToken);
#pragma warning restore CA1845
    }
}
