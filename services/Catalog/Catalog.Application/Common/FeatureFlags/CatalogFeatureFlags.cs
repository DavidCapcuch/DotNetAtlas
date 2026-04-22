namespace Catalog.Application.Common.FeatureFlags;

/// <summary>
/// OpenFeature flag keys owned by the Catalog BC (ADR-0014).
/// </summary>
public static class CatalogFeatureFlags
{
    /// <summary>
    /// When <c>true</c>, <c>SearchProductsQueryHandler</c> includes products in
    /// <c>Status == Discontinued</c> in its results. When <c>false</c> (default), the handler
    /// filters to <c>Status == Active</c> only. Flip at runtime via the OpenFeature JSON
    /// provider — no redeploy required.
    /// </summary>
    public const string ShowDiscontinuedInSearch = "catalog.show-discontinued-in-search";
}
