using EShop.BFF.Infrastructure.Clients.Common;

namespace EShop.BFF.Infrastructure.Clients.Catalog;

/// <summary>Bound from <c>Bff:Catalog</c>. Default scope <c>catalog.read</c> (ADR-0010).</summary>
internal sealed class CatalogClientOptions : UpstreamClientOptions
{
    public const string Section = "Bff:Catalog";

    public const string DefaultScope = "catalog.read";
}
