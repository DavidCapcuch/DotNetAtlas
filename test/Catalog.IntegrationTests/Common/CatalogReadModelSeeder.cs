using Catalog.Application.Common.ReadModels;
using Catalog.Domain.Categories;
using Catalog.Infrastructure.Persistence.Database;

namespace Catalog.IntegrationTests.Common;

/// <summary>
/// Seeds <c>product_search_view</c> rows for read-projection integration tests while honouring the
/// real Postgres FK <c>product_search_view.category_id → categories(id)</c> (OnDelete.Restrict)
/// that the retired EF-InMemory unit tier never enforced. Categories are seeded as pure data —
/// their domain events are popped first, so neither the (no-op) projection handler nor the outbox
/// publisher fires. Any row whose <c>CategoryId</c> is not backed by a category seeded via
/// <see cref="SeedCategoryAsync"/> is repointed at a shared filler category; this is safe because
/// the handlers under test filter such rows by <c>CategoryPath</c> / <c>Status</c> / price, never by
/// the filler Id.
/// </summary>
internal sealed class CatalogReadModelSeeder
{
    private readonly CatalogDbContext _db;
    private readonly HashSet<Guid> _backedCategoryIds = [];
    private Category? _filler;

    public CatalogReadModelSeeder(CatalogDbContext db)
    {
        _db = db;
    }

    /// <summary>Persists a <see cref="Category"/> aggregate and records its Id as a valid FK target.</summary>
    public async Task<Category> SeedCategoryAsync(Category category, CancellationToken ct)
    {
        category.PopDomainEvents();
        _db.Categories.Add(category);
        _backedCategoryIds.Add(category.Id);
        await _db.SaveChangesAsync(ct);
        return category;
    }

    /// <summary>Persists projection rows, repointing any unbacked <c>CategoryId</c> at the filler category.</summary>
    public async Task SeedRowsAsync(CancellationToken ct, params ProductSearchViewRow[] rows)
    {
        foreach (var row in rows)
        {
            if (!_backedCategoryIds.Contains(row.CategoryId))
            {
                row.CategoryId = (await EnsureFillerAsync(ct)).Id;
            }
        }

        _db.ProductSearchView.AddRange(rows);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<Category> EnsureFillerAsync(CancellationToken ct)
    {
        return _filler ??= await SeedCategoryAsync(CatalogFactories.RootCategory("Filler"), ct);
    }
}
