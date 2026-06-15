using EShop.BFF.Infrastructure.Clients.Common;

namespace EShop.BFF.Infrastructure.Clients.Inventory;

/// <summary>Bound from <c>Bff:Inventory</c>. Default scope <c>inventory.read</c> (ADR-0010).</summary>
internal sealed class InventoryClientOptions : UpstreamClientOptions
{
    public const string Section = "Bff:Inventory";

    public const string DefaultScope = "inventory.read";
}
