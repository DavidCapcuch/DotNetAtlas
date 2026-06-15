namespace EShop.BFF.Api.Composition;

/// <summary>
/// Thrown from the product-page cache factory when a <em>gating</em> upstream (Catalog) is
/// unavailable — distinct from a 404. It signals FusionCache to fall back to a stale page if one
/// exists (fail-safe); when none does the exception surfaces and the endpoint maps it to 503. Control
/// flow only — never logged as a fault.
/// </summary>
internal sealed class UpstreamUnavailableException(string upstream)
    : Exception($"Upstream '{upstream}' is unavailable.")
{
    public string Upstream { get; } = upstream;
}
