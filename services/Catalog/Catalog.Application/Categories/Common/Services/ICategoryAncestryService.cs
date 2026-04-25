namespace Catalog.Application.Categories.Common.Services;

/// <summary>
/// Domain service used by <c>ReparentCategoryCommandHandler</c> to detect cycle attempts
/// before delegating to <c>Category.Reparent</c>. The aggregate itself only rejects
/// the trivial self-parent case; descendant cycles need an out-of-aggregate read.
/// </summary>
public interface ICategoryAncestryService
{
    /// <summary>
    /// Returns <see langword="true"/> when making <paramref name="newParentCategoryId"/> the
    /// parent of <paramref name="categoryId"/> would create a cycle — i.e., either the IDs
    /// are equal or the candidate parent's <c>Path</c> sits beneath the category's current
    /// <c>Path</c> (segment-bounded prefix match).
    /// </summary>
    /// <remarks>
    /// Implementation reads the materialized <c>Path</c> on both nodes; no recursive walk.
    /// Returns <see langword="false"/> when either id is missing — the handler has already
    /// resolved both via <c>Categories.FirstOrDefaultAsync</c> and surfaced
    /// <c>CategoryErrors.NotFound</c> for the missing-row case.
    /// </remarks>
    Task<bool> WouldCreateCycleAsync(
        Guid categoryId,
        Guid newParentCategoryId,
        CancellationToken cancellationToken);
}
