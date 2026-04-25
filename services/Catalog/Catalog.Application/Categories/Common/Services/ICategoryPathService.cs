namespace Catalog.Application.Categories.Common.Services;

/// <summary>
/// Domain service used by <c>ReparentCategoryCommandHandler</c> after a successful
/// <c>Category.Reparent</c> to bulk-rewrite the materialized <c>Path</c> column on every
/// descendant category and the corresponding <c>CategoryPath</c> column on every
/// <c>product_search_view</c> row.
/// </summary>
/// <remarks>
/// <para>
/// The calling handler wraps the cascade and the aggregate save in
/// <c>Database.EnsureTransactionAsync</c> so the bulk updates and <c>SaveChanges</c> commit
/// (or roll back) together — without that wrap, EF's <c>ExecuteUpdateAsync</c> auto-commits
/// per statement and a later <c>SaveChanges</c> failure would leave descendants out of sync
/// with the reparented parent.
/// </para>
/// <para>
/// Implementations use bulk EF Core <c>ExecuteUpdateAsync</c> rather than load-then-mutate to
/// keep the cascade tractable for branchy taxonomies. Path matching uses the segment-bounded
/// prefix form (<c>== oldPath || StartsWith(oldPath + "/")</c>) so reparenting <c>/electronics</c>
/// never accidentally rewrites <c>/electronics-toys</c>.
/// </para>
/// </remarks>
public interface ICategoryPathService
{
    /// <summary>
    /// Rewrites <c>Categories.Path</c> and <c>ProductSearchView.CategoryPath</c> for every row
    /// whose path equals <paramref name="oldPath"/> or starts with <c>oldPath + "/"</c>, replacing
    /// the leading <paramref name="oldPath"/> segment with <paramref name="newPath"/>.
    /// </summary>
    /// <param name="oldPath">The reparented category's path before <c>Category.Reparent</c> ran.</param>
    /// <param name="newPath">The reparented category's path after <c>Category.Reparent</c> ran.</param>
    /// <param name="excludedCategoryId">
    /// The reparented category itself — already mutated in the change tracker by
    /// <c>Category.Reparent</c>; excluded from the bulk update on <c>Categories</c> so EF and SQL agree.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the bulk-update commands.</param>
    Task RewriteDescendantPathsAsync(
        string oldPath,
        string newPath,
        Guid excludedCategoryId,
        CancellationToken cancellationToken);
}
