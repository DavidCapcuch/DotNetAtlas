namespace Catalog.Application.Categories.Common.Services;

/// <summary>
/// Pure helper that converts a materialized <c>CategoryPath</c> (e.g.
/// <c>/electronics/laptops</c>) into the human-readable <c>CategoryBreadcrumb</c>
/// (<c>Electronics &gt; Laptops</c>) stored on the <c>product_search_view</c> projection.
/// </summary>
/// <remarks>
/// Lives next to <see cref="ICategoryPathService"/> because the breadcrumb is a function of the
/// path; the reparent cascade in <c>CategoryPathService.RewriteDescendantPathsAsync</c> uses it
/// to rewrite descendants' breadcrumbs alongside their paths, and
/// <c>ProductCreatedProjectionDomainEventHandler</c> uses it on initial projection.
/// </remarks>
public static class CategoryBreadcrumbBuilder
{
    public static string Build(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" > ", segments.Select(ToHumanReadableSegment));
    }

    // CAT-RV-L01: category slug segments contain hyphens between words
    // ("electronics-toys"). Title-case each space-delimited token, not just the first
    // character of the whole segment, so "electronics-toys" -> "Electronics Toys" rather
    // than "Electronics-toys".
    private static string ToHumanReadableSegment(string segment)
    {
        if (segment.Length == 0)
        {
            return segment;
        }

        var tokens = segment.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens.Select(TitleCaseToken));
    }

    private static string TitleCaseToken(string token)
    {
        if (token.Length == 0)
        {
            return token;
        }

        return char.ToUpperInvariant(token[0]) + token[1..];
    }
}
